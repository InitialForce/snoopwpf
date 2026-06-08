namespace SnoopWPF.Agent.Contracts.Protocol;

using System.Runtime.Serialization;

/// <summary>
/// An error payload in a <see cref="PipeResponse"/>.
/// </summary>
[DataContract]
public sealed class PipeErrorPayload
{
    [DataMember(Name = "code")]
    public string Code { get; set; } = string.Empty;

    [DataMember(Name = "message")]
    public string Message { get; set; } = string.Empty;

    [DataMember(Name = "suggestion")]
    public string Suggestion { get; set; } = string.Empty;
}

/// <summary>
/// A non-fatal diagnostic warning carried on a <see cref="PipeResponse"/>.
/// </summary>
/// <remarks>
/// Mirrors <see cref="SnoopWPF.Agent.Contracts.Diagnostics.AgentWarning"/> but carries only
/// the wire-relevant fields. The target-side server drains the engine's warning scope into
/// this list; the broker-side proxy re-emits each entry into its own warning scope so the
/// tool dispatcher surfaces them in the top-level <c>warnings</c> array.
/// </remarks>
[DataContract]
public sealed class PipeWarning
{
    [DataMember(Name = "code")]
    public string Code { get; set; } = string.Empty;

    [DataMember(Name = "message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// A response frame sent from the injected agent back to the host.
/// </summary>
[DataContract]
public sealed class PipeResponse
{
    [DataMember(Name = "id")]
    public int Id { get; set; }

    /// <summary>
    /// Raw JSON of the method-specific result. Not escaped. Null on error.
    /// </summary>
    [DataMember(Name = "resultJson")]
    public string? ResultJson { get; set; }

    [DataMember(Name = "error")]
    public PipeErrorPayload? Error { get; set; }

    /// <summary>
    /// Non-fatal diagnostics accumulated by the engine while handling the request.
    /// Null or empty when none were emitted.
    /// </summary>
    [DataMember(Name = "warnings", EmitDefaultValue = false)]
    public PipeWarning[]? Warnings { get; set; }
}
