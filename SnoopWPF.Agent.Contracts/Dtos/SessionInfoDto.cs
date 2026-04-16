namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// Compact summary of a top-level WPF window, inlined into <see cref="SessionInfoDto"/>.
/// </summary>
[DataContract]
public sealed class WindowSummaryDto
{
    /// <summary>Node ID of the window's root visual element.</summary>
    [DataMember(Name = "nodeId")]
    public string NodeId { get; init; } = string.Empty;

    /// <summary>Window title bar text.</summary>
    [DataMember(Name = "title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Actual rendered width of the window in device-independent pixels.</summary>
    [DataMember(Name = "width")]
    public double Width { get; init; }

    /// <summary>Actual rendered height of the window in device-independent pixels.</summary>
    [DataMember(Name = "height")]
    public double Height { get; init; }

    /// <summary>
    /// WpfLocator raw string (e.g. <c>$type:MainWindow</c>) that can be passed back to
    /// any locator-accepting tool call.
    /// </summary>
    [DataMember(Name = "locator")]
    public string Locator { get; init; } = string.Empty;
}

/// <summary>
/// Information about a single WPF Dispatcher in a session.
/// </summary>
[DataContract]
public sealed class DispatcherInfoDto
{
    /// <summary>Dispatcher ID, used to correlate windows to dispatchers.</summary>
    [DataMember(Name = "id")]
    public int Id { get; set; }

    /// <summary>Managed thread ID of the dispatcher's thread.</summary>
    [DataMember(Name = "threadId")]
    public int ThreadId { get; set; }

    /// <summary>Node IDs of all top-level windows owned by this dispatcher.</summary>
    [DataMember(Name = "windowNodeIds")]
    public List<string> WindowNodeIds { get; set; } = new List<string>();
}

/// <summary>
/// Session-level information returned by wpf_get_session_info.
/// </summary>
[DataContract]
public sealed class SessionInfoDto
{
    /// <summary>Name of the attached process executable (without extension).</summary>
    [DataMember(Name = "processName")]
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Operating-system process ID of the attached process.</summary>
    [DataMember(Name = "pid")]
    public int Pid { get; set; }

    /// <summary>.NET runtime version string (e.g. <c>8.0.5</c>).</summary>
    [DataMember(Name = "dotnetVersion")]
    public string DotnetVersion { get; set; } = string.Empty;

    /// <summary><see langword="true"/> when the session allows property mutation tools.</summary>
    [DataMember(Name = "mutationEnabled")]
    public bool MutationEnabled { get; set; }

    /// <summary>All WPF dispatchers running in the attached process.</summary>
    [DataMember(Name = "dispatchers")]
    public List<DispatcherInfoDto> Dispatchers { get; set; } = new List<DispatcherInfoDto>();

    /// <summary>Capability tokens advertised by the agent (e.g. <c>automation</c>, <c>audit</c>).</summary>
    [DataMember(Name = "capabilities")]
    public List<string> Capabilities { get; set; } = new List<string>();

    /// <summary>Compact summaries of all top-level windows, with locator strings for tool calls.</summary>
    [DataMember(Name = "windows")]
    public List<WindowSummaryDto> Windows { get; init; } = new List<WindowSummaryDto>();
}
