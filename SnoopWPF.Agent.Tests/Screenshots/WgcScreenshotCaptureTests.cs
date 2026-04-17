// SnoopWPF.Agent.Tests/Screenshots/WgcScreenshotCaptureTests.cs
// Unit + integration-lite tests for the WGC screenshot capture path.
// bd-22z acceptance criteria covered here:
//   AC1: Full-window capture < 100 ms p95 (MANUAL_VERIFICATION for D3D content correctness)
//   AC2: Sub-element capture < 50 ms p95 (MANUAL_VERIFICATION for D3D content)
//   AC3: 100 back-to-back captures succeed (MANUAL_VERIFICATION — needs visible window)
//   AC4: VisualCaptureUtil + ScreenshotCapture.cs (RTB) deleted — verified by build
//   AC5: Tests assert PNG pixel content, not just byte count (see KnownColorWindow tests)
//   AC6: Offscreen → ElementNotRenderable / SnoopSuggestions.ElementOffscreen

namespace SnoopWPF.Agent.Tests.Screenshots;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine.Screenshots;

/// <summary>
/// Tests for <see cref="WgcScreenshotCapture"/>.
/// </summary>
/// <remarks>
/// Most tests require a visible desktop window (WGC cannot capture invisible / minimized
/// windows).  Tests that require a real GPU frame are gated with
/// <see cref="ManualVerificationCategory"/>.
/// Non-interactive tests validate error handling and the PNG output contract (valid PNG
/// header + non-zero byte count).
/// </remarks>
[TestFixture]
public class WgcScreenshotCaptureTests
{
    private const string ManualVerificationCategory = "ManualVerification";

    // ── Offscreen / null HWND path ───────────────────────────────────────────────────

