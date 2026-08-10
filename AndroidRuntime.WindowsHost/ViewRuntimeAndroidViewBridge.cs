#nullable enable
using System.Runtime.InteropServices;
using System.Text;
using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.WindowsHost;

/// <summary>
/// Concrete Phase-2 view bridge backed by ViewRuntime's native ABI
/// (android.h). App Runtime serializes the parsed AXML tree (raw values,
/// dimensions unconverted) and hands it to android_ui_inflate; ViewRuntime
/// owns 100% of view hierarchy/measure/layout/style/paint/hit-test. This side
/// answers ViewRuntime's resource callbacks (resolve_resource / resolve_style /
/// fetch_file) through AndroidResourceQueryService and forwards guest View API
/// calls by native view handle. It owns NO visual logic and NO hardcoded
/// defaults — if ViewRuntime can't resolve something, that's ViewRuntime's
/// problem, not a number picked here.
/// </summary>
public sealed class ViewRuntimeAndroidViewBridge : IAndroidViewBridge
{
    private readonly AndroidResourceResolver _resources;
    private readonly AndroidResourceQueryService _queries;
    private readonly int _applicationThemeStyleId;
    private readonly nint _ui;
    private readonly nint _surface;
    private readonly bool _available;
    private readonly Dictionary<DexObject, nint> _guestToNative = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<nint, DexObject> _nativeToGuest = [];
    private readonly Dictionary<nint, DexObject?> _listeners = [];
    private readonly ResourceCallbacks? _callbacks;
    private nint _root;
    private int _disposed;

