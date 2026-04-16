namespace SnoopWPF.Agent.Contracts;

using System;
using System.Runtime.Serialization;

/// <summary>
/// Immutable per-session policy constructed once at session start.
/// All tool handlers read from this record; nothing mutates it after construction (S1).
/// </summary>
/// <remarks>
/// Use <see cref="Create"/> to construct instances. Do not use the record constructor directly.
/// </remarks>
[DataContract]
public sealed record SessionPolicy
{
    /// <summary>The session mode (how the agent is integrated with the target process).</summary>
    [DataMember(Name = "mode")]
    public SessionMode Mode { get; init; }

    /// <summary>
    /// Maximum input tier permitted for this session.
    /// Injection mode forces <see cref="InputTier.L0ReadOnly"/> (S7).
    /// </summary>
    [DataMember(Name = "maxTier")]
    public InputTier MaxTier { get; init; }

    /// <summary>Whether UI Automation-based input is enabled.</summary>
    [DataMember(Name = "enableAutomation")]
    public bool EnableAutomation { get; init; }

    /// <summary>Whether property mutation (SetProperty) is enabled.</summary>
    [DataMember(Name = "enableMutation")]
    public bool EnableMutation { get; init; }

    /// <summary>
    /// Whether sensitive property values are redacted in tool output.
    /// Injection mode forces this to <see langword="true"/> (MF-11).
    /// </summary>
    [DataMember(Name = "enableRedaction")]
    public bool EnableRedaction { get; init; }

    /// <summary>Structural redaction policy. Default is <see cref="RedactionPolicy.Default"/>.</summary>
    [DataMember(Name = "redactionPolicy")]
    public RedactionPolicy RedactionPolicy { get; init; } = RedactionPolicy.Default;

    /// <summary>Whether sensitive values may be retained in tool responses.</summary>
    [DataMember(Name = "allowSensitiveRetention")]
    public bool AllowSensitiveRetention { get; init; }

    /// <summary>
    /// Constructs a <see cref="SessionPolicy"/> from a <paramref name="mode"/> and caller-supplied
    /// <paramref name="opts"/>. Applies all mandatory security overrides:
    /// <list type="bullet">
    ///   <item><see cref="SessionMode.Injection"/>: forces <see cref="EnableRedaction"/> = <see langword="true"/> (MF-11)
    ///         and caps <see cref="MaxTier"/> = <see cref="InputTier.L0ReadOnly"/> (S7).</item>
    ///   <item><see cref="SessionMode.CoLocated"/> and <see cref="SessionMode.Brokered"/>:
    ///         passes <paramref name="opts"/> through unchanged (owned app — caller owns policy).</item>
    /// </list>
    /// </summary>
    /// <param name="mode">Integration mode.</param>
    /// <param name="opts">Caller-supplied options.</param>
    /// <returns>An immutable <see cref="SessionPolicy"/> for this session.</returns>
    public static SessionPolicy Create(SessionMode mode, SnoopAgentOptions opts)
    {
        if (opts is null)
        {
            throw new System.ArgumentNullException(nameof(opts));
        }

        // MF-11: injection mode always forces redaction.
        var enableRedaction = mode == SessionMode.Injection ? true : opts.EnableRedaction;

        // S7: injection mode caps tier at L0ReadOnly (inspection-only in MVP).
        var maxTier = mode == SessionMode.Injection ? InputTier.L0ReadOnly : opts.MaxTier;

        // CoLocated and Brokered: pass opts through unchanged.
        // Brokered is an owned process (caller is the broker) — identical policy to CoLocated.
        return new SessionPolicy
        {
            Mode = mode,
            MaxTier = maxTier,
            EnableAutomation = opts.EnableAutomation,
            EnableMutation = opts.EnableMutation,
            EnableRedaction = enableRedaction,
            RedactionPolicy = RedactionPolicy.Default,
            AllowSensitiveRetention = opts.AllowSensitiveRetention,
        };
    }
}
