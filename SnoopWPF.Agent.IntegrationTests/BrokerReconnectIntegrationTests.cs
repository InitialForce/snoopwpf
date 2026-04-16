namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Contracts.Protocol;
using SnoopWPF.Agent.Remote;

/// <summary>
/// Integration tests for broker→target pipe reconnect behavior (FX-N3, gap 2).
///
/// Verifies that:
/// - After the target process "crashes" (pipe closed), the proxy reports the target is not
///   running via <see cref="SnoopException"/> with <see cref="SnoopErrorCode.SessionNotFound"/>.
/// - After a kill+respawn cycle, a fresh proxy connecting to a new pipe succeeds on subsequent calls.
///
/// Uses real Windows named pipes + a minimal fake injection server (same pattern as
/// <c>SnoopWPF.Agent.InjectionTests.PipeProtocolTests</c>) to avoid requiring a live WPF process.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class BrokerReconnectIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // -------------------------------------------------------------------------
    // Minimal fake injection server (client-side pipe)
    // -------------------------------------------------------------------------

    /// <summary>
    /// A minimal fake target that connects over a named pipe, performs the handshake as an
    /// agent, and then processes requests indefinitely (until cancelled or disposed).
    /// Closing/disposing this server simulates a target process crash.
    /// </summary>
    private sealed class FakeTarget : IAsyncDisposable
    {
        private readonly NamedPipeClientStream clientPipe;
        private readonly FramedJsonTransport transport;
        private readonly Dictionary<string, Func<string, string>> handlers;
        private Task? runTask;

        public FakeTarget(string pipeName, Dictionary<string, Func<string, string>> handlers)
        {
            this.clientPipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            this.transport = new FramedJsonTransport(this.clientPipe);
            this.handlers = handlers;
        }

        /// <summary>Connects, performs handshake, and begins serving requests in the background.</summary>
        public async Task StartAsync(string sessionToken, CancellationToken ct)
        {
            await this.clientPipe.ConnectAsync(10_000, ct).ConfigureAwait(false);

            var challenge = await this.transport.ReceiveAsync<HandshakeChallenge>(ct).ConfigureAwait(false);
            if (challenge?.Nonce is null || challenge.Nonce.Length != 16)
            {
                throw new InvalidOperationException("Invalid handshake challenge.");
            }

            byte[] keyBytes = System.Text.Encoding.UTF8.GetBytes(sessionToken);
            byte[] proofHmac = System.Security.Cryptography.HMACSHA256.HashData(keyBytes, challenge.Nonce);

            await this.transport.SendAsync(
                new HandshakeResponse
                {
                    ProtocolVersion = ProtocolConstants.ProtocolVersion,
                    AgentVersion = "0.0.1-test",
                    TargetRuntime = ".NET 8.0",
                    ProofHmac = proofHmac,
                    Capabilities = new List<string> { "inspection" },
                },
                ct).ConfigureAwait(false);

            this.runTask = Task.Run(() => this.ServeAsync(ct), CancellationToken.None);
        }

        private async Task ServeAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                PipeRequest? request;
                try
                {
                    request = await this.transport.ReceiveAsync<PipeRequest>(ct).ConfigureAwait(false);
                }
                catch
                {
                    break;
                }

                if (request is null)
                {
                    break;
                }

                PipeResponse response;
                if (this.handlers.TryGetValue(request.Method ?? string.Empty, out var handler))
                {
                    try
                    {
                        response = new PipeResponse { Id = request.Id, ResultJson = handler(request.ParamsJson ?? "{}") };
                    }
                    catch (Exception ex)
                    {
                        response = new PipeResponse
                        {
                            Id = request.Id,
                            Error = new PipeErrorPayload { Code = "InternalError", Message = ex.Message },
                        };
                    }
                }
                else
                {
                    response = new PipeResponse
                    {
                        Id = request.Id,
                        Error = new PipeErrorPayload { Code = "ProtocolMismatch", Message = $"Unknown method: {request.Method}" },
                    };
                }

                try
                {
                    await this.transport.SendAsync(response, ct).ConfigureAwait(false);
                }
                catch
                {
                    break;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            this.transport.Dispose();
            await this.clientPipe.DisposeAsync().ConfigureAwait(false);
            if (this.runTask is not null)
            {
                try
                {
                    await this.runTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch
                {
                    // Expected on crash simulation — the run task may be cancelled or faulted.
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string UniquePipeName() => $"SnpBrkRecon_{Guid.NewGuid():N}";

    private static string SerializeResult<T>(T value) => JsonSerializer.Serialize(value, JsonOpts);

    private static Dictionary<string, Func<string, string>> SessionInfoHandler() =>
        new Dictionary<string, Func<string, string>>
        {
            ["GetSessionInfo"] = _ => SerializeResult(new SessionInfoDto
            {
                ProcessName = "FakeTarget",
                Pid = 9999,
                DotnetVersion = ".NET 8.0",
                Dispatchers = new List<DispatcherInfoDto>(),
                Capabilities = new List<string> { "inspection" },
            }),
        };

    /// <summary>
    /// Sets up a <see cref="PipeConnection"/> with a connected <see cref="FakeTarget"/>.
    /// Returns the proxy (pump started), the pipe connection, and the target (for disposal).
    /// </summary>
    private static async Task<(PipeSnoopInspectorProxy Proxy, PipeConnection HostConn, FakeTarget Target, CancellationTokenSource TargetCts)>
        ConnectAsync(string pipeName, string sessionToken, Dictionary<string, Func<string, string>> handlers)
    {
        var targetCts = new CancellationTokenSource();
        var target = new FakeTarget(pipeName, handlers);

        // Start the target connect task before creating the server-side connection.
        Task targetStart = target.StartAsync(sessionToken, targetCts.Token);

        var hostConn = new PipeConnection(pipeName, expectedClientPid: -1);
        await hostConn.WaitForConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        await hostConn.HandshakeAsync(sessionToken, CancellationToken.None).ConfigureAwait(false);
        await targetStart.ConfigureAwait(false);

        var proxy = new PipeSnoopInspectorProxy(hostConn, operationTimeoutMs: 5_000);
        proxy.StartPump();

        return (proxy, hostConn, target, targetCts);
    }

    // -------------------------------------------------------------------------
    // Gap 2a: Proxy_WhenTargetCrashes_ReportsTargetNotRunning
    // -------------------------------------------------------------------------

    /// <summary>
    /// Spawns a fake target, verifies initial call succeeds, then "crashes" the target by
    /// disposing its pipe. Verifies that the next proxy call throws
    /// <see cref="SnoopException"/> with <see cref="SnoopErrorCode.SessionNotFound"/>
    /// (the proxy's representation of "target not running").
    ///
    /// NOTE: The proxy currently uses <see cref="SnoopErrorCode.SessionNotFound"/> to signal
    /// this condition rather than a dedicated TargetNotRunning code. This is the expected
    /// behavior per the proxy implementation (PipeSnoopInspectorProxy.ThrowIfDisconnected).
    /// If a dedicated TargetNotRunning code is introduced later, update this assertion.
    /// </summary>
    [Test]
    public async Task Proxy_WhenTargetCrashes_ReportsTargetNotRunning()
    {
        string pipeName = UniquePipeName();
        string sessionToken = "tok-crash-test";

        var (proxy, hostConn, target, targetCts) = await ConnectAsync(
            pipeName, sessionToken, SessionInfoHandler()).ConfigureAwait(false);

        await using (proxy)
        using (hostConn)
        {
            // Verify the proxy works initially.
            var info = await proxy.GetSessionInfoAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(info.ProcessName, Is.EqualTo("FakeTarget"), "Initial call must succeed before crash.");

            // Simulate target crash: dispose the fake target pipe (drops the connection).
            targetCts.Cancel();
            await target.DisposeAsync().ConfigureAwait(false);

            // Give the proxy pump time to detect the EOF and set disconnected=true.
            await Task.Delay(300).ConfigureAwait(false);

            // Next call must throw SessionNotFound (proxy's TargetNotRunning equivalent).
            var ex = Assert.ThrowsAsync<SnoopException>(async () =>
            {
                await proxy.GetSessionInfoAsync(CancellationToken.None).ConfigureAwait(false);
            });

            Assert.That(
                ex!.Code,
                Is.EqualTo(SnoopErrorCode.SessionNotFound),
                "After target pipe closes, proxy must throw SessionNotFound (= TargetNotRunning signal).");
        }

        targetCts.Dispose();
    }

    // -------------------------------------------------------------------------
    // Gap 2b: Proxy_WhenTargetRestarts_Reconnects
    // -------------------------------------------------------------------------

    /// <summary>
    /// After the target crashes, a fresh proxy connecting to a new pipe must succeed.
    /// This validates that the broker can reconnect to a respawned target by creating a new
    /// <see cref="PipeConnection"/> + <see cref="PipeSnoopInspectorProxy"/> pair (the
    /// standard restart/reconnect pattern for brokered mode).
    ///
    /// Sequence:
    ///   1. Connect to pipe1 → succeed.
    ///   2. "Kill" target1 (dispose).
    ///   3. "Respawn" target2 on pipe2 (new pipe name, simulating a new target process).
    ///   4. Connect fresh proxy to pipe2 → succeed.
    /// </summary>
    [Test]
    public async Task Proxy_WhenTargetRestarts_Reconnects()
    {
        string pipe1 = UniquePipeName();
        string pipe2 = UniquePipeName();
        string sessionToken = "tok-restart-test";

        // ── Phase 1: connect and verify. ────────────────────────────────────
        var (proxy1, hostConn1, target1, targetCts1) = await ConnectAsync(
            pipe1, sessionToken, SessionInfoHandler()).ConfigureAwait(false);

        await using (proxy1)
        using (hostConn1)
        {
            var info1 = await proxy1.GetSessionInfoAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(info1.ProcessName, Is.EqualTo("FakeTarget"), "Phase 1: initial call must succeed.");
        }

        // ── Phase 2: kill the target. ────────────────────────────────────────
        targetCts1.Cancel();
        await target1.DisposeAsync().ConfigureAwait(false);
        targetCts1.Dispose();

        // ── Phase 3: respawn on a new pipe — simulate broker creating a new session. ──
        var (proxy2, hostConn2, target2, targetCts2) = await ConnectAsync(
            pipe2, sessionToken, SessionInfoHandler()).ConfigureAwait(false);

        await using (proxy2)
        using (hostConn2)
        {
            // Phase 4: fresh proxy to restarted target must succeed.
            var info2 = await proxy2.GetSessionInfoAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                info2.ProcessName,
                Is.EqualTo("FakeTarget"),
                "Phase 4: proxy to restarted target must succeed after reconnect.");
            Assert.That(info2.Pid, Is.EqualTo(9999), "Pid must be correct from the restarted target.");
        }

        targetCts2.Cancel();
        await target2.DisposeAsync().ConfigureAwait(false);
        targetCts2.Dispose();
    }
}
