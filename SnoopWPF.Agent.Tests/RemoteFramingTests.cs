namespace SnoopWPF.Agent.Tests;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Protocol;
using SnoopWPF.Agent.Remote;

/// <summary>
/// Tests for <see cref="FramedJsonTransport"/> targeted by the acceptance-criteria filter
/// <c>FullyQualifiedName~RemoteFraming</c>.  Covers:
/// <list type="bullet">
///   <item>Normal request/response correlation via requestId.</item>
///   <item>In-order delivery guarantee under concurrent sends.</item>
///   <item>Malformed frames.</item>
///   <item>Client disconnect mid-request (partial header bytes then EOF).</item>
///   <item>Server disconnect mid-response (partial body then EOF).</item>
/// </list>
/// Uses in-process <see cref="System.IO.Pipelines.Pipe"/>-backed streams (no OS handles) so
/// tests run reliably on WSL1.
/// </summary>
[TestFixture]
public sealed class RemoteFramingTests
{
    // -------------------------------------------------------------------------
    // Infrastructure
    // -------------------------------------------------------------------------

    private sealed class InProcessPipePair : IAsyncDisposable
    {
        private readonly Pipe serverToClient = new Pipe();

        private readonly Pipe clientToServer = new Pipe();

        internal Stream ServerStream { get; }

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

    private sealed class DuplexStream : Stream
    {
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

        public override Task FlushAsync(CancellationToken ct) => this.writeTo.FlushAsync(ct);

        public override int Read(byte[] buffer, int offset, int count) => this.readFrom.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => this.readFrom.ReadAsync(buffer, offset, count, ct);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => this.readFrom.ReadAsync(buffer, ct);

        public override void Write(byte[] buffer, int offset, int count) => this.writeTo.Write(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => this.writeTo.WriteAsync(buffer, offset, count, ct);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
            => this.writeTo.WriteAsync(buffer, ct);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing) => base.Dispose(disposing);

#pragma warning disable CA2215 // base.DisposeAsync delegates to Dispose(true) — correct here
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
#pragma warning restore CA2215
    }

    // -------------------------------------------------------------------------
    // Normal request/response with requestId correlation
    // -------------------------------------------------------------------------

    [Test]
    public async Task RequestResponse_RequestIdCorrelates()
    {
        await using var pipes = new InProcessPipePair();
        var clientTransport = new FramedJsonTransport(pipes.ClientStream);
        var serverTransport = new FramedJsonTransport(pipes.ServerStream);

        var request = new PipeRequest { Id = 101, Method = "GetWindows", ParamsJson = string.Empty };
        await clientTransport.SendAsync(request, CancellationToken.None);

        var received = await serverTransport.ReceiveAsync<PipeRequest>(CancellationToken.None);
        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Id, Is.EqualTo(101));
        Assert.That(received.Method, Is.EqualTo("GetWindows"));

        var response = new PipeResponse { Id = received.Id, ResultJson = "{\"ok\":true}" };
        await serverTransport.SendAsync(response, CancellationToken.None);

        var receivedResponse = await clientTransport.ReceiveAsync<PipeResponse>(CancellationToken.None);
        Assert.That(receivedResponse, Is.Not.Null);
        Assert.That(receivedResponse!.Id, Is.EqualTo(101), "Response Id must echo request Id for correlation.");
    }

    // -------------------------------------------------------------------------
    // In-order delivery under concurrent sends
    // -------------------------------------------------------------------------

    [Test]
    public async Task InOrderDelivery_ConcurrentSends_AllFramesArriveExactlyOnce()
    {
        // N senders each queue a frame concurrently. The internal write-lock must serialise
        // them so no frame bytes interleave. We verify every Id arrives exactly once.
        const int count = 20;
        await using var pipes = new InProcessPipePair();
        var senderTransport = new FramedJsonTransport(pipes.ServerStream);
        var receiverTransport = new FramedJsonTransport(pipes.ClientStream);

        var sendTasks = new Task[count];
        for (int i = 0; i < count; i++)
        {
            int id = i;
            sendTasks[i] = senderTransport.SendAsync(
                new PipeRequest { Id = id, Method = "M" + id },
                CancellationToken.None);
        }

        await Task.WhenAll(sendTasks);

        // Complete the writer side so ReceiveAsync returns null after the last frame.
        await pipes.DisposeAsync();

        var receivedIds = new List<int>();
        while (true)
        {
            var msg = await receiverTransport.ReceiveAsync<PipeRequest>(CancellationToken.None);
            if (msg is null)
            {
                break;
            }

            receivedIds.Add(msg.Id);
        }

        Assert.That(receivedIds, Has.Count.EqualTo(count), "All frames must arrive exactly once.");
        for (int i = 0; i < count; i++)
        {
            Assert.That(receivedIds, Does.Contain(i), $"Frame with Id={i} is missing.");
        }
    }

    // -------------------------------------------------------------------------
    // Malformed frames
    // -------------------------------------------------------------------------

    [Test]
    public void ReceiveAsync_MalformedJson_ThrowsInvalidOperation()
    {
        using var ms = new MemoryStream();
        byte[] bad = System.Text.Encoding.UTF8.GetBytes("{{{{not json");
        byte[] len = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(len, bad.Length);
        ms.Write(len, 0, 4);
        ms.Write(bad, 0, bad.Length);
        ms.Seek(0, SeekOrigin.Begin);

        var transport = new FramedJsonTransport(ms);
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await transport.ReceiveAsync<PipeRequest>(CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // Client disconnect mid-request (partial length prefix then EOF)
    // -------------------------------------------------------------------------

    [Test]
    public void ReceiveAsync_ClientDisconnectMidLengthPrefix_ThrowsEndOfStream()
    {
        // Write only 2 of the 4 required length-prefix bytes, then EOF.
        using var ms = new MemoryStream(new byte[] { 0x00, 0x01 });
        var transport = new FramedJsonTransport(ms);

        Assert.ThrowsAsync<EndOfStreamException>(
            async () => await transport.ReceiveAsync<PipeRequest>(CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // Server disconnect mid-response (valid header but truncated body)
    // -------------------------------------------------------------------------

    [Test]
    public void ReceiveAsync_ServerDisconnectMidBody_ThrowsEndOfStream()
    {
        using var ms = new MemoryStream();
        byte[] len = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(len, 50);
        ms.Write(len, 0, 4);
        ms.Write(new byte[10], 0, 10); // only 10 of 50 body bytes
        ms.Seek(0, SeekOrigin.Begin);

        var transport = new FramedJsonTransport(ms);
        Assert.ThrowsAsync<EndOfStreamException>(
            async () => await transport.ReceiveAsync<PipeResponse>(CancellationToken.None));
    }
}
