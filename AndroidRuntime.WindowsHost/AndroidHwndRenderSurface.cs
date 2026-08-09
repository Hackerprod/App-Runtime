#nullable enable
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AndroidRuntime.Core;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.WindowsHost;

internal sealed partial class AndroidHwndRenderSurface : HwndHost
{
    private const int WsChild = 0x40000000, WsVisible = 0x10000000, WsTabStop = 0x00010000;
    private const int WmPaint = 0x000F, WmEraseBackground = 0x0014, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202, WmKeyDown = 0x0100;
    private const int VkReturn = 0x0D, VkSpace = 0x20;
    private readonly WindowsRetainedRenderer _renderer = new();
    private readonly RetainedFrameScheduler _scheduler;
    private AndroidHostedActivitySession? _session;
    private nint _hwnd;
    private int? _pressedId;
    private int? _focusedId;
    private long _revision;
    private int _frameInFlight, _frameAgain, _disposed;

    internal AndroidHwndRenderSurface()
    {
        Focusable = true;
        AutomationProperties.SetName(this, "Android application content");
        _scheduler = new RetainedFrameScheduler(
            action => Dispatcher.BeginInvoke(action, DispatcherPriority.Render),
            frame => { _renderer.Resize(frame.PixelWidth, frame.PixelHeight, frame.Density); _renderer.Render(frame); Invalidate(); });
        Loaded += (_, _) => RequestFrame();
        SizeChanged += (_, _) => RequestFrame();
    }

    internal nint SurfaceHandle => _hwnd;
    internal RetainedFrameSchedulerMetrics SchedulerMetrics => _scheduler.Metrics;
    internal WindowsFrameCapture Capture() => _renderer.Capture();
    internal int? HitTest(float pixelX, float pixelY) => _session?.ViewBridge.HitTest(pixelX, pixelY);
    internal void InjectPointerClick(float pixelX, float pixelY)
    {
        if (!InputEnabled) return;
        int? pressed = _session?.ViewBridge.HitTest(pixelX, pixelY);
        int? released = _session?.ViewBridge.HitTest(pixelX, pixelY);
        if (pressed is int id && released == pressed) { _focusedId = id; EnqueueClick(id); }
    }
    internal void Attach(AndroidHostedActivitySession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); RequestFrame(); }
    internal void Detach(AndroidHostedActivitySession session) { if (ReferenceEquals(_session, session)) _session = null; }
    internal void SetToast(string? text) { _renderer.SetToast(text); Invalidate(); }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = Native.CreateWindowEx(0, "STATIC", string.Empty, WsChild | WsVisible | WsTabStop, 0, 0, 1, 1, hwndParent.Handle, 0, 0, 0);
        if (_hwnd == 0) throw new InvalidOperationException($"Unable to create Android render child HWND ({Marshal.GetLastWin32Error()}).");
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd) { if (hwnd.Handle != 0) Native.DestroyWindow(hwnd.Handle); _hwnd = 0; }

    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmEraseBackground: handled = true; return 1;
            case WmPaint:
                handled = true;
                Native.BeginPaint(hwnd, out PaintStruct paint);
                try { _renderer.Present(paint.Hdc); } finally { Native.EndPaint(hwnd, ref paint); }
                return 0;
            case WmLButtonDown:
                if (!InputEnabled) break;
                Native.SetFocus(hwnd); Native.SetCapture(hwnd);
                _pressedId = _session?.ViewBridge.HitTest(SignedLow(lParam), SignedHigh(lParam));
                _focusedId = _pressedId;
                handled = true; return 0;
            case WmLButtonUp:
                if (!InputEnabled) break;
                Native.ReleaseCapture();
                int? released = _session?.ViewBridge.HitTest(SignedLow(lParam), SignedHigh(lParam));
                int? invoke = released == _pressedId ? released : null; _pressedId = null;
                if (invoke is int id) EnqueueClick(id);
                handled = true; return 0;
            case WmKeyDown when InputEnabled && (wParam == VkReturn || wParam == VkSpace):
                if (_focusedId is int focused) EnqueueClick(focused);
                handled = true; return 0;
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private bool InputEnabled => Volatile.Read(ref _disposed) == 0 && _session?.Session.State == AndroidActivityState.Resumed;

    private void EnqueueClick(int id)
    {
        AndroidHostedActivitySession? session = _session;
        if (session is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                DexObject? view = session.ViewBridge.FindViewById(id);
                if (view is not null && session.ViewBridge.PerformClick(view)) RequestFrame();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) when (!InputEnabled) { }
        });
    }

    internal void RequestFrame()
    {
        if (!Dispatcher.CheckAccess()) { _ = Dispatcher.BeginInvoke(RequestFrame, DispatcherPriority.Background); return; }
        if (_session is null || Volatile.Read(ref _disposed) != 0 || !IsLoaded) return;
        if (Interlocked.Exchange(ref _frameInFlight, 1) != 0) { Interlocked.Exchange(ref _frameAgain, 1); return; }
        double width = ActualWidth, height = ActualHeight; DpiScale dpi = VisualTreeHelper.GetDpi(this);
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(width * dpi.DpiScaleX));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(height * dpi.DpiScaleY));
        float density = (float)dpi.DpiScaleX;
        AndroidHostedActivitySession session = _session;
        _ = Task.Run(async () =>
        {
            try
            {
                // Phase 2: ViewRuntime renders the whole frame; this side just
                // presents the finished buffer. No bridge attached -> no frame.
                byte[]? pixels = session.ViewBridge.RenderFrame(pixelWidth, pixelHeight, density);
                if (pixels is not null)
                    _scheduler.Publish(new WindowsRetainedFrame(Interlocked.Increment(ref _revision), pixelWidth, pixelHeight, density, pixels, string.Empty));
            }
            catch (InvalidOperationException) when (session.Session.State != AndroidActivityState.Resumed) { }
            catch (ObjectDisposedException) { }
            finally
            {
                Interlocked.Exchange(ref _frameInFlight, 0);
                if (Interlocked.Exchange(ref _frameAgain, 0) != 0 && Volatile.Read(ref _disposed) == 0)
                    _ = Dispatcher.BeginInvoke(RequestFrame, DispatcherPriority.Background);
            }
        });
    }

    private void Invalidate() { if (_hwnd != 0) Native.InvalidateRect(_hwnd, 0, false); }
    private static int SignedLow(nint value) => unchecked((short)((long)value & 0xffff));
    private static int SignedHigh(nint value) => unchecked((short)(((long)value >> 16) & 0xffff));

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) { _session = null; _scheduler.Dispose(); _renderer.Dispose(); }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)] private unsafe struct PaintStruct { internal nint Hdc; internal int Erase; internal Rect Rect; internal int Restore; internal int IncUpdate; internal fixed byte Reserved[32]; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    private static partial class Native
    {
        [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)] internal static partial nint CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool DestroyWindow(nint hwnd);
        [LibraryImport("user32.dll")] internal static partial nint BeginPaint(nint hwnd, out PaintStruct paint);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool EndPaint(nint hwnd, ref PaintStruct paint);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool InvalidateRect(nint hwnd, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);
        [LibraryImport("user32.dll")] internal static partial nint SetFocus(nint hwnd);
        [LibraryImport("user32.dll")] internal static partial nint SetCapture(nint hwnd);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool ReleaseCapture();
    }
}
