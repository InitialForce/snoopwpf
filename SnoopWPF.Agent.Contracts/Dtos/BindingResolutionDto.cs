namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// Full binding chain resolution result for a single DP property (M2-08).
/// Returned by <c>wpf_resolve_binding</c>.
/// </summary>
[DataContract]
public sealed class BindingResolutionDto
{
    /// <summary>
    /// <c>true</c> when a data binding was found on the property; <c>false</c> otherwise.
    /// </summary>
    [DataMember(Name = "hasBinding")]
    public bool HasBinding { get; set; }

    /// <summary>
    /// Binding path string, e.g. <c>SelectedSession.User.Name</c>.
    /// Empty string when the binding has no path (source binding) or no binding at all.
    /// </summary>
    [DataMember(Name = "path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Type name of the binding source object (DataContext, ElementName, RelativeSource, or
    /// StaticResource), e.g. <c>MyViewModel</c>.
    /// Empty when the source cannot be resolved.
    /// </summary>
    [DataMember(Name = "sourceTypeName")]
    public string SourceTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Resolved source object <c>ToString()</c> value.
    /// Null when the source is null or cannot be resolved.
    /// </summary>
    [DataMember(Name = "sourceValue")]
    public string? SourceValue { get; set; }

    /// <summary>
    /// Values at each intermediate path segment.
    /// For path <c>A.B.C</c> there are three entries: A, A.B, A.B.C.
    /// The last entry corresponds to the final resolved value.
    /// Empty when the path is empty, the source is null, or any step fails.
    /// </summary>
    [DataMember(Name = "pathSteps")]
    public List<PathStepDto> PathSteps { get; set; } = new();

    /// <summary>
    /// Short type name of the value converter, e.g. <c>BoolToVisibilityConverter</c>.
    /// Empty when no converter is set.
    /// </summary>
    [DataMember(Name = "converterTypeName")]
    public string ConverterTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Converter parameter <c>ToString()</c> value.
    /// Null when no converter parameter is set.
    /// </summary>
    [DataMember(Name = "converterParameter")]
    public string? ConverterParameter { get; set; }

    /// <summary>
    /// Binding mode: <c>OneWay</c>, <c>TwoWay</c>, <c>OneWayToSource</c>,
    /// <c>OneTime</c>, or <c>Default</c>.
    /// </summary>
    [DataMember(Name = "mode")]
    public string Mode { get; set; } = string.Empty;

    /// <summary>
    /// Validation errors from <c>Validation.GetErrors</c> on the target element.
    /// Empty when there are no validation errors.
    /// </summary>
    [DataMember(Name = "validationErrors")]
    public List<string> ValidationErrors { get; set; } = new();

    /// <summary>
    /// Overall binding resolution status.
    /// One of: <c>OK</c>, <c>NoBinding</c>, <c>PathError</c>, <c>ValidationError</c>,
    /// <c>MissingDataContext</c>, <c>ConverterError</c>, <c>Unknown</c>.
    /// </summary>
    [DataMember(Name = "status")]
    public string Status { get; set; } = BindingResolutionStatus.NoBinding;

    /// <summary>
    /// Human-readable error detail when <see cref="Status"/> is not <c>OK</c>.
    /// Null on success.
    /// </summary>
    [DataMember(Name = "errorDetail")]
    public string? ErrorDetail { get; set; }
}

/// <summary>
/// One resolved step along a binding path.
/// </summary>
[DataContract]
public sealed class PathStepDto
{
    /// <summary>Property or indexer name for this step, e.g. <c>SelectedSession</c>.</summary>
    [DataMember(Name = "segment")]
    public string Segment { get; set; } = string.Empty;

    /// <summary>Type name of the value at this step. Empty when null.</summary>
    [DataMember(Name = "typeName")]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// <c>ToString()</c> of the value at this step.
    /// Null when the value itself is null.
    /// </summary>
    [DataMember(Name = "value")]
    public string? Value { get; set; }

    /// <summary><c>true</c> when this step failed (property not found, exception, etc.).</summary>
    [DataMember(Name = "isError")]
    public bool IsError { get; set; }

    /// <summary>Error message when <see cref="IsError"/> is <c>true</c>.</summary>
    [DataMember(Name = "errorDetail")]
    public string? ErrorDetail { get; set; }
}

/// <summary>
/// Well-known status strings for <see cref="BindingResolutionDto.Status"/>.
/// </summary>
public static class BindingResolutionStatus
{
    /// <summary>No binding is set on the property.</summary>
    public const string NoBinding = "NoBinding";

    /// <summary>Binding resolved successfully; all path steps walked.</summary>
    public const string OK = "OK";

    /// <summary>One or more path steps failed (property not found or null mid-chain).</summary>
    public const string PathError = "PathError";

    /// <summary>Binding has one or more active validation errors.</summary>
    public const string ValidationError = "ValidationError";

    /// <summary>The DataContext is null and the binding relies on it.</summary>
    public const string MissingDataContext = "MissingDataContext";

    /// <summary>The converter threw an exception during status evaluation.</summary>
    public const string ConverterError = "ConverterError";

    /// <summary>Binding status is known-error but does not match other categories.</summary>
    public const string Unknown = "Unknown";
}
