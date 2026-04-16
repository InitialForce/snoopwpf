namespace SnoopWPF.Agent.Contracts.Protocol;

using System.Runtime.Serialization;

/// <summary>
/// A cancellation frame sent from the host to cancel an in-flight request.
/// </summary>
[DataContract]
public sealed class PipeCancelPayload
{
    [DataMember(Name = "id")]
    public int Id { get; set; }

    /// <summary>
    /// Always true on wire.
    /// </summary>
    [DataMember(Name = "cancel")]
    public bool Cancel { get; set; }
}
