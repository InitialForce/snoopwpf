namespace SnoopWPF.Agent.Engine;

/// <summary>
/// Configuration options for <see cref="SnoopInspector"/>.
/// </summary>
public sealed class SnoopInspectorOptions
{
    /// <summary>
    /// Overall operation timeout in milliseconds (default 5000 ms).
    /// If the Dispatcher accepts work but the operation exceeds this, <c>OperationTimedOut</c> is thrown.
    /// </summary>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Whether mutation operations (SetProperty) are allowed (default false).
    /// </summary>
    public bool EnableMutation { get; set; } = false;

    /// <summary>
    /// Whether sensitive property values are redacted (default true).
    /// </summary>
    public bool EnableRedaction { get; set; } = true;

    /// <summary>
    /// Whether sensitive values (e.g. PasswordBox input, S3) may be retained in tool responses.
    /// When <see langword="false"/> (default), PasswordBox newValue is redacted in SetTextValueAsync
    /// responses even on success. Set to <see langword="true"/> only in controlled test environments.
    /// </summary>
    public bool AllowSensitiveRetention { get; set; } = false;
}
