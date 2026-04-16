namespace SnoopWPF.Agent.Host;

using CommandLine;

/// <summary>
/// Command-line options for the <c>snoop-mcp</c> host executable.
/// </summary>
public sealed class CommandLineOptions
{
    /// <summary>
    /// Target process ID. Mutually exclusive with <see cref="WindowTitle"/>.
    /// </summary>
    [Option("pid", Required = false, HelpText = "Target process ID to inject into.")]
    public int? Pid { get; set; }

    /// <summary>
    /// Find target process by window title pattern (case-insensitive substring match).
    /// Mutually exclusive with <see cref="Pid"/>.
    /// </summary>
    [Option("window-title", Required = false, HelpText = "Find target process by window title (case-insensitive substring match).")]
    public string? WindowTitle { get; set; }

    /// <summary>
    /// Handshake timeout in seconds. Defaults to 30.
    /// </summary>
    [Option("timeout", Required = false, Default = 30, HelpText = "Handshake timeout in seconds (default: 30).")]
    public int Timeout { get; set; }

    /// <summary>
    /// Log verbose output to stderr.
    /// </summary>
    [Option("verbose", Required = false, Default = false, HelpText = "Enable verbose logging to stderr.")]
    public bool Verbose { get; set; }
}
