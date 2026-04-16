namespace SnoopWPF.Agent.Tests;

using System;
using System.IO;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Protocol;
using SnoopWPF.Agent.Remote;

/// <summary>
/// Tests for <see cref="FramedJsonTransport"/>: framing correctness, max-frame enforcement,
/// cancel frame processing, and malformed message handling.
/// Uses in-process <see cref="System.IO.Pipelines.Pipe"/>-backed stream pairs rather than
/// <see cref="NamedPipeServerStream"/>/<see cref="NamedPipeClientStream"/> because WSL1's
/// Windows named-pipe emulation does not reliably signal async reads, causing tests to hang.
/// </summary>
[TestFixture]
public sealed class PipeTransportTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// A fully in-process bidirectional stream pair backed by two
    /// <see cref="System.IO.Pipelines.Pipe"/> objects (one per direction).
    /// Both <see cref="ServerStream"/> and <see cref="ClientStream"/> are duplex
    /// <see cref="Stream"/> objects suitable for passing to <see cref="FramedJsonTransport"/>.
    /// Unlike OS-level named/anonymous pipes, <see cref="System.IO.Pipelines.Pipe"/> does not
    /// involve kernel handles and therefore works reliably on WSL1.
    /// </summary>
    private sealed class InProcessPipePair : IAsyncDisposable
    {
        // Pipe A carries data from server to client.
        private readonly Pipe serverToClient = new Pipe();

        // Pipe B carries data from client to server.
        private readonly Pipe clientToServer = new Pipe();

        /// <summary>Server-side stream: writes go to client, reads come from client.</summary>
        internal Stream ServerStream { get; }

        /// <summary>Client-side stream: writes go to server, reads come from server.</summary>
        internal Stream ClientStream { get; }

        internal InProcessPipePair()
        {
            this.ServerStream = new DuplexStream(
                readFrom: this.clientToServer.Reader.AsStream(),
                writeTo: this.serverToClient.Writer.AsStream());

            this.ClientStream = new DuplexStream(
                readFrom: this.serverToClient.Reader.AsStream(),
                writeTo: this.clientToServer.Writer.AsStream());
        }

        public async ValueTask DisposeAsync()
        {
            await this.serverToClient.Writer.CompleteAsync().ConfigureAwait(false);
            await this.clientToServer.Writer.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Combines a separate read stream and write stream into a single <see cref="Stream"/>
    /// so that <see cref="FramedJsonTransport"/> (which takes one stream) can read from one
    /// anonymous pipe and write to another.
    /// Lifetime of the underlying streams is managed by <see cref="InProcessPipePair"/>;
    /// <see cref="DuplexStream"/> does not own them and does not dispose them.
    /// </summary>
    private sealed class DuplexStream : Stream
    {
        // Streams are borrowed — owned and disposed by InProcessPipePair.
#pragma warning disable CA2213 // Disposable fields should be disposed — ownership belongs to InProcessPipePair
        private readonly Stream readFrom;
        private readonly Stream writeTo;
#pragma warning restore CA2213

        internal DuplexStream(Stream readFrom, Stream writeTo)
        {
            this.readFrom = readFrom;
            this.writeTo = writeTo;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => this.writeTo.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken)
            => this.writeTo.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
            => this.readFrom.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => this.readFrom.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => this.readFrom.ReadAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count)
            => this.writeTo.Write(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => this.writeTo.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => this.writeTo.WriteAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        // DuplexStream does not own its underlying streams — disposal is a no-op.
        protected override void Dispose(bool disposing) => base.Dispose(disposing);

#pragma warning disable CA2215 // Call base class Dispose — base.DisposeAsync delegates to Dispose(true) which is correct
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
#pragma warning restore CA2215
    }

    // -------------------------------------------------------------------------
    // Framing correctness
    // -------------------------------------------------------------------------

    [Test]
    public async Task SendAndReceive_RoundTripsPipeRequest()
    {
        await using var pipes = new InProcessPipePair();
        var senderTransport = new FramedJsonTransport(pipes.ServerStream);
        var receiverTransport = new FramedJsonTransport(pipes.ClientStream);

        var original = new PipeRequest { Id = 42, Method = "GetWindows", ParamsJson = "{\"includeHidden\":false}" };
        await senderTransport.SendAsync(original, CancellationToken.None);

        var received = await receiverTransport.ReceiveAsync<PipeRequest>(CancellationToken.None);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Id, Is.EqualTo(42));
        Assert.That(received.Method, Is.EqualTo("GetWindows"));
        Assert.That(received.ParamsJson, Is.EqualTo("{\"includeHidden\":false}"));
    }

    [Test]
    public async Task SendAndReceive_RoundTripsPipeResponse()
    {
        await using var pipes = new InProcessPipePair();
        var senderTransport = new FramedJsonTransport(pipes.ClientStream);
        var receiverTransport = new FramedJsonTransport(pipes.ServerStream);

        var original = new PipeResponse { Id = 7, ResultJson = "{\"processName\":\"TestApp\",\"pid\":1234}" };
        await senderTransport.SendAsync(original, CancellationToken.None);

        var received = await receiverTransport.ReceiveAsync<PipeResponse>(CancellationToken.None);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Id, Is.EqualTo(7));
        Assert.That(received.ResultJson, Does.Contain("\"processName\""));
        Assert.That(received.Error, Is.Null);
    }

    [Test]
    public async Task SendAndReceive_ErrorResponse_PreservesErrorPayload()
    {
        await using var pipes = new InProcessPipePair();
        var senderTransport = new FramedJsonTransport(pipes.ClientStream);
        var receiverTransport = new FramedJsonTransport(pipes.ServerStream);

        var original = new PipeResponse
        {
            Id = 99,
            ResultJson = null,
            Error = new PipeErrorPayload
            {
                Code = "NodeNotFound",
                Message = "Node 0:42 not found",
                Suggestion = SnoopSuggestions.NodeNotFound,
            },
        };

        await senderTransport.SendAsync(original, CancellationToken.None);
        var received = await receiverTransport.ReceiveAsync<PipeResponse>(CancellationToken.None);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Error, Is.Not.Null);
        Assert.That(received.Error!.Code, Is.EqualTo("NodeNotFound"));
        Assert.That(received.Error.Suggestion, Is.EqualTo(SnoopSuggestions.NodeNotFound));
    }

    // -------------------------------------------------------------------------
    // Cancel frame
    // -------------------------------------------------------------------------

    [Test]
    public async Task SendAndReceive_CancelPayload_RoundTrips()
    {
        await using var pipes = new InProcessPipePair();
        var senderTransport = new FramedJsonTransport(pipes.ServerStream);
        var receiverTransport = new FramedJsonTransport(pipes.ClientStream);

        var cancel = new PipeCancelPayload { Id = 5, Cancel = true };
        await senderTransport.SendAsync(cancel, CancellationToken.None);

        var received = await receiverTransport.ReceiveAsync<PipeCancelPayload>(CancellationToken.None);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Id, Is.EqualTo(5));
        Assert.That(received.Cancel, Is.True);
    }

    // -------------------------------------------------------------------------
    // Max frame enforcement
    // -------------------------------------------------------------------------

    [Test]
    public void SendAsync_FrameExceedsMaxSize_ThrowsInvalidOperation()
    {
        using var ms = new MemoryStream();
        var transport = new FramedJsonTransport(ms);

        // Build an object whose JSON exceeds 10 MiB.
        var big = new { Data = new string('x', ProtocolConstants.MaxFrameSize + 1) };

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await transport.SendAsync(big, CancellationToken.None);
        });
    }

    [Test]
    public void ReceiveAsync_OversizedFrameLength_ThrowsInvalidOperation()
    {
        using var ms = new MemoryStream();

        int oversized = ProtocolConstants.MaxFrameSize + 1;
        ms.Write(BitConverter.GetBytes(oversized), 0, 4);
        ms.Seek(0, SeekOrigin.Begin);

        var transport = new FramedJsonTransport(ms);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await transport.ReceiveAsync<PipeRequest>(CancellationToken.None);
        });
    }

    // -------------------------------------------------------------------------
    // Malformed message handling
    // -------------------------------------------------------------------------

    [Test]
    public void ReceiveAsync_MalformedJson_ThrowsInvalidOperation()
    {
        using var ms = new MemoryStream();

        byte[] badJson = System.Text.Encoding.UTF8.GetBytes("not valid json !!!");
        byte[] lengthBytes = BitConverter.GetBytes(badJson.Length);
        ms.Write(lengthBytes, 0, 4);
        ms.Write(badJson, 0, badJson.Length);
        ms.Seek(0, SeekOrigin.Begin);

        var transport = new FramedJsonTransport(ms);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await transport.ReceiveAsync<PipeRequest>(CancellationToken.None);
        });
    }

    [Test]
    public void ReceiveAsync_NegativeFrameLength_ThrowsInvalidOperation()
    {
        using var ms = new MemoryStream();

        ms.Write(BitConverter.GetBytes(-1), 0, 4);
        ms.Seek(0, SeekOrigin.Begin);

        var transport = new FramedJsonTransport(ms);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await transport.ReceiveAsync<PipeRequest>(CancellationToken.None);
        });
    }

    // -------------------------------------------------------------------------
    // EOF handling
    // -------------------------------------------------------------------------

    [Test]
    public async Task ReceiveAsync_EmptyStream_ReturnsNull()
    {
        using var ms = new MemoryStream(Array.Empty<byte>());
        var transport = new FramedJsonTransport(ms);

        // Clean EOF returns default (null for reference types).
        var result = await transport.ReceiveAsync<PipeRequest>(CancellationToken.None);
        Assert.That(result, Is.Null);
    }

    // -------------------------------------------------------------------------
    // Handshake protocol types
    // -------------------------------------------------------------------------

    [Test]
    public async Task Handshake_ChallengeAndResponse_RoundTrip()
    {
        await using var pipes = new InProcessPipePair();
        var hostTransport = new FramedJsonTransport(pipes.ServerStream);
        var agentTransport = new FramedJsonTransport(pipes.ClientStream);

        const string sessionToken = "tok-abc-123";
        byte[] sessionTokenBytes = System.Text.Encoding.UTF8.GetBytes(sessionToken);

        // Host sends challenge (nonce only — no token).
        byte[] nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var challenge = new HandshakeChallenge
        {
            Nonce = nonce,
            ProtocolVersion = ProtocolConstants.ProtocolVersion,
        };
        await hostTransport.SendAsync(challenge, CancellationToken.None);

        // Agent reads challenge, computes HMAC proof, sends response.
        var receivedChallenge = await agentTransport.ReceiveAsync<HandshakeChallenge>(CancellationToken.None);
        Assert.That(receivedChallenge!.Nonce, Is.Not.Null);
        Assert.That(receivedChallenge.Nonce.Length, Is.EqualTo(16));
        Assert.That(receivedChallenge.ProtocolVersion, Is.EqualTo(ProtocolConstants.ProtocolVersion));

        byte[] proofHmac = System.Security.Cryptography.HMACSHA256.HashData(sessionTokenBytes, receivedChallenge.Nonce);
        var response = new HandshakeResponse
        {
            ProtocolVersion = ProtocolConstants.ProtocolVersion,
            AgentVersion = "1.0.0",
            TargetRuntime = "net8.0",
            ProofHmac = proofHmac,
        };
        await agentTransport.SendAsync(response, CancellationToken.None);

        // Host reads response and verifies HMAC.
        var receivedResponse = await hostTransport.ReceiveAsync<HandshakeResponse>(CancellationToken.None);
        Assert.That(receivedResponse!.ProtocolVersion, Is.EqualTo(ProtocolConstants.ProtocolVersion));
        Assert.That(receivedResponse.ProofHmac, Is.Not.Null);
        Assert.That(receivedResponse.ProofHmac.Length, Is.EqualTo(32));
        Assert.That(receivedResponse.AgentVersion, Is.EqualTo("1.0.0"));

        byte[] expectedHmac = System.Security.Cryptography.HMACSHA256.HashData(sessionTokenBytes, nonce);
        Assert.That(
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(receivedResponse.ProofHmac, expectedHmac),
            Is.True,
            "HMAC proof should match expected value.");
    }

    // -------------------------------------------------------------------------
    // SerializeJson / DeserializeJson round-trip
    // -------------------------------------------------------------------------

    [Test]
    public void SerializeDeserializeJson_RoundTrips()
    {
        var original = new { nodeId = "0:42", treeType = "Visual", take = 100 };
        string json = FramedJsonTransport.SerializeJson(original);

        Assert.That(json, Does.Contain("nodeId"));
        Assert.That(json, Does.Contain("0:42"));

        // Deserialize back via anonymous-type helper won't work with STJ (no parameterless ctor),
        // so deserialise into a JsonDocument as a sanity check.
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.GetProperty("nodeId").GetString(), Is.EqualTo("0:42"));
        Assert.That(doc.RootElement.GetProperty("take").GetInt32(), Is.EqualTo(100));
    }
}
