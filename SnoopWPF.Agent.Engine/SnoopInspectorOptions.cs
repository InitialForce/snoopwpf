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
    /// Dispatcher acceptance timeout in milliseconds (default 500 ms).
    /// This is Phase 1 of the two-phase timeout: if the Dispatcher does not even accept (start)
    /// queued work within this window, <c>DispatcherBusy</c> is thrown. Raise this in test
    /// environments where the STA dispatcher thread may be starved under CPU contention.
    /// </summary>
    public int DispatcherAcceptanceTimeoutMs { get; set; } = 500;

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

    /// <summary>
    /// Whether UI Automation-based input (L1 tier) is enabled. Default false (safe by default).
    /// Must be <see langword="true"/> for <c>wpf_click</c> and other L1 act-tools to execute.
    /// </summary>
    public bool EnableAutomation { get; set; } = false;
}
