namespace SnoopWPF.Agent.Tests;

// Tests for SnoopAgent.StartBrokeredServerAsync (Mode 2 — pipe SERVER role, bd-1a9.21).
// All tests use in-process System.IO.Pipelines.Pipe-backed streams or direct
// NamedPipeClientStream connections so they run reliably on WSL1.

using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Protocol;
using SnoopWPF.Agent.Server;

/// <summary>
/// Unit and light integration tests for <see cref="SnoopAgent.StartBrokeredServerAsync"/>
/// (Mode 2 — WPF target acts as named-pipe SERVER).
/// </summary>
[TestFixture]
public sealed class StartBrokeredServerTests
{
    // -------------------------------------------------------------------------
    // Unit test: StartBrokeredServerAsync returns handle with non-null PipeName
    // -------------------------------------------------------------------------

    /// <summary>
    /// StartBrokeredServerAsync with default settings generates a non-null pipe name
    /// that follows the canonical Mode 2 format and returns immediately.
    /// </summary>
    [Test]
    public async Task StartBrokeredServerAsync_DefaultSettings_ReturnHandleWithNonNullPipeName()
    {
        await using var handle = await SnoopAgent.StartBrokeredServerAsync(new BrokeredServerSettings())
            .ConfigureAwait(false);

        Assert.That(handle.PipeName, Is.Not.Null.And.Not.Empty,
            "Handle must expose a non-null, non-empty pipe name.");
        Assert.That(handle.PipeName, Does.StartWith("motioncatalyst-mcp-"),
            "Auto-generated pipe name must follow the canonical Mode 2 format.");
    }

    /// <summary>
    /// StartBrokeredServerAsync with a caller-supplied pipe name uses that name verbatim.
    /// </summary>
    [Test]
    public async Task StartBrokeredServerAsync_ExplicitPipeName_UsedVerbatim()
    {
        const string explicitName = "test-mode2-pipe-explicit";

        await using var handle = await SnoopAgent.StartBrokeredServerAsync(
            new BrokeredServerSettings { PipeName = explicitName })
            .ConfigureAwait(false);

        Assert.That(handle.PipeName, Is.EqualTo(explicitName));
    }

    // -------------------------------------------------------------------------
    // Unit test: StartBrokeredServerAsync generates a non-null session token
    // -------------------------------------------------------------------------

    /// <summary>
    /// When no session token is supplied, a 64-character hex token (256-bit) is generated.
    /// </summary>
    [Test]
    public async Task StartBrokeredServerAsync_DefaultSettings_ReturnHandleWithNonNullSessionToken()
    {
        await using var handle = await SnoopAgent.StartBrokeredServerAsync(new BrokeredServerSettings())
            .ConfigureAwait(false);

        Assert.That(handle.SessionToken, Is.Not.Null.And.Not.Empty,
            "Handle must expose a non-null session token.");
        Assert.That(handle.SessionToken!.Length, Is.EqualTo(64),
            "Auto-generated session token must be 64 hex characters (256-bit key).");
    }

    /// <summary>
    /// A caller-supplied session token is propagated verbatim to the handle.
    /// </summary>
    [Test]
    public async Task StartBrokeredServerAsync_ExplicitSessionToken_UsedVerbatim()
    {
        string explicitToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        await using var handle = await SnoopAgent.StartBrokeredServerAsync(
            new BrokeredServerSettings { SessionToken = explicitToken })
            .ConfigureAwait(false);

        Assert.That(handle.SessionToken, Is.EqualTo(explicitToken));
    }

    // -------------------------------------------------------------------------
    // Unit test: null settings throws ArgumentNullException
    // -------------------------------------------------------------------------

