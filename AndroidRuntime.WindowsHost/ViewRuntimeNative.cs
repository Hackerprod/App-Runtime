#nullable enable
using System.IO;
using System.Runtime.InteropServices;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.WindowsHost;

/// <summary>
/// P/Invoke surface for ViewRuntime's Phase-1 C ABI (Ui\ViewRuntime, header
/// viewruntime_backend.h). Host-side only, per the "WPF types never enter
/// Core" rule — the native-interop surface must not live in
/// AndroidRuntime.Core. The ABI exports are unmangled `extern "C"` symbols
/// (verified with dumpbin: viewruntime_surface_create/destroy/resize,
/// frame_begin, draw_fill_rect, draw_text, frame_end, measure_text,
/// surface_pixels).
///
/// Pixel format: the native buffer is straight ARGB8888 stored as uint32
/// (pack_color = a&lt;&lt;24 | r&lt;&lt;16 | g&lt;&lt;8 | b). On little-endian Windows that
/// uint32's memory byte order is B,G,R,A — EXACTLY the BGRA byte order
/// WindowsRetainedRenderer's `pixels` array and GDI StretchDIBits consume, so
/// a raw byte copy with no per-byte reorder is correct (confirmed against
/// viewruntime_backend.cpp's pack_color and the renderer's FillPixels).
/// </summary>
internal static partial class ViewRuntimeNative
{
    private const string Dll = "viewruntime_core.dll";

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint viewruntime_surface_create(string? font_path);

    [LibraryImport(Dll)]
    internal static partial void viewruntime_surface_destroy(nint surface);

    [LibraryImport(Dll)]
    internal static partial void viewruntime_surface_resize(nint surface, int pixel_width, int pixel_height, float density);

    [LibraryImport(Dll)]
    internal static partial void viewruntime_frame_begin(nint surface);

    [LibraryImport(Dll)]
    internal static partial void viewruntime_draw_fill_rect(
        nint surface, float x, float y, float w, float h,
        byte a, byte r, byte g, byte b, int view_id);

    [LibraryImport(Dll)]
    internal static partial void viewruntime_draw_text(
        nint surface, float x, float y, float w, float h,
        [MarshalAs(UnmanagedType.LPWStr)] string utf16_text, int text_len,
        float text_size_px, byte a, byte r, byte g, byte b, int view_id);

    [LibraryImport(Dll)]
    internal static partial void viewruntime_frame_end(nint surface);

    [LibraryImport(Dll)]
    internal static partial void viewruntime_measure_text(
        nint surface,
        [MarshalAs(UnmanagedType.LPWStr)] string utf16_text, int text_len,
        float text_size_px, float max_width_px,
        out float out_width_px, out float out_height_px, out float out_baseline_px);

    [LibraryImport(Dll)]
    internal static partial void viewruntime_surface_pixels(
        nint surface, out nint out_pixels, out int out_pitch, out int out_width, out int out_height);

    /// <summary>Creates a native ViewRuntime surface with the given TrueType font
    /// path (null → the native deterministic proportional fallback). Returns a
    /// zero handle when creation fails.</summary>
    internal static nint CreateSurface(string? fontPath) => viewruntime_surface_create(fontPath);

    /// <summary>Picks a real Windows TrueType font for the native surface, falling
    /// back to null (the native deterministic fallback) if none is present.</summary>
    internal static string? PickSystemFont()
    {
        string windowsFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (string candidate in new[] { "segoeui.ttf", "arial.ttf", "tahoma.ttf", "times.ttf" })
        {
            string path = Path.Combine(windowsFonts, candidate);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}

/// <summary>Owns a native ViewRuntime surface handle; deterministic disposal.
/// Not thread-safe by design — one surface per owning thread (the measurer's
/// surface is used on the Android lane; the renderer's on the WPF thread).</summary>
internal sealed class ViewRuntimeSurface : IDisposable
{
    private nint _handle;
    private int _disposed;

    private ViewRuntimeSurface(nint handle) => _handle = handle;

    internal static ViewRuntimeSurface? Create(string? fontPath)
    {
        nint handle = ViewRuntimeNative.CreateSurface(fontPath);
        return handle == 0 ? null : new ViewRuntimeSurface(handle);
    }

    internal void Resize(int pixelWidth, int pixelHeight, float density) => ViewRuntimeNative.viewruntime_surface_resize(_handle, pixelWidth, pixelHeight, density);

    internal void BeginFrame() => ViewRuntimeNative.viewruntime_frame_begin(_handle);

    internal void DrawFillRect(float x, float y, float w, float h, AndroidColor color, int viewId) =>
        ViewRuntimeNative.viewruntime_draw_fill_rect(_handle, x, y, w, h, color.A, color.R, color.G, color.B, viewId);

    internal void DrawText(float x, float y, float w, float h, string text, float textSizePx, AndroidColor color, int viewId) =>
        ViewRuntimeNative.viewruntime_draw_text(_handle, x, y, w, h, text, text.Length, textSizePx, color.A, color.R, color.G, color.B, viewId);

    internal void EndFrame() => ViewRuntimeNative.viewruntime_frame_end(_handle);

    /// <summary>Copies the finished BGRA frame into the caller's byte array
    /// (must be width*height*4). Returns the native pitch (width*4).</summary>
    internal int CopyPixels(byte[] destination)
    {
        ViewRuntimeNative.viewruntime_surface_pixels(_handle, out nint pixels, out int pitch, out int width, out int height);
        if (pixels == 0) return 0;
        int bytes = checked(width * height * 4);
        if (destination.Length < bytes) throw new ArgumentException("Destination buffer is smaller than the native frame.", nameof(destination));
        Marshal.Copy(pixels, destination, 0, bytes);
        return pitch;
    }

    internal AndroidTextMetrics Measure(string text, float textSizePixels, float maxWidth)
    {
        ViewRuntimeNative.viewruntime_measure_text(_handle, text, text.Length, textSizePixels, maxWidth, out float width, out float height, out float baseline);
        return new(width, height, baseline);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        nint handle = Interlocked.Exchange(ref _handle, 0);
        if (handle != 0) ViewRuntimeNative.viewruntime_surface_destroy(handle);
    }
}

/// <summary>Host-injected text measurer backed by ViewRuntime's real glyph
/// metrics (same font/advances as the rasterizer's draw path), so LAYOUT and
/// PAINT agree pixel-for-pixel. Owns its own native surface (used on the
/// Android lane); the renderer owns a separate one (used on the WPF thread) —
/// both created with the same font path, so metrics are identical. Falls back
/// to the deterministic stub when the native DLL is unavailable.</summary>
internal sealed class ViewRuntimeTextMeasurer : AndroidRuntime.Core.Ui.IAndroidTextMeasurer, IDisposable
{
    private readonly ViewRuntimeSurface? _surface;
    private readonly AndroidRuntime.Core.Ui.IAndroidTextMeasurer _fallback = new DeterministicAndroidTextMeasurer();

    internal ViewRuntimeTextMeasurer()
    {
        try { _surface = ViewRuntimeSurface.Create(ViewRuntimeNative.PickSystemFont()); }
        catch (DllNotFoundException) { _surface = null; }
        catch (BadImageFormatException) { _surface = null; }
    }

    public AndroidTextMetrics Measure(string text, float textSizePixels, float maxWidth) =>
        _surface is not null ? _surface.Measure(text, textSizePixels, maxWidth) : _fallback.Measure(text, textSizePixels, maxWidth);

    public void Dispose() => _surface?.Dispose();
}
