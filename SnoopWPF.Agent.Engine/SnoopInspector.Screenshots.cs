// SnoopInspector.Screenshots.cs
// FX6-A7: Screenshot capture methods.

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
    public Task<ScreenshotResultDto> CaptureScreenshotAsync(string? nodeId, CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            Visual? visual;

            if (nodeId is not null)
            {
                var target = this.ResolveNodeOrThrow(nodeId);
                this.VerifyElementConnectivity(target, nodeId);

                if (target is not Visual v)
                {
                    throw new SnoopException(
                        SnoopErrorCode.ElementNotRenderable,
                        $"Node '{nodeId}' is not a Visual and cannot be captured.",
                        targetId: nodeId,
                        suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
                }

                visual = v;
            }
            else
            {
                // Default: capture main window.
                var app = Application.Current;
                if (app?.MainWindow is null)
                {
                    throw new SnoopException(
                        SnoopErrorCode.SessionNotFound,
                        "No main window available for screenshot.",
                        suggestions: new[] { SnoopSuggestions.SessionNotFound });
                }

                visual = app.MainWindow;
            }

            // Check the visual has renderable size.
            var size = ScreenshotCapture.GetRenderSize(visual!);
            if (size.Width <= 0 || size.Height <= 0)
            {
                throw new SnoopException(
                    SnoopErrorCode.ElementNotRenderable,
                    $"Element has zero size ({size.Width}x{size.Height}) and cannot be captured.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
            }

            var pngBytes = ScreenshotCapture.CaptureAsPng(visual!);

            if (pngBytes is null || pngBytes.Length == 0)
            {
                throw new SnoopException(
                    SnoopErrorCode.ElementNotRenderable,
                    "Screenshot capture returned no data — element may not be visible or connected.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
            }

            // Use actual pixel dimensions capped to max.
            var effectiveWidth = (int)Math.Min(size.Width, ScreenshotCapture.MaxDimension);
            var effectiveHeight = (int)Math.Min(size.Height, ScreenshotCapture.MaxDimension);

            return new ScreenshotResultDto
            {
                Metadata = new ScreenshotMetadataDto
                {
                    Width = effectiveWidth,
                    Height = effectiveHeight,
                    NodeId = nodeId ?? string.Empty,
                },
                PngBytes = pngBytes,
            };
        }, ct);
    }

    /// <inheritdoc/>

    public async Task<ScreenshotResultDto> CaptureScreenshotAsync(WpfLocator locator, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.CaptureScreenshotAsync(nodeId, ct).ConfigureAwait(false);
    }
}
