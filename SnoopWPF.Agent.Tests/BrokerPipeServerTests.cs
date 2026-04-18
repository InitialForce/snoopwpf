namespace SnoopWPF.Agent.Tests;

// Tests for BrokerPipeServer (bd-1a9.22):
//   - Hardened DACL: protected, single ALLOW ACE for current user SID, no inherited ACEs
//   - First client: connects and completes HMAC handshake successfully
//   - Second client: rejected with ALREADY_ATTACHED structured frame
//   - HMAC rejection: bad proof is correctly rejected

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Protocol;
using SnoopWPF.Agent.Server;

/// <summary>
/// Tests for <see cref="BrokerPipeServer"/> (bd-1a9.22):
/// hardened DACL, ALREADY_ATTACHED enforcement, and HMAC rejection.
/// </summary>
[TestFixture]
public sealed class BrokerPipeServerTests
{
    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // -------------------------------------------------------------------------
    // DACL test: protected single ALLOW ACE for current user SID
    // -------------------------------------------------------------------------

    /// <summary>
    /// The pipe DACL produced by <see cref="BrokerPipeServer.CreateHardenedPipeSecurity"/>
    /// must be protected (no inherited ACEs) and contain exactly one ALLOW ACE for the
    /// current user SID with FullControl.
    /// </summary>
    [Test]
    public void CreateHardenedPipeSecurity_HasProtectedSingleAllowAceForCurrentUser()
    {
        var ps = BrokerPipeServer.CreateHardenedPipeSecurity();

        // Protected: inherited ACEs are blocked.
        Assert.That(ps.AreAccessRulesProtected, Is.True,
            "PipeSecurity must have AreAccessRulesProtected = true (SetAccessRuleProtection called).");

        // Exactly one access rule.
        var rules = ps.GetAccessRules(includeExplicit: true, includeInherited: false, targetType: typeof(SecurityIdentifier));
        Assert.That(rules.Count, Is.EqualTo(1),
            "PipeSecurity must have exactly one explicit access rule.");

        var rule = (PipeAccessRule)rules[0]!;
        Assert.That(rule.AccessControlType, Is.EqualTo(AccessControlType.Allow),
            "The single ACE must be an ALLOW rule.");
        Assert.That(rule.PipeAccessRights, Is.EqualTo(PipeAccessRights.FullControl),
            "The ALLOW ACE must grant FullControl.");

        // The identity must match the current user SID.
        var currentUserSid = WindowsIdentity.GetCurrent().User!;
        var ruleSid = (SecurityIdentifier)rule.IdentityReference;
        Assert.That(ruleSid.Value, Is.EqualTo(currentUserSid.Value),
            "The ALLOW ACE must target the current user SID.");
    }

    /// <summary>
    /// A pipe created via <see cref="BrokerPipeServer.CreateHardenedPipe"/> inherits the
    /// hardened DACL: <c>AreAccessRulesProtected == true</c>, single ALLOW ACE for current user.
    /// </summary>
    [Test]
    public void CreateHardenedPipe_Dacl_HasProtectedSingleAce()
    {
        string pipeName = "test-dacl-" + Guid.NewGuid().ToString("N")[..8];

        using var pipe = BrokerPipeServer.CreateHardenedPipe(pipeName);

        var ps = pipe.GetAccessControl();

        Assert.That(ps.AreAccessRulesProtected, Is.True,
            "Pipe ACL must be protected (no inherited ACEs).");

        var rules = ps.GetAccessRules(includeExplicit: true, includeInherited: false, targetType: typeof(SecurityIdentifier));
        Assert.That(rules.Count, Is.EqualTo(1),
            "Pipe ACL must have exactly one explicit rule.");

        var rule = (PipeAccessRule)rules[0]!;
        Assert.That(rule.AccessControlType, Is.EqualTo(AccessControlType.Allow));

        var currentUserSid = WindowsIdentity.GetCurrent().User!;
        var ruleSid = (SecurityIdentifier)rule.IdentityReference;
        Assert.That(ruleSid.Value, Is.EqualTo(currentUserSid.Value),
            "The single ALLOW ACE must target the current user SID.");
    }

    // -------------------------------------------------------------------------
    // Happy-path: first client connects and HMAC completes
    // -------------------------------------------------------------------------

    /// <summary>
    /// The first broker to connect and supply a valid HMAC proof is accepted.
    /// <see cref="BrokerPipeServer.AcceptAuthenticatedClientAsync"/> returns a non-null
    /// <see cref="NamedPipeServerStream"/>.
    /// </summary>
    [Test]
    [CancelAfter(12000)]
    public async Task BrokerPipeServer_AcceptsFirstClient()
    {
        string pipeName = "test-broker-first-" + Guid.NewGuid().ToString("N")[..8];
        string sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new BrokerPipeServer(pipeName, sessionToken);

        // Start server accept on background thread.
        var serverTask = Task.Run(
            () => server.AcceptAuthenticatedClientAsync(cts.Token), cts.Token);

        // Give listener time to reach WaitForConnectionAsync.
        await Task.Delay(80).ConfigureAwait(false);

        // Client: connect and perform a valid HMAC handshake.
        bool clientOk;
        await using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(cts.Token).ConfigureAwait(false);
            clientOk = await ClientHandshakeAsync(client, sessionToken, cts.Token).ConfigureAwait(false);
        }

