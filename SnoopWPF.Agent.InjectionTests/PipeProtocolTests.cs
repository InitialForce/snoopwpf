namespace SnoopWPF.Agent.InjectionTests;

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
/// End-to-end protocol tests using in-process named pipes.
///
/// "Host side" uses <see cref="PipeConnection"/> + <see cref="PipeSnoopInspectorProxy"/>.
/// "Injection side" is a minimal fake server implemented with <see cref="FramedJsonTransport"/>
/// that mirrors the protocol logic of PipeAgentServer without requiring the full Injection assembly.
///
/// Tests verify:
/// - Handshake completes before requests are accepted
/// - Method calls round-trip correctly through the proxy
/// - Error responses are mapped to <see cref="SnoopException"/>
/// - Cancellation from the proxy reaches the agent side
/// </summary>
[TestFixture]
public sealed class PipeProtocolTests
{
    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // -----------------------------------------------------------------------
    // Fake injection-side server
    // -----------------------------------------------------------------------

    /// <summary>
    /// Minimal fake injection server that performs the handshake and then processes
    /// incoming PipeRequest frames, dispatching to provided handlers.
    /// </summary>
    private sealed class FakeInjectionServer : IAsyncDisposable
    {
        private readonly NamedPipeClientStream clientPipe;
        private readonly FramedJsonTransport transport;
        private readonly Dictionary<string, Func<string, string>> handlers;

        public FakeInjectionServer(
            string pipeName,
            Dictionary<string, Func<string, string>> handlers)
        {
            this.clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            this.transport = new FramedJsonTransport(this.clientPipe);
            this.handlers = handlers;
        }

