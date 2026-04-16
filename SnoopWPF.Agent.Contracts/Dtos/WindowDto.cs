namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// A top-level WPF window.
/// </summary>
[DataContract]
public sealed class WindowDto
{
    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [DataMember(Name = "title")]
    public string Title { get; set; } = string.Empty;

    [DataMember(Name = "typeName")]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Name = "width")]
    public double Width { get; set; }

    [DataMember(Name = "height")]
    public double Height { get; set; }

    [DataMember(Name = "dispatcherId")]
    public int DispatcherId { get; set; }
}
