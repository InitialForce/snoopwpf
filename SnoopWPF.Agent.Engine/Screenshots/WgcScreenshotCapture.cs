// SnoopWPF.Agent.Engine/Screenshots/WgcScreenshotCapture.cs
// Windows.Graphics.Capture-based screenshot capture (replaces RenderTargetBitmap path).
// FX6 bd-22z: WGC runs off the UI thread, captures GPU-composited frames including D3D-interop
// content, and avoids dispatcher starvation on complex windows.
//
// Runtime requirement: Windows 10 version 1903 (19H1, build 18362) or later.
// The versioned TFM (net6.0-windows10.0.19041.0 / net8.0-windows10.0.19041.0) gates compilation.
// A runtime guard further enforces the Windows version contract.

namespace SnoopWPF.Agent.Engine.Screenshots;

#if !NET462

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SnoopWPF.Agent.Contracts;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

/// <summary>
/// Captures a window (or sub-region) as PNG bytes using Windows.Graphics.Capture (WGC).
/// </summary>
/// <remarks>
/// <para>WGC runs entirely off the WPF UI thread — only a brief dispatcher touch is needed
/// to obtain the HWND and element bounds before capture begins.</para>
/// <para>Requires Windows 10 1903+ (build 18362). Throws <see cref="SnoopException"/> with
/// <see cref="SnoopErrorCode.ElementNotRenderable"/> if called on an older OS.</para>
/// </remarks>
internal static class WgcScreenshotCapture
{
    // ── COM / WinRT interop ──────────────────────────────────────────────────────────

    /// <summary>
    /// IGraphicsCaptureItemInterop — factory interface to create a <see cref="GraphicsCaptureItem"/>
    /// from a Win32 HWND.  GUID from the Windows SDK header.
    /// </summary>
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        /// <summary>Creates a capture item for a Win32 window.</summary>
        GraphicsCaptureItem CreateForWindow(IntPtr window, ref Guid iid);

