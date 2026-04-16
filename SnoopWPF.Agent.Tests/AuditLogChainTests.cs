// SnoopWPF.Agent.Tests/AuditLogChainTests.cs
// M1-13 acceptance tests for the HMAC-chained audit log writer.

namespace SnoopWPF.Agent.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Audit;
using SnoopWPF.Agent.Engine.Audit;

[TestFixture]
public class AuditLogChainTests
{
    private static readonly DataContractJsonSerializerSettings JsonSettings = new DataContractJsonSerializerSettings
    {
        UseSimpleDictionaryFormat = true,
    };

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static AuditEntry MakeEntry(long seq, string sessionId, long nonce) =>
        new AuditEntry
        {
            Seq = seq,
            At = DateTimeOffset.UtcNow,
            ToolName = "test.tool",
            SessionId = sessionId,
            Outcome = "ok",
            Reason = null,
            CounterNonce = nonce,
            Hmac = string.Empty,
        };

    private static AuditEntry[] ReadLines(string path)
    {
        var serializer = new DataContractJsonSerializer(typeof(AuditEntry), JsonSettings);
        var entries = new List<AuditEntry>();
        foreach (var line in File.ReadAllLines(path))
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

    private static async Task WriteAndDisposeAsync(AuditLogWriter writer, string sessionId, int count)
    {
        long nonce = 0;
        for (int i = 0; i < count; i++)
        {
            var entry = MakeEntry(i + 1, sessionId, Interlocked.Increment(ref nonce));
            await writer.Writer.WriteAsync(entry);
        }

        writer.Writer.TryComplete();

        // Wait for the background worker to drain and flush all entries.
        await writer.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Test 1: 1,000 writes produce a file with monotonically-increasing seq
    // -------------------------------------------------------------------------

    [Test]
    public async Task Writes1000Entries_MonotonicSeq()
    {
        var sessionId = "test-mono-" + Guid.NewGuid().ToString("N");
        var auditWriter = new AuditLogWriter(sessionId);
        string filePath = auditWriter.FilePathForTest;

        await WriteAndDisposeAsync(auditWriter, sessionId, 1000);

        var entries = ReadLines(filePath);
        Assert.That(entries.Length, Is.EqualTo(1000), "Should have 1000 entries");

        for (int i = 0; i < entries.Length; i++)
        {
            Assert.That(entries[i].Seq, Is.EqualTo(i + 1),
                $"Entry {i}: expected seq {i + 1}, got {entries[i].Seq}");
        }
    }

    // -------------------------------------------------------------------------
    // Test 2: HMAC chain is fully recomputable from session key + file contents
    // -------------------------------------------------------------------------

    [Test]
    public async Task HmacChain_IsRecomputableFromSessionKeyAndFile()
    {
        var sessionId = "test-recompute-" + Guid.NewGuid().ToString("N");
        var auditWriter = new AuditLogWriter(sessionId);
        byte[] sessionKey = auditWriter.SessionKeyForTest;
        string filePath = auditWriter.FilePathForTest;

        await WriteAndDisposeAsync(auditWriter, sessionId, 20);

        var entries = ReadLines(filePath);
        var serializer = new DataContractJsonSerializer(typeof(AuditEntry), JsonSettings);

        byte[] prevHmac = Array.Empty<byte>();
        foreach (var entry in entries)
        {
            // Recompute: serialize entry with hmac=string.Empty then recompute HMAC.
            var entryForJson = entry with { Hmac = string.Empty };
            string entryJson;
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, entryForJson);
                entryJson = Encoding.UTF8.GetString(ms.ToArray());
            }

            byte[] expected = AuditLogWriter.ComputeHmac(entryJson, prevHmac, sessionKey, entry.CounterNonce);
            string expectedHex = Convert.ToHexString(expected);

            Assert.That(entry.Hmac, Is.EqualTo(expectedHex),
                $"HMAC mismatch at seq {entry.Seq}");

            prevHmac = expected;
        }
    }

    // -------------------------------------------------------------------------
    // Test 3: Tampering any single entry breaks the chain
    // -------------------------------------------------------------------------

