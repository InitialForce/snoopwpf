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
/// MCP tool: wpf_diagnostics — returns a self-health snapshot of the running agent (FX6-D3).
/// </summary>
/// <remarks>
/// Use this tool as a first step before any inspection session to verify the agent is
/// functional, the Dispatcher is responsive, and the BlobStore/audit subsystems are
/// operating within normal parameters.
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
    [Description("Returns a self-health snapshot of the running agent: version, mode, Dispatcher health, " +
                 "BlobStore fill, audit log queue depth, session policy, and uptime in seconds. " +
                 "Call this as a first step to verify the agent is functional before starting an inspection.")]
    public async Task<string> GetDiagnosticsAsync(CancellationToken ct)
    {
        // Probe the dispatcher by calling a lightweight inspector method.
        // GetSessionInfoAsync does a Dispatcher round-trip; a timeout or SnoopException
        // with DispatcherBusy/SessionNotFound indicates the Dispatcher is unhealthy.
        bool dispatcherHealthy;
        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(TimeSpan.FromMilliseconds(1000));
            await inspector.GetSessionInfoAsync(probeCts.Token).ConfigureAwait(false);
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
        };

        return JsonSerializer.Serialize(dto, ToolSerializerOptions.Default);
    }
}
