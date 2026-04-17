// SnoopWPF.Agent.Engine/Audit/AuditLogWriter.cs
// AuditLogWriter requires Channel<T>, IAsyncDisposable, and ValueTask — all unavailable
// on net462. The type is compiled only for net6+ targets.
#if NET6_0_OR_GREATER
// FX4-MEM-C2: DisposeAsync drains the channel with a bounded timeout; on timeout it
// cancels the internal CTS to unblock a stuck worker, so a hung disk cannot keep the
// MCP server shutdown path waiting forever.  Pending entries are always drained on the
// happy path; only entries in flight when the timeout fires can be lost.
//
// Brokered-mode audit log scope (B-5):
//   In Brokered mode the audit log is TARGET-ONLY. The broker process must NOT
//   construct an AuditLogWriter. This avoids HMAC chain collision when broker and
//   target would otherwise race to write to the same {sessionId}.jsonl file.
//   The M2-21 acceptance test asserts BrokerHost never constructs AuditLogWriter.

namespace SnoopWPF.Agent.Engine.Audit;

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SnoopWPF.Agent.Contracts.Audit;

/// <summary>
/// Background writer that reads <see cref="AuditEntry"/> values from a
/// <see cref="Channel{T}"/> and appends them as HMAC-SHA256-chained JSONL to
/// <c>%LOCALAPPDATA%\SnoopWPF\audit\{sessionId}.jsonl</c>.
///
/// A single per-instance session key (32 random bytes) is generated at construction
/// time. The HMAC is computed over: <c>entryJson || prevHmac || sessionKey || counterNonce</c>
/// (all as UTF-8 bytes, where counterNonce is the big-endian 8-byte encoding of the
/// monotonically-increasing counter). The <c>hmac</c> field on the entry contains the
/// uppercase hex-encoded result.
///
/// The file is created with owner-only ACL (Windows, net8+) so that no other local
/// accounts can read or tamper with the log.
/// </summary>
internal sealed class AuditLogWriter : IAsyncDisposable
{
    private static readonly DataContractJsonSerializerSettings JsonSettings = new DataContractJsonSerializerSettings
    {
        UseSimpleDictionaryFormat = true,
    };

    private readonly Channel<AuditEntry> channel;
    private readonly byte[] sessionKey;
    private readonly string filePath;
    private readonly Task workerTask;
    private readonly CancellationTokenSource cts = new CancellationTokenSource();
    private readonly TimeSpan drainTimeout;

    // Writer-owned counter. Any CounterNonce supplied by the caller at ingress is IGNORED;
    // this field is the authoritative source so duplicate nonces cannot be injected.
    private long counter = 0;

    /// <summary>
    /// Constructs a new <see cref="AuditLogWriter"/> for the given <paramref name="sessionId"/>
    /// and starts the background worker task.
    /// </summary>
    /// <param name="sessionId">Stable session identifier used as the log file name.</param>
    /// <param name="channel">
    ///   The channel to read from. Pass <c>null</c> to have the writer create its own
    ///   unbounded channel (convenient for tests and simple callers).
    /// </param>
    /// <param name="drainTimeout">
    ///   Maximum time to wait for the background worker to exit during <see cref="DisposeAsync"/>.
    ///   Defaults to 5 seconds. Pass a shorter value in unit tests.
    /// </param>
    public AuditLogWriter(string sessionId, Channel<AuditEntry>? channel = null, TimeSpan drainTimeout = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("sessionId must not be empty.", nameof(sessionId));
        }