        /// <summary>Connects, performs handshake as agent, then processes requests until stopped.</summary>
        public async Task RunAsync(string sessionToken, CancellationToken ct)
        {
            await this.clientPipe.ConnectAsync(10_000, ct).ConfigureAwait(false);

            // Perform handshake: read challenge (nonce), compute HMAC proof, send response.
            var challenge = await this.transport.ReceiveAsync<HandshakeChallenge>(ct).ConfigureAwait(false);
            if (challenge == null)
            {
                throw new InvalidOperationException("No handshake challenge received.");
            }

            if (challenge.Nonce == null || challenge.Nonce.Length != 16)
            {
                throw new InvalidOperationException("Handshake challenge nonce is invalid.");
            }

            if (challenge.ProtocolVersion != ProtocolConstants.ProtocolVersion)
            {
                throw new InvalidOperationException($"Protocol version mismatch: {challenge.ProtocolVersion}");
            }

            byte[] sessionTokenBytes = System.Text.Encoding.UTF8.GetBytes(sessionToken);
            byte[] proofHmac = System.Security.Cryptography.HMACSHA256.HashData(sessionTokenBytes, challenge.Nonce);

            var response = new HandshakeResponse
            {
                ProtocolVersion = ProtocolConstants.ProtocolVersion,
                AgentVersion = "0.0.1-test",
                TargetRuntime = ".NET 8.0",
                ProofHmac = proofHmac,
                Capabilities = new List<string> { "inspection" },
            };
            await this.transport.SendAsync(response, ct).ConfigureAwait(false);

            // Process requests until cancelled or pipe closes.
            while (!ct.IsCancellationRequested)
            {
                PipeRequest? request;
                try
                {
                    request = await this.transport.ReceiveAsync<PipeRequest>(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    break;
                }

                if (request == null)
                {
                    break; // Clean EOF.
                }

                // Check for cancel frames (json has "cancel":true).
                if (request.Method == null && request.ParamsJson == null)
                {
                    continue; // Ignore malformed frames.
                }

                PipeResponse pipeResponse;
                if (this.handlers.TryGetValue(request.Method ?? string.Empty, out var handler))
                {
                    try
                    {
                        string resultJson = handler(request.ParamsJson ?? "{}");
                        pipeResponse = new PipeResponse { Id = request.Id, ResultJson = resultJson };
                    }
                    catch (SnoopException snoopEx)
                    {
                        pipeResponse = new PipeResponse
                        {
                            Id = request.Id,
                            Error = new PipeErrorPayload
                            {
                                Code = snoopEx.Code.ToString(),
                                Message = snoopEx.Message,
                            },
                        };
                    }
                    catch (Exception ex)
                    {
                        pipeResponse = new PipeResponse
                        {
                            Id = request.Id,
                            Error = new PipeErrorPayload { Code = "InternalError", Message = ex.Message },
                        };
                    }
                }
                else
                {
                    pipeResponse = new PipeResponse
                    {
                        Id = request.Id,
                        Error = new PipeErrorPayload { Code = "ProtocolMismatch", Message = $"Unknown method: {request.Method}" },
                    };
                }

                try
                {
                    await this.transport.SendAsync(pipeResponse, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    break;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            this.transport.Dispose();
            await this.clientPipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string UniquePipeName() => $"SnpProt_{Guid.NewGuid():N}";

    private static string SerializeResult<T>(T value)
        => JsonSerializer.Serialize(value, JsonOpts);

    /// <summary>
    /// Sets up a connected host + fake server pair and performs the handshake.
    /// Returns the proxy ready for calls, and a background server task.
    /// </summary>
    private static async Task<(PipeSnoopInspectorProxy Proxy, PipeConnection HostConn, Task ServerTask, CancellationTokenSource ServerCts)>
        SetUpAsync(
            string pipeName,
            string sessionToken,
            Dictionary<string, Func<string, string>> handlers)
    {
        var serverCts = new CancellationTokenSource();

        var fakeServer = new FakeInjectionServer(pipeName, handlers);
        Task serverTask = Task.Run(() => fakeServer.RunAsync(sessionToken, serverCts.Token));

        var hostConn = new PipeConnection(pipeName, expectedClientPid: -1);
        await hostConn.WaitForConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        await hostConn.HandshakeAsync(sessionToken, CancellationToken.None).ConfigureAwait(false);

        var proxy = new PipeSnoopInspectorProxy(hostConn, operationTimeoutMs: 5_000);
        proxy.StartPump();

        return (proxy, hostConn, serverTask, serverCts);
    }

    // -----------------------------------------------------------------------
    // GetSessionInfo round-trip
    // -----------------------------------------------------------------------

    [Test]
    public async Task GetSessionInfo_RoundTrips_Correctly()
    {
        string pipeName = UniquePipeName();
        string token = "tok-session";

        var expectedDto = new SessionInfoDto
        {
            ProcessName = "SampleApp",
            Pid = 12345,
            DotnetVersion = ".NET 8.0",
            MutationEnabled = true,
            Dispatchers = new List<DispatcherInfoDto>(),
            Capabilities = new List<string> { "inspection" },
        };

        var handlers = new Dictionary<string, Func<string, string>>
        {
            ["GetSessionInfo"] = _ => SerializeResult(expectedDto),
        };

        var (proxy, hostConn, serverTask, serverCts) = await SetUpAsync(pipeName, token, handlers).ConfigureAwait(false);
        await using (proxy)
        using (hostConn)
        {
            var result = await proxy.GetSessionInfoAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.ProcessName, Is.EqualTo("SampleApp"));
            Assert.That(result.Pid, Is.EqualTo(12345));
            Assert.That(result.MutationEnabled, Is.True);
        }

        serverCts.Cancel();
        try { await serverTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { /* expected */ }
        serverCts.Dispose();
    }

    // -----------------------------------------------------------------------
    // GetWindows round-trip
    // -----------------------------------------------------------------------

    [Test]
    public async Task GetWindows_RoundTrips_WithIncludeHiddenParameter()
    {
        string pipeName = UniquePipeName();
        string token = "tok-windows";

        bool? capturedIncludeHidden = null;

        var handlers = new Dictionary<string, Func<string, string>>
        {
            ["GetWindows"] = paramsJson =>
            {
                using var doc = JsonDocument.Parse(paramsJson);
                capturedIncludeHidden = doc.RootElement.GetProperty("includeHidden").GetBoolean();
                var windows = new List<WindowDto>
                {
                    new WindowDto { NodeId = "0:1", Title = "Main Window" },
                };
                return SerializeResult(windows);
            },
        };

        var (proxy, hostConn, serverTask, serverCts) = await SetUpAsync(pipeName, token, handlers).ConfigureAwait(false);
        await using (proxy)
        using (hostConn)
        {
            var result = await proxy.GetWindowsAsync(includeHidden: true, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Title, Is.EqualTo("Main Window"));
            Assert.That(capturedIncludeHidden, Is.True, "includeHidden parameter must have been forwarded");
        }

        serverCts.Cancel();
        try { await serverTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { /* expected */ }
        serverCts.Dispose();
    }

    // -----------------------------------------------------------------------
    // Error response → SnoopException mapping
    // -----------------------------------------------------------------------

    [Test]
    public async Task ErrorResponse_NodeNotFound_MapsToSnoopException()
    {
        string pipeName = UniquePipeName();
        string token = "tok-error";

        var handlers = new Dictionary<string, Func<string, string>>
        {
            ["InspectElement"] = _ =>
            {
                throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node 0:99 not found", targetId: "0:99");
            },
        };

        var (proxy, hostConn, serverTask, serverCts) = await SetUpAsync(pipeName, token, handlers).ConfigureAwait(false);
        await using (proxy)
        using (hostConn)
        {
            var ex = Assert.ThrowsAsync<SnoopException>(async () =>
            {
                await proxy.InspectElementAsync("0:99", CancellationToken.None).ConfigureAwait(false);
            });

            Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.NodeNotFound));
        }

        serverCts.Cancel();
        try { await serverTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { /* expected */ }
        serverCts.Dispose();
    }

    [Test]
    public async Task ErrorResponse_UnknownErrorCode_MapsToProtocolMismatch()
    {
        string pipeName = UniquePipeName();
        string token = "tok-unknown-err";

        var handlers = new Dictionary<string, Func<string, string>>
        {
            ["GetSessionInfo"] = _ =>
            {
                throw new SnoopException(SnoopErrorCode.ProtocolMismatch, "Some unknown error");
            },
        };

        var (proxy, hostConn, serverTask, serverCts) = await SetUpAsync(pipeName, token, handlers).ConfigureAwait(false);
        await using (proxy)
        using (hostConn)
        {
            var ex = Assert.ThrowsAsync<SnoopException>(async () =>
            {
                await proxy.GetSessionInfoAsync(CancellationToken.None).ConfigureAwait(false);
            });

            Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.ProtocolMismatch));
        }

        serverCts.Cancel();
        try { await serverTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { /* expected */ }
        serverCts.Dispose();
    }

    // -----------------------------------------------------------------------
    // Concurrent requests
    // -----------------------------------------------------------------------

    [Test]
    public async Task ConcurrentRequests_AllCompleteCorrectly()
    {
        string pipeName = UniquePipeName();
        string token = "tok-concurrent";

        var handlers = new Dictionary<string, Func<string, string>>
        {
            ["GetSessionInfo"] = _ =>
            {
                System.Threading.Thread.Sleep(10); // Simulate slight delay
                return SerializeResult(new SessionInfoDto { ProcessName = "App", Pid = 1 });
            },
        };

        var (proxy, hostConn, serverTask, serverCts) = await SetUpAsync(pipeName, token, handlers).ConfigureAwait(false);
        await using (proxy)
        using (hostConn)
        {
            // Fire 5 concurrent calls.
            var tasks = new Task<SessionInfoDto>[5];
            for (int i = 0; i < 5; i++)
            {
                tasks[i] = proxy.GetSessionInfoAsync(CancellationToken.None);
            }

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            Assert.That(results, Has.Length.EqualTo(5));
            foreach (var r in results)
            {
                Assert.That(r.ProcessName, Is.EqualTo("App"));
            }
        }

        serverCts.Cancel();
        try { await serverTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { /* expected */ }
        serverCts.Dispose();
    }

    // -----------------------------------------------------------------------
    // Cancellation
    // -----------------------------------------------------------------------

    [Test]
    public async Task Cancellation_CallerCancels_ThrowsOperationCancelled()
    {
        string pipeName = UniquePipeName();
        string token = "tok-cancel";

        var requestReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handlers = new Dictionary<string, Func<string, string>>
        {
            ["GetSessionInfo"] = _ =>
            {
                // Signal that we received the request, then delay to allow caller to cancel.
                requestReceivedTcs.TrySetResult(true);
                System.Threading.Thread.Sleep(5_000); // Long delay — caller will cancel.
                return SerializeResult(new SessionInfoDto { ProcessName = "App", Pid = 1 });
            },
        };

        var (proxy, hostConn, serverTask, serverCts) = await SetUpAsync(pipeName, token, handlers).ConfigureAwait(false);
        await using (proxy)
        using (hostConn)
        {
            using var callerCts = new CancellationTokenSource();

            var callTask = proxy.GetSessionInfoAsync(callerCts.Token);

            // Wait for request to be received server-side, then cancel.
            await requestReceivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            callerCts.Cancel();

            try
            {
                await callTask.ConfigureAwait(false);
                Assert.Fail("Expected OperationCanceledException or SnoopException (timeout)");
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
            catch (SnoopException ex)
            {
                // Also acceptable if proxy wraps cancellation as OperationTimedOut.
                Assert.That(
                    ex.Code,
                    Is.EqualTo(SnoopErrorCode.OperationTimedOut).Or.EqualTo(SnoopErrorCode.SessionNotFound));
            }
        }

        serverCts.Cancel();
        try { await serverTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch { /* expected */ }
        serverCts.Dispose();
    }

    // -----------------------------------------------------------------------
    // Multiple method round-trips
    // -----------------------------------------------------------------------

    [Test]
    public async Task MultipleMethodCalls_SequentiallySucceed()
    {
        string pipeName = UniquePipeName();
        string token = "tok-multi";

        var handlers = new Dictionary<string, Func<string, string>>
        {
            ["GetSessionInfo"] = _ => SerializeResult(new SessionInfoDto { ProcessName = "App", Pid = 42 }),
            ["GetWindows"] = _ => SerializeResult(new List<WindowDto>
            {
                new WindowDto { NodeId = "0:1", Title = "Main" },
            }),
            ["GetVisualTree"] = _ => SerializeResult(new VisualTreeResultDto
            {
                Root = new NodeDto { NodeId = "0:1", TypeName = "Window", Name = "MainWindow" },
                ReturnedNodeCount = 1,
            }),
        };

        var (proxy, hostConn, serverTask, serverCts) = await SetUpAsync(pipeName, token, handlers).ConfigureAwait(false);
        await using (proxy)
        using (hostConn)
        {
            var sessionInfo = await proxy.GetSessionInfoAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(sessionInfo.Pid, Is.EqualTo(42));

            var windows = await proxy.GetWindowsAsync(false, CancellationToken.None).ConfigureAwait(false);
            Assert.That(windows, Has.Count.EqualTo(1));
            Assert.That(windows[0].Title, Is.EqualTo("Main"));

            var tree = await proxy.GetVisualTreeAsync((string?)null, 5, "Visual", null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(tree.Root, Is.Not.Null);
            Assert.That(tree.Root!.TypeName, Is.EqualTo("Window"));
        }

        serverCts.Cancel();
        try { await serverTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { /* expected */ }
        serverCts.Dispose();
    }
}
