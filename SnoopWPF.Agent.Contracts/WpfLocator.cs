namespace SnoopWPF.Agent.Contracts;

using System.Runtime.Serialization;

/// <summary>
/// Immutable locator that identifies a WPF element by one of four forms (PRD §6, FD-1).
/// Construct via <see cref="WpfLocatorParser.Parse"/>.
/// </summary>
[DataContract]
public sealed record WpfLocator
{
    /// <summary>Which locator grammar was used.</summary>
    [DataMember(Name = "form")]
    public WpfLocatorForm Form { get; init; }

    /// <summary>
    /// Normalised primary value: the automation-id string, short type name, or path tokens.
    /// For <see cref="WpfLocatorForm.ViewModel"/> this is the short type name.
    /// </summary>
    [DataMember(Name = "value")]
    public string Value { get; init; } = string.Empty;

    /// <summary>Original $locator string as supplied by the caller (preserved verbatim).</summary>
    [DataMember(Name = "raw")]
    public string Raw { get; init; } = string.Empty;

    // ── optional secondary fields ──────────────────────────────────────────

    /// <summary>
    /// For <see cref="WpfLocatorForm.TypeName"/>: the optional <c>name=</c> qualifier.
    /// For <see cref="WpfLocatorForm.ViewModel"/>: the optional <c>property=</c> qualifier.
    /// </summary>
    [DataMember(Name = "qualifier", IsRequired = false)]
    public string? Qualifier { get; init; }

    /// <summary>
    /// For <see cref="WpfLocatorForm.ViewModel"/>: the optional <c>value=</c> qualifier.
    /// </summary>
    [DataMember(Name = "qualifierValue", IsRequired = false)]
    public string? QualifierValue { get; init; }
}
