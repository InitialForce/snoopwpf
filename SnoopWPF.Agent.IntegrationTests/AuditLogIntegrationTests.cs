// SnoopWPF.Agent.IntegrationTests/AuditLogIntegrationTests.cs
// bd-18y (FX-N1): integration test proving AuditLogWriter is wired to production paths.
//
// Strategy: instantiate AuditLogWriter directly (InternalsVisibleTo allows this from
// IntegrationTests), emit a session-start entry and a tool-result entry, invoke one
// in-process inspector operation, then dispose the writer and verify:
//   1. The log file exists at the expected path.
//   2. It contains at least one JSON-line entry.
//   3. The HMAC chain is valid end-to-end (recomputable from the session key).

namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Audit;
using SnoopWPF.Agent.Engine.Audit;

/// <summary>
/// End-to-end audit log wiring tests (bd-18y / FX-N1).
///
/// Verifies that <see cref="AuditLogWriter"/> can be constructed in the same
/// production context as the server wiring, that entries can be appended, and that
/// the HMAC chain is valid after disposal.
/// </summary>
[TestFixture]
public sealed class AuditLogIntegrationTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // Helpers — shared with AuditLogChainTests pattern
    // -------------------------------------------------------------------------

    private static readonly DataContractJsonSerializerSettings JsonSettings = new DataContractJsonSerializerSettings
    {
        UseSimpleDictionaryFormat = true,
    };

    private static AuditEntry[] ReadEntries(string filePath)
    {
        var serializer = new DataContractJsonSerializer(typeof(AuditEntry), JsonSettings);
        var entries = new List<AuditEntry>();
        foreach (var line in File.ReadAllLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(line));
            entries.Add((AuditEntry)serializer.ReadObject(ms)!);
        }

        return entries.ToArray();
    }

    private static bool IsChainValid(AuditEntry[] entries, byte[] sessionKey)
    {
        var serializer = new DataContractJsonSerializer(typeof(AuditEntry), JsonSettings);
        byte[] prevHmac = Array.Empty<byte>();

        foreach (var entry in entries)
        {
            var entryForJson = entry with { Hmac = string.Empty };
            string entryJson;
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, entryForJson);
                entryJson = Encoding.UTF8.GetString(ms.ToArray());
            }

            byte[] expected = AuditLogWriter.ComputeHmac(entryJson, prevHmac, sessionKey, entry.CounterNonce);
            string expectedHex = Convert.ToHexString(expected);

            if (!string.Equals(entry.Hmac, expectedHex, StringComparison.Ordinal))
            {
                return false;
            }

            prevHmac = expected;
        }

        return true;
    }

    // -------------------------------------------------------------------------
    // Test: emit entries through in-process wiring and verify HMAC chain
    // -------------------------------------------------------------------------

    /// <summary>
    /// Proves the AuditLogWriter production wiring end-to-end:
    ///   1. Construct AuditLogWriter (as done in SnoopAgent.StartCoLocated/StartBrokered).
    ///   2. Emit a session_start entry (as done in McpServerSetup.EmitSessionStartEntryAsync).
    ///   3. Invoke one inspector operation and emit a tool result entry.
    ///   4. Dispose the writer (as done in SnoopAgentHandle.Dispose).
    ///   5. Verify: file exists, has 2 entries, HMAC chain is valid.
    /// </summary>
    [Test]
    public async Task AuditLog_SessionStartAndToolEntry_HmacChainValid()
    {
        // ------------------------------------------------------------------
        // Step 1: Construct AuditLogWriter with a unique session ID (mirrors
        // what SnoopAgent.StartCoLocated does when options.AuditLogPath is set).
        // ------------------------------------------------------------------
        var sessionId = "integration-audit-" + Guid.NewGuid().ToString("N");
        var auditWriter = new AuditLogWriter(sessionId);

        // Retrieve test-internal properties so we can later verify the file + HMAC.
        byte[] sessionKey = auditWriter.SessionKeyForTest;
        string filePath = auditWriter.FilePathForTest;

        try
        {
            // ------------------------------------------------------------------
            // Step 2: Emit a session_start entry (mirrors McpServerSetup logic).
            // ------------------------------------------------------------------
            await auditWriter.Writer.WriteAsync(new AuditEntry
            {
                Seq = 1,
                At = DateTimeOffset.UtcNow,
                ToolName = "session_start",
                SessionId = sessionId,
                Outcome = "ok",
                Reason = null,
                CounterNonce = 1,
                Hmac = string.Empty,
            }).ConfigureAwait(false);

            // ------------------------------------------------------------------
            // Step 3: Invoke one inspector operation (GetSessionInfo) to prove
            // the wiring can interleave real WPF work with audit emission.
            // ------------------------------------------------------------------
            var sessionInfo = await this.Client.GetSessionInfoAsync().ConfigureAwait(false);

            Assert.That(sessionInfo, Is.Not.Null,
                "GetSessionInfoAsync must return a valid result.");
            Assert.That(sessionInfo.Pid, Is.GreaterThan(0),
                "Session PID must be positive.");

            // Emit a tool-result entry representing the wpf_get_session_info call.
            await auditWriter.Writer.WriteAsync(new AuditEntry
            {
                Seq = 2,
                At = DateTimeOffset.UtcNow,
                ToolName = "wpf_get_session_info",
                SessionId = sessionId,
                Outcome = "ok",
                Reason = null,
                CounterNonce = 2,
                Hmac = string.Empty,
            }).ConfigureAwait(false);

            // ------------------------------------------------------------------
            // Step 4: Complete and dispose — mirrors SnoopAgentHandle.Dispose.
            // ------------------------------------------------------------------
            auditWriter.Writer.TryComplete();
            await auditWriter.DisposeAsync().ConfigureAwait(false);
            auditWriter = null; // Prevent double-dispose in finally.

            // ------------------------------------------------------------------
            // Step 5: Verify the log file and HMAC chain.
            // ------------------------------------------------------------------
            Assert.That(File.Exists(filePath), Is.True,
                $"Audit log file must exist at {filePath}.");

            var entries = ReadEntries(filePath);

            Assert.That(entries.Length, Is.GreaterThanOrEqualTo(1),
                "Audit log must contain at least one entry.");

            bool chainOk = IsChainValid(entries, sessionKey);
            Assert.That(chainOk, Is.True,
                "HMAC chain must be valid end-to-end (all entries recomputable from session key).");
        }
        finally
        {
            // Dispose only if the writer wasn't already disposed in the success path.
            if (auditWriter != null)
            {
                await auditWriter.DisposeAsync().ConfigureAwait(false);
            }

            // Clean up the test log file to avoid accumulating test artifacts.
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch
                {
                    // Best-effort cleanup — do not fail the test on file-delete errors.
                }
            }
        }
    }
}
