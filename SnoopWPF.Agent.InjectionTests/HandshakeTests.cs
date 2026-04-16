namespace SnoopWPF.Agent.InjectionTests;

using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Protocol;
using SnoopWPF.Agent.Remote;

/// <summary>
/// Tests the handshake exchange between the host-side <see cref="PipeConnection"/>
/// and a fake injection-side server implemented using <see cref="FramedJsonTransport"/>.
///
/// The "fake injection side" mirrors what PipeAgentServer.PerformHandshakeAsync does:
/// reads HandshakeChallenge, validates token/version, sends HandshakeResponse.
/// </summary>
[TestFixture]
public sealed class HandshakeTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string UniquePipeName() => $"SnpHs_{Guid.NewGuid():N}";

    /// <summary>
    /// Creates a named-pipe server+client pair already connected.
    /// Returns (serverStream for host side, clientStream for injection side).
    /// </summary>
    private static async Task<(NamedPipeServerStream Server, NamedPipeClientStream Client)> CreateConnectedPairAsync(
        string name)
    {
        var server = new NamedPipeServerStream(
            name,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var client = new NamedPipeClientStream(
            ".",
            name,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        var serverConnectTask = server.WaitForConnectionAsync();
        await client.ConnectAsync(5000).ConfigureAwait(false);
        await serverConnectTask.ConfigureAwait(false);

        return (server, client);
    }

    // -----------------------------------------------------------------------
    // Valid handshake
    // -----------------------------------------------------------------------

    [Test]
    public async Task Handshake_ValidToken_Succeeds()
    {
        string pipeName = UniquePipeName();
        string sessionToken = "valid-token-abc";

        // Start fake injection-side server running concurrently.
        var fakeAgentTask = Task.Run(async () =>
        {
            // The fake agent CONNECTS FIRST, then waits for challenge.
            // But PipeConnection constructor creates the server; fake agent is the client.
            var clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await clientPipe.ConnectAsync(10_000).ConfigureAwait(false);

            var transport = new FramedJsonTransport(clientPipe);

            // Read challenge from host.
            var challenge = await transport.ReceiveAsync<HandshakeChallenge>(CancellationToken.None).ConfigureAwait(false);
            Assert.That(challenge, Is.Not.Null, "Fake agent: challenge must not be null");
            Assert.That(challenge!.SessionToken, Is.EqualTo(sessionToken));
            Assert.That(challenge.ProtocolVersion, Is.EqualTo(ProtocolConstants.ProtocolVersion));

            // Echo token back in response.
            var response = new HandshakeResponse
            {
                ProtocolVersion = ProtocolConstants.ProtocolVersion,
                AgentVersion = "0.0.1",
                TargetRuntime = ".NET 8.0",
                SessionToken = challenge.SessionToken,
                Capabilities = new System.Collections.Generic.List<string> { "inspection" },
            };
            await transport.SendAsync(response, CancellationToken.None).ConfigureAwait(false);

            await clientPipe.DisposeAsync().ConfigureAwait(false);
        });

        // Host side: create PipeConnection (creates server), wait for agent, handshake.
        using var pipeConn = new PipeConnection(pipeName, expectedClientPid: -1);

        await pipeConn.WaitForConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        await pipeConn.HandshakeAsync(sessionToken, CancellationToken.None).ConfigureAwait(false);

        // Verify the host recorded the handshake response.
        Assert.That(pipeConn.RemoteHandshake, Is.Not.Null);
        Assert.That(pipeConn.RemoteHandshake!.ProtocolVersion, Is.EqualTo(ProtocolConstants.ProtocolVersion));
        Assert.That(pipeConn.RemoteHandshake.SessionToken, Is.EqualTo(sessionToken));
        Assert.That(pipeConn.RemoteHandshake.Capabilities, Does.Contain("inspection"));

        await fakeAgentTask.ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Wrong token echoed back → host rejects
    // -----------------------------------------------------------------------

    [Test]
    public async Task Handshake_AgentReturnsWrongToken_HostThrowsProtocolMismatch()
    {
        string pipeName = UniquePipeName();
        string sessionToken = "correct-token";

        var fakeAgentTask = Task.Run(async () =>
        {
            var clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await clientPipe.ConnectAsync(10_000).ConfigureAwait(false);
            var transport = new FramedJsonTransport(clientPipe);

            var challenge = await transport.ReceiveAsync<HandshakeChallenge>(CancellationToken.None).ConfigureAwait(false);

            // Return a WRONG token — simulates a spoofed or wrong process connection.
            var response = new HandshakeResponse
            {
                ProtocolVersion = ProtocolConstants.ProtocolVersion,
                AgentVersion = "0.0.1",
                TargetRuntime = ".NET 8.0",
                SessionToken = "wrong-token",
            };
            await transport.SendAsync(response, CancellationToken.None).ConfigureAwait(false);

            await clientPipe.DisposeAsync().ConfigureAwait(false);
        });

        using var pipeConn = new PipeConnection(pipeName, expectedClientPid: -1);
        await pipeConn.WaitForConnectionAsync(CancellationToken.None).ConfigureAwait(false);

        var ex = Assert.ThrowsAsync<SnoopException>(async () =>
        {
            await pipeConn.HandshakeAsync(sessionToken, CancellationToken.None).ConfigureAwait(false);
        });

        Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.ProtocolMismatch));
        Assert.That(ex.Message, Does.Contain("session token").IgnoreCase);

        await fakeAgentTask.ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Wrong protocol version from agent → host rejects
    // -----------------------------------------------------------------------

    [Test]
    public async Task Handshake_AgentReturnsWrongProtocolVersion_HostThrowsProtocolMismatch()
    {
        string pipeName = UniquePipeName();
        string sessionToken = "tok-version-test";

        var fakeAgentTask = Task.Run(async () =>
        {
            var clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await clientPipe.ConnectAsync(10_000).ConfigureAwait(false);
            var transport = new FramedJsonTransport(clientPipe);

            var challenge = await transport.ReceiveAsync<HandshakeChallenge>(CancellationToken.None).ConfigureAwait(false);

            // Return a mismatched protocol version.
            var response = new HandshakeResponse
            {
                ProtocolVersion = 9999, // Wrong version
                AgentVersion = "0.0.1",
                TargetRuntime = ".NET 8.0",
                SessionToken = challenge!.SessionToken, // Correct token
            };
            await transport.SendAsync(response, CancellationToken.None).ConfigureAwait(false);

            await clientPipe.DisposeAsync().ConfigureAwait(false);
        });

        using var pipeConn = new PipeConnection(pipeName, expectedClientPid: -1);
        await pipeConn.WaitForConnectionAsync(CancellationToken.None).ConfigureAwait(false);

        var ex = Assert.ThrowsAsync<SnoopException>(async () =>
        {
            await pipeConn.HandshakeAsync(sessionToken, CancellationToken.None).ConfigureAwait(false);
        });

        Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.ProtocolMismatch));

        await fakeAgentTask.ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Agent closes pipe during handshake → host fails gracefully
    // -----------------------------------------------------------------------

    [Test]
    public async Task Handshake_AgentClosesConnectionBeforeResponse_HostThrowsProtocolMismatch()
    {
        string pipeName = UniquePipeName();
        string sessionToken = "tok-close-test";

        var fakeAgentTask = Task.Run(async () =>
        {
            var clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await clientPipe.ConnectAsync(10_000).ConfigureAwait(false);
            var transport = new FramedJsonTransport(clientPipe);

            // Read the challenge but do NOT send a response — just close.
            await transport.ReceiveAsync<HandshakeChallenge>(CancellationToken.None).ConfigureAwait(false);

            // Silently close — agent "crashed" before responding.
            await clientPipe.DisposeAsync().ConfigureAwait(false);
        });

        using var pipeConn = new PipeConnection(pipeName, expectedClientPid: -1);
        await pipeConn.WaitForConnectionAsync(CancellationToken.None).ConfigureAwait(false);

        var ex = Assert.ThrowsAsync<SnoopException>(async () =>
        {
            await pipeConn.HandshakeAsync(sessionToken, CancellationToken.None).ConfigureAwait(false);
        });

        Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.ProtocolMismatch));

        await fakeAgentTask.ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Handshake timeout
    // -----------------------------------------------------------------------

    [Test]
    public async Task Handshake_AgentNeverResponds_CancellationPropagates()
    {
        string pipeName = UniquePipeName();
        string sessionToken = "tok-timeout";
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var fakeAgentTask = Task.Run(async () =>
        {
            var clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await clientPipe.ConnectAsync(10_000).ConfigureAwait(false);
            var transport = new FramedJsonTransport(clientPipe);

            // Read challenge but deliberately hang — never send response.
            await transport.ReceiveAsync<HandshakeChallenge>(CancellationToken.None).ConfigureAwait(false);

            // Wait for the host to give up, then clean up.
            await Task.Delay(2000).ConfigureAwait(false);
            await clientPipe.DisposeAsync().ConfigureAwait(false);
        });

        using var pipeConn = new PipeConnection(pipeName, expectedClientPid: -1);
        await pipeConn.WaitForConnectionAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            await pipeConn.HandshakeAsync(sessionToken, cts.Token).ConfigureAwait(false);
            Assert.Fail("Expected OperationCanceledException or SnoopException — handshake should not succeed");
        }
        catch (OperationCanceledException)
        {
            // Expected — cancellation propagated.
        }
        catch (SnoopException ex)
        {
            // Also acceptable — some implementations wrap cancellation.
            Assert.That(ex.Code, Is.EqualTo(SnoopErrorCode.ProtocolMismatch).Or.EqualTo(SnoopErrorCode.OperationTimedOut));
        }

        // Allow the fake agent to clean up (ignore its timeout).
        try { await fakeAgentTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch { /* expected */ }
    }

    // -----------------------------------------------------------------------
    // Challenge fields propagated correctly
    // -----------------------------------------------------------------------

    [Test]
    public async Task Handshake_CorrectProtocolVersionSentInChallenge()
    {
        string pipeName = UniquePipeName();
        string sessionToken = "tok-challenge-check";

        HandshakeChallenge? receivedChallenge = null;

        var fakeAgentTask = Task.Run(async () =>
        {
            var clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await clientPipe.ConnectAsync(10_000).ConfigureAwait(false);
            var transport = new FramedJsonTransport(clientPipe);

            receivedChallenge = await transport.ReceiveAsync<HandshakeChallenge>(CancellationToken.None).ConfigureAwait(false);

            var response = new HandshakeResponse
            {
                ProtocolVersion = ProtocolConstants.ProtocolVersion,
                AgentVersion = "1.0.0",
                TargetRuntime = ".NET 8.0",
                SessionToken = receivedChallenge!.SessionToken,
            };
            await transport.SendAsync(response, CancellationToken.None).ConfigureAwait(false);

            await clientPipe.DisposeAsync().ConfigureAwait(false);
        });

        using var pipeConn = new PipeConnection(pipeName, expectedClientPid: -1);
        await pipeConn.WaitForConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        await pipeConn.HandshakeAsync(sessionToken, CancellationToken.None).ConfigureAwait(false);

        await fakeAgentTask.ConfigureAwait(false);

        Assert.That(receivedChallenge, Is.Not.Null);
        Assert.That(receivedChallenge!.SessionToken, Is.EqualTo(sessionToken));
        Assert.That(receivedChallenge.ProtocolVersion, Is.EqualTo(ProtocolConstants.ProtocolVersion));
    }
}
