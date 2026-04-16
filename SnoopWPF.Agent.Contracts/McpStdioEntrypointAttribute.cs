namespace SnoopWPF.Agent.Contracts;

using System;

/// <summary>
/// Marks an assembly or class as the MCP stdio entrypoint. Assemblies/types carrying this
/// attribute own the process stdout for MCP transport. Any Console.Write* call in such a
/// context corrupts the MCP stream — the SWPF0001 analyzer enforces this at build time.
/// </summary>
/// <remarks>
/// Apply to: CoLocated targets, Brokered brokers (e.g. UiMcpHost), Injection hosts (snoop-mcp.exe).
/// Do NOT apply to Brokered targets — they do not own the MCP stdio stream.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = false)]
public sealed class McpStdioEntrypointAttribute : Attribute
{
}
