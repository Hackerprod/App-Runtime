#nullable enable
using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.Ui;

/// <summary>
/// Phase-2 view bridge contract: the ONLY path from this side to view
/// hierarchy/measure/layout/style-resolution/paint behavior, which ViewRuntime
/// owns 100%. This side is a resource-and-behavior PROVIDER; it no longer has a
/// view-node implementation. Every View/ViewGroup/TypedArray API binding that
/// previously manipulated a local AndroidViewNode now forwards here.
///
/// The concrete C ABI (functions/structs across the viewruntime_core.dll
/// boundary) is a two-party contract being agreed between this side and the
/// ViewRuntime session; the C# interface below is the operation surface the
/// bindings need. An implementation that fails closed (UnavailableAndroidViewBridge)
/// is used until the real bridge is attached.
/// </summary>
public interface IAndroidViewBridge
{
    /// <summary>True when a real ViewRuntime-backed bridge is attached and can
    /// answer view operations.</summary>
    bool IsAvailable { get; }

    /// <summary>Releases the underlying bridge (native surface/hierarchy) when
    /// the session shuts down. No-op for the unavailable implementation.</summary>
    void DisposeBridge();

    /// <summary>Raised after a bridge operation mutated visual view state
    /// (text, enabled, visibility, pressed/hovered, scroll, inflate, a click
    /// that ran guest DEX). The host subscribes and renders a fresh frame;
    /// coalescing is the host's responsibility (a frame already in flight
    /// covers pending requests). Generic: every visual mutation announces
    /// itself through this ONE channel instead of each binding knowing how to
    /// reach the render loop.</summary>
    event Action? FrameRequested;

    /// <summary>Binds the session interpreter + Activity + a lane-dispatch
    /// function so the bridge can invoke guest click handlers (programmatic
    /// listeners and declarative android:onClick methods) through real DEX
    /// execution ON the session execution lane — real Android fires onClick on
    /// the main/UI thread, and lane-sensitive bindings (Toast, runOnUiThread)
    /// enforce it. dispatchToLane runs its function on the lane and returns the
    /// result; implementations use the on-lane fast path when already there.
    /// No-op for the unavailable implementation.</summary>
    void AttachSession(DexInterpreter interpreter, DexObject activity, Func<Func<object?>, object?> dispatchToLane);

    // ---- inflate / content (ViewRuntime builds the real view hierarchy) ----

    /// <summary>Installs the given layout resource as the window content view.
    /// ViewRuntime parses the provided element tree and builds its own views.</summary>
    void SetContentView(int layoutResourceId);

    /// <summary>Inflates a layout resource into a detached view tree; returns the
    /// guest View object for the root (and registers peers for every node so
    /// findViewById/setText etc. work).</summary>
    DexObject Inflate(int layoutResourceId);

    // ---- view lookups / state (forwarded to ViewRuntime, relayed back) ----

    DexObject? FindViewById(int id, DexObject? receiver = null);
    int GetId(DexObject view);
    void SetEnabled(DexObject view, bool enabled);
    bool IsEnabled(DexObject view);
    void SetVisibility(DexObject view, int visibility);
    int GetVisibility(DexObject view);

    /// <summary>Interaction visual state: pressed (mouse down) / hovered (mouse
    /// enter) per hit-test result. ViewRuntime re-resolves the background from
    /// the drawable's selector for the reported state instead of the stateless
    /// default; no click dispatch involvement.</summary>
    void SetPressed(DexObject view, bool pressed);
    void SetHovered(DexObject view, bool hovered);

    /// <summary>Real mouse-wheel scroll: given the hit-tested (deepest) view,
    /// resolves the nearest SCROLL container ancestor and applies the
    /// accumulated pixel offset. ViewRuntime clamps the range on its side; this
    /// side never duplicates that logic.</summary>
    void SetScrollOffset(DexObject view, float x, float y);
    void SetOnClickListener(DexObject view, DexObject? listener);
    bool PerformClick(DexObject view);
    void SetText(DexObject view, string? text);
    string GetText(DexObject view);

