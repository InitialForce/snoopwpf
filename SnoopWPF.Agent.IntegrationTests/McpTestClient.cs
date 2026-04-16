namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine;

/// <summary>
/// In-process test helper that creates a <see cref="SnoopInspector"/> backed by a
/// <see cref="TestWpfApp"/> and calls inspector methods directly without the MCP wire
/// protocol overhead.
///
/// For integration tests we call ISnoopInspector directly rather than going through the
/// full MCP server stack. This avoids the stdio/pipe transport complexity while still
/// exercising the entire inspection engine end-to-end.
/// </summary>
/// <remarks>
/// If full MCP wire-protocol coverage is required (e.g., schema validation), use
/// <see cref="PipeClientFixture"/> which exercises the pipe transport end-to-end.
/// </remarks>
public sealed class McpTestClient : IDisposable
{
    private readonly TestWpfApp wpfApp;
    private readonly SnoopInspector inspector;
    private bool disposed;

    /// <summary>
    /// Initializes a new <see cref="McpTestClient"/> backed by the given
    /// <see cref="TestWpfApp"/>.
    /// </summary>
    /// <param name="wpfApp">The WPF application under test.</param>
    public McpTestClient(TestWpfApp wpfApp)
    {
        this.wpfApp = wpfApp ?? throw new ArgumentNullException(nameof(wpfApp));

        var options = new SnoopInspectorOptions
        {
            TimeoutMs = 10_000,
            EnableMutation = true,   // Allow mutation in tests
            EnableRedaction = false, // Expose all values so tests can assert them
        };

        this.inspector = new SnoopInspector(
            dispatcher: this.wpfApp.Dispatcher,
            rootTarget: this.wpfApp.App,
            options: options);
    }

    /// <summary>
    /// Gets the underlying <see cref="SnoopInspector"/> for direct invocation.
    /// </summary>
    public SnoopInspector Inspector => this.inspector;

    // -------------------------------------------------------------------------
    // Convenience helpers — thin wrappers over ISnoopInspector
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns session info for the test application.
    /// </summary>
    public Task<Contracts.Dtos.SessionInfoDto> GetSessionInfoAsync(
        CancellationToken ct = default)
        => this.inspector.GetSessionInfoAsync(ct);

    /// <summary>
    /// Returns all visible windows in the test application.
    /// </summary>
    public Task<List<Contracts.Dtos.WindowDto>> GetWindowsAsync(
        bool includeHidden = false,
        CancellationToken ct = default)
        => this.inspector.GetWindowsAsync(includeHidden, ct);

    /// <summary>
    /// Returns a page of children for the given node (or the root if null).
    /// </summary>
    public Task<CursorPage<Contracts.Dtos.NodeDto>> GetChildrenAsync(
        string? nodeId = null,
        string treeType = "visual",
        string? cursor = null,
        int take = 50,
        CancellationToken ct = default)
        => this.inspector.GetChildrenAsync(nodeId, treeType, cursor, take, ct);

    /// <summary>
    /// Returns the visual tree rooted at the given node.
    /// </summary>
    public Task<Contracts.Dtos.VisualTreeResultDto> GetVisualTreeAsync(
        string? rootNodeId = null,
        int maxDepth = 5,
        string treeType = "visual",
        List<string>? includeProperties = null,
        CancellationToken ct = default)
        => this.inspector.GetVisualTreeAsync(rootNodeId, maxDepth, treeType, includeProperties, ct);

    /// <summary>
    /// Returns properties for the given node.
    /// </summary>
    public Task<CursorPage<Contracts.Dtos.PropertyDto>> GetPropertiesAsync(
        string nodeId,
        string? filter = null,
        string? category = null,
        bool includeDefaults = true,
        string? cursor = null,
        int take = 100,
        CancellationToken ct = default)
        => this.inspector.GetPropertiesAsync(nodeId, filter, category, includeDefaults, cursor, take, ct);

    /// <summary>
    /// Returns binding information for a property on a node.
    /// </summary>
    public Task<Contracts.Dtos.BindingInfoDto> GetBindingInfoAsync(
        string nodeId,
        string propertyName,
        CancellationToken ct = default)
        => this.inspector.GetBindingInfoAsync(nodeId, propertyName, ct);

    /// <summary>
    /// Returns the full binding chain resolution for a property on a node (M2-08).
    /// </summary>
    public Task<Contracts.Dtos.BindingResolutionDto> ResolveBindingAsync(
        string nodeId,
        string propertyName,
        CancellationToken ct = default)
        => this.inspector.ResolveBindingAsync(nodeId, propertyName, ct);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.inspector.Dispose();
    }
}
