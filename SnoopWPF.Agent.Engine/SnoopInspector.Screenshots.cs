// SnoopInspector.Screenshots.cs
// FX6-A7 / bd-22z: Screenshot capture methods — WGC path.

namespace SnoopWPF.Agent.Engine;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine.Screenshots;

/// <content/>
public sealed partial class SnoopInspector
{
    /// <inheritdoc/>
    public async Task<ScreenshotResultDto> CaptureScreenshotAsync(string? nodeId, CancellationToken ct)
    {
        // Phase 1: brief dispatcher touch — resolve visual + extract HWND / crop rect.
        // This is the ONLY dispatcher work; the actual WGC capture runs off-thread.
        var captureInfo = await this.RunOnDispatcherAsync(
            () => this.ResolveScreenshotTarget(nodeId),
            ct).ConfigureAwait(false);

#if NET462
        // .NET Framework 4.6.2 injection path — WGC not available.
        throw new SnoopException(
            SnoopErrorCode.UnsupportedOnNet462,
            "Windows.Graphics.Capture is not available in .NET Framework 4.6.2 injection mode. " +
            "Screenshot capture requires the .NET 6+ in-process agent.",
            suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
#else
        // Phase 2: async WGC capture — entirely off the WPF UI thread.
        var pngBytes = await WgcScreenshotCapture.CaptureAsPngAsync(
            captureInfo.Hwnd,
            captureInfo.CropRect,
            ct).ConfigureAwait(false);

        return new ScreenshotResultDto
        {
            Metadata = new ScreenshotMetadataDto
            {
                Width = captureInfo.PixelWidth,
                Height = captureInfo.PixelHeight,
                NodeId = nodeId ?? string.Empty,
            },
            PngBytes = pngBytes,
        };
#endif
    }

    /// <inheritdoc/>
    public async Task<ScreenshotResultDto> CaptureScreenshotAsync(WpfLocator locator, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.CaptureScreenshotAsync(nodeId, ct).ConfigureAwait(false);
    }

    // ── Private helpers (must run on the Dispatcher thread) ────────────────────────

    /// <summary>
    /// Resolves the screenshot target from a nodeId (or main window if null), extracting
    /// the HWND of the containing window and (optionally) the crop rect for sub-elements.
    /// All WPF object access happens here on the Dispatcher thread.
    /// </summary>
    private ScreenshotTargetInfo ResolveScreenshotTarget(string? nodeId)
    {
        Visual visual;

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
            // Default: capture the main window.
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

        // Get the HwndSource (root window hosting this visual).
        var hwndSource = PresentationSource.FromVisual(visual) as HwndSource;
        if (hwndSource is null || hwndSource.Handle == IntPtr.Zero)
        {
            throw new SnoopException(
                SnoopErrorCode.ElementNotRenderable,
                nodeId is not null
                    ? $"Node '{nodeId}' is not connected to a visible window (no HwndSource). " +
                      "Scroll the element into view or focus its parent window first."
                    : "Main window has no HwndSource — it may not be visible.",
                targetId: nodeId,
                suggestions: new[] { SnoopSuggestions.ElementOffscreen });
        }

        var hwnd = hwndSource.Handle;

        // For sub-element capture: compute the element bounds in device pixels
        // relative to the top-left of the containing window.
        Int32Rect? cropRect = null;
        int pixelWidth;
        int pixelHeight;

        if (nodeId is not null && !ReferenceEquals(visual, Application.Current?.MainWindow))
        {
            // Get bounds of the element in root visual coordinates.
            var rootVisual = hwndSource.RootVisual;

            if (rootVisual is not null && !ReferenceEquals(visual, rootVisual))
            {
                GeneralTransform? transform;
                try
                {
                    transform = visual.TransformToAncestor(rootVisual);
                }
                catch (InvalidOperationException)
                {
                    transform = null;
                }

                if (transform is not null)
                {
                    var renderSize = visual is UIElement ui ? ui.RenderSize : new Size(0, 0);

                    if (renderSize.Width > 0 && renderSize.Height > 0)
                    {
                        var topLeft = transform.Transform(new Point(0, 0));
                        var bottomRight = transform.Transform(new Point(renderSize.Width, renderSize.Height));

                        // Convert from DIPs to device pixels.
                        var dpiScaleX = hwndSource.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                        var dpiScaleY = hwndSource.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                        var pixX = (int)Math.Round(topLeft.X * dpiScaleX);
                        var pixY = (int)Math.Round(topLeft.Y * dpiScaleY);
                        var pixW = (int)Math.Round((bottomRight.X - topLeft.X) * dpiScaleX);
                        var pixH = (int)Math.Round((bottomRight.Y - topLeft.Y) * dpiScaleY);

                        if (pixW > 0 && pixH > 0)
                        {
                            cropRect = new Int32Rect(pixX, pixY, pixW, pixH);
                            return new ScreenshotTargetInfo(hwnd, cropRect, pixW, pixH);
                        }

                        throw new SnoopException(
                            SnoopErrorCode.ElementNotRenderable,
                            $"Element has zero pixel size ({pixW}×{pixH}) and cannot be captured.",
                            targetId: nodeId,
                            suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
                    }
                }
            }
        }

        // Full-window capture: use the window client area size.
        var windowSize = hwndSource.RootVisual is UIElement rootUi
            ? rootUi.RenderSize
            : new Size(0, 0);

        if (windowSize.Width <= 0 || windowSize.Height <= 0)
        {
            throw new SnoopException(
                SnoopErrorCode.ElementNotRenderable,
                "Window has zero render size and cannot be captured.",
                targetId: nodeId,
                suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
        }

        var winDpiX = hwndSource.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var winDpiY = hwndSource.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        pixelWidth = (int)Math.Round(windowSize.Width * winDpiX);
        pixelHeight = (int)Math.Round(windowSize.Height * winDpiY);

        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            throw new SnoopException(
                SnoopErrorCode.ElementNotRenderable,
                $"Window pixel size ({pixelWidth}×{pixelHeight}) is invalid.",
                targetId: nodeId,
                suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
        }

        return new ScreenshotTargetInfo(hwnd, null, pixelWidth, pixelHeight);
    }

    /// <summary>
    /// Carry-only record: everything the async WGC capture step needs.
    /// Passed across the dispatcher boundary (all fields are plain structs / IntPtr).
    /// </summary>
    private sealed record ScreenshotTargetInfo(
        IntPtr Hwnd,
        Int32Rect? CropRect,
        int PixelWidth,
        int PixelHeight);
}