    public ViewRuntimeAndroidViewBridge(AndroidResourceResolver resources, AndroidResourceQueryService queries, int applicationThemeStyleId)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _applicationThemeStyleId = applicationThemeStyleId;
        try
        {
            var options = new AndroidUiOptions { density = 1f, scaled_density = 1f };
            int createStatus = ViewRuntimeBridgeNative.android_ui_create(ref options, out nint ui);
            _ui = createStatus == 0 ? ui : 0;
            // A real TrueType font makes draw_text render glyphs; null would make
            // ViewRuntime paint a solid block instead of text (the pink-block bug).
            _surface = ViewRuntimeBridgeNative.viewruntime_surface_create(ViewRuntimeBridgeNative.PickSystemFont());
            _available = _ui != 0 && _surface != 0;
            if (_available)
            {
                // Link the render surface to the UI session: ViewRuntime records
                // commands and paints them itself (Phase-2 ownership).
                ViewRuntimeBridgeNative.android_ui_set_surface(_ui, _surface);
                // Register the resource callbacks (ViewRuntime -> this side).
                // The delegate instances are stored as fields so they stay
                // rooted for the life of the session and the pointers stable.
                _callbacks = new ResourceCallbacks(this);
                ViewRuntimeBridgeNative.android_ui_set_resource_bridge(
                    _ui,
                    Marshal.GetFunctionPointerForDelegate(_callbacks.ResolveResourceDelegate),
                    Marshal.GetFunctionPointerForDelegate(_callbacks.ResolveStyleDelegate),
                    Marshal.GetFunctionPointerForDelegate(_callbacks.FetchFileDelegate),
                    nint.Zero);
            }
        }
        catch (DllNotFoundException) { _ui = 0; _surface = 0; _available = false; }
        catch (BadImageFormatException) { _ui = 0; _surface = 0; _available = false; }
        catch (EntryPointNotFoundException) { _ui = 0; _surface = 0; _available = false; }
    }

    /// <summary>Host-side factory used by AndroidRuntimeServices: attaches a real
    /// ViewRuntime-backed bridge when the native Phase-2 ABI is available,
    /// otherwise returns null so the framework falls back to the fail-closed
    /// unavailable bridge (no fabricated visual answers).</summary>
    public static IAndroidViewBridge? TryCreate(AndroidResourceResolver resources, AndroidResourceQueryService queries, int applicationThemeStyleId)
    {
        try { return new ViewRuntimeAndroidViewBridge(resources, queries, applicationThemeStyleId); }
        catch (Exception) { return null; }
    }

    public bool IsAvailable => _available;

    public void DisposeBridge()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_ui != 0) { try { ViewRuntimeBridgeNative.android_ui_destroy(_ui); } catch { } }
        if (_surface != 0) { try { ViewRuntimeBridgeNative.viewruntime_surface_destroy(_surface); } catch { } }
    }

    // ---- inflate / content ----

    public void SetContentView(int layoutResourceId) => Inflate(layoutResourceId);

    public DexObject Inflate(int layoutResourceId)
    {
        RequireAvailable();
        AndroidXmlDocument document = _resources.LoadLayout(unchecked((uint)layoutResourceId));
        AndroidInflateTree tree = AndroidInflateSerializer.Serialize(document, _applicationThemeStyleId);
        nint nodesPtr = AllocateNativeTree(tree, out int nodeCount);
        try
        {
            int status = ViewRuntimeBridgeNative.android_ui_inflate(_ui, nodesPtr, nodeCount, out nint root);
            if (status != 0 || root == 0)
                throw new InvalidOperationException($"ViewRuntime inflate failed (status {status}).");
            _root = root;
            return RegisterView(root);
        }
        finally
        {
            FreeNativeTree(nodesPtr, nodeCount);
        }
    }

    /// <summary>Allocates a CONTIGUOUS block of android_node_t structs (the
    /// native ABI iterates nodes[0..count-1] as an array — scattered pointers
    /// would read garbage). Each node's attrs array is a separate contiguous
    /// block pointed to by the node's attrs field.</summary>
    private nint AllocateNativeTree(AndroidInflateTree tree, out int nodeCount)
    {
        nodeCount = tree.Nodes.Count;
        if (nodeCount == 0) return 0;
        int nodeSize = Marshal.SizeOf<AndroidNodeNative>();
        nint nodesPtr = Marshal.AllocCoTaskMem(nodeSize * nodeCount);
        for (int i = 0; i < nodeCount; i++)
        {
            AndroidInflateNode node = tree.Nodes[i];
            nint classNamePtr = Marshal.StringToCoTaskMemUTF8(node.ClassName);
            int attrCount = node.Attributes.Count;
            nint attrsPtr = attrCount == 0 ? 0 : Marshal.AllocCoTaskMem(attrCount * Marshal.SizeOf<AndroidAttrNative>());
            for (int a = 0; a < attrCount; a++)
            {
                AndroidAttrNative attr = ToNativeAttr(node.Attributes[a]);
                Marshal.StructureToPtr(attr, attrsPtr + a * Marshal.SizeOf<AndroidAttrNative>(), false);
            }
            var native = new AndroidNodeNative
            {
                class_name = classNamePtr,
                resource_id = node.ResourceId,
                parent_index = node.ParentIndex,
                theme_style_id = node.Index == 0 ? unchecked((uint)tree.ApplicationThemeStyleId) : 0,
                attr_count = attrCount,
                attrs = attrsPtr
            };
            Marshal.StructureToPtr(native, nodesPtr + i * nodeSize, false);
        }
        return nodesPtr;
    }

    private AndroidAttrNative ToNativeAttr(AndroidInflateAttribute attribute)
    {
        AndroidRawValue value = attribute.Value;
        nint stringPtr = value.String is null ? 0 : Marshal.StringToCoTaskMemUTF8(value.String);
        nint namePtr = attribute.Name is null ? 0 : Marshal.StringToCoTaskMemUTF8(attribute.Name);
        var raw = ToNative(value);
        return new AndroidAttrNative { name = namePtr, name_id = attribute.ResourceId, value = raw };
    }

    private void FreeNativeTree(nint nodesPtr, int nodeCount)
    {
        if (nodesPtr == 0) return;
        int nodeSize = Marshal.SizeOf<AndroidNodeNative>();
        for (int i = 0; i < nodeCount; i++)
        {
            nint nodePtr = nodesPtr + i * nodeSize;
            AndroidNodeNative node = Marshal.PtrToStructure<AndroidNodeNative>(nodePtr);
            if (node.class_name != 0) Marshal.FreeCoTaskMem(node.class_name);
            if (node.attrs != 0)
            {
                for (int a = 0; a < node.attr_count; a++)
                {
                    AndroidAttrNative attr = Marshal.PtrToStructure<AndroidAttrNative>(node.attrs + a * Marshal.SizeOf<AndroidAttrNative>());
                    if (attr.name != 0) Marshal.FreeCoTaskMem(attr.name);
                    if (attr.value.string_value != 0) Marshal.FreeCoTaskMem(attr.value.string_value);
                }
                Marshal.FreeCoTaskMem(node.attrs);
            }
        }
        Marshal.FreeCoTaskMem(nodesPtr);
    }

    // ---- view lookups / state (forwarded by native handle) ----

    public DexObject? FindViewById(int id, DexObject? receiver = null)
    {
        RequireAvailable();
        nint native = ViewRuntimeBridgeNative.android_ui_find_view_by_id(_ui, id);
        return native == 0 ? null : RegisterView(native);
    }

    public int GetId(DexObject view)
    {
        RequireAvailable();
        nint native = NativeOf(view);
        ViewRuntimeBridgeNative.android_view_get_resource_id(native, out int id);
        return id;
    }

    public void SetEnabled(DexObject view, bool enabled)
    {
        RequireAvailable();
        ViewRuntimeBridgeNative.android_view_set_enabled(NativeOf(view), (byte)(enabled ? 1 : 0));
    }

    public bool IsEnabled(DexObject view)
    {
        // The landed ABI has android_view_set_enabled but no enabled getter yet.
        // No fabricated answer: fail closed until the reverse query exists.
        RequireAvailable();
        throw NotWired();
    }

    public void SetVisibility(DexObject view, int visibility)
    {
        RequireAvailable();
        ViewRuntimeBridgeNative.android_view_set_visibility(NativeOf(view), visibility);
    }

    public int GetVisibility(DexObject view)
    {
        // No visibility getter in the landed ABI. No fabricated answer.
        RequireAvailable();
        throw NotWired();
    }

    public void SetOnClickListener(DexObject view, DexObject? listener)
    {
        RequireAvailable();
        nint native = NativeOf(view);
        _listeners[native] = listener;
        // The XML onClick handler (if any) is what ViewRuntime's hit-test
        // dispatch uses; programmatic listeners are dispatched by App Runtime
        // after hit-test returns the view id (see PerformClick).
    }

    public bool PerformClick(DexObject view)
    {
        // Click dispatch is DEX execution (this side owns invoking the guest
        // listener), but the landed ABI has no native perform-click trigger and
        // no click-handler query. No fabricated success: fail closed.
        RequireAvailable();
        throw NotWired();
    }

    public void SetText(DexObject view, string? text)
    {
        RequireAvailable();
        ViewRuntimeBridgeNative.android_view_set_text(NativeOf(view), text);
    }

    public string GetText(DexObject view)
    {
        RequireAvailable();
        ViewRuntimeBridgeNative.android_view_get_text(NativeOf(view), out nint textPtr);
        if (textPtr == 0) return string.Empty;
        try { return Marshal.PtrToStringUTF8(textPtr) ?? string.Empty; }
        finally { ViewRuntimeBridgeNative.string_free(textPtr); }
    }

    public bool IsLaidOut(DexObject view)
    {
        // No laid-out query in the landed ABI. No fabricated answer.
        RequireAvailable();
        throw NotWired();
    }

    public int GetPaddingLeft(DexObject view)
    {
        RequireAvailable();
        ViewRuntimeBridgeNative.android_view_get_padding_dp(NativeOf(view), out AndroidThicknessF padding);
        return (int)padding.left;
    }

    public int GetPaddingTop(DexObject view)
    {
        RequireAvailable();
        ViewRuntimeBridgeNative.android_view_get_padding_dp(NativeOf(view), out AndroidThicknessF padding);
        return (int)padding.top;
    }

    public int GetPaddingRight(DexObject view)
    {
        RequireAvailable();
        ViewRuntimeBridgeNative.android_view_get_padding_dp(NativeOf(view), out AndroidThicknessF padding);
        return (int)padding.right;
    }

    public int GetPaddingBottom(DexObject view)
    {
        RequireAvailable();
        ViewRuntimeBridgeNative.android_view_get_padding_dp(NativeOf(view), out AndroidThicknessF padding);
        return (int)padding.bottom;
    }

    // ---- TypedArray / styled attributes (ViewRuntime owns resolution) ----
    // The native TypedArray surface is being implemented alongside the rest of
    // the ABI; until the specific operation is wired, fail closed rather than
    // fabricate a styled value.

    public DexObject ObtainStyledAttributes() { RequireAvailable(); throw NotWired(); }
    public int TypedArrayGetIndexCount() { RequireAvailable(); throw NotWired(); }
    public bool TypedArrayHasValue(int index) { RequireAvailable(); throw NotWired(); }
    public string? TypedArrayGetString(int index) { RequireAvailable(); throw NotWired(); }
    public int TypedArrayGetColor(int index, int defaultValue) { RequireAvailable(); throw NotWired(); }
    public DexObject? TypedArrayGetColorStateList(int index) { RequireAvailable(); throw NotWired(); }
    public float TypedArrayGetDimension(int index, float defaultValue) { RequireAvailable(); throw NotWired(); }
    public int TypedArrayGetInt(int index, int defaultValue) { RequireAvailable(); throw NotWired(); }
    public int TypedArrayGetResourceId(int index, int defaultValue) { RequireAvailable(); throw NotWired(); }
    public bool TypedArrayGetBoolean(int index, bool defaultValue) { RequireAvailable(); throw NotWired(); }
    public float TypedArrayGetFloat(int index, float defaultValue) { RequireAvailable(); throw NotWired(); }
    public int TypedArrayGetDimensionPixelSize(int index, int defaultValue) { RequireAvailable(); throw NotWired(); }
    public int TypedArrayGetDimensionPixelOffset(int index, int defaultValue) { RequireAvailable(); throw NotWired(); }
    public int TypedArrayGetIndex(int index) { RequireAvailable(); throw NotWired(); }
    public bool TypedArrayGetValue(int index) { RequireAvailable(); throw NotWired(); }

    // ---- frame lifecycle / hit-test ----

    public byte[]? RenderFrame(int pixelWidth, int pixelHeight, float density)
    {
        RequireAvailable();
        if (_root == 0) return null;
        ViewRuntimeBridgeNative.viewruntime_surface_resize(_surface, pixelWidth, pixelHeight, density);
        // Full Phase-2 frame pipeline: ViewRuntime measures, lays out, records
        // a display list, and renders it to the surface — this side only
        // copies the finished buffer. No command interpretation here.
        int measureStatus = ViewRuntimeBridgeNative.android_ui_measure(_ui, _root, pixelWidth, pixelHeight);
        int layoutStatus = ViewRuntimeBridgeNative.android_ui_layout(_ui, _root, 0, 0, pixelWidth, pixelHeight);
        int recordStatus = ViewRuntimeBridgeNative.android_ui_record(_ui, _root, out nint list);
        if (recordStatus != 0 || list == 0) return null;
        try
        {
            int renderStatus = ViewRuntimeBridgeNative.android_ui_render(_ui, list);
            if (renderStatus != 0) return null;
        }
        finally
        {
            ViewRuntimeBridgeNative.display_list_destroy(list);
        }
        ViewRuntimeBridgeNative.viewruntime_surface_pixels(_surface, out nint pixels, out int pitch, out int width, out int height);
        if (pixels == 0 || width <= 0 || height <= 0) return null;
        int bytes = checked(width * height * 4);
        var buffer = new byte[bytes];
        Marshal.Copy(pixels, buffer, 0, bytes);
        return buffer;
    }

    public int? HitTest(float pixelX, float pixelY)
    {
        RequireAvailable();
        nint hit = ViewRuntimeBridgeNative.android_ui_hit_test(_ui, 0, pixelX, pixelY);
        if (hit == 0) return null;
        ViewRuntimeBridgeNative.android_view_get_resource_id(hit, out int id);
        return id;
    }

    // ---- internal helpers ----

    private DexObject RegisterView(nint native)
    {
        lock (_nativeToGuest)
        {
            if (_nativeToGuest.TryGetValue(native, out DexObject? existing)) return existing;
            // Real view class from ViewRuntime's actual hierarchy (android_view
            // class enum in android.h): guest casts (e.g. (Button) findViewById)
            // must resolve against the REAL widget class, not a generic View.
            string descriptor = "Landroid/view/View;";
            int viewClass = -1;
            try
            {
                viewClass = ViewRuntimeBridgeNative.android_view_get_class(native);
                descriptor = viewClass switch
                {
                    1 => "Landroid/widget/LinearLayout;",
                    2 => "Landroid/widget/FrameLayout;",
                    3 => "Landroid/widget/RelativeLayout;",
                    4 => "Landroid/widget/ScrollView;",
                    5 => "Landroid/widget/TextView;",
                    6 => "Landroid/widget/Button;",
                    7 => "Landroid/widget/EditText;",
                    8 => "Landroid/widget/ImageView;",
                    9 => "Landroid/widget/CheckBox;",
                    10 => "Landroid/widget/RadioButton;",
                    11 => "Landroid/widget/ProgressBar;",
                    _ => "Landroid/view/View;"
                };
            }
            catch (Exception) { }
            var guest = new DexObject(descriptor);
            _nativeToGuest[native] = guest;
            _guestToNative[guest] = native;
            _listeners[native] = null;
            return guest;
        }
    }

    private nint NativeOf(DexObject view)
    {
        lock (_guestToNative)
        {
            if (_guestToNative.TryGetValue(view, out nint native)) return native;
            throw new InvalidOperationException("View receiver does not belong to this bridge session.");
        }
    }

    private void RequireAvailable()
    {
        if (!_available)
            throw new InvalidOperationException("Android view bridge is not attached; ViewRuntime owns all view behavior in Phase 2.");
    }

    private static InvalidOperationException NotWired() =>
        new("ViewRuntime bridge operation not yet wired (native ABI in progress).");

    /// <summary>Rooted native callbacks — ViewRuntime → App Runtime resource
    /// queries backed by the existing AndroidResourceQueryService. Each returns
    /// FALSE (fails closed, no fallback value) when the data cannot be resolved.
    /// The delegate instances are fields so their function pointers stay stable
    /// for the life of the session.</summary>
    private sealed class ResourceCallbacks
    {
        private readonly ViewRuntimeAndroidViewBridge _owner;

        internal ResourceCallbacks(ViewRuntimeAndroidViewBridge owner)
        {
            _owner = owner;
            ResolveResourceDelegate = ResolveResource;
            ResolveStyleDelegate = ResolveStyle;
            FetchFileDelegate = FetchFile;
        }

        internal delegate byte ResolveResourceFn(uint resource_id, out AndroidRawValueNative out_value, nint user_data);
        internal delegate byte ResolveStyleFn(uint style_id, out nint out_attrs, out int out_attr_count, out uint out_parent_style_id, nint user_data);
        internal delegate byte FetchFileFn(nint path, out nint out_bytes, out int out_size, nint user_data);

        internal ResolveResourceFn ResolveResourceDelegate { get; }
        internal ResolveStyleFn ResolveStyleDelegate { get; }
        internal FetchFileFn FetchFileDelegate { get; }

        internal byte ResolveResource(uint resource_id, out AndroidRawValueNative out_value, nint user_data)
        {
            try
            {
                AndroidRawValue value = _owner._queries.ResolveResource(resource_id);
                out_value = ToNative(value);
                return 1;
            }
            catch (Exception)
            {
                out_value = default;
                return 0;
            }
        }

        internal byte ResolveStyle(uint style_id, out nint out_attrs, out int out_attr_count, out uint out_parent_style_id, nint user_data)
        {
            out_attrs = 0; out_attr_count = 0; out_parent_style_id = 0;
            try
            {
                AndroidResourceStyleLink? link = _owner._queries.ResolveStyle(style_id);
                if (link is null) return 0;
                out_parent_style_id = link.ParentStyleId;
                out_attr_count = link.Attributes.Count;
                out_attrs = Marshal.AllocCoTaskMem(out_attr_count * Marshal.SizeOf<AndroidAttrNative>());
                for (int i = 0; i < out_attr_count; i++)
                    Marshal.StructureToPtr(_owner.ToNativeAttr(link.Attributes[i]), out_attrs + i * Marshal.SizeOf<AndroidAttrNative>(), false);
                return 1;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        internal byte FetchFile(nint path, out nint out_bytes, out int out_size, nint user_data)
        {
            out_bytes = 0; out_size = 0;
            try
            {
                string? pathString = path == 0 ? null : Marshal.PtrToStringUTF8(path);
                if (pathString is null) return 0;
                byte[]? bytes = _owner._queries.FetchFile(pathString);
                if (bytes is null) return 0;
                out_bytes = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, out_bytes, bytes.Length);
                out_size = bytes.Length;
                return 1;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }

    private static AndroidRawValueNative ToNative(AndroidRawValue value)
    {
        nint stringPtr = value.String is null ? 0 : Marshal.StringToCoTaskMemUTF8(value.String);
        return new AndroidRawValueNative
        {
            kind = (int)value.Kind,
            string_value = stringPtr,
            ref_id = value.Data,
            float_value = value.FloatValue,
            unit = value.Unit,
            int_value = unchecked((int)value.Data)
        };
    }
}
