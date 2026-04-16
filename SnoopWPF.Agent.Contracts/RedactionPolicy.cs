namespace SnoopWPF.Agent.Contracts;

using System.Runtime.Serialization;

/// <summary>
/// Controls how sensitive property values are redacted in tool output.
/// </summary>
[DataContract]
public enum RedactionPolicy
{
    /// <summary>
    /// Default structural redaction: values whose runtime type is on the sensitivity map
    /// are replaced with a fixed placeholder string. Keyword-only matching is insufficient (S3).
    /// </summary>
    [EnumMember(Value = "default")]
    Default = 0,

    /// <summary>
    /// Aggressive: redact any property whose name contains a sensitivity keyword in addition
    /// to structural matching. Useful when injection mode is combined with unknown third-party controls.
    /// </summary>
    [EnumMember(Value = "aggressive")]
    Aggressive = 1,
}
