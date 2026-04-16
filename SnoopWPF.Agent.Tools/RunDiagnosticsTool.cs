namespace SnoopWPF.Agent.Tools;

using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_run_diagnostics — run diagnostic providers and return issues found.
/// </summary>
[McpServerToolType]
public sealed class RunDiagnosticsTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_run_diagnostics")]
    [Description("Run diagnostic providers and return issues found in the WPF application. " +
                 "Each result includes name, description, area, level (Error/Warning/Info), nodeId, and nodePath. " +
                 "Results are cursor-paginated. Filter by nodeId to scope to a subtree, by providers to run only " +
                 "specific diagnostic checks, or by minLevel to surface only high-severity issues.")]
    public async Task<string> RunDiagnosticsAsync(
        [Description("Node ID to scope diagnostics to a subtree (omit for full application scan).")] string? nodeId = null,
        [Description("List of diagnostic provider names to run (omit for all providers).")] List<string>? providers = null,
        [Description("Minimum severity level to include: \"Error\", \"Warning\", or \"Info\". Default: \"Info\" (all levels).")] string? minLevel = null,
        [Description("Cursor from previous response for pagination (omit for first page).")] string? cursor = null,
        [Description("Number of results to return. Default: 100, max: 200.")] int take = 100,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.RunDiagnosticsAsync(nodeId, providers, minLevel, cursor, take, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
