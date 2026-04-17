namespace SnoopWPF.Agent.BrokerHost;

using Microsoft.Extensions.DependencyInjection;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine.Blob;

/// <summary>
/// Registers the broker-side MCP tool proxies into a <see cref="IServiceCollection"/>.
/// Each tool proxy forwards its calls over the named pipe to the currently-connected
/// target process via <see cref="ISnoopInspector"/>.
/// </summary>
/// <remarks>
/// Downstream consumers (e.g. MC's <c>UiMcpHost</c>) can call
/// <see cref="AddBrokerToolProxies"/> and then register their own lifecycle tools on top.
/// </remarks>
public static class ToolProxyRegistrar
{
    /// <summary>
    /// Registers a singleton <see cref="ISnoopInspector"/> implementation, a broker-side
    /// <see cref="BlobStore"/> singleton, a <see cref="SnoopAgentOptions"/> singleton, and
    /// the 27 broker-side MCP tool proxies that delegate to the inspector.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="inspector">The proxy inspector that routes calls over the pipe.</param>
    /// <param name="agentOptions">
    /// Options controlling feature flags (blob TTL, mutation, redaction, etc.).
    /// When <see langword="null"/> a safe default instance is used.
    /// </param>
    /// <returns>The <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="BlobStore"/> is required by <c>CaptureScreenshotTool</c> and
    /// <c>FetchBlobTool</c>: even in brokered mode the broker-side tool receives PNG bytes
    /// from the target over the pipe, stores them locally in the BlobStore, and returns a
    /// blobRef to the LLM. <c>FetchBlobTool</c> then reads from the same broker-local store.
    /// </para>
    /// <para>
    /// <see cref="SnoopAgentOptions"/> is required by all mutation tools (8 total) for
    /// feature-flag enforcement (EnableMutation, MaxTier, EnableAutomation, etc.).
    /// </para>
    /// </remarks>
    public static IServiceCollection AddBrokerToolProxies(
        this IServiceCollection services,
        ISnoopInspector inspector,
        SnoopAgentOptions? agentOptions = null)
    {
        services.AddSingleton<ISnoopInspector>(inspector);

        // BlobStore: broker-local blob store used by CaptureScreenshotTool (stores PNG
        // bytes received from the target) and FetchBlobTool (reads them back on demand).
        services.AddSingleton<BlobStore>();

        // SnoopAgentOptions: feature-flag container consumed by all mutation tools.
        // Use caller-supplied options, or fall back to the safe defaults (mutation off,
        // redaction on, MaxTier = L0) so the broker is safe-by-default out of the box.
        services.AddSingleton(agentOptions ?? new SnoopAgentOptions());

        // Register all 29 tools from the SnoopWPF.Agent.Tools assembly.
        // Each tool takes ISnoopInspector (or BlobStore, SnoopAgentOptions) from DI
        // and routes calls over the pipe to the target.
        var toolsAssembly = typeof(SnoopWPF.Agent.Tools.SessionInfoTool).Assembly;
        services
            .AddMcpServer()
            .WithToolsFromAssembly(toolsAssembly);

        return services;
    }
}
