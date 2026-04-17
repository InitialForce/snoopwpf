namespace SnoopWPF.Agent.Contracts;

/// <summary>
/// Describes why a deterministic input strategy or act-tool invocation failed.
/// Used in <see cref="Dtos.DeterministicInputResult"/> and <see cref="Dtos.StateDeltaDto"/>.
/// </summary>
/// <remarks>Per FD-3 (PRD §7).</remarks>
public enum FailureReason
{
    /// <summary>The targeted element could not be located.</summary>
    ElementNotFound = 0,

    /// <summary>The targeted element exists but is not visible on screen.</summary>
    ElementNotVisible = 1,

    /// <summary>The targeted element is visible but disabled (IsEnabled=false).</summary>
    ElementNotEnabled = 2,

    /// <summary>The routed/relay command cannot execute (CanExecute returned false).</summary>
    CannotExecuteCommand = 3,

    /// <summary>The intent requires Automation (EnableAutomation=false).</summary>
    AutomationDisabled = 4,

    /// <summary>The intent requires mutation (EnableMutation=false).</summary>
    MutationDisabled = 5,

    /// <summary>The best-matching strategy's tier exceeds the session's MaxTier.</summary>
    TierMismatch = 6,

    /// <summary>The operation completed but the target element's state did not change.</summary>
    StateUnchanged = 7,

    /// <summary>The locator matched more than one element.</summary>
    LocatorAmbiguous = 8,

    /// <summary>The WPF dispatcher was too busy to process the request.</summary>
    DispatcherBusy = 9,

    /// <summary>The element is outside the visible viewport.</summary>
    ElementOutsideViewport = 10,

    /// <summary>The required UI Automation pattern is not supported by the element.</summary>
    PatternNotSupported = 11,

    /// <summary>The target process is not running or is unreachable.</summary>
    TargetNotRunning = 12,

    /// <summary>
    /// The UI Automation pattern is supported but the element is disabled (IsEnabled=false).
    /// Returned when <c>ElementNotEnabledException</c> is thrown by Invoke/Toggle/Expand/Collapse.
    /// Distinct from <see cref="PatternNotSupported"/> which means the pattern is absent.
    /// </summary>
    ElementDisabled = 13,

    /// <summary>
    /// The requested blob reference key was not found in the BlobStore, or its TTL has expired.
    /// Re-run the originating tool to get a fresh reference.
    /// </summary>
    BlobNotFound = 14,
}