        this.drainTimeout = drainTimeout == default ? TimeSpan.FromSeconds(5) : drainTimeout;
        this.sessionKey = RandomNumberGenerator.GetBytes(32);
        this.channel = channel ?? Channel.CreateUnbounded<AuditEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false,
        });

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "SnoopWPF", "audit");
        Directory.CreateDirectory(dir);

        this.filePath = Path.Combine(dir, SanitizeFileName(sessionId) + ".jsonl");
        this.workerTask = Task.Run(() => this.RunAsync(this.cts.Token));
    }

    /// <summary>Exposes the writer end of the internal channel for callers that supply entries.</summary>
    public ChannelWriter<AuditEntry> Writer => this.channel.Writer;

    /// <summary>Returns the session key (32 bytes) — exposed for chain verification in tests.</summary>
    internal byte[] SessionKeyForTest => this.sessionKey;

    /// <summary>Returns the absolute path of the log file — exposed for tests.</summary>
    internal string FilePathForTest => this.filePath;

    // -------------------------------------------------------------------------
    // Background worker
    // -------------------------------------------------------------------------

    private async Task RunAsync(CancellationToken ct)
    {
        await using var stream = OpenWithOwnerOnlyAcl(this.filePath);
        await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);

        var serializer = new DataContractJsonSerializer(typeof(AuditEntry), JsonSettings);
        byte[] prevHmac = Array.Empty<byte>();

        await foreach (var rawEntry in this.channel.Reader.ReadAllAsync(ct))
        {
            // 1. Overwrite any caller-supplied CounterNonce with the writer-owned counter.
            //    The caller's value is ignored; the writer is authoritative over nonce assignment
            //    so a buggy or malicious caller cannot submit duplicate nonces to weaken replay detection.
            var entryWithNonce = rawEntry with { CounterNonce = Interlocked.Increment(ref this.counter) };

            // 2. Sanitize reason before computing HMAC so the on-disk value matches.
            var entry = entryWithNonce with { Reason = SanitizeReason(entryWithNonce.Reason) };

            // 3. Serialize to JSON with hmac=string.Empty to obtain the payload.
            var entryForJson = entry with { Hmac = string.Empty };
            string entryJson = SerializeToJson(serializer, entryForJson);

            // 4. Compute HMAC-SHA256 over: entryJson || prevHmac || sessionKey || counterNonce
            byte[] hmacBytes = ComputeHmac(entryJson, prevHmac, this.sessionKey, entry.CounterNonce);
            string hmacHex = Convert.ToHexString(hmacBytes);

            // 5. Produce the final entry with the computed hmac.
            var finalEntry = entry with { Hmac = hmacHex };
            string finalJson = SerializeToJson(serializer, finalEntry);

            await writer.WriteLineAsync(finalJson.AsMemory(), ct);
#if NET8_0_OR_GREATER
            await writer.FlushAsync(ct);
#else
            await writer.FlushAsync();
#endif

            prevHmac = hmacBytes;
        }
    }

    // -------------------------------------------------------------------------
    // HMAC computation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Computes HMAC-SHA256 over: <c>entryJson_utf8 || prevHmac || sessionKey || counterNonce_be8</c>.
    /// </summary>
    internal static byte[] ComputeHmac(
        string entryJson,
        byte[] prevHmac,
        byte[] sessionKey,
        long counterNonce)
    {
        var entryBytes = Encoding.UTF8.GetBytes(entryJson);
        var nonceBytes = BitConverter.GetBytes(counterNonce);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(nonceBytes);
        }

        using var hmac = new HMACSHA256(sessionKey);
        hmac.TransformBlock(entryBytes, 0, entryBytes.Length, null, 0);
        hmac.TransformBlock(prevHmac, 0, prevHmac.Length, null, 0);
        hmac.TransformBlock(sessionKey, 0, sessionKey.Length, null, 0);
        hmac.TransformFinalBlock(nonceBytes, 0, nonceBytes.Length);
        return hmac.Hash!;
    }

    // -------------------------------------------------------------------------
    // Reason sanitization
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sanitizes a reason string: strips NUL bytes, replaces CR/LF with spaces,
    /// and caps at 256 characters. Returns <c>null</c> if the input is <c>null</c>.
    /// </summary>
    public static string? SanitizeReason(string? reason)
    {
        if (reason is null)
        {
            return null;
        }

        var sb = new StringBuilder(Math.Min(reason.Length, 256));
        foreach (char c in reason)
        {
            if (c == '\0')
            {
                continue;
            }

            if (c == '\r' || c == '\n')
            {
                sb.Append(' ');
            }
            else
            {
                sb.Append(c);
            }

            if (sb.Length == 256)
            {
                break;
            }
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string SerializeToJson(DataContractJsonSerializer serializer, AuditEntry entry)
    {
        using var ms = new MemoryStream();
        serializer.WriteObject(ms, entry);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static FileStream OpenWithOwnerOnlyAcl(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var security = new FileSecurity();
            var currentUser = WindowsIdentity.GetCurrent().User!;
            security.SetOwner(currentUser);
            security.SetAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            return FileSystemAclExtensions.Create(
                new FileInfo(path),
                FileMode.Append,
                FileSystemRights.AppendData | FileSystemRights.WriteData | FileSystemRights.Synchronize,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous,
                security);
        }

        // Non-Windows platform: best-effort open; ACL not enforced.
        return new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None);
    }

    private static string SanitizeFileName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // 1. Signal the channel that no more entries will be written.  ReadAllAsync
        //    will observe the completion, drain the remaining queued entries, then
        //    exit naturally — this is the happy-path shutdown and preserves every
        //    entry the caller has already enqueued.
        this.channel.Writer.TryComplete();

        try
        {
            // 2. Await the worker with a bounded timeout.  If the worker is healthy
            //    it will return as soon as the channel is drained, typically within
            //    microseconds.  If it is stuck (e.g. a hung disk or a wedged network
            //    share), the timeout wins and we escalate to cancellation below.
            var drainTimeoutTask = Task.Delay(this.drainTimeout);
            var winner = await Task.WhenAny(this.workerTask, drainTimeoutTask).ConfigureAwait(false);

            if (winner == drainTimeoutTask)
            {
                // 3. Timeout expired — the worker is not draining.  Cancel the CTS to
                //    unblock it and abandon any remaining in-flight entries.  This is
                //    the FX4-MEM-C2 path: without this escalation DisposeAsync would
                //    hang forever if the sink became unresponsive.
                this.cts.Cancel();

                Trace.WriteLine(
                    $"[AuditLogWriter] Background audit worker did not drain within {this.drainTimeout.TotalSeconds:F0} s — " +
                    "continuing disposal.  Some in-flight audit entries may have been lost.");

                // Give the cancellation a brief chance to unwind the worker so the
                // finally block disposes the CTS with no active token consumers.
                try
                {
                    await this.workerTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected after the cancellation above.
                }
                catch (Exception)
                {
                    // Swallow other faults — shutdown must not crash the host.
                }
            }
            else
            {
                // Worker finished naturally.  Re-await so any non-cancellation fault
                // surfaces to the catch below (which logs and swallows on shutdown).
                await this.workerTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Worker observed a previously-cancelled token; nothing to do.
        }
        catch (Exception)
        {
            // Swallow other worker faults on shutdown to avoid triggering
            // TaskScheduler.UnobservedTaskException and crashing the host.
        }
        finally
        {
            this.cts.Dispose();
        }
    }
}
#endif