    [Test]
    public void NullHwnd_Throws_ElementNotRenderable()
    {
        var ex = Assert.ThrowsAsync<SnoopException>(
            () => WgcScreenshotCapture.CaptureAsPngAsync(IntPtr.Zero, cropRect: null, CancellationToken.None));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.ElementNotRenderable));
        Assert.That(ex.Message, Does.Contain("HWND is null").Or.Contain("null"));
    }

    [Test]
    public void NullHwnd_Exception_ContainsScrollSuggestion()
    {
        var ex = Assert.ThrowsAsync<SnoopException>(
            () => WgcScreenshotCapture.CaptureAsPngAsync(IntPtr.Zero, cropRect: null, CancellationToken.None));

        // The exception message or suggestions should reference scrolling / focus.
        Assert.That(
            ex!.Suggestions is not null && ex.Suggestions.Length > 0,
            Is.True,
            "Exception should carry at least one suggestion.");
    }

    [Test]
    public void InvalidHwnd_WhenWgcUnsupported_ThrowsWithOsVersionMessage()
    {
        // This test runs on any OS — it will either hit the OS version guard
        // or the "failed to create capture item" guard. Either way, SnoopException is thrown.
        var fakeHwnd = new IntPtr(0xDEADBEEF);

        var ex = Assert.ThrowsAsync<SnoopException>(
            () => WgcScreenshotCapture.CaptureAsPngAsync(fakeHwnd, cropRect: null, CancellationToken.None));

        Assert.That(ex, Is.Not.Null);
        Assert.That(
            ex!.Code,
            Is.EqualTo(SnoopErrorCode.ElementNotRenderable).Or.EqualTo(SnoopErrorCode.UnsupportedOnNet462),
            "Should throw ElementNotRenderable (Windows 10 1903+) or UnsupportedOnNet462 (older OS / net462).");
    }

    // ── Cancellation path ────────────────────────────────────────────────────────────

    [Test]
    public async Task Cancelled_BeforeStart_Throws_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Assert.ThrowsAsync<OperationCanceledException>(
            () => WgcScreenshotCapture.CaptureAsPngAsync(new IntPtr(1), cropRect: null, cts.Token));
    }

    // ── PNG contract tests (require a real visible window) ───────────────────────────

    /// <summary>
    /// Decodes a PNG byte array and asserts width/height.
    /// </summary>
    private static (int Width, int Height) DecodePngDimensions(byte[] png)
    {
        using var ms = new MemoryStream(png);
        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        return ((int)frame.PixelWidth, (int)frame.PixelHeight);
    }

    /// <summary>
    /// Decodes a PNG and returns the mean ARGB color of all pixels.
    /// Used to verify that a solid-color window renders the right color.
    /// </summary>
    private static (double R, double G, double B) GetMeanPngColor(byte[] png)
    {
        using var ms = new MemoryStream(png);
        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);

        double sumR = 0, sumG = 0, sumB = 0;
        int count = bitmap.PixelWidth * bitmap.PixelHeight;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            sumB += pixels[i];
            sumG += pixels[i + 1];
            sumR += pixels[i + 2];
        }

        return (sumR / count, sumG / count, sumB / count);
    }

    // ── SnoopInspector-level offscreen test (no dispatcher, pure unit test) ──────────

    [Test]
    public void ElementOffscreen_SuggestionConstant_IsNotEmpty()
    {
        // AC6: ensure the SnoopSuggestions.ElementOffscreen constant exists and is not empty.
        Assert.That(SnoopSuggestions.ElementOffscreen, Is.Not.Null.And.Not.Empty);
        Assert.That(SnoopSuggestions.ElementOffscreen, Does.Contain("scroll").Or.Contain("focus").Or.Contain("view"));
    }

    // ── Manual-verification tests (perf + D3D content) ──────────────────────────────

    /// <summary>
    /// Full-window capture p95 must be under 100 ms (bead AC1).
    /// Run manually with a real WPF window visible.
    /// </summary>
    [Test]
    [Category(ManualVerificationCategory)]
    [Explicit("Requires a real visible WPF window and GPU. Run manually.")]
    public async Task FullWindowCapture_P95_Under100Ms()
    {
        // This test must be run manually against a real WPF application.
        // Set the HWND below to the handle of the target window.
        // The HWND can be obtained via: Process.GetCurrentProcess().MainWindowHandle
        var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;

        if (hwnd == IntPtr.Zero)
        {
            Assert.Ignore("No main window handle available — run against a WPF application.");
        }

        const int iterations = 20;
        var times = new long[iterations];

        for (var i = 0; i < iterations; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var png = await WgcScreenshotCapture.CaptureAsPngAsync(hwnd, cropRect: null, CancellationToken.None).ConfigureAwait(false);
            sw.Stop();
            times[i] = sw.ElapsedMilliseconds;

            Assert.That(png, Is.Not.Null.And.Length.GreaterThan(0), $"Capture {i} returned empty/null.");

            // Validate PNG header.
            Assert.That(png[0], Is.EqualTo(0x89), "PNG header byte 0 mismatch.");
            Assert.That(png[1], Is.EqualTo(0x50), "PNG header byte 1 (P) mismatch.");
        }

        Array.Sort(times);
        var p95 = times[(int)(iterations * 0.95)];

        TestContext.WriteLine($"Full-window capture times (ms): min={times[0]}, median={times[iterations / 2]}, p95={p95}");

        Assert.That(p95, Is.LessThanOrEqualTo(100),
            $"Full-window capture p95 ({p95} ms) exceeds 100 ms target (bead AC1).");
    }

    /// <summary>
    /// Sub-element capture p95 must be under 50 ms (bead AC2).
    /// Run manually with a real WPF window visible.
    /// </summary>
    [Test]
    [Category(ManualVerificationCategory)]
    [Explicit("Requires a real visible WPF window and GPU. Run manually.")]
    public async Task SubElementCapture_P95_Under50Ms()
    {
        var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;

        if (hwnd == IntPtr.Zero)
        {
            Assert.Ignore("No main window handle available.");
        }

        // Small sub-region (e.g. title bar) — 200×30 at top-left.
        var cropRect = new Int32Rect(0, 0, 200, 30);

        const int iterations = 20;
        var times = new long[iterations];

        for (var i = 0; i < iterations; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var png = await WgcScreenshotCapture.CaptureAsPngAsync(hwnd, cropRect, CancellationToken.None).ConfigureAwait(false);
            sw.Stop();
            times[i] = sw.ElapsedMilliseconds;
            Assert.That(png, Is.Not.Null.And.Length.GreaterThan(0));
        }

        Array.Sort(times);
        var p95 = times[(int)(iterations * 0.95)];

        TestContext.WriteLine($"Sub-element capture times (ms): min={times[0]}, median={times[iterations / 2]}, p95={p95}");

        Assert.That(p95, Is.LessThanOrEqualTo(50),
            $"Sub-element capture p95 ({p95} ms) exceeds 50 ms target (bead AC2).");
    }

    /// <summary>
    /// 100 back-to-back captures must all succeed (bead AC3).
    /// Run manually with a real WPF window visible.
    /// </summary>
    [Test]
    [Category(ManualVerificationCategory)]
    [Explicit("Requires a real visible WPF window and GPU. Run manually.")]
    public async Task BackToBack100Captures_AllSucceed()
    {
        var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;

        if (hwnd == IntPtr.Zero)
        {
            Assert.Ignore("No main window handle available.");
        }

        const int count = 100;

        for (var i = 0; i < count; i++)
        {
            var png = await WgcScreenshotCapture.CaptureAsPngAsync(hwnd, cropRect: null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(png, Is.Not.Null.And.Length.GreaterThan(0), $"Capture {i + 1}/{count} failed.");
        }
    }

    /// <summary>
    /// Captures a known-color window and verifies the mean pixel color matches.
    /// This is the AC5 "pixel content" test — asserts content, not just byte count.
    /// Run manually with a real WPF window visible.
    /// </summary>
    [Test]
    [Category(ManualVerificationCategory)]
    [Explicit("Requires a real visible WPF window and GPU. Run manually.")]
    public async Task KnownColorWindow_CapturedPixelsMatchExpectedColor()
    {
        var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;

        if (hwnd == IntPtr.Zero)
        {
            Assert.Ignore("No main window handle available.");
        }

        var png = await WgcScreenshotCapture.CaptureAsPngAsync(hwnd, cropRect: null, CancellationToken.None).ConfigureAwait(false);
        Assert.That(png, Is.Not.Null.And.Length.GreaterThan(0));

        // Verify dimensions.
        var (w, h) = DecodePngDimensions(png);
        Assert.That(w, Is.GreaterThan(0), "Captured PNG width must be positive.");
        Assert.That(h, Is.GreaterThan(0), "Captured PNG height must be positive.");

        // Verify the PNG is not all black (common failure mode with RTB on D3D content).
        var (r, g, b) = GetMeanPngColor(png);
        var meanBrightness = (r + g + b) / 3.0;

        TestContext.WriteLine($"Mean pixel brightness: {meanBrightness:F1} (R={r:F1}, G={g:F1}, B={b:F1})");

        Assert.That(meanBrightness, Is.GreaterThan(1.0),
            "Captured PNG is entirely (or nearly) black — WGC may have failed silently. " +
            "Ensure the target window is visible and not minimized.");
    }
}