    [Test]
    public async Task TamperingAnyEntry_BreaksChain()
    {
        var sessionId = "test-tamper-" + Guid.NewGuid().ToString("N");
        var auditWriter = new AuditLogWriter(sessionId);
        byte[] sessionKey = auditWriter.SessionKeyForTest;
        string filePath = auditWriter.FilePathForTest;

        await WriteAndDisposeAsync(auditWriter, sessionId, 10);

        var lines = File.ReadAllLines(filePath).ToList();
        Assert.That(lines.Count, Is.EqualTo(10));

        // Tamper entry index 4 (0-based) by flipping one character in the outcome field.
        var tamperedLine = lines[4].Replace("\"ok\"", "\"TAMPERED\"", StringComparison.Ordinal);
        Assert.That(tamperedLine, Is.Not.EqualTo(lines[4]), "Tamper should have changed the line");
        lines[4] = tamperedLine;

        // Re-parse and verify the chain is broken at index 4 or later.
        var serializer = new DataContractJsonSerializer(typeof(AuditEntry), JsonSettings);
        var entries = lines.Select(l =>
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(l));
            return (AuditEntry)serializer.ReadObject(ms)!;
        }).ToArray();

        byte[] prevHmac = Array.Empty<byte>();
        bool chainBroken = false;
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var entryForJson = entry with { Hmac = string.Empty };
            string entryJson;
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, entryForJson);
                entryJson = Encoding.UTF8.GetString(ms.ToArray());
            }

            byte[] expected = AuditLogWriter.ComputeHmac(entryJson, prevHmac, sessionKey, entry.CounterNonce);
            string expectedHex = Convert.ToHexString(expected);

            if (entry.Hmac != expectedHex)
            {
                chainBroken = true;
                break;
            }

            prevHmac = expected;
        }

        Assert.That(chainBroken, Is.True, "Tampering entry 4 should break the HMAC chain");
    }

    // -------------------------------------------------------------------------
    // Test 4: reason field sanitization (newlines replaced, NUL dropped, 256-char cap)
    // -------------------------------------------------------------------------

    [Test]
    public void ReasonSanitization_NewlinesReplacedNulDropped256CharCap()
    {
        // Newlines and CR should become spaces.
        var withNewlines = "line1\nline2\r\nline3\rend";
        var sanitized = AuditLogWriter.SanitizeReason(withNewlines);
        Assert.That(sanitized, Does.Not.Contain('\n'), "LF should be removed");
        Assert.That(sanitized, Does.Not.Contain('\r'), "CR should be removed");
        Assert.That(sanitized, Is.EqualTo("line1 line2  line3 end"),
            "Newlines should be replaced with spaces");

        // NUL bytes should be dropped.
        var withNul = "he\0llo\0world";
        var sanitizedNul = AuditLogWriter.SanitizeReason(withNul);
        Assert.That(sanitizedNul, Is.EqualTo("helloworld"), "NUL bytes must be dropped");

        // 256-char cap.
        var longReason = new string('x', 500);
        var capped = AuditLogWriter.SanitizeReason(longReason);
        Assert.That(capped!.Length, Is.EqualTo(256), "Reason should be capped at 256 chars");

        // null passthrough.
        Assert.That(AuditLogWriter.SanitizeReason(null), Is.Null, "null reason should pass through as null");
    }

    // -------------------------------------------------------------------------
    // Test 5: entries written end-to-end through writer have sanitized reasons
    // -------------------------------------------------------------------------

    [Test]
    public async Task WrittenEntries_HaveReasonSanitized()
    {
        var sessionId = "test-reason-" + Guid.NewGuid().ToString("N");
        var auditWriter = new AuditLogWriter(sessionId);
        string filePath = auditWriter.FilePathForTest;

        var dirtyEntry = new AuditEntry
        {
            Seq = 1,
            At = DateTimeOffset.UtcNow,
            ToolName = "test.tool",
            SessionId = sessionId,
            Outcome = "fail",
            Reason = "Error:\nFile not\0found\r\n" + new string('x', 300),
            CounterNonce = 1,
            Hmac = string.Empty,
        };

        await auditWriter.Writer.WriteAsync(dirtyEntry);
        auditWriter.Writer.TryComplete();
        await auditWriter.DisposeAsync();

        var entries = ReadLines(filePath);
        Assert.That(entries.Length, Is.EqualTo(1));

        var reason = entries[0].Reason;
        Assert.That(reason, Is.Not.Null);
        Assert.That(reason!.Length, Is.LessThanOrEqualTo(256), "Reason must be capped at 256 chars");
        Assert.That(reason, Does.Not.Contain('\n'), "LF must be gone");
        Assert.That(reason, Does.Not.Contain('\r'), "CR must be gone");
        Assert.That(reason, Does.Not.Contain('\0'), "NUL must be gone");
    }
}
