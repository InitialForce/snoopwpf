namespace SnoopWPF.Agent.Contracts;

/// <summary>
/// Canonical suggestion strings used in <see cref="SnoopException"/>, keyed by <see cref="SnoopErrorCode"/>.
/// </summary>
public static class SnoopSuggestions
{
    public const string NodeNotFound =
        "Re-navigate from wpf_get_windows — element was likely garbage collected";

    public const string DispatcherBusy =
        "Retry the call; if repeated, the WPF app may be performing a long UI operation";

    public const string OperationTimedOut =
        "Reduce scope (smaller subtree, fewer properties) or retry when app is idle";

    public const string PropertyReadOnly =
        "This property cannot be set; use wpf_get_properties to find writable properties";

    public const string TypeConversionFailed =
        "Check value format; Color=#RRGGBB or named; Thickness=L,T,R,B; see tool description";

    public const string UnsupportedPropertyType =
        "Only primitive and common WPF value types are settable; see tool description for list";

    public const string MutationDisabled =
        "Mutations disabled; set EnableMutation=true in SnoopAgentOptions to allow changes";

    public const string PropertyRedacted =
        "This property is redacted for security; its value cannot be read or set";

    public const string SessionNotFound =
        "No active session; the target process may have exited";

    public const string ProtocolMismatch =
        "Agent and host protocol versions differ; update to matching versions";

    public const string ElementNotRenderable =
        "Element has zero size or is not visible; try wpf_get_windows for a full window screenshot instead";

    public const string BlobNotFound =
        "Blob has expired (default 60 s TTL; configurable via SnoopAgentOptions.BlobTtl) or the key is invalid; re-run the originating tool to get a fresh blobRef";

    public const string WaitForPropertyTimeout =
        "Call wpf_pump_until_idle before wpf_wait_for_property to ensure animations and bindings have settled";

    public const string InvalidArgument =
        "Check the parameter value; reduce timeoutMs to at most MaxWaitForPropertyMs (default 30000)";

    public const string CursorMismatch =
        "Cursor was issued for a different nodeId; re-fetch the first page without a cursor token";

    public const string AgentDisposed =
        "The SnoopInspector has been disposed; reconnect or restart the agent session";

    public const string InvalidState =
        "Operation is not valid in the current state; retry after the UI is ready";
}
