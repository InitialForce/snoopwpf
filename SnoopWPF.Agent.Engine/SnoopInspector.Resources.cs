// SnoopInspector.Resources.cs
// FX6-A7: Resource inspection methods.

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
    public Task<CursorPage<ResourceDto>> GetResourcesAsync(
        string? nodeId,
        string? resourceKey,
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

            // ResourceInspector requires a DependencyObject to walk the tree.
            if (target is not DependencyObject depObj)
            {
                // Return empty page — non-DependencyObject roots have no resource dictionaries.
                return new CursorPage<ResourceDto>
                {
                    Items = new List<ResourceDto>(),
                    NextCursor = null,
                    TotalCount = 0,
                    HasMore = false,
                    Stale = false,
                };
            }

            var resources = ResourceInspector.GetResources(depObj, resourceKey, this.options.EnableRedaction);

            // Paginate using index-based cursor snapshot.
            // Honor cursor: re-use existing snapshot when provided; create one only on first call.
            // FX6-A2: bind cursor to nodeId (or "ROOT" for application-level resources).
            var resCursorNodeId = nodeId ?? "ROOT";
            var snapIds = resources.Select((_, i) => i.ToString()).ToList();
            var cursorToken = !string.IsNullOrEmpty(cursor) ? cursor : this.cursorManager.CreateCursor(snapIds, nodeId: resCursorNodeId);
            var page = this.cursorManager.GetPage(cursorToken, effectiveTake, nodeId: resCursorNodeId);

            var pageItems = new List<ResourceDto>(page.Items.Count);
            foreach (var idxStr in page.Items)
            {
                if (int.TryParse(idxStr, out var idx) && idx < resources.Count)
                {
                    pageItems.Add(resources[idx]);
                }
            }

            return new CursorPage<ResourceDto>
            {
                Items = pageItems,
                NextCursor = page.NextCursor,
                TotalCount = page.TotalCount,
                HasMore = page.HasMore,
                Stale = page.Stale,
            };
        }, ct);
    }

    /// <inheritdoc/>

    public async Task<CursorPage<ResourceDto>> GetResourcesAsync(
        WpfLocator locator,
        string? resourceKey,
        string? cursor,
        int take,
        CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.GetResourcesAsync(nodeId, resourceKey, cursor, take, ct).ConfigureAwait(false);
    }
}
