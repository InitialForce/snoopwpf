// SnoopWPF.Agent.Engine/Audit/AuditLogWriter.cs
// AuditLogWriter requires Channel<T>, IAsyncDisposable, and ValueTask — all unavailable
// on net462. The type is compiled only for net6+ targets.
#if NET6_0_OR_GREATER
//
// Brokered-mode audit log scope (B-5):
//   In Brokered mode the audit log is TARGET-ONLY. The broker process must NOT
//   construct an AuditLogWriter. This avoids HMAC chain collision when broker and
//   target would otherwise race to write to the same {sessionId}.jsonl file.
//   The M2-21 acceptance test asserts BrokerHost never constructs AuditLogWriter.

namespace SnoopWPF.Agent.Engine.Audit;

using System;
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

    /// <summary>
    /// Constructs a new <see cref="AuditLogWriter"/> for the given <paramref name="sessionId"/>
    /// and starts the background worker task.
    /// </summary>
    /// <param name="sessionId">Stable session identifier used as the log file name.</param>
    /// <param name="channel">
    ///   The channel to read from. Pass <c>null</c> to have the writer create its own
    ///   unbounded channel (convenient for tests and simple callers).
    /// </param>
    public AuditLogWriter(string sessionId, Channel<AuditEntry>? channel = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("sessionId must not be empty.", nameof(sessionId));
        }

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
            // 1. Sanitize reason before computing HMAC so the on-disk value matches.
            var entry = rawEntry with { Reason = SanitizeReason(rawEntry.Reason) };

            // 2. Serialize to JSON with hmac=string.Empty to obtain the payload.
            var entryForJson = entry with { Hmac = string.Empty };
            string entryJson = SerializeToJson(serializer, entryForJson);

            // 3. Compute HMAC-SHA256 over: entryJson || prevHmac || sessionKey || counterNonce
            byte[] hmacBytes = ComputeHmac(entryJson, prevHmac, this.sessionKey, entry.CounterNonce);
            string hmacHex = Convert.ToHexString(hmacBytes);

            // 4. Produce the final entry with the computed hmac.
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
        // Signal the channel that no more entries will be written.
        // The background worker drains remaining items then exits naturally.
        this.channel.Writer.TryComplete();

        try
        {
            await this.workerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected if CancellationToken was triggered externally.
        }
        catch (Exception)
        {
            // Swallow other worker faults on shutdown.
        }
        finally
        {
            this.cts.Dispose();
        }
    }
}
#endif
