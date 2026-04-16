namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// A top-level WPF window.
/// </summary>
[DataContract]
public sealed class WindowDto
{
    /// <summary>Node ID of this window's root visual element.</summary>
    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Window title bar text.</summary>
    [DataMember(Name = "title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Fully-qualified CLR type name of the window class.</summary>
    [DataMember(Name = "typeName")]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Actual rendered width of the window in device-independent pixels.</summary>
    [DataMember(Name = "width")]
    public double Width { get; set; }

    /// <summary>Actual rendered height of the window in device-independent pixels.</summary>
    [DataMember(Name = "height")]
    public double Height { get; set; }

    /// <summary>ID of the WPF Dispatcher that owns this window.</summary>
    [DataMember(Name = "dispatcherId")]
    public int DispatcherId { get; set; }
}
