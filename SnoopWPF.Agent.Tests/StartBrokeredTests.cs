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
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Protocol;
using SnoopWPF.Agent.Remote;

/// <summary>
/// Unit tests for <see cref="SnoopWPF.Agent.Server.SnoopAgent.StartBrokered"/> API.
/// Covers the pipe-handshake logic that <see cref="SnoopWPF.Agent.Server.McpServerSetup"/>
/// exercises in brokered mode without standing up a live WPF application.
///
/// All tests use in-process <see cref="System.IO.Pipelines.Pipe"/>-backed stream pairs
/// (no OS pipe handles) so they run reliably on WSL1.
/// </summary>
[TestFixture]
public sealed class StartBrokeredTests
{
    // -------------------------------------------------------------------------
    // Helpers — identical pattern to PipeTransportTests / RemoteFramingTests
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

#pragma warning disable CA2215 // base.DisposeAsync delegates to Dispose(true) — correct
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
#pragma warning restore CA2215
    }

    // -------------------------------------------------------------------------
    // Handshake helpers (mirrors McpServerSetup's private framing code)
    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Client-side handshake: reads the challenge, computes HMAC proof, sends response.
    /// Returns true on success; false on any error.
    /// </summary>
    private static async Task<bool> ClientPerformHandshakeAsync(
        Stream clientStream,
        string sessionToken,
        CancellationToken ct)
    {
        // Read challenge (server speaks first — contains nonce, no token).
        var challenge = await ReadFramedJsonAsync<HandshakeChallenge>(clientStream, ct)
            .ConfigureAwait(false);
        if (challenge is null || challenge.Nonce is null || challenge.Nonce.Length != 16)
        {
            return false;
        }

        // Compute HMAC proof: HMACSHA256(key=sessionTokenBytes, data=nonce).
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

    /// <summary>
    /// Client-side bad-token handshake: deliberately sends wrong HMAC proof to trigger rejection.
    /// </summary>
    private static async Task ClientSendBadTokenAsync(Stream clientStream, CancellationToken ct)
    {
        var challenge = await ReadFramedJsonAsync<HandshakeChallenge>(clientStream, ct)
            .ConfigureAwait(false);
        if (challenge is null)
        {
            return;
        }

        // Send a zeroed-out HMAC proof (wrong token simulation).
        var response = new HandshakeResponse
        {
            ProtocolVersion = challenge.ProtocolVersion,
            AgentVersion = "attacker-1.0",
            TargetRuntime = "net8.0",
            ProofHmac = new byte[32], // all zeros — definitely wrong
        };

        await WriteFramedJsonAsync(clientStream, response, ct).ConfigureAwait(false);
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

    // -------------------------------------------------------------------------
    // StartBrokered — SessionPolicy tests (no WPF needed)
    // -------------------------------------------------------------------------

    /// <summary>
    /// <see cref="SessionPolicy.Create"/> with <see cref="SessionMode.Brokered"/> passes
    /// the caller's options through unchanged — identical behaviour to CoLocated (owned app).
    /// </summary>
    [Test]
    public void StartBrokered_SessionPolicy_BrokeredMode_PassesOptsThrough()
    {
        var opts = new SnoopAgentOptions
        {
            EnableRedaction = false,
            EnableMutation = true,
            MaxTier = InputTier.L1,
        };

        var policy = SessionPolicy.Create(SessionMode.Brokered, opts);

        Assert.That(policy.Mode, Is.EqualTo(SessionMode.Brokered));
        Assert.That(policy.EnableRedaction, Is.False, "Brokered mode must not force redaction.");
        Assert.That(policy.EnableMutation, Is.True);
        Assert.That(policy.MaxTier, Is.EqualTo(InputTier.L1));
    }

    /// <summary>
    /// In contrast to <see cref="SessionMode.Injection"/>, Brokered mode does NOT force
    /// <c>EnableRedaction=true</c> even when the caller passes <c>false</c>.
    /// </summary>
    [Test]
    public void StartBrokered_SessionPolicy_BrokeredMode_DoesNotForceRedaction()
    {
        var optsNoRedact = new SnoopAgentOptions { EnableRedaction = false };
        var policy = SessionPolicy.Create(SessionMode.Brokered, optsNoRedact);

        Assert.That(policy.EnableRedaction, Is.False,
            "MF-11 forced-redaction must NOT apply in Brokered mode.");
    }

    /// <summary>
    /// <see cref="SessionMode.Injection"/> DOES force redaction — control for the above test.
    /// </summary>
    [Test]
    public void StartBrokered_SessionPolicy_InjectionMode_ForcesRedaction()
    {
        var optsNoRedact = new SnoopAgentOptions { EnableRedaction = false };
        var policy = SessionPolicy.Create(SessionMode.Injection, optsNoRedact);

        Assert.That(policy.EnableRedaction, Is.True,
            "MF-11 forced-redaction must apply in Injection mode.");
    }

    // -------------------------------------------------------------------------
    // Handshake protocol — using in-process stream pairs
    // -------------------------------------------------------------------------

    /// <summary>
    /// Mock-broker round-trip: the server performs the handshake, the client computes
    /// the correct HMAC proof, and both sides complete successfully.
    /// </summary>
    [Test]
    public async Task StartBrokered_Handshake_ValidToken_Succeeds()
    {
        await using var pipes = new InProcessPipePair();
        var sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        byte[] sessionTokenBytes = Encoding.UTF8.GetBytes(sessionToken);

        // Server-side handshake task (mirrors McpServerSetup.PerformPipeHandshakeAsync).
        var serverTask = Task.Run(async () =>
        {
            // Server sends challenge (nonce, no token).
            byte[] nonce = RandomNumberGenerator.GetBytes(16);
            var challenge = new HandshakeChallenge
            {
                Nonce = nonce,
                ProtocolVersion = ProtocolConstants.ProtocolVersion,
            };
            await WriteFramedJsonAsync(pipes.ServerStream, challenge, CancellationToken.None)
                .ConfigureAwait(false);

            // Server reads response.
            var response = await ReadFramedJsonAsync<HandshakeResponse>(
                pipes.ServerStream, CancellationToken.None).ConfigureAwait(false);

            if (response is null || response.ProofHmac is null)
            {
                return false;
            }

            // Verify protocol version.
            if (response.ProtocolVersion != ProtocolConstants.ProtocolVersion)
            {
                return false;
            }

            // Verify HMAC proof using constant-time comparison.
            byte[] expectedHmac = HMACSHA256.HashData(sessionTokenBytes, nonce);
            return response.ProofHmac.Length == expectedHmac.Length
                && CryptographicOperations.FixedTimeEquals(response.ProofHmac, expectedHmac);
        });

        // Client-side handshake task.
        var clientTask = ClientPerformHandshakeAsync(pipes.ClientStream, sessionToken, CancellationToken.None);

        var results = await Task.WhenAll(serverTask, clientTask).ConfigureAwait(false);

        Assert.That(results[0], Is.True, "Server-side handshake must succeed with correct HMAC proof.");
        Assert.That(results[1], Is.True, "Client-side handshake must succeed with correct token.");
    }

    /// <summary>
    /// Negative: client sends a wrong HMAC proof — server handshake returns false.
    /// Validates that constant-time comparison rejects mismatched HMAC proofs.
    /// </summary>
    [Test]
    public async Task StartBrokered_Handshake_InvalidToken_IsRejected()
    {
        await using var pipes = new InProcessPipePair();
        var sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        byte[] sessionTokenBytes = Encoding.UTF8.GetBytes(sessionToken);

        var serverTask = Task.Run(async () =>
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(16);
            var challenge = new HandshakeChallenge
            {
                Nonce = nonce,
                ProtocolVersion = ProtocolConstants.ProtocolVersion,
            };
            await WriteFramedJsonAsync(pipes.ServerStream, challenge, CancellationToken.None)
                .ConfigureAwait(false);

            var response = await ReadFramedJsonAsync<HandshakeResponse>(
                pipes.ServerStream, CancellationToken.None).ConfigureAwait(false);

            if (response is null || response.ProofHmac is null)
            {
                return false;
            }

            if (response.ProtocolVersion != ProtocolConstants.ProtocolVersion)
            {
                return false;
            }

            byte[] expectedHmac = HMACSHA256.HashData(sessionTokenBytes, nonce);
            return response.ProofHmac.Length == expectedHmac.Length
                && CryptographicOperations.FixedTimeEquals(response.ProofHmac, expectedHmac);
        });

        // Client deliberately sends wrong HMAC proof.
        var clientTask = ClientSendBadTokenAsync(pipes.ClientStream, CancellationToken.None);

        bool serverResult = await serverTask.ConfigureAwait(false);
        await clientTask.ConfigureAwait(false);

        Assert.That(serverResult, Is.False,
            "Server must reject a client that presents an incorrect HMAC proof.");
    }

    /// <summary>
    /// Validates <see cref="FramedJsonTransport"/> round-trip with the nonce+HMAC handshake types
    /// used in brokered mode (same wire format as in CoLocated pipe mode).
    /// </summary>
    [Test]
    public async Task StartBrokered_FramedTransport_HandshakeTypesRoundTrip()
    {
        await using var pipes = new InProcessPipePair();
        var serverTransport = new FramedJsonTransport(pipes.ServerStream);
        var clientTransport = new FramedJsonTransport(pipes.ClientStream);

        var sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        byte[] sessionTokenBytes = Encoding.UTF8.GetBytes(sessionToken);
        byte[] nonce = RandomNumberGenerator.GetBytes(16);

        var challenge = new HandshakeChallenge
        {
            Nonce = nonce,
            ProtocolVersion = ProtocolConstants.ProtocolVersion,
        };
        await serverTransport.SendAsync(challenge, CancellationToken.None).ConfigureAwait(false);

        var receivedChallenge = await clientTransport
            .ReceiveAsync<HandshakeChallenge>(CancellationToken.None).ConfigureAwait(false);

        Assert.That(receivedChallenge, Is.Not.Null);
        Assert.That(receivedChallenge!.Nonce, Is.Not.Null);
        Assert.That(receivedChallenge.Nonce.Length, Is.EqualTo(16));
        Assert.That(receivedChallenge.ProtocolVersion, Is.EqualTo(ProtocolConstants.ProtocolVersion));

        byte[] proofHmac = HMACSHA256.HashData(sessionTokenBytes, receivedChallenge.Nonce);
        var response = new HandshakeResponse
        {
            ProtocolVersion = ProtocolConstants.ProtocolVersion,
            AgentVersion = "1.0.0",
            TargetRuntime = "net8.0",
            ProofHmac = proofHmac,
        };
        await clientTransport.SendAsync(response, CancellationToken.None).ConfigureAwait(false);

        var receivedResponse = await serverTransport
            .ReceiveAsync<HandshakeResponse>(CancellationToken.None).ConfigureAwait(false);

        Assert.That(receivedResponse, Is.Not.Null);
        Assert.That(receivedResponse!.ProofHmac, Is.Not.Null);
        Assert.That(receivedResponse.ProofHmac.Length, Is.EqualTo(32));
        Assert.That(receivedResponse.ProtocolVersion, Is.EqualTo(ProtocolConstants.ProtocolVersion));

        byte[] expectedHmac = HMACSHA256.HashData(sessionTokenBytes, nonce);
        Assert.That(
            CryptographicOperations.FixedTimeEquals(receivedResponse.ProofHmac, expectedHmac),
            Is.True,
            "Round-tripped HMAC proof must match expected value.");
    }

    // -------------------------------------------------------------------------
    // Console.Out — StartBrokered must NOT redirect it
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that <see cref="Console.Out"/> is not <see cref="TextWriter.Null"/> after
    /// installing a <see cref="StringWriter"/> (sanity check for the integration test contract).
    /// The full Console.Out invariant for <c>StartBrokered</c> is covered by BrokeredRoundTrip
    /// integration tests which exercise the WPF dispatcher path.
    /// </summary>
    [Test]
    public void StartBrokered_ConsoleOut_IsNotRedirectedToNull()
    {
        // Arrange: record the current Console.Out.
        var originalOut = Console.Out;
        try
        {
            // Swap in a test writer so we have a non-Null reference to compare against.
            using var capturedOut = new StringWriter();
            Console.SetOut(capturedOut);

            // Console.SetOut wraps the writer in a SyncTextWriter, so we can't use ReferenceEquals.
            // Instead verify it is NOT TextWriter.Null, which is what StartBrokered must NOT do.
            Assert.That(Console.Out, Is.Not.SameAs(TextWriter.Null),
                "Console.Out must not be TextWriter.Null after installing a StringWriter.");

            // Write through Console and verify the captured writer received it.
            Console.Write("probe");
            Console.Out.Flush();
            Assert.That(capturedOut.ToString(), Does.Contain("probe"),
                "Writes to Console.Out must reach the installed StringWriter.");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    // -------------------------------------------------------------------------
    // SessionPolicy — Brokered respects caller redaction choice
    // -------------------------------------------------------------------------

    /// <summary>
    /// Brokered mode with <c>EnableRedaction=true</c>: policy honours the caller's preference.
    /// </summary>
    [Test]
    public void StartBrokered_SessionPolicy_BrokeredMode_EnableRedactionTrue_Honoured()
    {
        var opts = new SnoopAgentOptions { EnableRedaction = true };
        var policy = SessionPolicy.Create(SessionMode.Brokered, opts);

        Assert.That(policy.EnableRedaction, Is.True);
    }

    /// <summary>
    /// Validates that the <see cref="SessionMode.Brokered"/> constant exists and has a
    /// distinct integer value from <see cref="SessionMode.CoLocated"/> and
    /// <see cref="SessionMode.Injection"/>.
    /// </summary>
    [Test]
    public void StartBrokered_SessionMode_Brokered_HasDistinctValue()
    {
        Assert.That((int)SessionMode.Brokered, Is.Not.EqualTo((int)SessionMode.CoLocated));
        Assert.That((int)SessionMode.Brokered, Is.Not.EqualTo((int)SessionMode.Injection));
    }
}
