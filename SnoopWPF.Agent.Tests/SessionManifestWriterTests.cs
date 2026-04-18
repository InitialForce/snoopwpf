// SnoopWPF.Agent.Tests/SessionManifestWriterTests.cs
// Unit tests for SessionManifestWriter + SessionManifest DTO (bd-1a9.24).
// All tests run without a WPF dispatcher and without launching a real process.

namespace SnoopWPF.Agent.Tests;

using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SnoopWPF.Agent.Server;

[TestFixture]
public sealed class SessionManifestWriterTests
{
    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string UniquePipe() => "test-pipe-" + Guid.NewGuid().ToString("N")[..8];

    private static string UniqueToken() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    // -------------------------------------------------------------------------
    // SessionManifest_Schema_RoundTrip
    // -------------------------------------------------------------------------

    /// <summary>
    /// Populate all fields, JSON round-trip; assert camelCase field names and values preserved.
    /// </summary>
    [Test]
    public void SessionManifest_Schema_RoundTrip()
    {
        var now = DateTimeOffset.UtcNow;
        var manifest = new SessionManifest(
            SchemaVersion: 1,
            SessionId: "test-session-id",
            Pid: 42,
            StartTimeTicks: 123456789L,
            ImagePath: @"C:\app\test.exe",
            ImageHash: "deadbeef",
            RuntimeFamily: "CoreCLR",
            RuntimeVersion: "8.0.5",
            Bitness: 64,
            IntegrityLevel: "Medium",
            PipeName: "test-pipe",
            TokenB64: "dGVzdA==",
            LeaseId: "lease-abc",
            IssuedAt: now,
            ExpiresAt: now.AddHours(24));

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOpts);
        string jsonStr = System.Text.Encoding.UTF8.GetString(json);

        // Verify camelCase field names.
        Assert.That(jsonStr, Does.Contain("\"schemaVersion\""), "schemaVersion must be camelCase");
        Assert.That(jsonStr, Does.Contain("\"sessionId\""), "sessionId must be camelCase");
        Assert.That(jsonStr, Does.Contain("\"startTimeTicks\""), "startTimeTicks must be camelCase");
        Assert.That(jsonStr, Does.Contain("\"imagePath\""), "imagePath must be camelCase");
        Assert.That(jsonStr, Does.Contain("\"imageHash\""), "imageHash must be camelCase");
        Assert.That(jsonStr, Does.Contain("\"runtimeFamily\""), "runtimeFamily must be camelCase");
        Assert.That(jsonStr, Does.Contain("\"runtimeVersion\""), "runtimeVersion must be camelCase");
        Assert.That(jsonStr, Does.Contain("\"integrityLevel\""), "integrityLevel must be camelCase");
        Assert.That(jsonStr, Does.Contain("\"pipeName\""), "pipeName must be camelCase");
        Assert.That(jsonStr, Does.Contain("\"tokenB64\""), "tokenB64 must be camelCase");
        Assert.That(jsonStr, Does.Contain("\"leaseId\""), "leaseId must be camelCase");
        Assert.That(jsonStr, Does.Contain("\"issuedAt\""), "issuedAt must be camelCase");
        Assert.That(jsonStr, Does.Contain("\"expiresAt\""), "expiresAt must be camelCase");

