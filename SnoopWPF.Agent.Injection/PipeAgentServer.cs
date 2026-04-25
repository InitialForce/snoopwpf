namespace SnoopWPF.Agent.Injection;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Contracts.Protocol;

/// <summary>
/// Connects to the host-side named pipe (the host owns the NamedPipeServerStream; the agent is the client).
/// Performs handshake, then routes incoming <see cref="PipeRequest"/> frames to the local <see cref="SnoopInspector"/>
/// and sends <see cref="PipeResponse"/> frames back.
/// </summary>
/// <remarks>
/// Used by two callers:
/// <list type="bullet">
///   <item>The CLR-injected agent entry point (<see cref="SnoopAgentEntryPoint"/>), which runs inside a target
///         after GenericInjector has loaded the assembly.</item>
///   <item><c>SnoopAgent.StartBrokeredClient</c> in <c>SnoopWPF.Agent.Server</c>, which wires the same pipe-client
///         dispatch into a non-injected WPF host that was launched by a broker with <c>--ui-mcp-pipe</c>-style args
///         and wants to expose its local inspector over the pipe as a brokered target.</item>
/// </list>
/// </remarks>
public sealed class PipeAgentServer : IDisposable
{
    private readonly string pipeName;
    private readonly byte[] sessionTokenBytes;
    private readonly ISnoopInspector inspector;

    // Tracks in-flight request CancellationTokenSources keyed by request id.
    private readonly ConcurrentDictionary<int, CancellationTokenSource> inFlightRequests = new();

