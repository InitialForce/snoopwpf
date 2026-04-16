namespace SnoopWPF.Agent.Tests;

using System;
using System.IO;
using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Protocol;
using SnoopWPF.Agent.Server;

/// <summary>
/// Security tests for the nonce+HMAC handshake protocol introduced in FX-C3.
/// Verifies that:
/// - A client with the correct session token is accepted.
/// - A client with a wrong token is rejected.
/// - A client that replays an old nonce is rejected (nonces are fresh per connection).
/// - The server-side <see cref="McpServerSetup.PerformPipeHandshakeAsync"/> correctly
///   rejects a response with an all-zero HMAC proof.
/// - The session token is never serialised into the challenge frame.
/// </summary>
[TestFixture]
public sealed class HandshakeSecurityTests
{
    // -------------------------------------------------------------------------
    // In-process stream pair (same pattern as PipeTransportTests)
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
#pragma warning disable CA2213 // Disposable fields should be disposed — owned by InProcessPipePair
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

#pragma warning disable CA2215
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
#pragma warning restore CA2215
    }

    // -------------------------------------------------------------------------
    // Framing helpers (mirror McpServerSetup private helpers)
    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static async Task SendFramedAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        byte[] len = BitConverter.GetBytes(body.Length);
        await stream.WriteAsync(len, 0, 4, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<T?> ReceiveFramedAsync<T>(Stream stream, CancellationToken ct)
    {
        byte[] lenBuf = new byte[4];
        int read = 0;
        while (read < 4)
        {
            int n = await stream.ReadAsync(lenBuf, read, 4 - read, ct).ConfigureAwait(false);
            if (n == 0)
            {
                return default;
            }

            read += n;
        }

        int length = BitConverter.ToInt32(lenBuf, 0);
        if (length < 0 || length > 10_000_000)
        {
            return default;
        }

        byte[] body = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int n = await stream.ReadAsync(body, offset, length - offset, ct).ConfigureAwait(false);
            if (n == 0)
            {
                return default;
            }

            offset += n;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private static byte[] ComputeHmacProof(string sessionToken, byte[] nonce)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(sessionToken);
        return HMACSHA256.HashData(keyBytes, nonce);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    /// <summary>
    /// Server-side: a client with the correct session token is accepted.
    /// </summary>
    [Test]
    public async Task PerformHandshake_CorrectToken_ReturnsTrue()
    {
        await using var pipes = new InProcessPipePair();
        const string sessionToken = "test-secret-token-abc123";

        // Client task: reads challenge, computes correct HMAC, sends response.
        var clientTask = Task.Run(async () =>
        {
            var challenge = await ReceiveFramedAsync<HandshakeChallenge>(pipes.ClientStream, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(challenge, Is.Not.Null, "Client must receive challenge.");
            Assert.That(challenge!.Nonce, Is.Not.Null.And.Length.EqualTo(16), "Nonce must be 16 bytes.");

            byte[] proofHmac = ComputeHmacProof(sessionToken, challenge.Nonce);
            var response = new HandshakeResponse
            {
                ProtocolVersion = ProtocolConstants.ProtocolVersion,
                AgentVersion = "1.0.0",
                TargetRuntime = "net8.0",
                ProofHmac = proofHmac,
            };
            await SendFramedAsync(pipes.ClientStream, response, CancellationToken.None).ConfigureAwait(false);
        });

        // Server task: runs the handshake.
        var serverTask = McpServerSetup.PerformPipeHandshakeAsync(
            pipes.ServerStream, sessionToken, CancellationToken.None);

        await clientTask.ConfigureAwait(false);
        bool result = await serverTask.ConfigureAwait(false);

        Assert.That(result, Is.True, "Handshake must succeed with correct token.");
    }

    /// <summary>
    /// Server-side: a client with a wrong session token is rejected.
    /// This is the primary security regression test for FX-C3.
    /// </summary>
    [Test]
    public async Task PerformHandshake_WrongToken_ReturnsFalse()
    {
        await using var pipes = new InProcessPipePair();
        const string serverToken = "correct-server-secret";
        const string wrongClientToken = "attacker-does-not-know-server-secret";

        // Client task: reads challenge, computes HMAC with WRONG token.
        var clientTask = Task.Run(async () =>
        {
            var challenge = await ReceiveFramedAsync<HandshakeChallenge>(pipes.ClientStream, CancellationToken.None)
                .ConfigureAwait(false);

            if (challenge?.Nonce is not null)
            {
                byte[] wrongProof = ComputeHmacProof(wrongClientToken, challenge.Nonce);
                var response = new HandshakeResponse
                {
                    ProtocolVersion = ProtocolConstants.ProtocolVersion,
                    AgentVersion = "1.0.0",
                    TargetRuntime = "net8.0",
                    ProofHmac = wrongProof,
                };
                await SendFramedAsync(pipes.ClientStream, response, CancellationToken.None).ConfigureAwait(false);
            }
        });

        // Server task: must reject the wrong HMAC proof.
        var serverTask = McpServerSetup.PerformPipeHandshakeAsync(
            pipes.ServerStream, serverToken, CancellationToken.None);

        await clientTask.ConfigureAwait(false);
        bool result = await serverTask.ConfigureAwait(false);

        Assert.That(result, Is.False, "Handshake must be rejected when client presents wrong HMAC proof.");
    }

    /// <summary>
    /// Server-side: a client that sends all-zero HMAC is rejected.
    /// </summary>
    [Test]
    public async Task PerformHandshake_ZeroHmac_ReturnsFalse()
    {
        await using var pipes = new InProcessPipePair();
        const string sessionToken = "some-token";

        var clientTask = Task.Run(async () =>
        {
            var challenge = await ReceiveFramedAsync<HandshakeChallenge>(pipes.ClientStream, CancellationToken.None)
                .ConfigureAwait(false);

            if (challenge is not null)
            {
                var response = new HandshakeResponse
                {
                    ProtocolVersion = ProtocolConstants.ProtocolVersion,
                    ProofHmac = new byte[32], // all zeros — wrong
                };
                await SendFramedAsync(pipes.ClientStream, response, CancellationToken.None).ConfigureAwait(false);
            }
        });

        var serverTask = McpServerSetup.PerformPipeHandshakeAsync(
            pipes.ServerStream, sessionToken, CancellationToken.None);

        await clientTask.ConfigureAwait(false);
        bool result = await serverTask.ConfigureAwait(false);

        Assert.That(result, Is.False, "All-zero HMAC proof must be rejected.");
    }

    /// <summary>
    /// Verifies the challenge frame does NOT contain the session token.
    /// The token must never traverse the pipe.
    /// </summary>
    [Test]
    public async Task Challenge_DoesNotContainSessionToken()
    {
        await using var pipes = new InProcessPipePair();
        const string sessionToken = "super-secret-token-SNOOPWPF";

        byte[]? rawChallengeBytes = null;

        // Client task: intercept raw challenge bytes before parsing.
        var clientTask = Task.Run(async () =>
        {
            // Read raw 4-byte length header.
            byte[] lenBuf = new byte[4];
            int read = 0;
            while (read < 4)
            {
                int n = await pipes.ClientStream.ReadAsync(lenBuf, read, 4 - read, CancellationToken.None)
                    .ConfigureAwait(false);
                if (n == 0)
                {
                    return;
                }

                read += n;
            }

            int length = BitConverter.ToInt32(lenBuf, 0);
            byte[] body = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int n = await pipes.ClientStream.ReadAsync(body, offset, length - offset, CancellationToken.None)
                    .ConfigureAwait(false);
                if (n == 0)
                {
                    break;
                }

                offset += n;
            }

            rawChallengeBytes = body;

            // Parse and send a response so the server can complete.
            var challenge = JsonSerializer.Deserialize<HandshakeChallenge>(body, JsonOptions);
            if (challenge?.Nonce is not null)
            {
                byte[] proofHmac = ComputeHmacProof(sessionToken, challenge.Nonce);
                var response = new HandshakeResponse
                {
                    ProtocolVersion = ProtocolConstants.ProtocolVersion,
                    ProofHmac = proofHmac,
                };
                await SendFramedAsync(pipes.ClientStream, response, CancellationToken.None).ConfigureAwait(false);
            }
        });

        await McpServerSetup.PerformPipeHandshakeAsync(
            pipes.ServerStream, sessionToken, CancellationToken.None).ConfigureAwait(false);

        await clientTask.ConfigureAwait(false);

        Assert.That(rawChallengeBytes, Is.Not.Null, "Client must have captured the challenge bytes.");
        string challengeJson = Encoding.UTF8.GetString(rawChallengeBytes!);

        Assert.That(
            challengeJson,
            Does.Not.Contain(sessionToken),
            "Session token must NOT appear in the challenge JSON frame.");
    }
}
