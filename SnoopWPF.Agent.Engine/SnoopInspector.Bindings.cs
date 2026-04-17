// SnoopInspector.Bindings.cs
// FX6-A7: Binding inspection methods.

namespace SnoopWPF.Agent.Engine;

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Data.Tree;
using Snoop.Infrastructure;
using Snoop.Infrastructure.Diagnostics;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine.Infrastructure;
using SnoopWPF.Agent.Engine.StateDelta;
using SnoopWPF.Agent.Engine.Sync;

/// <content/>
public sealed partial class SnoopInspector
{
    /// <inheritdoc/>
    public Task<BindingInfoDto> GetBindingInfoAsync(string nodeId, string propertyName, CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            var isRedacted = this.options.EnableRedaction && RedactionFilter.IsRedacted(propertyName, null);

            // ONE synchronous block: get properties, find named one, read binding, teardown.
            BindingInfoDto result;

            var props = PropertyInformation.GetProperties(target);
            try
            {
                var match = props.FirstOrDefault(p =>
                    string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.DisplayName, propertyName, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    result = new BindingInfoDto { HasBinding = false };
                }
                else
                {
                    var dto = DtoProjection.ToBindingInfoDto(match);
                    if (dto is null)
                    {
                        result = new BindingInfoDto { HasBinding = false };
                    }
                    else
                    {
                        if (isRedacted)
                        {
                            dto.Path = "[REDACTED]";
                            dto.ResolvedValue = "[REDACTED]";
                        }

                        result = dto;
                    }
                }
            }
            finally
            {
                foreach (var prop in props)
                {
                    prop.Teardown();
                    StopChangeTimer(prop);
                }
            }

            return result;
        }, ct);
    }

    /// <inheritdoc/>
    public Task<CursorPage<DiagnosticItemDto>> RunDiagnosticsAsync(
        string? nodeId,
        List<string>? providers,
        string? minLevel,
        string? cursor,
        int take,
        CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            int effectiveTake = Math.Min(Math.Max(take, 1), InputConstants.MaxPageSize);

            object target;

            if (nodeId is not null)
            {
                target = this.ResolveNodeOrThrow(nodeId);
                this.VerifyElementConnectivity(target, nodeId);
            }
            else
            {
                target = this.GetEffectiveRootTarget();
            }

            using var treeService = TreeService.From(TreeType.Visual);
            treeService.Construct(target, parent: null);

            // Run all diagnostics.
            treeService.DiagnosticContext.AnalyzeTree();

            var diagItems = treeService.DiagnosticContext.DiagnosticItems.ToList();

            // Filter by minLevel if specified.
            if (!string.IsNullOrEmpty(minLevel) && Enum.TryParse<DiagnosticLevel>(minLevel, ignoreCase: true, out var minLevelEnum))
            {
                diagItems = diagItems.Where(d => d.Level >= minLevelEnum).ToList();
            }

            // Filter by providers if specified.
            if (providers is { Count: > 0 })
            {
                diagItems = diagItems.Where(d =>
                    providers.Any(p => string.Equals(p, d.DiagnosticProvider.Name, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            var dtos = diagItems.Select(d =>
            {
                var nodeIdForItem = d.TreeItem is not null
                    ? this.nodeRegistry.GetOrCreateId(d.TreeItem.Target)
                    : string.Empty;

                return new DiagnosticItemDto
                {
                    Name = d.Name ?? string.Empty,
                    Description = d.Description ?? string.Empty,
                    Area = d.Area.ToString(),
                    Level = d.Level.ToString(),
                    NodeId = nodeIdForItem,
                    NodePath = new List<string>(),
                };
            }).ToList();

            // Paginate. Honor cursor: re-use existing snapshot when provided; create one only on first call.
            // FX6-A2: bind cursor to nodeId (or "ROOT" for whole-tree diagnostics).
            var diagCursorNodeId = nodeId ?? "ROOT";
            var snapIds = dtos.Select((_, i) => i.ToString()).ToList();
            var cursorToken = !string.IsNullOrEmpty(cursor) ? cursor : this.cursorManager.CreateCursor(snapIds, nodeId: diagCursorNodeId);
            var page = this.cursorManager.GetPage(cursorToken, effectiveTake, nodeId: diagCursorNodeId);

            var pageItems = new List<DiagnosticItemDto>(page.Items.Count);
            foreach (var idxStr in page.Items)
            {
                if (int.TryParse(idxStr, out var idx) && idx < dtos.Count)
                {
                    pageItems.Add(dtos[idx]);
                }
            }

            return new CursorPage<DiagnosticItemDto>
            {
                Items = pageItems,
                NextCursor = page.NextCursor,
                TotalCount = page.TotalCount,
                HasMore = page.HasMore,
                Stale = page.Stale,
            };
        }, ct);
    }

    // -------------------------------------------------------------------------
    // ISnoopInspector — additional methods
    // -------------------------------------------------------------------------

    /// <inheritdoc/>

    public async Task<BindingInfoDto> GetBindingInfoAsync(WpfLocator locator, string propertyName, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.GetBindingInfoAsync(nodeId, propertyName, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<CursorPage<DiagnosticItemDto>> RunDiagnosticsAsync(
        WpfLocator locator,
        List<string>? providers,
        string? minLevel,
        string? cursor,
        int take,
        CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.RunDiagnosticsAsync(nodeId, providers, minLevel, cursor, take, ct).ConfigureAwait(false);
    }

    // ── M2-08: wpf_resolve_binding ────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<BindingResolutionDto> ResolveBindingAsync(string nodeId, string propertyName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            throw new ArgumentException("propertyName must not be null or empty.", nameof(propertyName));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);
            return this.bindingResolver.Resolve(target, propertyName);
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<BindingResolutionDto> ResolveBindingAsync(WpfLocator locator, string propertyName, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.ResolveBindingAsync(nodeId, propertyName, ct).ConfigureAwait(false);
    }
}