        Assert.That(clientOk, Is.True, "Client-side handshake must succeed.");

        using var authenticatedPipe = await serverTask.ConfigureAwait(false);
        Assert.That(authenticatedPipe, Is.Not.Null,
            "AcceptAuthenticatedClientAsync must return a non-null pipe stream after a valid handshake.");
    }

    // -------------------------------------------------------------------------
    // ALREADY_ATTACHED: second client receives rejection frame
    // -------------------------------------------------------------------------

    /// <summary>
    /// After the first client is authenticated, a second connection attempt receives an
    /// <c>ALREADY_ATTACHED</c> structured error frame (type + message).
    /// </summary>
    [Test]
    [CancelAfter(15000)]
    public async Task BrokerPipeServer_RejectsSecondClient_WithAlreadyAttachedFrame()
    {
        string pipeName = "test-broker-second-" + Guid.NewGuid().ToString("N")[..8];
        string sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        using var sessionCts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var server = new BrokerPipeServer(pipeName, sessionToken);

        var serverTask = Task.Run(
            () => server.AcceptAuthenticatedClientAsync(sessionCts.Token), sessionCts.Token);

        // Give listener time to reach WaitForConnectionAsync.
        await Task.Delay(80).ConfigureAwait(false);

        // First client: connect with valid HMAC.
        await using (var first = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await first.ConnectAsync(sessionCts.Token).ConfigureAwait(false);
            bool firstOk = await ClientHandshakeAsync(first, sessionToken, sessionCts.Token).ConfigureAwait(false);
            Assert.That(firstOk, Is.True, "First client handshake must succeed.");
        }

        // Wait for server task and ALREADY_ATTACHED guard to start.
        using var authenticatedPipe = await serverTask.ConfigureAwait(false);
        Assert.That(authenticatedPipe, Is.Not.Null, "Server must have accepted first client.");

        // Give guard loop time to reach its WaitForConnectionAsync.
        await Task.Delay(150).ConfigureAwait(false);

        // Second client: connect and attempt to read the rejection frame.
        BrokerErrorFrame? rejectionFrame = null;
        try
        {
            await using var second = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await second.ConnectAsync(sessionCts.Token).ConfigureAwait(false);
            rejectionFrame = await ReadFramedJsonAsync<BrokerErrorFrame>(second, sessionCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Assert.Fail($"Second client connection/read should not throw: {ex.GetType().Name}: {ex.Message}");
        }

        Assert.That(rejectionFrame, Is.Not.Null,
            "Second client must receive a structured rejection frame.");
        Assert.That(rejectionFrame!.Type, Is.EqualTo("ALREADY_ATTACHED"),
            "Rejection frame Type must be 'ALREADY_ATTACHED'.");
        Assert.That(rejectionFrame.Message, Is.Not.Null.And.Not.Empty,
            "Rejection frame must include a human-readable message.");

        // Cancel session so the guard loop exits cleanly.
        sessionCts.Cancel();
    }

    // -------------------------------------------------------------------------
    // Bad HMAC: handshake fails cleanly
    // -------------------------------------------------------------------------

    /// <summary>
    /// A client that sends an incorrect HMAC proof is rejected.
    /// <see cref="BrokerPipeServer.AcceptAuthenticatedClientAsync"/> returns <see langword="null"/>
    /// and does not throw.
    /// </summary>
    [Test]
    [CancelAfter(12000)]
    public async Task BrokerPipeServer_BadHmac_HandshakeFails_ReturnsNull()
    {
        string pipeName = "test-broker-badhmac-" + Guid.NewGuid().ToString("N")[..8];
        string serverToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string wrongToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new BrokerPipeServer(pipeName, serverToken);

        var serverTask = Task.Run(
            () => server.AcceptAuthenticatedClientAsync(cts.Token), cts.Token);

        await Task.Delay(80).ConfigureAwait(false);

        // Client sends wrong HMAC.
        await using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(cts.Token).ConfigureAwait(false);
            // Deliberately use wrong token.
            await ClientHandshakeAsync(client, wrongToken, cts.Token).ConfigureAwait(false);
        }

        using var result = await serverTask.ConfigureAwait(false);
        Assert.That(result, Is.Null,
            "AcceptAuthenticatedClientAsync must return null when HMAC proof is wrong.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Client-side handshake: reads nonce challenge, computes HMAC, sends response.
    /// Returns true on success, false on error or EOF.
    /// </summary>
    private static async Task<bool> ClientHandshakeAsync(
        Stream clientStream,
        string sessionToken,
        CancellationToken ct)
    {
        try
        {
            var challenge = await ReadFramedJsonAsync<HandshakeChallenge>(clientStream, ct)
                .ConfigureAwait(false);

            if (challenge?.Nonce is null || challenge.Nonce.Length != 16)
            {
                return false;
            }

            byte[] keyBytes = Encoding.UTF8.GetBytes(sessionToken);
            byte[] proof = HMACSHA256.HashData(keyBytes, challenge.Nonce);

            var response = new HandshakeResponse
            {
                ProtocolVersion = challenge.ProtocolVersion,
                AgentVersion = "test-1.0",
                TargetRuntime = "net8.0",
                ProofHmac = proof,
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
        byte[] lenBuf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBuf, body.Length);
        await s.WriteAsync(lenBuf, 0, 4, ct).ConfigureAwait(false);
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

        int frameLen = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
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
