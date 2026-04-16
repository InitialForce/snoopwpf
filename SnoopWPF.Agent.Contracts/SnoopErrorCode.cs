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
}