    // ---- view state queries (forwarded to ViewRuntime; no local defaults) ----
    bool IsLaidOut(DexObject view);
    int GetPaddingLeft(DexObject view);
    int GetPaddingTop(DexObject view);
    int GetPaddingRight(DexObject view);
    int GetPaddingBottom(DexObject view);

    // ---- TypedArray / styled attributes (real resolution owned by ViewRuntime) ----

    DexObject ObtainStyledAttributes();
    int TypedArrayGetIndexCount();
    bool TypedArrayHasValue(int index);
    string? TypedArrayGetString(int index);
    int TypedArrayGetColor(int index, int defaultValue);
    DexObject? TypedArrayGetColorStateList(int index);
    float TypedArrayGetDimension(int index, float defaultValue);
    int TypedArrayGetInt(int index, int defaultValue);
    int TypedArrayGetResourceId(int index, int defaultValue);
    bool TypedArrayGetBoolean(int index, bool defaultValue);
    float TypedArrayGetFloat(int index, float defaultValue);
    int TypedArrayGetDimensionPixelSize(int index, int defaultValue);
    int TypedArrayGetDimensionPixelOffset(int index, int defaultValue);
    int TypedArrayGetIndex(int index);
    bool TypedArrayGetValue(int index);

    // ---- frame lifecycle (ViewRuntime renders the whole frame) ----

    /// <summary>Asks ViewRuntime to produce a finished frame at the given pixel
    /// size/density. Returns the completed BGRA buffer (or null when no bridge
    /// is attached and nothing can be rendered). The caller presents the buffer;
    /// this side never interprets it.</summary>
    byte[]? RenderFrame(int pixelWidth, int pixelHeight, float density);

    /// <summary>Hit-tests a finished frame at pixel coordinates; returns the
    /// view resource id that was hit, or null. ViewRuntime owns the real view
    /// bounds; this side only relays the result back to guest click dispatch.</summary>
    int? HitTest(float pixelX, float pixelY);

    // ---- Toast (android.widget.Toast — exact AOSP port, state owned by
    // ViewRuntime; this side only relays the guest API calls) ----

    /// <summary>Toast.makeText: creates the toast state (text + duration).</summary>
    void ToastMakeText(string? text, int duration);

    /// <summary>Toast.setText.</summary>
    void ToastSetText(string? text);

    /// <summary>Toast.setDuration.</summary>
    void ToastSetDuration(int duration);

    /// <summary>Toast.getDuration.</summary>
    int ToastGetDuration();

    /// <summary>Toast.show — ViewRuntime starts its SHORT/LONG timeout.</summary>
    void ToastShow();

    /// <summary>Toast.cancel.</summary>
    void ToastCancel();

    /// <summary>True while a toast is showing (ViewRuntime hides it itself after
    /// the 4000/7000ms timeout). The host polls this each frame.</summary>
    bool ToastIsActive();

    /// <summary>Render the active toast overlay over the current frame. No-op
    /// when inactive. Call after the app frame.</summary>
    void ToastRender();

    // ---- input dispatch (ViewRuntime owns the ENTIRE gesture machine) ----
    // The host forwards raw pointer/key events; ViewRuntime decides
    // hit-testing, mFirstTouchTarget, touch slop, long-press, pressed visuals
    // and the performClick/performLongClick decision. The click decision comes
    // back through the registered click callback (DispatchClickByResourceId on
    // this side) — never through a C# tap heuristic.

    /// <summary>Forwards a MotionEvent action (AndroidInputAction.Down/Up/
    /// Move/Cancel) with render-surface pixel coordinates.</summary>
    void DispatchTouch(int action, float x, float y);

    /// <summary>Forwards a KeyEvent (AndroidKeyCode.Enter/Space/DpadCenter).
    /// Only the Down action triggers the native click on the focused view.</summary>
    void DispatchKey(int action, int keyCode);