        /// <summary>Creates a capture item for a monitor.</summary>
        GraphicsCaptureItem CreateForMonitor(IntPtr monitor, ref Guid iid);
    }

    /// <summary>
    /// IGraphicsCaptureSession2 — allows suppressing the yellow capture border.
    /// Available on Windows 11 22H2 / Windows 10 19041+, best-effort.
    /// </summary>
    [ComImport]
    [Guid("2C4D7F9F-4B9B-4730-A2A2-9AB0EDED3CE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureSession2
    {
        /// <summary>Gets or sets whether the yellow border is shown around the captured window.</summary>
        bool IsBorderRequired { get; set; }
    }

    // ── Native interop (P/Invoke) ────────────────────────────────────────────────────

    private static class NativeMethods
    {
        // D3D_DRIVER_TYPE values.
        internal const int D3dDriverTypeHardware = 1;
        internal const int D3dDriverTypeWarp = 5;

        // D3D11_SDK_VERSION.
        internal const uint D3d11SdkVersion = 7;

        // IDXGIDevice GUID — used for QueryInterface from ID3D11Device.
        internal static readonly Guid DxgiDeviceGuid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

        [DllImport("d3d11.dll", ExactSpelling = true)]
        internal static extern int D3D11CreateDevice(
            IntPtr pAdapter,
            int driverType,
            IntPtr software,
            uint flags,
            IntPtr pFeatureLevels,
            uint featureLevels,
            uint sdkVersion,
            out IntPtr ppDevice,
            IntPtr pFeatureLevel,
            out IntPtr ppImmediateContext);

        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", ExactSpelling = true)]
        internal static extern int CreateDirect3D11DeviceFromDXGIDevice(
            IntPtr dxgiDevice,
            out IntPtr graphicsDevice);
    }

    // ── Public API ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Captures the entire window identified by <paramref name="hwnd"/> as PNG bytes,
    /// then optionally crops to <paramref name="cropRect"/> (in screen-device pixels).
    /// </summary>
    /// <param name="hwnd">Window handle of the target window.</param>
    /// <param name="cropRect">
    /// If not <see langword="null"/>, the captured frame is cropped to this region.
    /// The rect is in device pixels relative to the top-left of the captured window client area.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>PNG-encoded bytes of the captured image.</returns>
    /// <exception cref="SnoopException">
    /// <see cref="SnoopErrorCode.ElementNotRenderable"/> when capture fails.
    /// </exception>
    internal static async Task<byte[]> CaptureAsPngAsync(
        IntPtr hwnd,
        Int32Rect? cropRect,
        CancellationToken ct)
    {
        // Check cancellation first — before any validation that might throw a different exception.
        ct.ThrowIfCancellationRequested();

        // Runtime OS guard — WGC requires Windows 10 1903+ (build 18362).
        if (!IsWgcSupported())
        {
            throw new SnoopException(
                SnoopErrorCode.ElementNotRenderable,
                "Windows.Graphics.Capture requires Windows 10 version 1903 (build 18362) or later. " +
                "The current OS does not meet this requirement.",
                suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
        }

        if (hwnd == IntPtr.Zero)
        {
            throw new SnoopException(
                SnoopErrorCode.ElementNotRenderable,
                "Cannot capture screenshot: the element is not connected to a visible window (HWND is null). " +
                "Scroll the element into view or focus the parent window first.",
                suggestions: new[] { SnoopSuggestions.ElementOffscreen });
        }

        // Create WinRT GraphicsCaptureItem for the HWND.
        GraphicsCaptureItem item;
        try
        {
            var factory = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            var iid = typeof(GraphicsCaptureItem).GUID;
            item = factory.CreateForWindow(hwnd, ref iid);
        }
        catch (Exception ex) when (ex is COMException or SEHException or AccessViolationException or ArgumentException)
        {
            throw new SnoopException(
                SnoopErrorCode.ElementNotRenderable,
                $"Failed to create WGC capture item for HWND 0x{hwnd:X}: {ex.Message}. " +
                "The window may be minimized, off-screen, invalid, or belong to a different desktop session.",
                ex,
                suggestions: new[] { SnoopSuggestions.ElementOffscreen });
        }

        // Create D3D11 device (hardware, with WARP software fallback for headless CI).
        IDirect3DDevice d3dDevice;
        try
        {
            d3dDevice = CreateD3D11Device();
        }
        catch (COMException ex)
        {
            throw new SnoopException(
                SnoopErrorCode.ElementNotRenderable,
                $"Failed to create D3D11 device for WGC capture: {ex.Message}",
                ex,
                suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
        }

        using var frameArrived = new SemaphoreSlim(0, 1);
        Direct3D11CaptureFrame? capturedFrame = null;

        var itemSize = item.Size;

        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            d3dDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,     // single-frame buffer — we only need one frame
            itemSize);

        var session = framePool.CreateCaptureSession(item);

        // Suppress the yellow capture border — best-effort (Win10 19041+ or Win11 22H2).
        TrySuppressBorder(session);

        framePool.FrameArrived += (pool, _) =>
        {
            var frame = pool.TryGetNextFrame();
            if (frame is not null)
            {
                var prev = Interlocked.Exchange(ref capturedFrame, frame);
                prev?.Dispose();
                frameArrived.Release();
            }
        };

        session.StartCapture();

        // Wait for the first frame (typically 1–2 display refresh cycles, ≤33 ms).
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(2000));

        try
        {
            await frameArrived.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SnoopException(
                SnoopErrorCode.ElementNotRenderable,
                "WGC frame capture timed out after 2 s — the window may not be visible on screen. " +
                "Ensure the window is in the foreground and not minimized.",
                suggestions: new[] { SnoopSuggestions.ElementOffscreen });
        }
        finally
        {
            // Dispose session to stop capture; FramePool Dispose follows (using above).
            session.Dispose();
        }

        var frame = Interlocked.Exchange(ref capturedFrame, null)
            ?? throw new SnoopException(
                SnoopErrorCode.ElementNotRenderable,
                "WGC raised FrameArrived but no frame was retrievable.",
                suggestions: new[] { SnoopSuggestions.ElementNotRenderable });

        using (frame)
        {
            ct.ThrowIfCancellationRequested();

            // Convert the IDirect3DSurface to a SoftwareBitmap (GPU → CPU transfer).
            var softwareBitmap = await SoftwareBitmap
                .CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied)
                .AsTask(ct)
                .ConfigureAwait(false);

            using (softwareBitmap)
            {
                // Apply crop if this is a sub-element capture.
                SoftwareBitmap renderBitmap;
                if (cropRect.HasValue)
                {
                    renderBitmap = await CropSoftwareBitmapAsync(softwareBitmap, cropRect.Value, ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    renderBitmap = softwareBitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                        ? SoftwareBitmap.Copy(softwareBitmap)
                        : SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                }

                using (renderBitmap)
                {
                    return await EncodeToPngAsync(renderBitmap, ct).ConfigureAwait(false);
                }
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private static bool IsWgcSupported()
    {
        // Windows 10 1903 = build 18362.
        var version = Environment.OSVersion.Version;
        return version.Major > 10
            || (version.Major == 10 && version.Build >= 18362);
    }

    private static IDirect3DDevice CreateD3D11Device()
    {
        // Try hardware device first; fall back to WARP (software) for headless environments.
        int hr = NativeMethods.D3D11CreateDevice(
            IntPtr.Zero,
            NativeMethods.D3dDriverTypeHardware,
            IntPtr.Zero,
            flags: 0,
            IntPtr.Zero,
            featureLevels: 0,
            NativeMethods.D3d11SdkVersion,
            ppDevice: out IntPtr d3dDevicePtr,
            IntPtr.Zero,
            ppImmediateContext: out IntPtr contextPtr);

        if (hr < 0)
        {
            // WARP software fallback — always available, used in CI without a GPU.
            hr = NativeMethods.D3D11CreateDevice(
                IntPtr.Zero,
                NativeMethods.D3dDriverTypeWarp,
                IntPtr.Zero,
                flags: 0,
                IntPtr.Zero,
                featureLevels: 0,
                NativeMethods.D3d11SdkVersion,
                ppDevice: out d3dDevicePtr,
                IntPtr.Zero,
                ppImmediateContext: out contextPtr);

            Marshal.ThrowExceptionForHR(hr);
        }

        // Release the context — we don't use it directly.
        if (contextPtr != IntPtr.Zero)
        {
            Marshal.Release(contextPtr);
        }

        try
        {
            // QI for IDXGIDevice from ID3D11Device.
            var dxgiGuid = NativeMethods.DxgiDeviceGuid;
            int qiHr = Marshal.QueryInterface(d3dDevicePtr, ref dxgiGuid, out IntPtr dxgiDevicePtr);
            Marshal.ThrowExceptionForHR(qiHr);

            try
            {
                // Wrap DXGI device as WinRT IDirect3DDevice.
                int wrapHr = NativeMethods.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePtr, out IntPtr rtPtr);
                Marshal.ThrowExceptionForHR(wrapHr);

                var device = (IDirect3DDevice)Marshal.GetObjectForIUnknown(rtPtr);
                Marshal.Release(rtPtr);
                return device;
            }
            finally
            {
                Marshal.Release(dxgiDevicePtr);
            }
        }
        finally
        {
            Marshal.Release(d3dDevicePtr);
        }
    }

    private static async Task<SoftwareBitmap> CropSoftwareBitmapAsync(
        SoftwareBitmap source,
        Int32Rect crop,
        CancellationToken ct)
    {
        // Clamp crop rect to source dimensions.
        var clampedX = Math.Max(0, Math.Min(crop.X, source.PixelWidth - 1));
        var clampedY = Math.Max(0, Math.Min(crop.Y, source.PixelHeight - 1));
        var clampedW = Math.Max(1, Math.Min(crop.Width, source.PixelWidth - clampedX));
        var clampedH = Math.Max(1, Math.Min(crop.Height, source.PixelHeight - clampedY));

        // Encode source to in-memory BMP stream, then decode with crop transform.
        using var ms = new InMemoryRandomAccessStream();

        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, ms).AsTask(ct).ConfigureAwait(false);

        var bgra8Source = source.BitmapPixelFormat == BitmapPixelFormat.Bgra8
            ? SoftwareBitmap.Copy(source)
            : SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        using (bgra8Source)
        {
            encoder.SetSoftwareBitmap(bgra8Source);
            await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);
        }

        ms.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(ms).AsTask(ct).ConfigureAwait(false);

        var transform = new BitmapTransform
        {
            Bounds = new BitmapBounds
            {
                X = (uint)clampedX,
                Y = (uint)clampedY,
                Width = (uint)clampedW,
                Height = (uint)clampedH,
            },
        };

        return await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> EncodeToPngAsync(SoftwareBitmap bitmap, CancellationToken ct)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(ct).ConfigureAwait(false);

        // BitmapEncoder requires Bgra8.
        var bgra8 = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
            ? SoftwareBitmap.Copy(bitmap)
            : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        using (bgra8)
        {
            encoder.SetSoftwareBitmap(bgra8);
            await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);
        }

        // Read back to byte[] via WindowsRuntimeBufferExtensions.
        stream.Seek(0);
        using var memStream = new MemoryStream();
        await stream.AsStream().CopyToAsync(memStream, ct).ConfigureAwait(false);
        return memStream.ToArray();
    }

    private static void TrySuppressBorder(GraphicsCaptureSession session)
    {
        try
        {
            if (GraphicsCaptureSession.IsSupported())
            {
                // Use direct COM cast — IGraphicsCaptureSession2 is a super-interface.
                var session2 = (IGraphicsCaptureSession2)(object)session;
                session2.IsBorderRequired = false;
            }
        }
        catch
        {
            // Best-effort — silently ignore if not available on this OS version.
        }
    }
}

#endif
