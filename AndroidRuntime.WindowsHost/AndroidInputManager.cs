#nullable enable
using AndroidRuntime.Core;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.WindowsHost;

/// <summary>
/// THE single input entry point for the guest app. Receives physical pointer /
/// keyboard events already converted to render-surface pixel coordinates and
/// FORWARDS them to ViewRuntime's native gesture machine — the exact AOSP port
/// (android_input.cpp) that owns hit-testing, mFirstTouchTarget, touch slop,
/// long-press, pressed visuals and the performClick decision. The click comes
/// back through the bridge's click callback (resource id → guest DEX onClick);
/// this type holds NO tap heuristic of its own.
///
///   pointer down  -> DispatchTouch(DOWN)
///   pointer up    -> DispatchTouch(UP)   (native decides tap vs cancel)
///   pointer move  -> DispatchTouch(MOVE) (native applies touch slop)
///   pointer leave -> DispatchTouch(CANCEL)
///   scroll        -> hit-test + accumulate wheel delta on the scrollable view
///   key (Enter/Space) -> DispatchKey(DOWN) on the native focused view
///
/// The surface host owns coordinate conversion (DPI/physical vs render pixels)
/// and the Win32 message plumbing; this type owns ALL guest input forwarding.
/// It is deliberately decoupled from WPF so the same manager can later serve
/// any host (WPF, AetherUI window, headless tests, synthesized touch).
/// </summary>
public sealed class AndroidInputManager
{
    private readonly AndroidHostedActivitySession _session;
    private readonly Action _requestFrame;
    private readonly Action<string>? _trace;

    private float _scrollY;
    private int _gesturePolling;
    private bool _disposed;

    public AndroidInputManager(AndroidHostedActivitySession session, Action requestFrame, Action<string>? trace = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _requestFrame = requestFrame ?? throw new ArgumentNullException(nameof(requestFrame));
        _trace = trace;
    }

    /// <summary>True while the guest session is resumed and input can be
    /// delivered (a paused/stopped app must not receive taps).</summary>
    public bool InputEnabled => !_disposed && _session.Session.State == AndroidActivityState.Resumed;

    /// <summary>Pointer press (mouse/touch down). The native gesture machine
    /// hit-tests, fixes the touch target and sets the pressed visual.</summary>
    public void HandlePointerDown(float pixelX, float pixelY)
    {
        if (!InputEnabled) return;
        _session.ViewBridge.DispatchTouch(AndroidInputAction.Down, pixelX, pixelY);
        Trace($"down({pixelX:0},{pixelY:0})");
        _requestFrame();
        EnsureGesturePolling();
    }

    /// <summary>Pointer release. Native decides: real tap on the DOWN target →
    /// performClick callback (guest DEX); moved out of slop / long-pressed →
    /// no click. The press visual clears natively.</summary>
    public void HandlePointerUp(float pixelX, float pixelY)
    {
        if (!InputEnabled) return;
        _session.ViewBridge.DispatchTouch(AndroidInputAction.Up, pixelX, pixelY);
        Trace($"up({pixelX:0},{pixelY:0})");
        _requestFrame();
        EnsureGesturePolling();
    }

    /// <summary>Pointer move: native applies the touch slop (leaving it cancels
    /// press + long-press so the gesture becomes a scroll).</summary>
    public void HandlePointerMove(float pixelX, float pixelY)
    {
        if (!InputEnabled) return;
        _session.ViewBridge.DispatchTouch(AndroidInputAction.Move, pixelX, pixelY);
        Trace($"move({pixelX:0},{pixelY:0})");
        _requestFrame();
        EnsureGesturePolling();
    }

    /// <summary>Pointer left the surface: cancel the gesture (native unpresses
    /// and drops pending long-press/tap timers).</summary>
    public void HandlePointerLeave()
    {
        if (!InputEnabled) return;
        _session.ViewBridge.DispatchTouch(AndroidInputAction.Cancel, 0f, 0f);
        Trace("leave -> cancel");
        _requestFrame();
        EnsureGesturePolling();
    }

    /// <summary>Mouse wheel: hit-tests the scrollable view under the pointer
    /// and accumulates the raw delta (120/notch) as scroll offset.</summary>
    public void HandleScroll(float pixelX, float pixelY, int wheelDelta)
    {
        if (!InputEnabled || wheelDelta == 0) return;
        int? hit = _session.ViewBridge.HitTest(pixelX, pixelY);
        if (hit is not int id || id == 0) return;
        DexObject? view = _session.ViewBridge.FindViewById(id);
        if (view is null) return;
        _scrollY += wheelDelta;
        _session.ViewBridge.SetScrollOffset(view, 0f, _scrollY);
        Trace($"scroll({pixelX:0},{pixelY:0}) id=0x{id:X8} delta={wheelDelta} total={_scrollY:0}");
        _requestFrame();
    }

    /// <summary>Key event: Enter/Space (mapped to AndroidKeyCode by the surface)
    /// → native clicks the focused view. Only the DOWN triggers the click
    /// (android_input.cpp; the native key dispatch ignores KEY_UP).</summary>
    public void HandleKeyPress(int androidKeyCode)
    {
        if (!InputEnabled) return;
        _session.ViewBridge.DispatchKey(AndroidInputAction.KeyDown, androidKeyCode);
        Trace($"key 0x{androidKeyCode:X} -> native focused click");
        _requestFrame();
        EnsureGesturePolling();
    }

    /// <summary>While a touch gesture is active (a view is pressed/targeted),
    /// poll the native gesture timers so the long-press (400ms) and tap (100ms)
    /// deadlines advance even when no new input or frame arrives. The frame
    /// loop also polls (see RequestFrame), but an idle hold generates no frames
    /// — the input path must keep ticking. Poll cadence is deliberately finer
    /// than the deadlines (~50ms).</summary>
    private void EnsureGesturePolling()
    {
        if (Volatile.Read(ref _gesturePolling) != 0) return;
        if (Interlocked.Exchange(ref _gesturePolling, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_disposed && _session.ViewBridge.GestureActive)
                {
                    if (_session.ViewBridge.GesturePoll() != 0) _requestFrame();
                    await Task.Delay(50).ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            finally
            {
                Volatile.Write(ref _gesturePolling, 0);
            }
        });
    }

    private void Trace(string line)
    {
        try { _trace?.Invoke(line); } catch { }
    }

    public void Dispose() => _disposed = true;
}
