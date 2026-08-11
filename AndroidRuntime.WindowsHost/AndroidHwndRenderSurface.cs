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
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.WindowsHost;

internal sealed partial class AndroidHwndRenderSurface : HwndHost
{
    private const int WsChild = 0x40000000, WsVisible = 0x10000000, WsTabStop = 0x00010000, SsNotify = 0x00000100;
    private const int WmPaint = 0x000F, WmEraseBackground = 0x0014, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202, WmKeyDown = 0x0100, WmMouseMove = 0x0200, WmMouseLeave = 0x02A3, WmMouseWheel = 0x020A;
    private const int VkReturn = 0x0D, VkSpace = 0x20;
    private const uint TmeLeave = 0x00000002;
    private readonly WindowsRetainedRenderer _renderer = new();
    private readonly RetainedFrameScheduler _scheduler;
    private AndroidHostedActivitySession? _session;
    private AndroidInputManager? _input;
    private nint _hwnd;
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
        if (_input is null || !_input.InputEnabled) return;
        _input.HandlePointerDown(pixelX, pixelY);
        _input.HandlePointerUp(pixelX, pixelY);
    }
    internal void Attach(AndroidHostedActivitySession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _input = new AndroidInputManager(session, RequestFrame);
        // Generic invalidate channel: ANY bridge mutation that changes visual
        // view state (setText, visibility, pressed, scroll, click handlers that
        // ran DEX…) announces through IAndroidViewBridge.FrameRequested. The
        // host just renders on every request — RequestFrame coalesces, so N
        // mutations in one guest turn cost exactly one frame. This closes the
        // gap where setStatus-style guest mutations updated ViewRuntime state
        // but nothing asked for a render (only real input did).
        if (session.ViewBridge is { } bridge)
            bridge.FrameRequested += RequestFrame;
        RequestFrame();
    }
    internal void Detach(AndroidHostedActivitySession session)
    {
        if (ReferenceEquals(_session, session))
        {
            if (_session is { ViewBridge: { } bridge })
                bridge.FrameRequested -= RequestFrame;
            _session = null; _input?.Dispose(); _input = null;
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = Native.CreateWindowEx(0, "STATIC", string.Empty, WsChild | WsVisible | WsTabStop | SsNotify, 0, 0, 1, 1, hwndParent.Handle, 0, 0, 0);
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
                if (_input is not { } input || !input.InputEnabled) break;
                Native.SetFocus(hwnd); Native.SetCapture(hwnd);
                input.HandlePointerDown(ToRenderX(lParam), ToRenderY(lParam));
                handled = true; return 0;
            case WmLButtonUp:
                if (_input is not { } inputUp || !inputUp.InputEnabled) break;
                Native.ReleaseCapture();
                inputUp.HandlePointerUp(ToRenderX(lParam), ToRenderY(lParam));
                handled = true; return 0;
            case WmMouseMove:
                if (_input is not { } inputMove || !inputMove.InputEnabled) break;
                // Arm leave-tracking so Windows posts WM_MOUSELEAVE when the
                // pointer leaves the surface (standard TrackMouseEvent pattern;
                // re-arming on every move is safe and resets the leave timer).
                var trackMouse = new TrackMouseEventStruct { Size = (uint)Marshal.SizeOf<TrackMouseEventStruct>(), Flags = TmeLeave, Hwnd = hwnd };
                Native.TrackMouseEvent(ref trackMouse);
                inputMove.HandlePointerMove(ToRenderX(lParam), ToRenderY(lParam));
                handled = true; return 0;
            case WmMouseLeave:
                _input?.HandlePointerLeave();
                handled = true; return 0;
            case WmMouseWheel:
                if (_input is not { } inputWheel || !inputWheel.InputEnabled) break;
                // lParam is in SCREEN coordinates for WM_MOUSEWHEEL (unlike the
                // button messages) — convert to surface-client, then render px.
                int wheelDelta = (short)((long)wParam >> 16); // GET_WHEEL_DELTA, ±120/notch
                if (wheelDelta != 0)
                {
                    var wheelPoint = new PointStruct { X = SignedLow(lParam), Y = SignedHigh(lParam) };
                    Native.ScreenToClient(hwnd, ref wheelPoint);
                    inputWheel.HandleScroll(ToRenderX(wheelPoint.X), ToRenderY(wheelPoint.Y), wheelDelta);
                }
                handled = true; return 0;
            case WmKeyDown when _input is { InputEnabled: true } inputKey && (wParam == VkReturn || wParam == VkSpace):
                // Win32 VK → Android KeyEvent key code (KeyEvent.java). Only the
                // DOWN matters; the native key dispatch ignores KEY_UP.
                inputKey.HandleKeyPress(wParam == VkReturn ? AndroidKeyCode.Enter : AndroidKeyCode.Space);
                handled = true; return 0;
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    /// <summary>Raw child-client coordinate straight through to the render
    /// space. The process is PerMonitorV2-aware (app.manifest), so Win32
    /// lParam/screen coordinates are already PHYSICAL pixels, and the render
    /// frame is sized in those same physical pixels (DIP * DpiScale, see
    /// RequestFrame). No DPI conversion may be applied here — scaling again
    /// would overshoot the frame and hit-test null. (Regression fixed: the
    /// previous ToRender multiplied by DpiScale, breaking every click at
    /// DPI &gt; 1.)</summary>
    private float ToRenderX(nint lParam) => SignedLow(lParam);
    private float ToRenderY(nint lParam) => SignedHigh(lParam);
    private float ToRenderX(int clientX) => clientX;
    private float ToRenderY(int clientY) => clientY;

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
                // Gesture timers (long-press 400ms / tap 100ms / pressed-state
                // 64ms) tick from the frame loop; the input path also polls
                // while a gesture is active (AndroidInputManager), so a held
                // press advances even with no frames arriving.
                try { session.ViewBridge.GesturePoll(); } catch { }
                // Phase 2: ViewRuntime renders the whole frame; this side just
                // presents the finished buffer. No bridge attached -> no frame.
                byte[]? pixels = session.ViewBridge.RenderFrame(pixelWidth, pixelHeight, density);
                if (pixels is not null)
                    _scheduler.Publish(new WindowsRetainedFrame(Interlocked.Increment(ref _revision), pixelWidth, pixelHeight, density, pixels, string.Empty));
                // Toast expiry polling: android.widget.Toast lives entirely in
                // ViewRuntime, which deactivates it itself after SHORT/LONG
                // (4000/7000ms). Ask for one more frame shortly after the
                // deadline so the overlay disappears without waiting for input.
                if (session.ViewBridge.ToastIsActive())
                {
                    int delayMs = 4500; /* > SHORT (4000); LONG re-polls via next frame */
                    if (Volatile.Read(ref _disposed) == 0)
                        _ = Dispatcher.BeginInvoke(async () =>
                        {
                            await Task.Delay(delayMs);
                            if (Volatile.Read(ref _disposed) == 0) RequestFrame();
                        }, DispatcherPriority.Background);
                }
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
        if (Interlocked.Exchange(ref _disposed, 1) == 0) { _session = null; _input?.Dispose(); _input = null; _scheduler.Dispose(); _renderer.Dispose(); }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)] private unsafe struct PaintStruct { internal nint Hdc; internal int Erase; internal Rect Rect; internal int Restore; internal int IncUpdate; internal fixed byte Reserved[32]; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PointStruct { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct TrackMouseEventStruct
    {
        internal uint Size;
        internal uint Flags;
        internal nint Hwnd;
        internal uint HoverTime;
    }
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
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool TrackMouseEvent(ref TrackMouseEventStruct tme);
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool ScreenToClient(nint hwnd, ref PointStruct point);
    }
}
