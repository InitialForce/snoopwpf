namespace SnoopWPF.Agent.Remote;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Contracts.Protocol;

/// <summary>
/// Host-side proxy that implements <see cref="ISnoopInspector"/> by forwarding calls
/// over a named pipe to the injected <c>SnoopWPF.Agent.Injection</c> running inside
/// the target process.
/// </summary>
/// <remarks>
/// Thread safety: multiple callers may invoke methods concurrently. Requests are
/// serialised over the pipe using an internal send-lock. Each in-flight request is
/// tracked by a monotonically-increasing integer ID; the response pump matches
/// responses to waiters via a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class PipeSnoopInspectorProxy : ISnoopInspector, IAsyncDisposable, IDisposable
{
    // --------------------------------------------------------------------------
    // State
    // --------------------------------------------------------------------------

    private readonly PipeConnection connection;
    private readonly int operationTimeoutMs;
    private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
    private readonly ConcurrentDictionary<int, PendingRequest> pending = new ConcurrentDictionary<int, PendingRequest>();
    private int nextId;
    private volatile bool disconnected;
    private Task? pumpTask;
    private CancellationTokenSource? pumpCts;

    // --------------------------------------------------------------------------
    // Construction
    // --------------------------------------------------------------------------

    /// <summary>
    /// Creates a proxy over an already-connected and already-handshaked <see cref="PipeConnection"/>.
    /// Call <see cref="StartPump"/> immediately after construction to begin receiving responses.
    /// </summary>
    /// <param name="connection">An open, post-handshake pipe connection.</param>
    /// <param name="operationTimeoutMs">Per-operation timeout in milliseconds (default 10 000).</param>
    public PipeSnoopInspectorProxy(PipeConnection connection, int operationTimeoutMs = 10_000)
    {
        this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
        this.operationTimeoutMs = operationTimeoutMs;
    }

    // --------------------------------------------------------------------------
    // Public lifecycle
    // --------------------------------------------------------------------------

    /// <summary>
    /// Starts the background response pump.
    /// Must be called before any <see cref="ISnoopInspector"/> method.
    /// </summary>
    public void StartPump()
    {
        this.pumpCts = new CancellationTokenSource();
        this.pumpTask = Task.Run(() => this.ResponsePumpAsync(this.pumpCts.Token));
    }

    // --------------------------------------------------------------------------
    // ISnoopInspector — every method delegates to InvokeAsync<TResult>.
    // --------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<SessionInfoDto> GetSessionInfoAsync(CancellationToken ct)
        => this.InvokeAsync<SessionInfoDto>("GetSessionInfo", new { }, ct);

    /// <inheritdoc/>
    public Task<List<WindowDto>> GetWindowsAsync(bool includeHidden, CancellationToken ct)
        => this.InvokeAsync<List<WindowDto>>("GetWindows", new { includeHidden }, ct);

    /// <inheritdoc/>
    public Task<VisualTreeResultDto> GetVisualTreeAsync(
        string? rootNodeId,
        int maxDepth,
        string treeType,
        List<string>? includeProperties,
        CancellationToken ct)
        => this.InvokeAsync<VisualTreeResultDto>(
            "GetVisualTree",
            new { rootNodeId, maxDepth, treeType, includeProperties },
            ct);

    /// <inheritdoc/>
    public Task<CursorPage<NodeDto>> GetChildrenAsync(
        string? nodeId,
        string treeType,
        string? cursor,
        int take,
        CancellationToken ct)
        => this.InvokeAsync<CursorPage<NodeDto>>(
            "GetChildren",
            new { nodeId, treeType, cursor, take },
            ct);

    /// <inheritdoc/>
    public Task<List<AncestorDto>> GetAncestorsAsync(
        string nodeId,
        int? maxLevels,
        CancellationToken ct)
        => this.InvokeAsync<List<AncestorDto>>(
            "GetAncestors",
            new { nodeId, maxLevels },
            ct);

    /// <inheritdoc/>
    public Task<FindElementResultDto> FindElementsAsync(
        string? typeName,
        string? name,
        string? rootNodeId,
        List<PropertyConditionDto>? conditions,
        string treeType,
        int maxResults,
        CancellationToken ct)
        => this.InvokeAsync<FindElementResultDto>(
            "FindElements",
            new { typeName, name, rootNodeId, conditions, treeType, maxResults },
            ct);

    /// <inheritdoc/>
    public Task<InspectElementDto> InspectElementAsync(string nodeId, CancellationToken ct)
        => this.InvokeAsync<InspectElementDto>("InspectElement", new { nodeId }, ct);

    /// <inheritdoc/>
    public Task<CursorPage<PropertyDto>> GetPropertiesAsync(
        string nodeId,
        string? filter,
        string? category,
        bool includeDefaults,
        string? cursor,
        int take,
        CancellationToken ct)
        => this.InvokeAsync<CursorPage<PropertyDto>>(
            "GetProperties",
            new { nodeId, filter, category, includeDefaults, cursor, take },
            ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> SetPropertyAsync(
        string nodeId,
        string propertyName,
        string value,
        CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>(
            "SetProperty",
            new { nodeId, propertyName, value },
            ct);

    /// <inheritdoc/>
    public Task<BindingInfoDto> GetBindingInfoAsync(
        string nodeId,
        string propertyName,
        CancellationToken ct)
        => this.InvokeAsync<BindingInfoDto>(
            "GetBindingInfo",
            new { nodeId, propertyName },
            ct);

    /// <inheritdoc/>
    public Task<CursorPage<DiagnosticItemDto>> RunDiagnosticsAsync(
        string? nodeId,
        List<string>? providers,
        string? minLevel,
        string? cursor,
        int take,
        CancellationToken ct)
        => this.InvokeAsync<CursorPage<DiagnosticItemDto>>(
            "RunDiagnostics",
            new { nodeId, providers, minLevel, cursor, take },
            ct);

    /// <inheritdoc/>
    public Task<CursorPage<ResourceDto>> GetResourcesAsync(
        string? nodeId,
        string? resourceKey,
        string? cursor,
        int take,
        CancellationToken ct)
        => this.InvokeAsync<CursorPage<ResourceDto>>(
            "GetResources",
            new { nodeId, resourceKey, cursor, take },
            ct);

    /// <inheritdoc/>
    public Task<ScreenshotResultDto> CaptureScreenshotAsync(string? nodeId, CancellationToken ct)
        => this.InvokeAsync<ScreenshotResultDto>("CaptureScreenshot", new { nodeId }, ct);

    /// <inheritdoc/>
    public Task<List<TriggerDto>> GetTriggersAsync(string nodeId, CancellationToken ct)
        => this.InvokeAsync<List<TriggerDto>>("GetTriggers", new { nodeId }, ct);

    /// <inheritdoc/>
    public Task<List<BehaviorDto>> GetBehaviorsAsync(string nodeId, CancellationToken ct)
        => this.InvokeAsync<List<BehaviorDto>>("GetBehaviors", new { nodeId }, ct);

    /// <inheritdoc/>
    public Task<ActionablesResultDto> GetActionablesAsync(string? rootNodeId, int maxResults, CancellationToken ct)
        => this.InvokeAsync<ActionablesResultDto>("GetActionables", new { rootNodeId, maxResults }, ct);

    // --------------------------------------------------------------------------
    // IDisposable / IAsyncDisposable
    // --------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Dispose()
    {
        this.pumpCts?.Cancel();
        this.pumpCts?.Dispose();
        this.connection.Dispose();
        this.sendLock.Dispose();

        this.FailAllPending(new SnoopException(
            SnoopErrorCode.SessionNotFound,
            "Pipe connection was disposed."));
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        this.pumpCts?.Cancel();

        if (this.pumpTask is not null)
        {
            try
            {
                await this.pumpTask.ConfigureAwait(false);
            }
            catch
            {
                // pump task may throw on cancellation — that's expected.
            }
        }

        this.pumpCts?.Dispose();
        this.connection.Dispose();
        this.sendLock.Dispose();

        this.FailAllPending(new SnoopException(
            SnoopErrorCode.SessionNotFound,
            "Pipe connection was disposed."));
    }

    // --------------------------------------------------------------------------
    // Core request/response machinery
    // --------------------------------------------------------------------------

    private async Task<TResult> InvokeAsync<TResult>(
        string method,
        object parameters,
        CancellationToken callerCt)
    {
        this.ThrowIfDisconnected();

        int id = Interlocked.Increment(ref this.nextId);
        string paramsJson = FramedJsonTransport.SerializeJson(parameters);

        var request = new PipeRequest
        {
            Id = id,
            Method = method,
            ParamsJson = paramsJson,
        };

        // Combine caller CancellationToken with a per-operation timeout.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(this.operationTimeoutMs));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerCt, timeoutCts.Token);

        var pendingRequest = new PendingRequest();
        this.pending[id] = pendingRequest;

        try
        {
            // Send the request (serialised — only one writer at a time).
            await this.sendLock.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            try
            {
                await this.connection.SendRequestAsync(request, linkedCts.Token).ConfigureAwait(false);
            }
            finally
            {
                this.sendLock.Release();
            }

            // Wait for the response pump to deliver a response.
            PipeResponse response;
            try
            {
                response = await pendingRequest.Completion.Task
                    .WaitAsync(linkedCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !callerCt.IsCancellationRequested)
            {
                // Our timeout fired — send cancel frame and throw OperationTimedOut.
                await this.SendCancelFrameAsync(id).ConfigureAwait(false);
                throw new SnoopException(
                    SnoopErrorCode.OperationTimedOut,
                    $"Pipe operation '{method}' timed out after {this.operationTimeoutMs}ms.",
                    suggestions: new[] { SnoopSuggestions.OperationTimedOut });
            }
            catch (OperationCanceledException) when (callerCt.IsCancellationRequested)
            {
                // Caller cancelled — send cancel frame.
                await this.SendCancelFrameAsync(id).ConfigureAwait(false);
                throw;
            }

            // Handle error payload.
            if (response.Error is PipeErrorPayload err)
            {
                throw MapPipeError(err);
            }

            // Deserialize result.
            if (response.ResultJson is null)
            {
                throw new SnoopException(
                    SnoopErrorCode.ProtocolMismatch,
                    $"Agent returned null resultJson for method '{method}' with no error.");
            }

            TResult? result = FramedJsonTransport.DeserializeJson<TResult>(response.ResultJson);
            if (result is null)
            {
                throw new SnoopException(
                    SnoopErrorCode.ProtocolMismatch,
                    $"Agent returned null result for method '{method}'.");
            }

            return result;
        }
        finally
        {
            this.pending.TryRemove(id, out _);
        }
    }

    private async Task SendCancelFrameAsync(int id)
    {
        try
        {
            var cancel = new PipeCancelPayload { Id = id, Cancel = true };

            // Best-effort: use a short independent timeout; do not propagate errors.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await this.sendLock.WaitAsync(cts.Token).ConfigureAwait(false);
            try
            {
                await this.connection.SendCancelAsync(cancel, cts.Token).ConfigureAwait(false);
            }
            finally
            {
                this.sendLock.Release();
            }
        }
        catch
        {
            // Best-effort — ignore all errors sending cancel frame.
        }
    }

    // --------------------------------------------------------------------------
    // Response pump (background task)
    // --------------------------------------------------------------------------

    private async Task ResponsePumpAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                PipeResponse? response;
                try
                {
                    response = await this.connection.ReceiveResponseAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (EndOfStreamException)
                {
                    // Peer disconnected cleanly.
                    break;
                }
                catch (IOException)
                {
                    // Pipe broken.
                    break;
                }
                catch (InvalidOperationException)
                {
                    // Protocol violation — malformed frame. Treat as disconnect.
                    break;
                }

                if (response is null)
                {
                    // EOF — peer closed.
                    break;
                }

                // Deliver to the matching waiter.
                if (this.pending.TryRemove(response.Id, out PendingRequest? pendingRequest))
                {
                    pendingRequest.Completion.TrySetResult(response);
                }

                // If no waiter, the response is a late response after timeout — ignore it.
            }
        }
        finally
        {
            // Mark as disconnected so new calls throw SessionNotFound immediately.
            this.disconnected = true;

            this.FailAllPending(new SnoopException(
                SnoopErrorCode.SessionNotFound,
                "The named pipe connection to the target process was lost. " +
                "The target process may have exited."));
        }
    }

    // --------------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------------

    private void ThrowIfDisconnected()
    {
        if (this.disconnected)
        {
            throw new SnoopException(
                SnoopErrorCode.SessionNotFound,
                "No active session; the target process may have exited.",
                suggestions: new[] { SnoopSuggestions.SessionNotFound });
        }
    }

    private void FailAllPending(Exception ex)
    {
        foreach (var pair in this.pending)
        {
            if (this.pending.TryRemove(pair.Key, out PendingRequest? pendingRequest))
            {
                pendingRequest.Completion.TrySetException(ex);
            }
        }
    }

    private static SnoopException MapPipeError(PipeErrorPayload err)
    {
        SnoopErrorCode code = Enum.TryParse<SnoopErrorCode>(err.Code, ignoreCase: true, out SnoopErrorCode parsed)
            ? parsed
            : SnoopErrorCode.ProtocolMismatch;

        string[] suggestions = string.IsNullOrEmpty(err.Suggestion)
            ? Array.Empty<string>()
            : new[] { err.Suggestion };

        return new SnoopException(code, err.Message, suggestions: suggestions);
    }

    // ── WpfLocator overloads (M1-06) ──

    /// <inheritdoc/>
    public Task<VisualTreeResultDto> GetVisualTreeAsync(WpfLocator locator, int maxDepth, string treeType, List<string>? includeProperties, CancellationToken ct)
        => this.InvokeAsync<VisualTreeResultDto>(
            "GetVisualTreeByLocator",
            new { locator, maxDepth, treeType, includeProperties },
            ct);

    /// <inheritdoc/>
    public Task<CursorPage<NodeDto>> GetChildrenAsync(WpfLocator locator, string treeType, string? cursor, int take, CancellationToken ct)
        => this.InvokeAsync<CursorPage<NodeDto>>(
            "GetChildrenByLocator",
            new { locator, treeType, cursor, take },
            ct);

    /// <inheritdoc/>
    public Task<List<AncestorDto>> GetAncestorsAsync(WpfLocator locator, int? maxLevels, CancellationToken ct)
        => this.InvokeAsync<List<AncestorDto>>(
            "GetAncestorsByLocator",
            new { locator, maxLevels },
            ct);

    /// <inheritdoc/>
    public Task<InspectElementDto> InspectElementAsync(WpfLocator locator, CancellationToken ct)
        => this.InvokeAsync<InspectElementDto>(
            "InspectElementByLocator",
            new { locator },
            ct);

    /// <inheritdoc/>
    public Task<CursorPage<PropertyDto>> GetPropertiesAsync(WpfLocator locator, string? filter, string? category, bool includeDefaults, string? cursor, int take, CancellationToken ct)
        => this.InvokeAsync<CursorPage<PropertyDto>>(
            "GetPropertiesByLocator",
            new { locator, filter, category, includeDefaults, cursor, take },
            ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> SetPropertyAsync(WpfLocator locator, string propertyName, string value, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>(
            "SetPropertyByLocator",
            new { locator, propertyName, value },
            ct);

    /// <inheritdoc/>
    public Task<BindingInfoDto> GetBindingInfoAsync(WpfLocator locator, string propertyName, CancellationToken ct)
        => this.InvokeAsync<BindingInfoDto>(
            "GetBindingInfoByLocator",
            new { locator, propertyName },
            ct);

    /// <inheritdoc/>
    public Task<CursorPage<DiagnosticItemDto>> RunDiagnosticsAsync(WpfLocator locator, List<string>? providers, string? minLevel, string? cursor, int take, CancellationToken ct)
        => this.InvokeAsync<CursorPage<DiagnosticItemDto>>(
            "RunDiagnosticsByLocator",
            new { locator, providers, minLevel, cursor, take },
            ct);

    /// <inheritdoc/>
    public Task<CursorPage<ResourceDto>> GetResourcesAsync(WpfLocator locator, string? resourceKey, string? cursor, int take, CancellationToken ct)
        => this.InvokeAsync<CursorPage<ResourceDto>>(
            "GetResourcesByLocator",
            new { locator, resourceKey, cursor, take },
            ct);

    /// <inheritdoc/>
    public Task<ScreenshotResultDto> CaptureScreenshotAsync(WpfLocator locator, CancellationToken ct)
        => this.InvokeAsync<ScreenshotResultDto>(
            "CaptureScreenshotByLocator",
            new { locator },
            ct);

    /// <inheritdoc/>
    public Task<List<TriggerDto>> GetTriggersAsync(WpfLocator locator, CancellationToken ct)
        => this.InvokeAsync<List<TriggerDto>>(
            "GetTriggersByLocator",
            new { locator },
            ct);

    /// <inheritdoc/>
    public Task<List<BehaviorDto>> GetBehaviorsAsync(WpfLocator locator, CancellationToken ct)
        => this.InvokeAsync<List<BehaviorDto>>(
            "GetBehaviorsByLocator",
            new { locator },
            ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> SelectItemAsync(string nodeId, string identifier, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>("SelectItem", new { nodeId, identifier }, ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> SelectItemAsync(WpfLocator locator, string identifier, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>(
            "SelectItemByLocator",
            new { locator, identifier },
            ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> SetCheckStateAsync(string nodeId, string state, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>("SetCheckState", new { nodeId, state }, ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> SetCheckStateAsync(WpfLocator locator, string state, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>(
            "SetCheckStateByLocator",
            new { locator, state },
            ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> SetTextValueAsync(string nodeId, string value, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>("SetTextValue", new { nodeId, value }, ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> SetTextValueAsync(WpfLocator locator, string value, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>(
            "SetTextValueByLocator",
            new { locator, value },
            ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> SetSliderValueAsync(string nodeId, double value, bool normalized, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>("SetSliderValue", new { nodeId, value, normalized }, ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> SetSliderValueAsync(WpfLocator locator, double value, bool normalized, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>(
            "SetSliderValueByLocator",
            new { locator, value, normalized },
            ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> ExecuteCommandAsync(string nodeId, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>("ExecuteCommand", new { nodeId }, ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> ExecuteCommandAsync(WpfLocator locator, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>(
            "ExecuteCommandByLocator",
            new { locator },
            ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> ClickAsync(string nodeId, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>("Click", new { nodeId }, ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> ClickAsync(WpfLocator locator, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>(
            "ClickByLocator",
            new { locator },
            ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> ToggleAsync(string nodeId, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>("Toggle", new { nodeId }, ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> ToggleAsync(WpfLocator locator, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>(
            "ToggleByLocator",
            new { locator },
            ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> ExpandCollapseAsync(string nodeId, string action, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>("ExpandCollapse", new { nodeId, action }, ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> ExpandCollapseAsync(WpfLocator locator, string action, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>(
            "ExpandCollapseByLocator",
            new { locator, action },
            ct);

    /// <inheritdoc/>
    public Task<BindingResolutionDto> ResolveBindingAsync(string nodeId, string propertyName, CancellationToken ct)
        => this.InvokeAsync<BindingResolutionDto>("ResolveBinding", new { nodeId, propertyName }, ct);

    /// <inheritdoc/>
    public Task<BindingResolutionDto> ResolveBindingAsync(WpfLocator locator, string propertyName, CancellationToken ct)
        => this.InvokeAsync<BindingResolutionDto>(
            "ResolveBindingByLocator",
            new { locator, propertyName },
            ct);

    /// <inheritdoc/>
    public Task<WaitForPropertyResultDto> WaitForPropertyAsync(
        WpfLocator locator, string propertyName, string? expectedValue, int timeoutMs, string presenceExpected, CancellationToken ct)
        => this.InvokeAsync<WaitForPropertyResultDto>(
            "WaitForProperty",
            new { locator, propertyName, expectedValue, timeoutMs, presenceExpected },
            ct);

    /// <inheritdoc/>
    public Task<PollChangesResultDto> PollChangesAsync(long sinceVersion, WpfLocator? rootLocator, CancellationToken ct)
        => this.InvokeAsync<PollChangesResultDto>(
            "PollChanges",
            new { sinceVersion, rootLocator },
            ct);

    /// <inheritdoc/>
    public Task<PumpUntilIdleResultDto> PumpUntilIdleAsync(int timeoutMs, IReadOnlyList<string>? resources, CancellationToken ct)
        => this.InvokeAsync<PumpUntilIdleResultDto>(
            "PumpUntilIdle",
            new { timeoutMs, resources },
            ct);

    // ── WS3: wpf_double_click, wpf_select_item_by_scroll, wpf_select_item_by_index, wpf_get_list_items ──

    /// <inheritdoc/>
    public Task<StateDeltaDto> DoubleClickAsync(string nodeId, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>("DoubleClick", new { nodeId }, ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> SelectItemByScrollAsync(string nodeId, int targetIndex, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>("SelectItemByScroll", new { nodeId, targetIndex }, ct);

    /// <inheritdoc/>
    public Task<StateDeltaDto> SelectItemByIndexAsync(string nodeId, int index, CancellationToken ct)
        => this.InvokeAsync<StateDeltaDto>("SelectItemByIndex", new { nodeId, index }, ct);

    /// <inheritdoc/>
    public Task<System.Collections.Generic.List<ListItemDto>> GetListItemsAsync(string nodeId, CancellationToken ct)
        => this.InvokeAsync<System.Collections.Generic.List<ListItemDto>>("GetListItems", new { nodeId }, ct);

    // --------------------------------------------------------------------------
    // Inner types
    // --------------------------------------------------------------------------

    private sealed class PendingRequest
    {
        internal TaskCompletionSource<PipeResponse> Completion { get; } =
            new TaskCompletionSource<PipeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