    // Serializes writes to pipeStream so concurrent response frames do not interleave.
    private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);

    private NamedPipeClientStream? pipeStream;
    private volatile bool disposed;

    /// <summary>
    /// Dispatch table: method name handler that takes paramsJson and a ct, returns resultJson.
    /// Populated lazily in <see cref="BuildDispatchTable"/>.
    /// </summary>
    private Dictionary<string, Func<string, CancellationToken, Task<string>>>? dispatchTable;

    public PipeAgentServer(string pipeName, byte[] sessionTokenBytes, ISnoopInspector inspector)
    {
        this.pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        if (sessionTokenBytes is null)
        {
            throw new ArgumentNullException(nameof(sessionTokenBytes));
        }

        // Defensive copy: callers typically zero their buffer after construction.
        // Storing only a reference meant the zero-out corrupted the HMAC key before
        // the handshake ran. Independent backing array removes that coupling.
        this.sessionTokenBytes = new byte[sessionTokenBytes.Length];
        Buffer.BlockCopy(sessionTokenBytes, 0, this.sessionTokenBytes, 0, sessionTokenBytes.Length);
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    /// <summary>
    /// Connects to the host pipe, performs handshake, and processes requests until the pipe closes
    /// or <paramref name="ct"/> is cancelled.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        this.pipeStream = new NamedPipeClientStream(
            ".",
            this.pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await this.pipeStream.ConnectAsync(5000, ct).ConfigureAwait(false);

        // Perform handshake (host speaks first).
        await this.PerformHandshakeAsync(ct).ConfigureAwait(false);

        // Build dispatch table after handshake so token is consumed.
        this.dispatchTable = this.BuildDispatchTable();

        // Process requests until disconnect.
        await this.ProcessRequestLoopAsync(ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Handshake
    // -----------------------------------------------------------------

    private async Task PerformHandshakeAsync(CancellationToken ct)
    {
        // Host sends HandshakeChallenge first (contains nonce, no token).
        var challengeBytes = await JsonFramedSerializer.ReadFrameAsync(this.pipeStream!, ct).ConfigureAwait(false);
        if (challengeBytes == null)
        {
            throw new InvalidOperationException("Pipe closed before handshake challenge was received.");
        }

        var challenge = JsonFramedSerializer.Deserialize<HandshakeChallenge>(challengeBytes);

        // Validate protocol version.
        if (challenge.ProtocolVersion != ProtocolConstants.ProtocolVersion)
        {
            throw new SnoopException(
                SnoopErrorCode.ProtocolMismatch,
                $"Protocol version mismatch: host sent {challenge.ProtocolVersion}, agent supports {ProtocolConstants.ProtocolVersion}.");
        }

        if (challenge.Nonce == null || challenge.Nonce.Length != 16)
        {
            throw new SnoopException(
                SnoopErrorCode.ProtocolMismatch,
                "Handshake challenge contained an invalid nonce.");
        }

        // Compute HMAC proof: HMACSHA256(key=sessionTokenBytes, data=nonce).
        byte[] proofHmac = ComputeHmacSha256(this.sessionTokenBytes, challenge.Nonce);

        // Send HandshakeResponse with proof (token never transmitted).
        var response = new HandshakeResponse
        {
            ProtocolVersion = ProtocolConstants.ProtocolVersion,
            AgentVersion = GetAgentVersion(),
            TargetRuntime = GetTargetRuntime(),
            ProofHmac = proofHmac,
            Capabilities = new List<string> { "inspection", "mutation", "screenshot", "diagnostics" },
        };

        var responseBytes = JsonFramedSerializer.Serialize(response);
        await JsonFramedSerializer.WriteFrameAsync(this.pipeStream!, responseBytes, ct).ConfigureAwait(false);
    }

    /// <summary>Computes HMACSHA256(key, data). Compatible with all target frameworks.</summary>
    private static byte[] ComputeHmacSha256(byte[] key, byte[] data)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(key);
        return hmac.ComputeHash(data);
    }

    // -----------------------------------------------------------------
    // Request loop
    // -----------------------------------------------------------------

    private async Task ProcessRequestLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            byte[]? frameBytes;
            try
            {
                frameBytes = await JsonFramedSerializer.ReadFrameAsync(this.pipeStream!, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Pipe disconnected or error -- exit loop gracefully.
                break;
            }

            if (frameBytes == null)
            {
                // Clean EOF -- host disconnected.
                break;
            }

            // Check for cancel frame first (has "cancel":true, no "method").
            if (IsCancelFrame(frameBytes, out var cancelId))
            {
                if (this.inFlightRequests.TryRemove(cancelId, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }

                continue;
            }

            // Deserialize as PipeRequest and dispatch (fire-and-forget per request).
            PipeRequest request;
            try
            {
                request = JsonFramedSerializer.Deserialize<PipeRequest>(frameBytes);
            }
            catch (Exception ex)
            {
                // Malformed message -- send error response with id=0 and close.
                var errorResponse = new PipeResponse
                {
                    Id = 0,
                    Error = new PipeErrorPayload
                    {
                        Code = nameof(SnoopErrorCode.ProtocolMismatch),
                        Message = $"Malformed request frame: {ex.Message}",
                    },
                };
                await this.SendResponseAsync(errorResponse, ct).ConfigureAwait(false);
                break;
            }

            // Start request processing on thread pool (don't await -- allows concurrent requests).
            var requestId = request.Id;
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            this.inFlightRequests[requestId] = linkedCts;

            _ = Task.Run(async () =>
            {
                PipeResponse response;
                try
                {
                    response = await this.DispatchRequestAsync(request, linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    response = new PipeResponse
                    {
                        Id = requestId,
                        Error = new PipeErrorPayload
                        {
                            Code = "Cancelled",
                            Message = "Request was cancelled.",
                        },
                    };
                }
                catch (SnoopException snoopEx)
                {
                    response = new PipeResponse
                    {
                        Id = requestId,
                        Error = new PipeErrorPayload
                        {
                            Code = snoopEx.Code.ToString(),
                            // Strip property values and sensitive info -- only include the error code name.
                            Message = snoopEx.Code.ToString(),
                        },
                    };
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError(
                        $"[PipeAgentServer] Unhandled exception in '{request.Method}': {ex.GetType().FullName}: {ex.Message}{System.Environment.NewLine}{ex.StackTrace}");
                    response = new PipeResponse
                    {
                        Id = requestId,
                        Error = new PipeErrorPayload
                        {
                            Code = "InternalError",
                            Message = $"An internal error occurred: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}",
                        },
                    };
                }
                finally
                {
                    this.inFlightRequests.TryRemove(requestId, out _);
                    linkedCts.Dispose();
                }

                try
                {
                    await this.SendResponseAsync(response, ct).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Pipe may have closed -- ignore send errors in cleanup.
                }
            });
        }

        // Cancel all in-flight requests on exit.
        // Use ToArray() + TryRemove so we don't race with handler finally blocks that also
        // call TryRemove and Dispose on their own CTS entries.
        foreach (var kvp in this.inFlightRequests.ToArray())
        {
            if (this.inFlightRequests.TryRemove(kvp.Key, out var removed))
            {
                removed.Cancel();
                removed.Dispose();
            }
        }
    }

    private async Task SendResponseAsync(PipeResponse response, CancellationToken ct)
    {
        var bytes = JsonFramedSerializer.Serialize(response);
        // Serialize writes so concurrent response frames do not interleave on the byte stream.
        await this.writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await JsonFramedSerializer.WriteFrameAsync(this.pipeStream!, bytes, ct).ConfigureAwait(false);
        }
        finally
        {
            this.writeLock.Release();
        }
    }

    // -----------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------

    private async Task<PipeResponse> DispatchRequestAsync(PipeRequest request, CancellationToken ct)
    {
        if (this.dispatchTable == null || !this.dispatchTable.TryGetValue(request.Method, out var handler))
        {
            return new PipeResponse
            {
                Id = request.Id,
                Error = new PipeErrorPayload
                {
                    Code = nameof(SnoopErrorCode.ProtocolMismatch),
                    Message = $"Unknown method: {request.Method}",
                },
            };
        }

        var resultJson = await handler(request.ParamsJson, ct).ConfigureAwait(false);
        return new PipeResponse { Id = request.Id, ResultJson = resultJson };
    }

    // -----------------------------------------------------------------
    // Dispatch table -- all ISnoopInspector methods
    // FX6-F (bd-1we.6.1): wired the 30 previously missing dispatch cases.
    // -----------------------------------------------------------------

    private Dictionary<string, Func<string, CancellationToken, Task<string>>> BuildDispatchTable()
    {
        return new Dictionary<string, Func<string, CancellationToken, Task<string>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["GetSessionInfo"] = async (paramsJson, ct) =>
            {
                var result = await this.inspector.GetSessionInfoAsync(ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetWindows"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetWindowsParams>(paramsJson);
                var result = await this.inspector.GetWindowsAsync(p.IncludeHidden, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetVisualTree"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetVisualTreeParams>(paramsJson);
                var result = await this.inspector.GetVisualTreeAsync(p.RootNodeId, p.MaxDepth, p.TreeType, p.IncludeProperties, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetChildren"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetChildrenParams>(paramsJson);
                var result = await this.inspector.GetChildrenAsync(p.NodeId, p.TreeType, p.Cursor, p.Take, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetAncestors"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetAncestorsParams>(paramsJson);
                var result = await this.inspector.GetAncestorsAsync(p.NodeId, p.MaxLevels, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["FindElements"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<FindElementsParams>(paramsJson);
                var result = await this.inspector.FindElementsAsync(p.TypeName, p.Name, p.RootNodeId, p.Conditions, p.TreeType, p.MaxResults, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["InspectElement"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<InspectElementParams>(paramsJson);
                var result = await this.inspector.InspectElementAsync(p.NodeId, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetProperties"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetPropertiesParams>(paramsJson);
                var result = await this.inspector.GetPropertiesAsync(p.NodeId, p.Filter, p.Category, p.IncludeDefaults, p.Cursor, p.Take, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["SetProperty"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<SetPropertyParams>(paramsJson);
                var result = await this.inspector.SetPropertyAsync(p.NodeId, p.PropertyName, p.Value, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetBindingInfo"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetBindingInfoParams>(paramsJson);
                var result = await this.inspector.GetBindingInfoAsync(p.NodeId, p.PropertyName, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["RunDiagnostics"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<RunDiagnosticsParams>(paramsJson);
                var result = await this.inspector.RunDiagnosticsAsync(p.NodeId, p.Providers, p.MinLevel, p.Cursor, p.Take, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetResources"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetResourcesParams>(paramsJson);
                var result = await this.inspector.GetResourcesAsync(p.NodeId, p.ResourceKey, p.Cursor, p.Take, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["CaptureScreenshot"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<CaptureScreenshotParams>(paramsJson);
                var result = await this.inspector.CaptureScreenshotAsync(p.NodeId, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetTriggers"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetTriggersParams>(paramsJson);
                var result = await this.inspector.GetTriggersAsync(p.NodeId, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetBehaviors"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetBehaviorsParams>(paramsJson);
                var result = await this.inspector.GetBehaviorsAsync(p.NodeId, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetActionables"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetActionablesParams>(paramsJson);
                var result = await this.inspector.GetActionablesAsync(p.RootNodeId, p.MaxResults, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["ExecuteActionSequence"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<ExecuteActionSequenceParams>(paramsJson);
                var result = await this.inspector.ExecuteActionSequenceAsync(
                    p.Steps ?? new List<ActionStepDto>(),
                    p.StopOnError,
                    ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["ActUntil"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<ActUntilParams>(paramsJson);
                var result = await this.inspector.ActUntilAsync(
                    p.Action ?? new ActionStepDto(),
                    p.Predicate ?? new ActUntilPredicateDto(),
                    p.TimeoutMs,
                    ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["SetTextValue"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<SetTextValueParams>(paramsJson);
                var result = await this.inspector.SetTextValueAsync(p.NodeId, p.Value, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["ExecuteCommand"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<ExecuteCommandParams>(paramsJson);
                var result = await this.inspector.ExecuteCommandAsync(p.NodeId, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["SetSliderValue"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<SetSliderValueParams>(paramsJson);
                var result = await this.inspector.SetSliderValueAsync(p.NodeId, p.Value, p.Normalized, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetVisualTreeByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetVisualTreeByLocatorParams>(paramsJson);
                var result = await this.inspector.GetVisualTreeAsync(p.Locator, p.MaxDepth, p.TreeType, p.IncludeProperties, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetChildrenByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetChildrenByLocatorParams>(paramsJson);
                var result = await this.inspector.GetChildrenAsync(p.Locator, p.TreeType, p.Cursor, p.Take, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetAncestorsByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetAncestorsByLocatorParams>(paramsJson);
                var result = await this.inspector.GetAncestorsAsync(p.Locator, p.MaxLevels, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["InspectElementByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<ByLocatorParams>(paramsJson);
                var result = await this.inspector.InspectElementAsync(p.Locator, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetPropertiesByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetPropertiesByLocatorParams>(paramsJson);
                var result = await this.inspector.GetPropertiesAsync(p.Locator, p.Filter, p.Category, p.IncludeDefaults, p.Cursor, p.Take, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["SetPropertyByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<SetPropertyByLocatorParams>(paramsJson);
                var result = await this.inspector.SetPropertyAsync(p.Locator, p.PropertyName, p.Value, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetBindingInfoByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetBindingInfoByLocatorParams>(paramsJson);
                var result = await this.inspector.GetBindingInfoAsync(p.Locator, p.PropertyName, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["RunDiagnosticsByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<RunDiagnosticsByLocatorParams>(paramsJson);
                var result = await this.inspector.RunDiagnosticsAsync(p.Locator, p.Providers, p.MinLevel, p.Cursor, p.Take, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetResourcesByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetResourcesByLocatorParams>(paramsJson);
                var result = await this.inspector.GetResourcesAsync(p.Locator, p.ResourceKey, p.Cursor, p.Take, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["CaptureScreenshotByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<ByLocatorParams>(paramsJson);
                var result = await this.inspector.CaptureScreenshotAsync(p.Locator, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetTriggersByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<ByLocatorParams>(paramsJson);
                var result = await this.inspector.GetTriggersAsync(p.Locator, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetBehaviorsByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<ByLocatorParams>(paramsJson);
                var result = await this.inspector.GetBehaviorsAsync(p.Locator, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["SelectItem"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<SelectItemParams>(paramsJson);
                var result = await this.inspector.SelectItemAsync(p.NodeId, p.Identifier, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["SelectItemByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<SelectItemByLocatorParams>(paramsJson);
                var result = await this.inspector.SelectItemAsync(p.Locator, p.Identifier, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["SetCheckState"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<SetCheckStateParams>(paramsJson);
                var result = await this.inspector.SetCheckStateAsync(p.NodeId, p.State, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["SetCheckStateByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<SetCheckStateByLocatorParams>(paramsJson);
                var result = await this.inspector.SetCheckStateAsync(p.Locator, p.State, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["SetTextValueByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<SetTextValueByLocatorParams>(paramsJson);
                var result = await this.inspector.SetTextValueAsync(p.Locator, p.Value, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["SetSliderValueByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<SetSliderValueByLocatorParams>(paramsJson);
                var result = await this.inspector.SetSliderValueAsync(p.Locator, p.Value, p.Normalized, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["ExecuteCommandByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<ByLocatorParams>(paramsJson);
                var result = await this.inspector.ExecuteCommandAsync(p.Locator, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["Click"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<NodeIdOnlyParams>(paramsJson);
                var result = await this.inspector.ClickAsync(p.NodeId, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["ClickByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<ByLocatorParams>(paramsJson);
                var result = await this.inspector.ClickAsync(p.Locator, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["Toggle"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<NodeIdOnlyParams>(paramsJson);
                var result = await this.inspector.ToggleAsync(p.NodeId, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["ToggleByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<ByLocatorParams>(paramsJson);
                var result = await this.inspector.ToggleAsync(p.Locator, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["ExpandCollapse"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<ExpandCollapseParams>(paramsJson);
                var result = await this.inspector.ExpandCollapseAsync(p.NodeId, p.Action, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["ExpandCollapseByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<ExpandCollapseByLocatorParams>(paramsJson);
                var result = await this.inspector.ExpandCollapseAsync(p.Locator, p.Action, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["ResolveBinding"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetBindingInfoParams>(paramsJson);
                var result = await this.inspector.ResolveBindingAsync(p.NodeId, p.PropertyName, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["ResolveBindingByLocator"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<GetBindingInfoByLocatorParams>(paramsJson);
                var result = await this.inspector.ResolveBindingAsync(p.Locator, p.PropertyName, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["WaitForProperty"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<WaitForPropertyParams>(paramsJson);
                var result = await this.inspector.WaitForPropertyAsync(p.Locator, p.PropertyName, p.ExpectedValue, p.TimeoutMs, p.PresenceExpected, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["PollChanges"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<PollChangesParams>(paramsJson);
                var result = await this.inspector.PollChangesAsync(p.SinceVersion, p.RootLocator, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["PumpUntilIdle"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<PumpUntilIdleParams>(paramsJson);
                var result = await this.inspector.PumpUntilIdleAsync(p.TimeoutMs, p.Resources, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            // ── WS3: wpf_double_click, wpf_select_item_by_scroll, wpf_select_item_by_index, wpf_get_list_items ──

            ["DoubleClick"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<NodeIdOnlyParams>(paramsJson);
                var result = await this.inspector.DoubleClickAsync(p.NodeId, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["SelectItemByScroll"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<SelectItemByScrollParams>(paramsJson);
                var result = await this.inspector.SelectItemByScrollAsync(p.NodeId, p.TargetIndex, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["SelectItemByIndex"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<SelectItemByIndexParams>(paramsJson);
                var result = await this.inspector.SelectItemByIndexAsync(p.NodeId, p.Index, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },

            ["GetListItems"] = async (paramsJson, ct) =>
            {
                var p = JsonFramedSerializer.DeserializeString<NodeIdOnlyParams>(paramsJson);
                var result = await this.inspector.GetListItemsAsync(p.NodeId, ct).ConfigureAwait(false);
                return JsonFramedSerializer.SerializeToString(result);
            },
        };
    }

    // -----------------------------------------------------------------
    // Cancel frame detection
    // -----------------------------------------------------------------

    private static bool IsCancelFrame(byte[] frameBytes, out int cancelId)
    {
        cancelId = 0;
        try
        {
            var json = Encoding.UTF8.GetString(frameBytes);
            // Quick prefilter: cancel frames always contain the literal "cancel" key.
            // Use IndexOf for net462 compatibility (string.Contains(string, StringComparison) is net5+).
            if (json.IndexOf("\"cancel\"", StringComparison.Ordinal) < 0)
            {
                return false;
            }

#if NET6_0_OR_GREATER
            // On net6+, use JsonDocument to verify the root-level "cancel" field is boolean true.
            // This prevents a false positive if "cancel" appears inside a property value or
            // nested object (e.g. a PipeRequest whose paramsJson contains "cancel":true).
            using var doc = System.Text.Json.JsonDocument.Parse(frameBytes);
            var root = doc.RootElement;
            if (!root.TryGetProperty("cancel", out var cancelProp) ||
                cancelProp.ValueKind != System.Text.Json.JsonValueKind.True)
            {
                return false;
            }

            if (!root.TryGetProperty("id", out var idProp) ||
                !idProp.TryGetInt32(out cancelId))
            {
                return false;
            }

            return true;
#else
            // On net462, DataContractJsonSerializer only maps top-level fields, so the
            // Cancel property is true only when "cancel":true appears at the root.
            var cancel = JsonFramedSerializer.Deserialize<PipeCancelPayload>(frameBytes);
            if (cancel.Cancel)
            {
                cancelId = cancel.Id;
                return true;
            }
#endif
        }
        catch (Exception)
        {
            // Not a cancel frame.
        }

        return false;
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static string GetAgentVersion()
    {
        try
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        }
        catch (Exception)
        {
            return "0.0.0.0";
        }
    }

    private static string GetTargetRuntime()
    {
#if NET6_0_OR_GREATER
        return System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
#else
        return $".NET Framework {Environment.Version}";
#endif
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        try
        {
            this.pipeStream?.Dispose();
        }
        catch (Exception)
        {
            // Ignore disposal errors.
        }

        this.writeLock.Dispose();
    }
}

// -----------------------------------------------------------------
// Parameter DTOs for dispatch table deserialization.
// These are internal shapes mirroring what ISnoopInspector accepts.
// DataContract attributes ensure DCJS produces correct camelCase on net462.
// -----------------------------------------------------------------
#pragma warning disable CA1812 // Avoid uninstantiated internal classes -- used by deserializer

[System.Runtime.Serialization.DataContract]
internal sealed class GetWindowsParams
{
    [System.Runtime.Serialization.DataMember(Name = "includeHidden")]
    public bool IncludeHidden { get; set; }
}

[System.Runtime.Serialization.DataContract]
internal sealed class GetVisualTreeParams
{
    [System.Runtime.Serialization.DataMember(Name = "rootNodeId")]
    public string? RootNodeId { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "maxDepth")]
    public int MaxDepth { get; set; } = 5;

    [System.Runtime.Serialization.DataMember(Name = "treeType")]
    public string TreeType { get; set; } = "Visual";

    [System.Runtime.Serialization.DataMember(Name = "includeProperties")]
    public List<string>? IncludeProperties { get; set; }
}

[System.Runtime.Serialization.DataContract]
internal sealed class GetChildrenParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string? NodeId { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "treeType")]
    public string TreeType { get; set; } = "Visual";

    [System.Runtime.Serialization.DataMember(Name = "cursor")]
    public string? Cursor { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "take")]
    public int Take { get; set; } = 50;
}

[System.Runtime.Serialization.DataContract]
internal sealed class GetAncestorsParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [System.Runtime.Serialization.DataMember(Name = "maxLevels")]
    public int? MaxLevels { get; set; }
}

[System.Runtime.Serialization.DataContract]
internal sealed class GetActionablesParams
{
    [System.Runtime.Serialization.DataMember(Name = "rootNodeId")]
    public string? RootNodeId { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "maxResults")]
    public int MaxResults { get; set; } = 100;
}

[System.Runtime.Serialization.DataContract]
internal sealed class ExecuteActionSequenceParams
{
    [System.Runtime.Serialization.DataMember(Name = "steps")]
    public List<ActionStepDto>? Steps { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "stopOnError")]
    public bool StopOnError { get; set; } = true;
}

[System.Runtime.Serialization.DataContract]
internal sealed class ActUntilParams
{
    [System.Runtime.Serialization.DataMember(Name = "action")]
    public ActionStepDto? Action { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "predicate")]
    public ActUntilPredicateDto? Predicate { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "timeoutMs")]
    public int TimeoutMs { get; set; } = 5000;
}

[System.Runtime.Serialization.DataContract]
internal sealed class FindElementsParams
{
    [System.Runtime.Serialization.DataMember(Name = "typeName")]
    public string? TypeName { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "name")]
    public string? Name { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "rootNodeId")]
    public string? RootNodeId { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "conditions")]
    public List<PropertyConditionDto>? Conditions { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "treeType")]
    public string TreeType { get; set; } = "Visual";

    [System.Runtime.Serialization.DataMember(Name = "maxResults")]
    public int MaxResults { get; set; } = 50;
}

[System.Runtime.Serialization.DataContract]
internal sealed class InspectElementParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class GetPropertiesParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [System.Runtime.Serialization.DataMember(Name = "filter")]
    public string? Filter { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "category")]
    public string? Category { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "includeDefaults")]
    public bool IncludeDefaults { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "cursor")]
    public string? Cursor { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "take")]
    public int Take { get; set; } = 50;
}

[System.Runtime.Serialization.DataContract]
internal sealed class SetPropertyParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [System.Runtime.Serialization.DataMember(Name = "propertyName")]
    public string PropertyName { get; set; } = string.Empty;

    [System.Runtime.Serialization.DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class GetBindingInfoParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [System.Runtime.Serialization.DataMember(Name = "propertyName")]
    public string PropertyName { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class RunDiagnosticsParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string? NodeId { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "providers")]
    public List<string>? Providers { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "minLevel")]
    public string? MinLevel { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "cursor")]
    public string? Cursor { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "take")]
    public int Take { get; set; } = 50;
}

[System.Runtime.Serialization.DataContract]
internal sealed class GetResourcesParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string? NodeId { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "resourceKey")]
    public string? ResourceKey { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "cursor")]
    public string? Cursor { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "take")]
    public int Take { get; set; } = 50;
}

[System.Runtime.Serialization.DataContract]
internal sealed class CaptureScreenshotParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string? NodeId { get; set; }
}

[System.Runtime.Serialization.DataContract]
internal sealed class GetTriggersParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class GetBehaviorsParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class SetTextValueParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [System.Runtime.Serialization.DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class ExecuteCommandParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class SetSliderValueParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [System.Runtime.Serialization.DataMember(Name = "value")]
    public double Value { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "normalized")]
    public bool Normalized { get; set; }
}

/// <summary>Generic single-locator param DTO (for methods that only take a locator + ct).</summary>
[System.Runtime.Serialization.DataContract]
internal sealed class ByLocatorParams
{
    [System.Runtime.Serialization.DataMember(Name = "locator")]
    public WpfLocator Locator { get; set; } = new WpfLocator();
}

/// <summary>Generic single-nodeId param DTO (for methods that only take a nodeId + ct).</summary>
[System.Runtime.Serialization.DataContract]
internal sealed class NodeIdOnlyParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class GetVisualTreeByLocatorParams
{
    [System.Runtime.Serialization.DataMember(Name = "locator")]
    public WpfLocator Locator { get; set; } = new WpfLocator();

    [System.Runtime.Serialization.DataMember(Name = "maxDepth")]
    public int MaxDepth { get; set; } = 5;

    [System.Runtime.Serialization.DataMember(Name = "treeType")]
    public string TreeType { get; set; } = "Visual";

    [System.Runtime.Serialization.DataMember(Name = "includeProperties")]
    public List<string>? IncludeProperties { get; set; }
}

[System.Runtime.Serialization.DataContract]
internal sealed class GetChildrenByLocatorParams
{
    [System.Runtime.Serialization.DataMember(Name = "locator")]
    public WpfLocator Locator { get; set; } = new WpfLocator();

    [System.Runtime.Serialization.DataMember(Name = "treeType")]
    public string TreeType { get; set; } = "Visual";

    [System.Runtime.Serialization.DataMember(Name = "cursor")]
    public string? Cursor { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "take")]
    public int Take { get; set; } = 50;
}

[System.Runtime.Serialization.DataContract]
internal sealed class GetAncestorsByLocatorParams
{
    [System.Runtime.Serialization.DataMember(Name = "locator")]
    public WpfLocator Locator { get; set; } = new WpfLocator();

    [System.Runtime.Serialization.DataMember(Name = "maxLevels")]
    public int? MaxLevels { get; set; }
}

[System.Runtime.Serialization.DataContract]
internal sealed class GetPropertiesByLocatorParams
{
    [System.Runtime.Serialization.DataMember(Name = "locator")]
    public WpfLocator Locator { get; set; } = new WpfLocator();

    [System.Runtime.Serialization.DataMember(Name = "filter")]
    public string? Filter { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "category")]
    public string? Category { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "includeDefaults")]
    public bool IncludeDefaults { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "cursor")]
    public string? Cursor { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "take")]
    public int Take { get; set; } = 50;
}

[System.Runtime.Serialization.DataContract]
internal sealed class SetPropertyByLocatorParams
{
    [System.Runtime.Serialization.DataMember(Name = "locator")]
    public WpfLocator Locator { get; set; } = new WpfLocator();

    [System.Runtime.Serialization.DataMember(Name = "propertyName")]
    public string PropertyName { get; set; } = string.Empty;

    [System.Runtime.Serialization.DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class GetBindingInfoByLocatorParams
{
    [System.Runtime.Serialization.DataMember(Name = "locator")]
    public WpfLocator Locator { get; set; } = new WpfLocator();

    [System.Runtime.Serialization.DataMember(Name = "propertyName")]
    public string PropertyName { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class RunDiagnosticsByLocatorParams
{
    [System.Runtime.Serialization.DataMember(Name = "locator")]
    public WpfLocator Locator { get; set; } = new WpfLocator();

    [System.Runtime.Serialization.DataMember(Name = "providers")]
    public List<string>? Providers { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "minLevel")]
    public string? MinLevel { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "cursor")]
    public string? Cursor { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "take")]
    public int Take { get; set; } = 50;
}

[System.Runtime.Serialization.DataContract]
internal sealed class GetResourcesByLocatorParams
{
    [System.Runtime.Serialization.DataMember(Name = "locator")]
    public WpfLocator Locator { get; set; } = new WpfLocator();

    [System.Runtime.Serialization.DataMember(Name = "resourceKey")]
    public string? ResourceKey { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "cursor")]
    public string? Cursor { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "take")]
    public int Take { get; set; } = 50;
}

[System.Runtime.Serialization.DataContract]
internal sealed class SelectItemParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [System.Runtime.Serialization.DataMember(Name = "identifier")]
    public string Identifier { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class SelectItemByLocatorParams
{
    [System.Runtime.Serialization.DataMember(Name = "locator")]
    public WpfLocator Locator { get; set; } = new WpfLocator();

    [System.Runtime.Serialization.DataMember(Name = "identifier")]
    public string Identifier { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class SetCheckStateParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [System.Runtime.Serialization.DataMember(Name = "state")]
    public string State { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class SetCheckStateByLocatorParams
{
    [System.Runtime.Serialization.DataMember(Name = "locator")]
    public WpfLocator Locator { get; set; } = new WpfLocator();

    [System.Runtime.Serialization.DataMember(Name = "state")]
    public string State { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class SetTextValueByLocatorParams
{
    [System.Runtime.Serialization.DataMember(Name = "locator")]
    public WpfLocator Locator { get; set; } = new WpfLocator();

    [System.Runtime.Serialization.DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class SetSliderValueByLocatorParams
{
    [System.Runtime.Serialization.DataMember(Name = "locator")]
    public WpfLocator Locator { get; set; } = new WpfLocator();

    [System.Runtime.Serialization.DataMember(Name = "value")]
    public double Value { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "normalized")]
    public bool Normalized { get; set; }
}

[System.Runtime.Serialization.DataContract]
internal sealed class ExpandCollapseParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [System.Runtime.Serialization.DataMember(Name = "action")]
    public string Action { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class ExpandCollapseByLocatorParams
{
    [System.Runtime.Serialization.DataMember(Name = "locator")]
    public WpfLocator Locator { get; set; } = new WpfLocator();

    [System.Runtime.Serialization.DataMember(Name = "action")]
    public string Action { get; set; } = string.Empty;
}

[System.Runtime.Serialization.DataContract]
internal sealed class WaitForPropertyParams
{
    [System.Runtime.Serialization.DataMember(Name = "locator")]
    public WpfLocator Locator { get; set; } = new WpfLocator();

    [System.Runtime.Serialization.DataMember(Name = "propertyName")]
    public string PropertyName { get; set; } = string.Empty;

    [System.Runtime.Serialization.DataMember(Name = "expectedValue")]
    public string? ExpectedValue { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "timeoutMs")]
    public int TimeoutMs { get; set; } = 5000;

    [System.Runtime.Serialization.DataMember(Name = "presenceExpected")]
    public string PresenceExpected { get; set; } = "present";
}

[System.Runtime.Serialization.DataContract]
internal sealed class PollChangesParams
{
    [System.Runtime.Serialization.DataMember(Name = "sinceVersion")]
    public long SinceVersion { get; set; }

    [System.Runtime.Serialization.DataMember(Name = "rootLocator")]
    public WpfLocator? RootLocator { get; set; }
}

[System.Runtime.Serialization.DataContract]
internal sealed class PumpUntilIdleParams
{
    [System.Runtime.Serialization.DataMember(Name = "timeoutMs")]
    public int TimeoutMs { get; set; } = 5000;

    [System.Runtime.Serialization.DataMember(Name = "resources")]
    public List<string>? Resources { get; set; }
}

[System.Runtime.Serialization.DataContract]
internal sealed class SelectItemByScrollParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [System.Runtime.Serialization.DataMember(Name = "targetIndex")]
    public int TargetIndex { get; set; }
}

[System.Runtime.Serialization.DataContract]
internal sealed class SelectItemByIndexParams
{
    [System.Runtime.Serialization.DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [System.Runtime.Serialization.DataMember(Name = "index")]
    public int Index { get; set; }
}

#pragma warning restore CA1812

