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
}

/// <summary>Fail-closed bridge used when no ViewRuntime-backed bridge is
/// attached. There is deliberately NO local view behavior here: view operations
/// throw rather than fake answers, matching the Phase-2 directive that this
/// side has zero visual logic.</summary>
public sealed class UnavailableAndroidViewBridge : IAndroidViewBridge
{
    public static UnavailableAndroidViewBridge Instance { get; } = new();

    private UnavailableAndroidViewBridge() { }

    public bool IsAvailable => false;
    public void DisposeBridge() { }
    public void AttachSession(DexInterpreter interpreter, DexObject activity, Func<Func<object?>, object?> dispatchToLane) { }
    public void SetContentView(int layoutResourceId) { Throw(); }
    public DexObject Inflate(int layoutResourceId) { Throw(); return null!; }
    public DexObject? FindViewById(int id, DexObject? receiver = null) { Throw(); return null; }
    public int GetId(DexObject view) { Throw(); return 0; }
    public void SetEnabled(DexObject view, bool enabled) { Throw(); }
    public bool IsEnabled(DexObject view) { Throw(); return false; }
    public void SetVisibility(DexObject view, int visibility) { Throw(); }
    public int GetVisibility(DexObject view) { Throw(); return 0; }
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
}
