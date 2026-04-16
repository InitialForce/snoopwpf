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
using SnoopWPF.Agent.Engine;

/// <summary>
/// Connects to the host-side named pipe (the host owns the NamedPipeServerStream; the injected agent is the client).
/// Performs handshake, then routes incoming <see cref="PipeRequest"/> frames to the local <see cref="SnoopInspector"/>
/// and sends <see cref="PipeResponse"/> frames back.
/// </summary>
internal sealed class PipeAgentServer : IDisposable
{
    private readonly string pipeName;
    private readonly byte[] sessionTokenBytes;
    private readonly SnoopInspector inspector;

    // Tracks in-flight request CancellationTokenSources keyed by request id.
    private readonly ConcurrentDictionary<int, CancellationTokenSource> inFlightRequests = new();

    // Serializes writes to pipeStream so concurrent response frames do not interleave.
    private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);

    private NamedPipeClientStream? pipeStream;
    private volatile bool disposed;

    /// <summary>
    /// Dispatch table: method name → handler that takes paramsJson and a ct, returns resultJson.
    /// Populated lazily in <see cref="BuildDispatchTable"/>.
    /// </summary>
    private Dictionary<string, Func<string, CancellationToken, Task<string>>>? dispatchTable;

    public PipeAgentServer(string pipeName, byte[] sessionTokenBytes, SnoopInspector inspector)
    {
        this.pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        this.sessionTokenBytes = sessionTokenBytes ?? throw new ArgumentNullException(nameof(sessionTokenBytes));
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
        // Host sends HandshakeChallenge first.
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

        // Constant-time token comparison to prevent timing attacks.
        if (!ConstantTimeEquals(challenge.SessionToken, this.sessionTokenBytes))
        {
            throw new UnauthorizedAccessException("Session token mismatch during handshake.");
        }

        // Send HandshakeResponse.
        var response = new HandshakeResponse
        {
            ProtocolVersion = ProtocolConstants.ProtocolVersion,
            AgentVersion = GetAgentVersion(),
            TargetRuntime = GetTargetRuntime(),
            SessionToken = challenge.SessionToken, // echo back
            Capabilities = new List<string> { "inspection", "mutation", "screenshot", "diagnostics" },
        };

        var responseBytes = JsonFramedSerializer.Serialize(response);
        await JsonFramedSerializer.WriteFrameAsync(this.pipeStream!, responseBytes, ct).ConfigureAwait(false);
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
                // Pipe disconnected or error — exit loop gracefully.
                break;
            }

            if (frameBytes == null)
            {
                // Clean EOF — host disconnected.
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
                // Malformed message — send error response with id=0 and close.
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

            // Start request processing on thread pool (don't await — allows concurrent requests).
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
                            // Strip property values and sensitive info — only include the error code name.
                            Message = snoopEx.Code.ToString(),
                        },
                    };
                }
                catch (Exception)
                {
                    response = new PipeResponse
                    {
                        Id = requestId,
                        Error = new PipeErrorPayload
                        {
                            Code = "InternalError",
                            Message = "An internal error occurred.",
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
                    // Pipe may have closed — ignore send errors in cleanup.
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
    // Dispatch table — all 15 ISnoopInspector methods
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

    /// <summary>Constant-time comparison of a string against a byte[] token to mitigate timing attacks.</summary>
    private static bool ConstantTimeEquals(string? candidate, byte[] tokenBytes)
    {
        if (candidate == null)
        {
            return false;
        }

        var candidateBytes = Encoding.UTF8.GetBytes(candidate);
        if (candidateBytes.Length != tokenBytes.Length)
        {
            return false;
        }

        var diff = 0;
        for (var i = 0; i < tokenBytes.Length; i++)
        {
            diff |= candidateBytes[i] ^ tokenBytes[i];
        }

        return diff == 0;
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
#pragma warning disable CA1812 // Avoid uninstantiated internal classes — used by deserializer

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
#pragma warning restore CA1812