    /// <summary>Ticks the native gesture timers (long-press 400ms / tap 100ms /
    /// pressed-state 64ms). Call from the frame loop. Returns nonzero when a
    /// timer fired (a frame refresh is worth it).</summary>
    int GesturePoll();

    /// <summary>True while a touch gesture is active (a view is pressed/
    /// targeted) — the host keeps polling while this is true.</summary>
    bool GestureActive { get; }
}

/// <summary>MotionEvent / KeyEvent action values (MotionEvent.java ACTION_*,
/// KeyEvent.java ACTION_*). Mirror of the native ANDROID_ACTION_* /
/// ANDROID_KEY_ACTION_* constants (android.h:692-698).</summary>
public static class AndroidInputAction
{
    public const int Down = 0;
    public const int Up = 1;
    public const int Move = 2;
    public const int Cancel = 3;
    public const int KeyDown = 0;
    public const int KeyUp = 1;
}

/// <summary>KeyEvent key codes the runtime forwards to the native key dispatch
/// (KeyEvent.java: KEYCODE_ENTER=66 / DPAD_CENTER=23 / SPACE=62).</summary>
public static class AndroidKeyCode
{
    public const int Enter = 66;
    public const int DpadCenter = 23;
    public const int Space = 62;
}

/// <summary>
/// Fail-closed bridge used when no ViewRuntime-backed bridge is
/// attached. There is deliberately NO local view behavior here: view operations
/// throw rather than fake answers, matching the Phase-2 directive that this
/// side has zero visual logic.</summary>
public sealed class UnavailableAndroidViewBridge : IAndroidViewBridge
{
    public static UnavailableAndroidViewBridge Instance { get; } = new();

    private UnavailableAndroidViewBridge() { }

    public bool IsAvailable => false;
    public void DisposeBridge() { }
    public event Action? FrameRequested { add { } remove { } }
    public void AttachSession(DexInterpreter interpreter, DexObject activity, Func<Func<object?>, object?> dispatchToLane) { }
    public void SetContentView(int layoutResourceId) { Throw(); }
    public DexObject Inflate(int layoutResourceId) { Throw(); return null!; }
    public DexObject? FindViewById(int id, DexObject? receiver = null) { Throw(); return null; }
    public int GetId(DexObject view) { Throw(); return 0; }
    public void SetEnabled(DexObject view, bool enabled) { Throw(); }
    public bool IsEnabled(DexObject view) { Throw(); return false; }
    public void SetVisibility(DexObject view, int visibility) { Throw(); }
    public int GetVisibility(DexObject view) { Throw(); return 0; }
    public void SetPressed(DexObject view, bool pressed) { Throw(); }
    public void SetHovered(DexObject view, bool hovered) { Throw(); }
    public void SetScrollOffset(DexObject view, float x, float y) { Throw(); }
    public void SetOnClickListener(DexObject view, DexObject? listener) { Throw(); }
    public bool PerformClick(DexObject view) { Throw(); return false; }
    public void SetText(DexObject view, string? text) { Throw(); }
    public string GetText(DexObject view) { Throw(); return null!; }
    public bool IsLaidOut(DexObject view) { Throw(); return false; }
    public int GetPaddingLeft(DexObject view) { Throw(); return 0; }
    public int GetPaddingTop(DexObject view) { Throw(); return 0; }
    public int GetPaddingRight(DexObject view) { Throw(); return 0; }
    public int GetPaddingBottom(DexObject view) { Throw(); return 0; }
    public DexObject ObtainStyledAttributes() { Throw(); return null!; }
    public int TypedArrayGetIndexCount() { Throw(); return 0; }
    public bool TypedArrayHasValue(int index) { Throw(); return false; }
    public string? TypedArrayGetString(int index) { Throw(); return null; }
    public int TypedArrayGetColor(int index, int defaultValue) { Throw(); return 0; }
    public DexObject? TypedArrayGetColorStateList(int index) { Throw(); return null; }
    public float TypedArrayGetDimension(int index, float defaultValue) { Throw(); return 0; }
    public int TypedArrayGetInt(int index, int defaultValue) { Throw(); return 0; }
    public int TypedArrayGetResourceId(int index, int defaultValue) { Throw(); return 0; }
    public bool TypedArrayGetBoolean(int index, bool defaultValue) { Throw(); return false; }
    public float TypedArrayGetFloat(int index, float defaultValue) { Throw(); return 0; }
    public int TypedArrayGetDimensionPixelSize(int index, int defaultValue) { Throw(); return 0; }
    public int TypedArrayGetDimensionPixelOffset(int index, int defaultValue) { Throw(); return 0; }
    public int TypedArrayGetIndex(int index) { Throw(); return 0; }
    public bool TypedArrayGetValue(int index) { Throw(); return false; }
    public byte[]? RenderFrame(int pixelWidth, int pixelHeight, float density) => null;
    public int? HitTest(float pixelX, float pixelY) => null;

