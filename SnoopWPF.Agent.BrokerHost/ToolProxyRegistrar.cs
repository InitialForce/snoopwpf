namespace SnoopWPF.Agent.BrokerHost;

using Microsoft.Extensions.DependencyInjection;
using SnoopWPF.Agent.Contracts;

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
    /// Registers a singleton <see cref="ISnoopInspector"/> implementation and the
    /// 18 broker-side MCP tool proxies that delegate to it.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="inspector">The proxy inspector that routes calls over the pipe.</param>
    /// <returns>The <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddBrokerToolProxies(
        this IServiceCollection services,
        ISnoopInspector inspector)
    {
        services.AddSingleton<ISnoopInspector>(inspector);

        // Register all 18 tools from the SnoopWPF.Agent.Tools assembly.
        // Each tool takes ISnoopInspector (or BlobStore) from DI and routes calls
        // over the pipe to the target.
        var toolsAssembly = typeof(SnoopWPF.Agent.Tools.SessionInfoTool).Assembly;
        services
            .AddMcpServer()
            .WithToolsFromAssembly(toolsAssembly);

        return services;
    }
}
