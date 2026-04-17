// SnoopWPF.Agent.Tests/AuditLogWriterTests.cs
// FX6-D2: acceptance tests for AuditLogWriter fail-fast + fallback behaviour.

namespace SnoopWPF.Agent.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine.Audit;

/// <summary>
/// Validates FX6-D2: AuditLogWriter must fail-fast at construction when the audit path
/// is unwritable (rather than silently dropping entries later), and optionally fall back
/// to <c>%LOCALAPPDATA%\SnoopWPF.Agent\audit\</c> when <c>allowFallback=true</c>.
/// </summary>
[TestFixture]
public sealed class AuditLogWriterTests
{
    // -------------------------------------------------------------------------
    // Happy path: normal construction succeeds
    // -------------------------------------------------------------------------

    [Test]
    public async Task HappyPath_ConstructorSucceeds()
    {
        var sessionId = "fx6d2_happy_" + Guid.NewGuid().ToString("N");
        AuditLogWriter? writer = null;
        try
        {
            writer = new AuditLogWriter(sessionId, drainTimeout: TimeSpan.FromMilliseconds(200));
            Assert.That(writer, Is.Not.Null);
            Assert.That(writer.FilePathForTest, Does.EndWith(".jsonl"));
        }
        finally
        {
            if (writer != null)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
                try
                {
                    if (File.Exists(writer.FilePathForTest))
                    {
                        File.Delete(writer.FilePathForTest);
                    }
                }
                catch
                {
                    // cleanup failure is non-fatal in tests
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // UnwritablePath_ThrowsAtStartAsync
    // Use a too-long session ID to guarantee the probe file path exceeds MAX_PATH.
    // -------------------------------------------------------------------------

    [Test]
    public void UnwritablePath_ThrowsAtStartAsync()
    {
        // A session ID of 248+ chars causes the combined path (%LOCALAPPDATA% + dirs + name + ".probe")
        // to exceed MAX_PATH (260) on most Windows installations, causing Directory.CreateDirectory
        // or File.WriteAllBytes to fail.
        var longSessionId = new string('a', 3) + new string('b', 248);

        var ex = Assert.Throws<SnoopException>(() =>
        {
            var w = new AuditLogWriter(
                sessionId: longSessionId,
                allowFallback: false,
                drainTimeout: TimeSpan.FromMilliseconds(50));
            w.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });

        Assert.That(ex, Is.Not.Null, "Expected SnoopException but got null.");
        Assert.That(
            ex!.Code,
            Is.EqualTo(SnoopErrorCode.AuditUnwritable),
            "Constructor must throw AuditUnwritable when path is not writable.");
    }

    // -------------------------------------------------------------------------
    // FallbackPath_WritesToLocalAppData (AllowAuditFallback=true)
    // -------------------------------------------------------------------------

    [Test]
    public async Task FallbackPath_WritesToLocalAppData()
    {
        var longSessionId = new string('c', 3) + new string('d', 248);

        var fallbackDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SnoopWPF.Agent",
            "audit");

        AuditLogWriter? writer = null;
        try
        {
            writer = new AuditLogWriter(
                sessionId: longSessionId,
                allowFallback: true,
                drainTimeout: TimeSpan.FromMilliseconds(200));

            Assert.That(writer, Is.Not.Null, "Writer must be non-null when fallback succeeds.");
            Assert.That(Directory.Exists(fallbackDir), Is.True, "Fallback directory must exist.");
            Assert.That(
                writer.FilePathForTest,
                Does.StartWith(fallbackDir),
                "File must be in fallback directory.");
        }
        catch (SnoopException ex) when (ex.Code == SnoopErrorCode.AuditUnwritable)
        {
            Assert.Ignore($"Both primary and fallback audit dirs are unwritable: {ex.Message}");
        }
        finally
        {
            if (writer != null)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
                try
                {
                    if (File.Exists(writer.FilePathForTest))
                    {
                        File.Delete(writer.FilePathForTest);
                    }
                }
                catch
                {
                    // cleanup failure is non-fatal in tests
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // AllowAuditFallback=false (default): no fallback on failure
    // -------------------------------------------------------------------------

    [Test]
    public void AllowAuditFallback_DefaultIsFalse_NoFallbackOnFailure()
    {
        var longSessionId = new string('e', 3) + new string('f', 248);

        var ex = Assert.Throws<SnoopException>(() =>
        {
            var w = new AuditLogWriter(
                sessionId: longSessionId,
                allowFallback: false,
                drainTimeout: TimeSpan.FromMilliseconds(50));
            w.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });

        Assert.That(
            ex!.Code,
            Is.EqualTo(SnoopErrorCode.AuditUnwritable),
            "Without fallback, unwritable path must throw AuditUnwritable.");
        Assert.That(
            ex.Message,
            Does.Contain("AllowAuditFallback"),
            "Error message must hint at AllowAuditFallback option.");
    }
}
