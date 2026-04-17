namespace SnoopWPF.Agent.Contracts;

/// <summary>
/// Error codes for <see cref="SnoopException"/>.
/// </summary>
public enum SnoopErrorCode
{
    NodeNotFound,
    DispatcherBusy,
    OperationTimedOut,
    PropertyReadOnly,
    TypeConversionFailed,
    UnsupportedPropertyType,
    MutationDisabled,
    PropertyRedacted,
    SessionNotFound,
    ProtocolMismatch,
    ElementNotRenderable,
    BlobNotFound,
    LocatorAmbiguous,
    LocatorInvalid,
    InvalidArgument,
    CursorMismatch,
    AgentDisposed,
    InvalidState,

    /// <summary>
    /// The configured audit log path is not writable and
    /// <see cref="SnoopAgentOptions.AllowAuditFallback"/> is <see langword="false"/>.
    /// Start-up is aborted to prevent a mutation session with no audit trail (FX6-D2).
    /// </summary>
    AuditUnwritable,

    /// <summary>
    /// The requested feature is not supported in the current target runtime (e.g.
    /// <c>EnableMutation=true</c> is not allowed in .NET Framework 4.6.2 injection mode
    /// because the audit subsystem requires .NET 6+ channels). (FX6-Z1)
    /// </summary>
    UnsupportedOnNet462,
}
