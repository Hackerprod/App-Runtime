#nullable enable
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AndroidRuntime.WindowsHost;

/// <summary>Immutable transfer object for a finished rendered frame. Phase 2:
/// ViewRuntime owns the entire frame; this side only carries the completed
/// pixel buffer and presents it. There is no display-list interpretation here.</summary>
internal sealed record WindowsRetainedFrame(
    long Revision,
    int PixelWidth,
    int PixelHeight,
    float Density,
    byte[] Bgra,
    string SemanticSnapshot)
{
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
/// Phase-2 presentation shim: holds the latest finished frame produced by
/// ViewRuntime and blits it to the Win32 child HWND via GDI StretchDIBits.
/// This side no longer interprets display lists or rasterizes anything — the
/// pixel buffer arrives complete from the view bridge. If no bridge is
/// attached there is no frame, and Capture/Present report an empty surface.
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
            _frame = frame;
            _renderedRevision = frame.Revision;
        }
    }

    internal void SetToast(string? text) { lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); _toast = text; } }

    internal WindowsFrameCapture Capture()
    {
        WindowsRetainedFrame? frame; int width, height; float density;
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); frame = _frame; width = _width; height = _height; density = _density; }
        byte[] pixels = frame is null ? new byte[checked(width * height * 4)] : EnsureSized(frame.Bgra, width, height);
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

    private static byte[] EnsureSized(byte[] bgra, int width, int height)
    {
        int expected = checked(width * height * 4);
        return bgra.Length == expected ? bgra : new byte[expected];
    }

    public void Dispose() { lock (_gate) { _disposed = true; _frame = null; _toast = null; } }

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