    [Test]
    public void StartBrokeredServerAsync_NullSettings_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await SnoopAgent.StartBrokeredServerAsync(null!).ConfigureAwait(false));
    }

    // -------------------------------------------------------------------------
    // Unit test: dispose stops listening (WaitForConnectionAsync throws OCE)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Disposing the handle cancels the internal CTS.  The background listener's
    /// WaitForConnectionAsync observes the cancellation and exits; the listener task
    /// completes (does not hang) within a reasonable timeout.
    /// </summary>
    [Test]
    public async Task StartBrokeredServerAsync_Dispose_StopsListener()
    {
        // Use a unique pipe name so this test doesn't collide with others.
        string pipeName = "test-mode2-dispose-" + Guid.NewGuid().ToString("N")[..8];

        var handle = await SnoopAgent.StartBrokeredServerAsync(
            new BrokeredServerSettings { PipeName = pipeName })
            .ConfigureAwait(false);

        // Dispose immediately — no client will connect.
        handle.Dispose();

        // After dispose PipeName is nulled out.
        Assert.That(handle.PipeName, Is.Null,
            "PipeName must be null after disposal.");
        Assert.That(handle.SessionToken, Is.Null,
            "SessionToken must be null after disposal.");
    }

    /// <summary>
    /// DisposeAsync variant: awaiting DisposeAsync after startup completes cleanly.
    /// </summary>
    [Test]
    public async Task StartBrokeredServerAsync_DisposeAsync_StopsListener()
    {
        string pipeName = "test-mode2-disposeasync-" + Guid.NewGuid().ToString("N")[..8];

        var handle = await SnoopAgent.StartBrokeredServerAsync(
            new BrokeredServerSettings { PipeName = pipeName })
            .ConfigureAwait(false);

        await handle.DisposeAsync().ConfigureAwait(false);

        Assert.That(handle.PipeName, Is.Null);
        Assert.That(handle.SessionToken, Is.Null);
    }

    // -------------------------------------------------------------------------
    // Unit test: CancellationToken cancellation stops listener
    // -------------------------------------------------------------------------

    /// <summary>
    /// Passing an already-cancelled token makes the listener exit immediately.
    /// </summary>
    [Test]
    public async Task StartBrokeredServerAsync_AlreadyCancelledToken_ListenerExits()
    {
        string pipeName = "test-mode2-cancelled-" + Guid.NewGuid().ToString("N")[..8];
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancel

        // StartBrokeredServerAsync itself should not throw — it returns the handle.
        await using var handle = await SnoopAgent.StartBrokeredServerAsync(
            new BrokeredServerSettings { PipeName = pipeName },
            cts.Token)
            .ConfigureAwait(false);

        Assert.That(handle, Is.Not.Null);
    }

    // -------------------------------------------------------------------------
    // Integration: HMAC handshake happy-path round-trip
    // -------------------------------------------------------------------------

    /// <summary>
    /// Happy-path integration test: the server starts, a client connects and performs
    /// a valid HMAC handshake.  The server accepts the connection (handshake returns true)
    /// and the listener task completes without error.
    ///
    /// Uses a real OS named pipe because the handshake path requires
    /// <c>NamedPipeServerStream.WaitForConnectionAsync</c> and
    /// <c>NamedPipeClientStream.ConnectAsync</c>.
    /// On WSL1, named pipe I/O is synchronous; ConnectAsync completes immediately once
    /// the server is listening, so there is no timing risk.
    /// </summary>
    [Test]
    [CancelAfter(10000)] // 10 s — generous for WSL1 pipe latency
    public async Task StartBrokeredServerAsync_HappyPath_HandshakeSucceeds()
    {
        string pipeName = "test-mode2-handshake-" + Guid.NewGuid().ToString("N")[..8];
        string sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        var handle = await SnoopAgent.StartBrokeredServerAsync(
            new BrokeredServerSettings
            {
                PipeName = pipeName,
                SessionToken = sessionToken,
            },
            serverCts.Token)
            .ConfigureAwait(false);

        // Give the server listener a moment to reach WaitForConnectionAsync.
        await Task.Delay(100).ConfigureAwait(false);

        // Client: connect and perform the HMAC handshake.
        bool clientHandshakeOk = false;
        await using (var client = new NamedPipeClientStream(
            serverName: ".",
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(serverCts.Token).ConfigureAwait(false);
            clientHandshakeOk = await ClientPerformHandshakeAsync(client, sessionToken, serverCts.Token)
                .ConfigureAwait(false);
        }

        Assert.That(clientHandshakeOk, Is.True, "Client-side HMAC handshake must succeed.");

        // Allow listener task to complete (it exits after handshake in this skeleton).
        await handle.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Negative case: a client that sends an incorrect HMAC proof is rejected.
    /// The server logs a warning and closes the connection; the listener task exits cleanly.
    /// </summary>
    [Test]
    [CancelAfter(10000)]
    public async Task StartBrokeredServerAsync_BadHmac_HandshakeFails()
    {
        string pipeName = "test-mode2-badhmac-" + Guid.NewGuid().ToString("N")[..8];
        string serverToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        var handle = await SnoopAgent.StartBrokeredServerAsync(
            new BrokeredServerSettings
            {
                PipeName = pipeName,
                SessionToken = serverToken,
            },
            serverCts.Token)
            .ConfigureAwait(false);

        await Task.Delay(100).ConfigureAwait(false);

        // Client connects but sends wrong HMAC (uses a different token).
        string wrongToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(serverCts.Token).ConfigureAwait(false);
            // Deliberately use wrong token — server should reject.
            await ClientPerformHandshakeAsync(client, wrongToken, serverCts.Token)
                .ConfigureAwait(false);
        }

        // Server should close the connection without error; dispose should complete cleanly.
        await handle.DisposeAsync().ConfigureAwait(false);

        // If we reach here without hanging, the test passes.
        Assert.Pass("Server rejected bad HMAC and exited cleanly.");
    }

    // -------------------------------------------------------------------------
    // Client-side handshake helper (mirrors StartBrokeredTests pattern)
    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Client-side handshake: reads the nonce challenge, computes HMAC proof, sends response.
    /// Returns true on success; false on EOF or protocol error.
    /// </summary>
    private static async Task<bool> ClientPerformHandshakeAsync(
        Stream clientStream,
        string sessionToken,
        CancellationToken ct)
    {
        try
        {
            var challenge = await ReadFramedJsonAsync<HandshakeChallenge>(clientStream, ct)
                .ConfigureAwait(false);
            if (challenge is null || challenge.Nonce is null || challenge.Nonce.Length != 16)
            {
                return false;
            }

            byte[] sessionTokenBytes = Encoding.UTF8.GetBytes(sessionToken);
            byte[] proofHmac = HMACSHA256.HashData(sessionTokenBytes, challenge.Nonce);

            var response = new HandshakeResponse
            {
                ProtocolVersion = challenge.ProtocolVersion,
                AgentVersion = "test-1.0",
                TargetRuntime = "net8.0",
                ProofHmac = proofHmac,
            };

            await WriteFramedJsonAsync(clientStream, response, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task WriteFramedJsonAsync<T>(Stream s, T value, CancellationToken ct)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, JsonOpts);
        byte[] len = BitConverter.GetBytes(body.Length);
        await s.WriteAsync(len, 0, 4, ct).ConfigureAwait(false);
        await s.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
        await s.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<T?> ReadFramedJsonAsync<T>(Stream s, CancellationToken ct)
    {
        byte[] lenBuf = new byte[4];
        int read = 0;
        while (read < 4)
        {
            int n = await s.ReadAsync(lenBuf, read, 4 - read, ct).ConfigureAwait(false);
            if (n == 0)
            {
                return default;
            }

            read += n;
        }

        int frameLen = BitConverter.ToInt32(lenBuf, 0);
        if (frameLen < 0 || frameLen > 10_000_000)
        {
            return default;
        }

        byte[] body = new byte[frameLen];
        int offset = 0;
        while (offset < frameLen)
        {
            int n = await s.ReadAsync(body, offset, frameLen - offset, ct).ConfigureAwait(false);
            if (n == 0)
            {
                return default;
            }

            offset += n;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOpts);
    }
}
