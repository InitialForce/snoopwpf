namespace SnoopWPF.Agent.Server;

using System;

/// <summary>
/// Immutable DTO representing the MC-owned session manifest written to
/// <c>%LOCALAPPDATA%/InitialForce/mcp-session/{pid}-{startTimeTicks}.json</c>.
/// Serialized with camelCase property names via <c>JsonNamingPolicy.CamelCase</c>.
/// Schema version 1.
/// </summary>
public sealed record SessionManifest(
    /// <summary>Schema version discriminator. Always 1 for this record.</summary>
    int SchemaVersion,

    /// <summary>Stable per-session identifier (GUID). Generated once at session start.</summary>
    string SessionId,

    /// <summary>Process ID of the WPF target at manifest-write time.</summary>
    int Pid,

    /// <summary>
    /// <see cref="System.Diagnostics.Process.StartTime"/> expressed as UTC ticks
    /// (<c>Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks</c>).
    /// Used by the broker's StaleManifestSweeper to detect PID reuse.
    /// </summary>
    long StartTimeTicks,

    /// <summary>Full path of the WPF target executable.</summary>
    string ImagePath,

    /// <summary>SHA-256 hex digest of the bytes at <see cref="ImagePath"/>.</summary>
    string ImageHash,

    /// <summary>"CoreCLR" or "NetFramework".</summary>
    string RuntimeFamily,

    /// <summary>Runtime version string (e.g. "8.0.5" for CoreCLR).</summary>
    string RuntimeVersion,

    /// <summary>Process bitness: 32, 64, or "ARM64".</summary>
    object Bitness,

    /// <summary>Windows integrity level: "Low", "Medium", "High", or "System".</summary>
    string IntegrityLevel,

    /// <summary>Named-pipe name the broker must connect to.</summary>
    string PipeName,

    /// <summary>Base64-encoded session token for the HMAC handshake.</summary>
    string TokenB64,

    /// <summary>
    /// Unique lease identifier (GUID). Generated fresh per manifest write; changes on rotation.
    /// </summary>
    string LeaseId,

    /// <summary>UTC instant at which this manifest was issued.</summary>
    DateTimeOffset IssuedAt,

    /// <summary>UTC expiry instant (<see cref="IssuedAt"/> + 24 h).</summary>
    DateTimeOffset ExpiresAt);
