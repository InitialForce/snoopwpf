namespace SnoopWPF.Agent.Engine;

using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snoop.Infrastructure;

/// <summary>
/// Captures a WPF Visual as a PNG byte array using RenderTargetBitmap.
/// IMPORTANT: Must be called from within a Dispatcher.Invoke block — accesses live WPF objects.
/// Never writes temp files — returns the PNG bytes in memory.
/// </summary>
public static class ScreenshotCapture
{
    /// <summary>Maximum dimension (width or height) for captured screenshots.</summary>
    public const int MaxDimension = 4096;

    /// <summary>
    /// Captures the given <paramref name="visual"/> as PNG bytes.
    /// Returns null if the visual is not safe to render (zero size, not connected, etc.).
    /// </summary>
    /// <param name="visual">The WPF Visual to capture.</param>
    /// <param name="dpiX">Horizontal DPI for the render (default 96).</param>
    /// <param name="dpiY">Vertical DPI for the render (default 96).</param>
    /// <returns>PNG-encoded byte array, or null if capture is not possible.</returns>
    public static byte[]? CaptureAsPng(Visual visual, int dpiX = 96, int dpiY = 96)
    {
        if (visual is null)
        {
            return null;
        }

        if (!VisualCaptureUtil.IsSafeToVisualize(visual))
        {
            return null;
        }

        // Determine render size.
        var size = GetRenderSize(visual);

        if (size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        // Cap at max dimension to avoid OOM.
        var scaleX = 1.0;
        var scaleY = 1.0;

        if (size.Width > MaxDimension)
        {
            scaleX = MaxDimension / size.Width;
        }

        if (size.Height > MaxDimension)
        {
            scaleY = MaxDimension / size.Height;
        }

        var scale = Math.Min(scaleX, scaleY);
        var clampedWidth = (int)Math.Ceiling(size.Width * scale);
        var clampedHeight = (int)Math.Ceiling(size.Height * scale);

        if (clampedWidth <= 0 || clampedHeight <= 0)
        {
            return null;
        }

        // Use VisualCaptureUtil.RenderVisualWithHighQuality for tiled high-quality rendering.
        // Pass the effective DPI (clamped for resolution cap).
        var effectiveDpiX = (int)Math.Round(dpiX * scale);
        var effectiveDpiY = (int)Math.Round(dpiY * scale);

        if (effectiveDpiX <= 0)
        {
            effectiveDpiX = 1;
        }

        if (effectiveDpiY <= 0)
        {
            effectiveDpiY = 1;
        }

        RenderTargetBitmap bitmap;
        try
        {
            bitmap = VisualCaptureUtil.RenderVisualWithHighQuality(visual, effectiveDpiX, effectiveDpiY);
        }
        catch
        {
            // Capture failed (e.g. D3D resources not available headlessly).
            return null;
        }

        // Encode as PNG in memory — no temp files.
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Returns the render size of the visual.
    /// </summary>
    public static Size GetRenderSize(Visual visual)
    {
        if (visual is UIElement uiElement)
        {
            return uiElement.RenderSize;
        }

        var bounds = VisualTreeHelper.GetDescendantBounds(visual);
        return new Size(bounds.Width, bounds.Height);
    }
}
