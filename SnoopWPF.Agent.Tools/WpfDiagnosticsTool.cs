namespace SnoopWPF.Agent.Tools;

using System;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine.Blob;

/// <summary>
/// MCP tool: wpf_diagnostics — returns a self-health snapshot of the running agent (FX6-D3)
/// together with the attached process's session info.
/// </summary>
/// <remarks>
/// Use this tool as a first step before any inspection session to verify the agent is
/// functional, the Dispatcher is responsive, and the BlobStore/audit subsystems are
/// operating within normal parameters. The <c>sessionInfo</c> field carries process/session
/// metadata (process name, PID, .NET version, dispatchers with window node IDs, capabilities,
/// top-level windows, mutation flag) obtained from the same Dispatcher probe.
/// </remarks>
[McpServerToolType]
public sealed class WpfDiagnosticsTool(
    ISnoopInspector inspector,
    BlobStore blobStore,
    SessionPolicy sessionPolicy,
    AgentStartInfo startInfo,
    IAuditDepthProvider? auditDepthProvider = null)
{
    [McpServerTool(Name = "wpf_diagnostics")]
    [Description("Agent self-health snapshot (version, mode, dispatcherHealthy, BlobStore fill, audit depth, " +
                 "sessionPolicy, uptime) plus the attached process's sessionInfo (process name, PID, .NET " +
                 "version, dispatchers with window node IDs, capabilities, windows, mutationEnabled). Call this " +
                 "first on every session to verify the agent and get the bootstrap window node IDs; sessionInfo " +
                 "is null when dispatcherHealthy is false. See docs/mcp-tools-reference.md.")]
    public async Task<string> GetDiagnosticsAsync(CancellationToken ct)
    {
        // Probe the dispatcher by calling a lightweight inspector method.
        // GetSessionInfoAsync does a Dispatcher round-trip; a timeout or SnoopException
        // with DispatcherBusy/SessionNotFound indicates the Dispatcher is unhealthy.
        // The same round-trip yields the session info folded into the response below.
        SessionInfoDto? sessionInfo = null;
        bool dispatcherHealthy;
        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(TimeSpan.FromMilliseconds(1000));
            sessionInfo = await inspector.GetSessionInfoAsync(probeCts.Token).ConfigureAwait(false);
            dispatcherHealthy = true;
        }
        catch (SnoopException ex) when (
            ex.Code == SnoopErrorCode.DispatcherBusy ||
            ex.Code == SnoopErrorCode.SessionNotFound ||
            ex.Code == SnoopErrorCode.OperationTimedOut)
        {
            dispatcherHealthy = false;
        }
        catch (OperationCanceledException)
        {
            dispatcherHealthy = false;
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        var dto = new AgentDiagnosticsDto
        {
            AgentVersion = version,
            Mode = sessionPolicy.Mode.ToString(),
            DispatcherHealthy = dispatcherHealthy,
            DispatcherQueueLength = 0, // Not publicly accessible via Dispatcher API
            BlobStoreCount = blobStore.Count,
            BlobStoreBytes = blobStore.TotalBytes,
            AuditLogDepth = auditDepthProvider?.PendingEntryCount ?? 0,
            SessionPolicy = new SessionPolicySnapshotDto
            {
                EnableMutation = sessionPolicy.EnableMutation,
                EnableAutomation = sessionPolicy.EnableAutomation,
                AllowSensitiveRetention = sessionPolicy.AllowSensitiveRetention,
            },
            UptimeSeconds = startInfo.UptimeSeconds,
            SessionInfo = sessionInfo,
        };

        return JsonSerializer.Serialize(dto, ToolSerializerOptions.Default);
    }
}
