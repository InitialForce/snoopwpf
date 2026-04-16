namespace SnoopWPF.Agent.Server;

using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Internal wiring: builds and runs the MCP server.
/// </summary>
internal static class McpServerSetup
{
    /// <summary>
    /// Builds the MCP server's service collection with all tools registered,
    /// then starts the server loop on a background thread.
    /// </summary>
    internal static Task RunServerAsync(
        ISnoopInspector inspector,
        SnoopAgentOptions options,
        CancellationToken ct)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "snoop-wpf",
                Version = version,
            },
        };

        switch (options.Transport)
        {
            case TransportMode.Stdio:
                return RunWithStdioAsync(inspector, serverOptions, ct);

            case TransportMode.Pipe:
                var pipeName = options.PipeName ?? $"snoop-agent-{Environment.ProcessId}";
                return RunWithPipeAsync(inspector, serverOptions, pipeName, ct);

            default:
                throw new ArgumentOutOfRangeException(nameof(options), $"Unknown transport: {options.Transport}");
        }
    }

    private static async Task RunWithStdioAsync(
        ISnoopInspector inspector,
        McpServerOptions serverOptions,
        CancellationToken ct)
    {
        var services = BuildServiceCollection(inspector);
        var sp = services.BuildServiceProvider();

        // StdioServerTransport reads from Console.In / writes to Console.Out.
        var transport = new StdioServerTransport(serverOptions);
        await using var server = McpServer.Create(transport, serverOptions, serviceProvider: sp);
        await server.RunAsync(ct).ConfigureAwait(false);
    }

    private static async Task RunWithPipeAsync(
        ISnoopInspector inspector,
        McpServerOptions serverOptions,
        string pipeName,
        CancellationToken ct)
    {
        var services = BuildServiceCollection(inspector);
        var sp = services.BuildServiceProvider();

        await using var pipeServer = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        Console.WriteLine($"SnoopWPF.Agent MCP server waiting on pipe: {pipeName}");
        await pipeServer.WaitForConnectionAsync(ct).ConfigureAwait(false);

        var transport = new StreamServerTransport(pipeServer, pipeServer);
        await using var server = McpServer.Create(transport, serverOptions, serviceProvider: sp);
        await server.RunAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a <see cref="IServiceCollection"/> with <see cref="ISnoopInspector"/> and all tool types
    /// from <c>SnoopWPF.Agent.Tools</c> registered.
    /// </summary>
    private static IServiceCollection BuildServiceCollection(ISnoopInspector inspector)
    {
        var services = new ServiceCollection();

        // Register ISnoopInspector so tool constructors can receive it via DI.
        services.AddSingleton<ISnoopInspector>(inspector);

        // Register every tool class from the Tools assembly via the MCP builder.
        var toolsAssembly = typeof(SnoopWPF.Agent.Tools.SessionInfoTool).Assembly;
        services
            .AddMcpServer()
            .WithToolsFromAssembly(toolsAssembly);

        return services;
    }
}
