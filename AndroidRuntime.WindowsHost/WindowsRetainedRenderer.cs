#nullable enable
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.WindowsHost;

/// <summary>Immutable, revision-bound transfer object published by the Android lane.</summary>
internal sealed record WindowsRetainedFrame(
    long Revision,
    int PixelWidth,
    int PixelHeight,
    float Density,
    IReadOnlyList<AndroidDrawCommand> Commands,
    string SemanticSnapshot)
{
    internal static WindowsRetainedFrame From(AndroidUiFrame frame, long revision, int width, int height, float density) =>
        new(revision,
            width, height, density,
            new ReadOnlyCollection<AndroidDrawCommand>(frame.DisplayList.Commands.ToArray()),
            frame.SemanticSnapshot);
}

internal readonly record struct RetainedFrameSchedulerMetrics(long PublishedFrames, long RenderedFrames, long StaleFramesDropped, long CoalescedFrames);

/// <summary>Single-post frame scheduler. Publishing never invokes rendering inline.</summary>
internal sealed class RetainedFrameScheduler : IDisposable
{
    private readonly Action<Action> _post;
    private readonly Action<WindowsRetainedFrame> _render;
    private readonly object _gate = new();
    private WindowsRetainedFrame? _pending;
    private bool _posted;
    private bool _disposed;
    private long _latestRevision;
    private long _published, _rendered, _stale, _coalesced;

    internal RetainedFrameScheduler(Action<Action> post, Action<WindowsRetainedFrame> render)
    { _post = post ?? throw new ArgumentNullException(nameof(post)); _render = render ?? throw new ArgumentNullException(nameof(render)); }

    internal RetainedFrameSchedulerMetrics Metrics { get { lock (_gate) return new(_published, _rendered, _stale, _coalesced); } }

    internal void Publish(WindowsRetainedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        bool schedule = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _published++;
            if (frame.Revision <= _latestRevision || (_pending is not null && frame.Revision <= _pending.Revision)) { _stale++; return; }
            if (_pending is not null) _coalesced++;
            _pending = frame;
            if (!_posted) { _posted = true; schedule = true; }
        }
        if (schedule) _post(Drain);
    }

    private void Drain()
    {
        WindowsRetainedFrame? frame;
        lock (_gate)
        {
            if (_disposed) return;
            frame = _pending; _pending = null; _posted = false;
            if (frame is null || frame.Revision <= _latestRevision) { if (frame is not null) _stale++; return; }
            _latestRevision = frame.Revision;
        }
        _render(frame);
        lock (_gate) _rendered++;
    }

    public void Dispose() { lock (_gate) { _disposed = true; _pending = null; } }
}

internal sealed record WindowsFrameCapture(int Width, int Height, byte[] Bgra, string Sha256, long Revision, string SemanticSnapshot)
{
    internal WindowsPixelMismatch? FirstMismatch(WindowsFrameCapture other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Width != other.Width || Height != other.Height) return new(0, 0, default, default);
        int length = Math.Min(Bgra.Length, other.Bgra.Length);
        for (int i = 0; i + 3 < length; i += 4)
            if (Bgra[i] != other.Bgra[i] || Bgra[i + 1] != other.Bgra[i + 1] || Bgra[i + 2] != other.Bgra[i + 2] || Bgra[i + 3] != other.Bgra[i + 3])
                return new((i / 4) % Width, (i / 4) / Width, BitConverter.ToUInt32(Bgra, i), BitConverter.ToUInt32(other.Bgra, i));
        return Bgra.Length == other.Bgra.Length ? null : new(0, 0, default, default);
    }
}
internal readonly record struct WindowsPixelMismatch(int X, int Y, uint ExpectedBgra, uint ActualBgra);

/// <summary>
/// Retained Windows backend with a deterministic BGRA DIB. The same backing bitmap is used for
/// capture and presentation, avoiding WPF child-HWND airspace and capture/render divergence.
/// </summary>
internal sealed partial class WindowsRetainedRenderer : IDisposable
{
    private readonly object _gate = new();
    private WindowsRetainedFrame? _frame;
    private int _width = 1, _height = 1;
    private float _density = 1;
    private long _renderedRevision;
    private long _stale;
    private string? _toast;
    private ViewRuntimeSurface? _native;
    private bool _nativeAvailable;
    private bool _disposed;

    internal long RenderedRevision { get { lock (_gate) return _renderedRevision; } }
    internal long StaleFramesDropped { get { lock (_gate) return _stale; } }