    // Toast without a real ViewRuntime bridge: no-op. android.widget.Toast
    // degrades to "nothing shown" when no UI surface exists (the AOSP app
    // process still runs; only the transient window is absent). Throwing here
    // would break every headless session whose APK happens to make a toast.
    public void ToastMakeText(string? text, int duration) { }
    public void ToastSetText(string? text) { }
    public void ToastSetDuration(int duration) { }
    public int ToastGetDuration() => 0;
    public void ToastShow() { }
    public void ToastCancel() { }
    public bool ToastIsActive() => false;
    public void ToastRender() { }

    // Input without a real ViewRuntime bridge: no-op. No guest input can be
    // delivered headless; the unavailable surface never forwards events.
    public void DispatchTouch(int action, float x, float y) { }
    public void DispatchKey(int action, int keyCode) { }
    public int GesturePoll() => 0;
    public bool GestureActive => false;

    private static void Throw()
    {
        throw new InvalidOperationException("Android view bridge is not attached; ViewRuntime owns all view behavior in Phase 2.");
    }
}

/// <summary>
/// Single source of truth for display metrics queried by guest code
/// (Configuration/DisplayMetrics/WindowManager). The host updates this from the
/// real window size/density, and ViewRuntime renders at the same state — no
/// independently-hardcoded resolution constants anywhere.
/// </summary>
public readonly record struct AndroidDisplayState(
    int ScreenWidthPx,
    int ScreenHeightPx,
    int DensityDpi,
    int ScreenWidthDp,
    int ScreenHeightDp,
    int Orientation,
    int ScreenLayout,
    int UiMode,
    float FontScale)
{
    public static AndroidDisplayState Default { get; } = new(
        ScreenWidthPx: 0,
        ScreenHeightPx: 0,
        DensityDpi: 160,
        ScreenWidthDp: 0,
        ScreenHeightDp: 0,
        Orientation: 1,
        ScreenLayout: 0x20,
        UiMode: 0x11,
        FontScale: 1.0f);

    /// <summary>The reference device shape (docs\installer-launcher-design.md):
    /// 1080x2196px at 3x density = 360x732dp portrait — exactly the dp size the
    /// WpfActivityWindowFactory opens. ONE source of truth for every guest
    /// display query (Configuration / DisplayMetrics / WindowManager), replacing
    /// the previous independently-hardcoded 720x1280 baseline.</summary>
    public static AndroidDisplayState Reference { get; } = new(
        ScreenWidthPx: 1080,
        ScreenHeightPx: 2196,
        DensityDpi: 480,
        ScreenWidthDp: 360,
        ScreenHeightDp: 732,
        Orientation: 1,
        ScreenLayout: 0x20,
        UiMode: 0x11,
        FontScale: 1.0f);
}
