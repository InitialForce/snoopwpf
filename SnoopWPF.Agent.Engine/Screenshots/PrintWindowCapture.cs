// SnoopWPF.Agent.Engine/Screenshots/PrintWindowCapture.cs
// Win32 PrintWindow-based screenshot capture. Works under RDP (no DWM composition
// required) and captures hardware-accelerated content via PW_RENDERFULLCONTENT
// (Windows 8.1+). Replaces WGC for environments where WGC cannot initialize —
// notably Remote Desktop sessions, where the WinRT GraphicsCaptureItem factory
// fails to project its COM pointer and throws InvalidCastException at CreateForWindow.

// P/Invoke structs intentionally mirror Win32 lowerCamelCase field names.
#pragma warning disable SA1307 // Accessible fields should begin with upper-case letter
#pragma warning disable SA1513 // Closing brace should be followed by blank line

namespace SnoopWPF.Agent.Engine.Screenshots;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// Captures a window (or sub-region) as PNG bytes using the Win32 PrintWindow API.
/// </summary>
/// <remarks>
/// <para>Unlike Windows.Graphics.Capture, PrintWindow does not depend on DWM
/// composition and works correctly inside Remote Desktop sessions. With the
/// <c>PW_RENDERFULLCONTENT</c> flag (Windows 8.1+) it also captures
/// hardware-accelerated surfaces (D3D, MediaElement, etc.).</para>
/// <para>Call on any thread — the native draw happens inside <c>PrintWindow</c>
/// on the caller thread; no WPF Dispatcher touch is required here.</para>
/// </remarks>
internal static class PrintWindowCapture
{
    private const uint PrintWindowRenderFullContent = 0x00000002;
    private const uint DibRgbColors = 0;
    private const int BiRgb = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("gdi32.dll")]
        internal static extern int GetDIBits(
            IntPtr hdc,
            IntPtr hbm,
            uint start,
            uint cLines,
            byte[] lpvBits,
            ref BITMAPINFO lpbmi,
            uint usage);
    }

    /// <summary>
    /// Captures the client area of <paramref name="hwnd"/> as PNG bytes and
    /// optionally crops to <paramref name="cropRect"/> (device pixels, relative
    /// to the client-area top-left).
    /// </summary>
    internal static Task<byte[]> CaptureAsPngAsync(
        IntPtr hwnd,
        Int32Rect? cropRect,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (hwnd == IntPtr.Zero)
        {
            throw new SnoopException(
                SnoopErrorCode.ElementNotRenderable,
                "Cannot capture screenshot: HWND is null.",
                suggestions: new[] { SnoopSuggestions.ElementOffscreen });
        }

        if (!NativeMethods.GetClientRect(hwnd, out var rect))
        {
            throw new SnoopException(
                SnoopErrorCode.ElementNotRenderable,
                $"GetClientRect failed for HWND 0x{hwnd:X} (error {Marshal.GetLastWin32Error()}).",
                suggestions: new[] { SnoopSuggestions.ElementOffscreen });
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            throw new SnoopException(
                SnoopErrorCode.ElementNotRenderable,
                $"Window client area is empty ({width}×{height}).",
                suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
        }

        var pixels = CaptureWindowPixels(hwnd, width, height);
        var png = EncodeToPng(pixels, width, height, cropRect);
        return Task.FromResult(png);
    }

    private static byte[] CaptureWindowPixels(IntPtr hwnd, int width, int height)
    {
        var windowDc = NativeMethods.GetDC(hwnd);
        if (windowDc == IntPtr.Zero)
        {
            throw new SnoopException(
                SnoopErrorCode.ElementNotRenderable,
                $"GetDC failed for HWND 0x{hwnd:X} (error {Marshal.GetLastWin32Error()}).",
                suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
        }

        var memDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var oldBitmap = IntPtr.Zero;
        try
        {
            memDc = NativeMethods.CreateCompatibleDC(windowDc);
            if (memDc == IntPtr.Zero)
            {
                throw new SnoopException(
                    SnoopErrorCode.ElementNotRenderable,
                    $"CreateCompatibleDC failed (error {Marshal.GetLastWin32Error()}).",
                    suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
            }

            bitmap = NativeMethods.CreateCompatibleBitmap(windowDc, width, height);
            if (bitmap == IntPtr.Zero)
            {
                throw new SnoopException(
                    SnoopErrorCode.ElementNotRenderable,
                    $"CreateCompatibleBitmap failed for {width}×{height} (error {Marshal.GetLastWin32Error()}).",
                    suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
            }

            oldBitmap = NativeMethods.SelectObject(memDc, bitmap);

            if (!NativeMethods.PrintWindow(hwnd, memDc, PrintWindowRenderFullContent))
            {
                throw new SnoopException(
                    SnoopErrorCode.ElementNotRenderable,
                    $"PrintWindow failed for HWND 0x{hwnd:X} (error {Marshal.GetLastWin32Error()}). " +
                    "Ensure the window is valid and not minimized.",
                    suggestions: new[] { SnoopSuggestions.ElementOffscreen });
            }

            var stride = width * 4;
            var pixels = new byte[stride * height];
            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // negative = top-down row order
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BiRgb,
                },
            };

            var scanLines = NativeMethods.GetDIBits(memDc, bitmap, 0, (uint)height, pixels, ref bmi, DibRgbColors);
            if (scanLines == 0)
            {
                throw new SnoopException(
                    SnoopErrorCode.ElementNotRenderable,
                    "GetDIBits returned zero scan lines.",
                    suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
            }

            return pixels;
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero && memDc != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memDc, oldBitmap);
            }
            if (bitmap != IntPtr.Zero)
            {
                _ = NativeMethods.DeleteObject(bitmap);
            }
            if (memDc != IntPtr.Zero)
            {
                _ = NativeMethods.DeleteDC(memDc);
            }
            _ = NativeMethods.ReleaseDC(hwnd, windowDc);
        }
    }

    private static byte[] EncodeToPng(byte[] bgraPixels, int width, int height, Int32Rect? cropRect)
    {
        var stride = width * 4;
        var source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            bgraPixels,
            stride);

        BitmapSource finalSource = source;
        if (cropRect.HasValue)
        {
            var crop = cropRect.Value;
            var clampedX = Math.Max(0, Math.Min(crop.X, width - 1));
            var clampedY = Math.Max(0, Math.Min(crop.Y, height - 1));
            var clampedW = Math.Max(1, Math.Min(crop.Width, width - clampedX));
            var clampedH = Math.Max(1, Math.Min(crop.Height, height - clampedY));
            finalSource = new CroppedBitmap(source, new Int32Rect(clampedX, clampedY, clampedW, clampedH));
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(finalSource));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
