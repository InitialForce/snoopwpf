namespace SnoopWPF.Agent.Server;

// SessionManifestWriter — atomic, hardened-ACL manifest writer for WS2-03 (bd-1a9.24).
//
// Write order (R4 invariant):
//   1. Pipe listener is already running (caller responsibility — StartBrokeredServerAsync).
//   2. Serialize manifest JSON.
//   3. Write to %LOCALAPPDATA%/InitialForce/mcp-session/.tmp-{guid}.json.
//   4. Apply protected ACL (single ALLOW ACE for current user SID) BEFORE rename.
//   5. File.Move(tmp, final) — atomic on NTFS.
//   6. Return ManifestHandle with final path + leaseId.
//
// Disposal: best-effort File.Delete on handle Dispose; errors are swallowed.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;

/// <summary>
/// Writes the MC-owned session manifest atomically to the well-known discovery directory.
/// The pipe listener MUST be running before calling <see cref="Write"/>.
/// </summary>
public static class SessionManifestWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Well-known directory where manifests are written.
    /// <c>%LOCALAPPDATA%\InitialForce\mcp-session\</c>
    /// </summary>
    public static string ManifestDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InitialForce",
            "mcp-session");

    /// <summary>
    /// Writes the session manifest atomically.
    /// </summary>
    /// <param name="pipeName">Named-pipe name the broker connects to.</param>
    /// <param name="sessionToken">Hex-encoded 256-bit session token.</param>
    /// <param name="sessionId">Optional session ID override (GUID). Auto-generated if null.</param>
    /// <returns>A <see cref="ManifestHandle"/> holding the final path and leaseId.</returns>
    public static ManifestHandle Write(
        string pipeName,
        string sessionToken,
        string? sessionId = null)
    {
        if (string.IsNullOrEmpty(pipeName))
        {
            throw new ArgumentException("pipeName must not be null or empty.", nameof(pipeName));
        }

        if (string.IsNullOrEmpty(sessionToken))
        {
            throw new ArgumentException("sessionToken must not be null or empty.", nameof(sessionToken));
        }

        var now = DateTimeOffset.UtcNow;
        var process = Process.GetCurrentProcess();
        int pid = process.Id;
        long startTimeTicks = process.StartTime.ToUniversalTime().Ticks;

        string resolvedSessionId = string.IsNullOrEmpty(sessionId)
            ? Guid.NewGuid().ToString()
            : sessionId;

        string leaseId = Guid.NewGuid().ToString();

        string imagePath = process.MainModule?.FileName ?? string.Empty;
        string imageHash = ComputeImageHash(imagePath);
        string runtimeFamily = DetectRuntimeFamily();
        string runtimeVersion = DetectRuntimeVersion();
        object bitness = DetectBitness();
        string integrityLevel = DetectIntegrityLevel();
        string tokenB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(sessionToken));

        var manifest = new SessionManifest(
            SchemaVersion: 1,
            SessionId: resolvedSessionId,
            Pid: pid,
            StartTimeTicks: startTimeTicks,
            ImagePath: imagePath,
            ImageHash: imageHash,
            RuntimeFamily: runtimeFamily,
            RuntimeVersion: runtimeVersion,
            Bitness: bitness,
            IntegrityLevel: integrityLevel,
            PipeName: pipeName,
            TokenB64: tokenB64,
            LeaseId: leaseId,
            IssuedAt: now,
            ExpiresAt: now.AddHours(24));

        Directory.CreateDirectory(ManifestDirectory);

        string tmpPath = Path.Combine(ManifestDirectory, $".tmp-{Guid.NewGuid():N}.json");
        string finalPath = Path.Combine(ManifestDirectory, $"{pid}-{startTimeTicks}.json");

        // Step 2: serialize JSON.
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);

        // Step 3: write temp file.
        File.WriteAllBytes(tmpPath, jsonBytes);

        // Step 4: apply protected ACL BEFORE rename (critical R4 order).
        ApplyProtectedAcl(tmpPath);

        // Step 5: atomic rename.
        File.Move(tmpPath, finalPath, overwrite: true);

        return new ManifestHandle(finalPath, leaseId);
    }

    /// <summary>
    /// Applies a protected ACL to the file: removes inherited ACEs, single ALLOW
    /// rule for the current user SID (FullControl).
    /// </summary>
    internal static void ApplyProtectedAcl(string filePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sid = WindowsIdentity.GetCurrent().User;
        if (sid is null)
        {
            return;
        }

        var security = new FileSecurity();
        security.SetOwner(sid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            sid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        new FileInfo(filePath).SetAccessControl(security);
    }

    // -------------------------------------------------------------------------
    // Capability detection (runs inside the target process)
    // -------------------------------------------------------------------------

    internal static string DetectRuntimeFamily()
    {
        return RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase)
            ? "NetFramework"
            : "CoreCLR";
    }

    internal static string DetectRuntimeVersion()
    {
        if (RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase))
        {
            // Strip ".NET Framework " prefix; e.g. ".NET Framework 4.8" → "4.8"
            var desc = RuntimeInformation.FrameworkDescription;
            int spaceIdx = desc.LastIndexOf(' ');
            return spaceIdx >= 0 ? desc.Substring(spaceIdx + 1) : desc;
        }

        // CoreCLR: use Environment.Version (accurate for the running runtime).
        return Environment.Version.ToString();
    }

    internal static object DetectBitness()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => (object)"ARM64",
            Architecture.X64 => (object)64,
            Architecture.X86 => (object)32,
            _ => (object)(IntPtr.Size * 8),
        };
    }

    /// <summary>
    /// Detects the Windows integrity level of the current process.
    /// Uses <c>WindowsIdentity.GetCurrent()</c> and inspects the token groups for the
    /// well-known integrity level SIDs (S-1-16-*).
    /// Falls back to "Medium" when the integrity SID cannot be determined.
    /// </summary>
    internal static string DetectIntegrityLevel()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "Medium";
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            var groups = identity.Groups;
            if (groups is null)
            {
                return "Medium";
            }

            foreach (var group in groups)
            {
                if (group is not SecurityIdentifier sid)
                {
                    continue;
                }

                // Integrity level SIDs are children of S-1-16 (Mandatory Label Authority).
                // We check the SDDL string for the known RID values.
                string sddl = sid.Value;
                if (!sddl.StartsWith("S-1-16-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // RIDs: 4096=Low, 8192=Medium, 12288=High, 16384=System
                return sddl switch
                {
                    "S-1-16-4096" => "Low",
                    "S-1-16-8192" => "Medium",
                    "S-1-16-12288" => "High",
                    "S-1-16-16384" => "System",
                    _ => "Medium",
                };
            }
        }
        catch
        {
            // Swallow: best-effort; return safe default.
        }

        return "Medium";
    }

    private static string ComputeImageHash(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
        {
            return string.Empty;
        }

        try
        {
            byte[] exeBytes = File.ReadAllBytes(imagePath);
            byte[] hash = SHA256.HashData(exeBytes);
            return Convert.ToHexString(hash).ToUpperInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }
}

/// <summary>
/// Holds the final manifest path and lease identifier returned by <see cref="SessionManifestWriter.Write"/>.
/// Dispose to delete the manifest file (best-effort; errors are swallowed).
/// </summary>
public sealed class ManifestHandle : IDisposable
{
    private int disposedFlag;

    internal ManifestHandle(string filePath, string leaseId)
    {
        this.FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        this.LeaseId = leaseId ?? throw new ArgumentNullException(nameof(leaseId));
    }

    /// <summary>Absolute path of the final manifest file.</summary>
    public string FilePath { get; }

    /// <summary>Lease identifier embedded in the manifest. Unique per write call.</summary>
    public string LeaseId { get; }

    /// <summary>
    /// Deletes the manifest file.  Errors are swallowed (best-effort cleanup).
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposedFlag, 1) == 1)
        {
            return;
        }

        try
        {
            File.Delete(this.FilePath);
        }
        catch
        {
            // Best-effort; swallow all errors.
        }
    }
}