    internal void Resize(int width, int height, float density)
    {
        if (width <= 0 || height <= 0 || !float.IsFinite(density) || density <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); _width = width; _height = height; _density = density; }
    }

    internal void Render(WindowsRetainedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (frame.Revision <= _renderedRevision) { _stale++; return; }
            _frame = frame with { Commands = new ReadOnlyCollection<AndroidDrawCommand>(frame.Commands.ToArray()) };
            _renderedRevision = frame.Revision;
        }
    }

    internal void SetToast(string? text) { lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); _toast = text; } }

    internal int? HitTest(float pixelX, float pixelY)
    {
        lock (_gate)
        {
            if (_frame is null || _disposed) return null;
            float x = pixelX / _density, y = pixelY / _density;
            for (int index = _frame.Commands.Count - 1; index >= 0; index--)
            {
                AndroidRect? rect = _frame.Commands[index] switch { AndroidFillRectCommand fill => fill.Rect, AndroidDrawTextCommand text => text.Rect, _ => null };
                if (rect is { } bounds && bounds.Contains(x, y) && _frame.Commands[index].ResourceId != 0) return _frame.Commands[index].ResourceId;
            }
            return null;
        }
    }

    internal WindowsFrameCapture Capture()
    {
        WindowsRetainedFrame? frame; int width, height; float density; string? toast;
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); frame = _frame; width = _width; height = _height; density = _density; toast = _toast; }
        byte[] pixels = RenderDib(frame, width, height, density, toast);
        return new(width, height, pixels, Convert.ToHexString(SHA256.HashData(pixels)), frame?.Revision ?? 0, frame?.SemanticSnapshot ?? string.Empty);
    }

    internal void Present(nint hdc)
    {
        WindowsFrameCapture capture = Capture();
        var header = BitmapInfoHeader.Create(capture.Width, -capture.Height);
        unsafe
        {
            fixed (byte* pixels = capture.Bgra)
                Gdi32.StretchDIBits(hdc, 0, 0, capture.Width, capture.Height, 0, 0, capture.Width, capture.Height, (nint)pixels, ref header, 0, 0x00CC0020);
        }
    }

    private byte[] RenderDib(WindowsRetainedFrame? frame, int width, int height, float density, string? toast)
    {
        int stride = checked(width * 4); var pixels = new byte[checked(stride * height)];
        EnsureNativeSurface(width, height, density);

        if (_nativeAvailable)
        {
            // Real ViewRuntime rasterizer: frame_begin, draw each command in
            // display-list order, frame_end, then copy the finished BGRA buffer.
            // Pixel byte order matches (ARGB8888 little-endian == BGRA), verified
            // against pack_color + FillPixels; direct Marshal.Copy, no reorder.
            _native!.BeginFrame();
            if (frame is not null)
                foreach (AndroidDrawCommand command in frame.Commands)
                    switch (command)
                    {
                        case AndroidFillRectCommand fill:
                            AndroidRect fr = Scale(fill.Rect, density);
                            _native.DrawFillRect(fr.X, fr.Y, fr.Width, fr.Height, fill.Color, fill.ViewId);
                            break;
                        case AndroidDrawTextCommand text:
                            AndroidRect tr = Scale(text.Rect, density);
                            _native.DrawText(tr.X, tr.Y, tr.Width, tr.Height, text.Text, Math.Max(1, text.TextSizePixels * density), text.Color, text.ViewId);
                            break;
                    }
            _native.EndFrame();
            _native.CopyPixels(pixels);
        }
        else
        {
            // Native rasterizer unavailable (DLL missing/load failed): fall back to
            // the original hand-rolled fake rasterizer so rendering still works.
            FillPixels(pixels, width, height, new AndroidRect(0, 0, width, height), new AndroidColor(255, 250, 250, 250));
            if (frame is not null)
                foreach (AndroidDrawCommand command in frame.Commands)
                    switch (command)
                    {
                        case AndroidFillRectCommand fill: FillPixels(pixels, width, height, Scale(fill.Rect, density), fill.Color); break;
                        case AndroidDrawTextCommand text: DrawPseudoText(pixels, width, height, Scale(text.Rect, density), text.Text, Math.Max(1, text.TextSizePixels * density), text.Color); break;
                    }
        }

        // Toast overlay stays host-owned pseudo-text (not part of the guest display
        // list; low priority — routed through ViewRuntime in a future unit).
        if (!string.IsNullOrEmpty(toast))
        {
            float toastWidth = Math.Min(width - 32, Math.Max(120, toast.Length * 9 + 32));
            var rect = new AndroidRect((width - toastWidth) / 2, Math.Max(8, height - 72), toastWidth, 48);
            FillPixels(pixels, width, height, rect, new AndroidColor(238, 38, 38, 38));
            DrawPseudoText(pixels, width, height, new(rect.X + 16, rect.Y + 8, rect.Width - 32, rect.Height - 16), toast, 16, new AndroidColor(255, 255, 255, 255));
        }
        return pixels;
    }

    /// <summary>Creates (once) and resizes the native ViewRuntime surface to the
    /// current frame dimensions. Falls back to the fake rasterizer when the DLL
    /// cannot load or the surface cannot be created.</summary>
    private void EnsureNativeSurface(int width, int height, float density)
    {
        if (_nativeAvailable) { _native!.Resize(width, height, density); return; }
        if (_native is not null) return; // already failed
        try
        {
            _native = ViewRuntimeSurface.Create(ViewRuntimeNative.PickSystemFont());
            if (_native is not null) { _native.Resize(width, height, density); _nativeAvailable = true; }
        }
        catch (DllNotFoundException) { _native = null; }
        catch (BadImageFormatException) { _native = null; }
    }

    private static AndroidRect Scale(AndroidRect rect, float density) => new(rect.X * density, rect.Y * density, rect.Width * density, rect.Height * density);
    private static void FillPixels(byte[] pixels, int width, int height, AndroidRect rect, AndroidColor color)
    {
        int left = Math.Clamp((int)MathF.Floor(rect.X), 0, width), top = Math.Clamp((int)MathF.Floor(rect.Y), 0, height);
        int right = Math.Clamp((int)MathF.Ceiling(rect.X + rect.Width), 0, width), bottom = Math.Clamp((int)MathF.Ceiling(rect.Y + rect.Height), 0, height);
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++) { int p = (y * width + x) * 4; pixels[p] = color.B; pixels[p + 1] = color.G; pixels[p + 2] = color.R; pixels[p + 3] = color.A; }
    }

    // Deterministic 5x7 cell painter. It deliberately shares its cell metrics with measurement.
    private static void DrawPseudoText(byte[] pixels, int width, int height, AndroidRect rect, string text, float size, AndroidColor color)
    {
        int cellH = Math.Max(7, (int)MathF.Round(size)), cellW = Math.Max(4, (int)MathF.Round(size * .6f));
        int cursorX = (int)rect.X, cursorY = (int)(rect.Y + Math.Max(0, (rect.Height - cellH) / 2));
        foreach (char ch in text)
        {
            if (cursorX + cellW > rect.X + rect.Width) break;
            if (!char.IsWhiteSpace(ch))
            {
                uint bits = unchecked((uint)(ch * 2654435761u));
                for (int row = 0; row < 7; row++) for (int col = 0; col < 5; col++)
                    if (row is 0 or 6 || col is 0 or 4 || ((bits >> ((row * 5 + col) & 31)) & 1) != 0)
                        FillPixels(pixels, width, height, new(cursorX + col * cellW / 6f, cursorY + row * cellH / 8f, Math.Max(1, cellW / 7f), Math.Max(1, cellH / 9f)), color);
            }
            cursorX += cellW;
        }
    }

    public void Dispose() { lock (_gate) { _disposed = true; _frame = null; _toast = null; _native?.Dispose(); _native = null; } }

    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader
    {
        internal uint Size; internal int Width; internal int Height; internal ushort Planes; internal ushort BitCount; internal uint Compression; internal uint SizeImage; internal int XPelsPerMeter; internal int YPelsPerMeter; internal uint ClrUsed; internal uint ClrImportant;
        internal static BitmapInfoHeader Create(int width, int height) => new() { Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = height, Planes = 1, BitCount = 32, Compression = 0, SizeImage = checked((uint)(width * Math.Abs(height) * 4)) };
    }
    private static partial class Gdi32
    {
        [LibraryImport("gdi32.dll")] internal static partial int StretchDIBits(nint hdc, int xDest, int yDest, int destWidth, int destHeight, int xSrc, int ySrc, int srcWidth, int srcHeight, nint bits, ref BitmapInfoHeader info, uint usage, uint rop);
    }
}