        // Round-trip: deserialize back and verify values.
        var deserialized = JsonSerializer.Deserialize<SessionManifest>(json, JsonOpts);
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.SchemaVersion, Is.EqualTo(1));
        Assert.That(deserialized.SessionId, Is.EqualTo("test-session-id"));
        Assert.That(deserialized.Pid, Is.EqualTo(42));
        Assert.That(deserialized.StartTimeTicks, Is.EqualTo(123456789L));
        Assert.That(deserialized.ImagePath, Is.EqualTo(@"C:\app\test.exe"));
        Assert.That(deserialized.ImageHash, Is.EqualTo("deadbeef"));
        Assert.That(deserialized.RuntimeFamily, Is.EqualTo("CoreCLR"));
        Assert.That(deserialized.RuntimeVersion, Is.EqualTo("8.0.5"));
        Assert.That(deserialized.IntegrityLevel, Is.EqualTo("Medium"));
        Assert.That(deserialized.PipeName, Is.EqualTo("test-pipe"));
        Assert.That(deserialized.TokenB64, Is.EqualTo("dGVzdA=="));
        Assert.That(deserialized.LeaseId, Is.EqualTo("lease-abc"));
    }

    // -------------------------------------------------------------------------
    // SessionManifest_StartTimeTicks_UsesUtc
    // -------------------------------------------------------------------------

    /// <summary>
    /// The startTimeTicks written to the manifest must equal
    /// Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks — same as the
    /// StaleManifestSweeper (bd-1a9.27) compares against.
    /// </summary>
    [Test]
    public void SessionManifest_StartTimeTicks_UsesUtc()
    {
        long expectedTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;

        string pipeName = UniquePipe();
        string token = UniqueToken();

        using var handle = SessionManifestWriter.Write(pipeName, token);
        string json = File.ReadAllText(handle.FilePath);

        var node = JsonNode.Parse(json);
        Assert.That(node, Is.Not.Null);

        long actualTicks = node!["startTimeTicks"]!.GetValue<long>();

        // Should match the fresh call made just before Write().
        // Allow a tiny delta for clock resolution; exact match is expected on the same process.
        Assert.That(actualTicks, Is.EqualTo(expectedTicks),
            "startTimeTicks must use Process.StartTime.ToUniversalTime().Ticks verbatim.");
    }

    // -------------------------------------------------------------------------
    // SessionManifestWriter_LeaseId_UniquenessAcrossRotations
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calling Write() twice with the same pipe/token produces different leaseId values.
    /// </summary>
    [Test]
    public void SessionManifestWriter_LeaseId_UniquenessAcrossRotations()
    {
        string pipeName = UniquePipe();
        string token = UniqueToken();

        using var handle1 = SessionManifestWriter.Write(pipeName, token);
        // Second write: use a different final path by passing a distinct sessionId.
        using var handle2 = SessionManifestWriter.Write(pipeName, token, sessionId: Guid.NewGuid().ToString());

        Assert.That(handle1.LeaseId, Is.Not.EqualTo(handle2.LeaseId),
            "Each Write() call must produce a unique leaseId.");
    }

    // -------------------------------------------------------------------------
    // SessionManifestWriter_WriteOrder_TempFirstThenRename
    // -------------------------------------------------------------------------

    /// <summary>
    /// The temp file is written before the final rename. After a write the temp file
    /// must not remain on disk (it was either renamed or cleaned up).
    /// This test verifies the atomic rename by asserting no .tmp-*.json litter remains.
    /// </summary>
    [Test]
    public void SessionManifestWriter_WriteOrder_TempFirstThenRename()
    {
        string pipeName = UniquePipe();
        string token = UniqueToken();

        using var handle = SessionManifestWriter.Write(pipeName, token);

        // The final file must exist.
        Assert.That(File.Exists(handle.FilePath), Is.True, "Final manifest file must exist after Write.");

        // No temp file should remain.
        var tmpFiles = Directory.GetFiles(SessionManifestWriter.ManifestDirectory, ".tmp-*.json");
        Assert.That(tmpFiles, Is.Empty, "No .tmp-*.json files should remain after a successful Write.");
    }

    // -------------------------------------------------------------------------
    // SessionManifestWriter_Dacl_SingleAllowAceCurrentUser
    // -------------------------------------------------------------------------

    /// <summary>
    /// After Write(), the manifest file must have AreAccessRulesProtected == true
    /// and exactly one ALLOW ACE for the current user SID.
    /// Only runs on Windows (ACL not enforced on other platforms).
    /// </summary>
    [Test]
    [Platform(Include = "Win")]
    public void SessionManifestWriter_Dacl_SingleAllowAceCurrentUser()
    {
        string pipeName = UniquePipe();
        string token = UniqueToken();

        using var handle = SessionManifestWriter.Write(pipeName, token);

        var fi = new FileInfo(handle.FilePath);
        var security = fi.GetAccessControl();

        Assert.That(security.AreAccessRulesProtected, Is.True,
            "File ACL must be protected (no inherited ACEs).");

        var rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: false,
            targetType: typeof(SecurityIdentifier));

        Assert.That(rules.Count, Is.EqualTo(1),
            "Exactly one explicit ACE expected.");

        var rule = (FileSystemAccessRule?)rules[0];
        Assert.That(rule, Is.Not.Null, "Expected a FileSystemAccessRule.");
        Assert.That(rule!.AccessControlType, Is.EqualTo(AccessControlType.Allow),
            "The single ACE must be ALLOW.");

        using var identity = WindowsIdentity.GetCurrent();
        var currentUserSid = identity.User!;

        Assert.That(rule.IdentityReference, Is.EqualTo(currentUserSid),
            "The ALLOW ACE must be for the current user SID.");
    }

    // -------------------------------------------------------------------------
    // ManifestHandle_Dispose_DeletesFile
    // -------------------------------------------------------------------------

    /// <summary>
    /// Disposing the ManifestHandle deletes the manifest file (best-effort).
    /// </summary>
    [Test]
    public void ManifestHandle_Dispose_DeletesFile()
    {
        string pipeName = UniquePipe();
        string token = UniqueToken();

        string filePath;
        using (var handle = SessionManifestWriter.Write(pipeName, token))
        {
            filePath = handle.FilePath;
            Assert.That(File.Exists(filePath), Is.True, "File must exist before dispose.");
        }

        Assert.That(File.Exists(filePath), Is.False, "File must be deleted after ManifestHandle.Dispose().");
    }

    // -------------------------------------------------------------------------
    // Capability detection unit tests
    // -------------------------------------------------------------------------

    [Test]
    public void DetectRuntimeFamily_ReturnsValidValue()
    {
        string family = SessionManifestWriter.DetectRuntimeFamily();
        Assert.That(family, Is.EqualTo("CoreCLR").Or.EqualTo("NetFramework"));
    }

    [Test]
    public void DetectRuntimeVersion_ReturnsNonEmpty()
    {
        string version = SessionManifestWriter.DetectRuntimeVersion();
        Assert.That(version, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void DetectBitness_ReturnsValidValue()
    {
        object bitness = SessionManifestWriter.DetectBitness();
        Assert.That(bitness, Is.EqualTo(32).Or.EqualTo(64).Or.EqualTo("ARM64"),
            "Bitness must be 32, 64, or 'ARM64'.");
    }

    [Test]
    public void DetectIntegrityLevel_ReturnsValidValue()
    {
        string level = SessionManifestWriter.DetectIntegrityLevel();
        Assert.That(level, Is.EqualTo("Low")
            .Or.EqualTo("Medium")
            .Or.EqualTo("High")
            .Or.EqualTo("System"),
            "IntegrityLevel must be one of Low/Medium/High/System.");
    }
}
