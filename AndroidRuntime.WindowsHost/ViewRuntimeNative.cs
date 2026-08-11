#nullable enable
using System.IO;
using System.Runtime.InteropServices;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.WindowsHost;

/// <summary>
/// Phase-2 P/Invoke surface for ViewRuntime's agreed bridge ABI
/// (Ui\ViewRuntime\include\viewruntime\android.h). This side calls INTO
/// ViewRuntime (session lifecycle, resource-bridge registration, inflate, view
/// mutations/queries, measure/layout/record, hit-test) and ViewRuntime calls
/// BACK through the resource callbacks (resolve_resource / resolve_style /
/// fetch_file) backed by the C# resource-query service.
///
/// Sizes are pixels; dp/sp values are converted with the session density
/// exactly once on ViewRuntime's side. The presentation surface functions
/// (viewruntime_surface_*) remain for the GDI blit of the finished frame.
/// </summary>
internal static partial class ViewRuntimeBridgeNative
{
    private const string Dll = "viewruntime_core.dll";

    // --- session lifecycle ---

    [LibraryImport(Dll)]
    internal static partial int android_ui_create(ref AndroidUiOptions options, out nint ui);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int android_ui_set_font(nint ui, string? path);

    [LibraryImport(Dll)]
    internal static partial void android_ui_destroy(nint ui);

    [LibraryImport(Dll)]
    internal static partial void android_ui_clear(nint ui);

    // --- resource bridge (ViewRuntime -> App Runtime callbacks) ---

    [LibraryImport(Dll)]
    internal static partial void android_ui_set_resource_bridge(
        nint ui,
        nint resolve_resource,
        nint resolve_style,
        nint fetch_file,
        nint user_data);

    // --- inflate (App Runtime serializes the AXML tree; ViewRuntime builds) ---

    [LibraryImport(Dll)]
    internal static partial int android_ui_inflate(
        nint ui,
        nint nodes,
        int node_count,
        out nint root);

    // --- view operations (forwarded by native view handle) ---

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int android_view_set_text(nint view, string? text);

    [LibraryImport(Dll)]
    internal static partial int android_view_set_visibility(nint view, int visibility);

    [LibraryImport(Dll)]
    internal static partial int android_view_set_enabled(nint view, byte enabled);

    // Interaction visual state (android.h:408-409): host reports mouse-down
    // (pressed) / mouse-enter (hovered); ViewRuntime re-resolves the background
    // from the drawable's selector for that state. Status_t return. NOTE:
    // bool_t in the native ABI is int32_t (viewruntime.h:62) — a byte arg would
    // be a signature mismatch (the get_resource_id lesson).
    [LibraryImport(Dll)]
    internal static partial int android_view_set_pressed(nint view, int pressed);

    [LibraryImport(Dll)]
    internal static partial int android_view_set_hovered(nint view, int hovered);

    [LibraryImport(Dll)]
    internal static partial int android_view_set_background_color(nint view, AndroidColorRgba color);

    [LibraryImport(Dll)]
    internal static partial int android_view_set_text_color(nint view, AndroidColorRgba color);

    [LibraryImport(Dll)]
    internal static partial int android_view_set_text_size_sp(nint view, float text_size_sp);

    [LibraryImport(Dll)]
    internal static partial int android_view_set_gravity(nint view, int gravity);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int android_view_set_click_handler(nint view, string? handler);

    // --- view queries (real answers from ViewRuntime's actual view state) ---

    [LibraryImport(Dll)]
    internal static partial int android_view_get_text(nint view, out nint text);

    [LibraryImport(Dll)]
    internal static partial int android_view_get_text_color(nint view, out AndroidColorRgba color);

    [LibraryImport(Dll)]
    internal static partial int android_view_get_background_color(nint view, out AndroidColorRgba color);

    [LibraryImport(Dll)]
    internal static partial int android_view_get_measured_size(nint view, out AndroidSizeF size);

    [LibraryImport(Dll)]
    internal static partial int android_view_get_bounds(nint view, out AndroidRectF bounds);

    [LibraryImport(Dll)]
    internal static partial int android_view_get_layout_params(nint view, out AndroidLayoutParamsNative layout);

    [LibraryImport(Dll)]
    internal static partial int android_view_get_padding_dp(nint view, out AndroidThicknessF padding);

    [LibraryImport(Dll)]
    internal static partial int android_view_get_resource_id(nint view);

    [LibraryImport(Dll)]
    internal static partial int android_view_get_class(nint view);

    // --- hierarchy lookups / hit-test ---

    [LibraryImport(Dll)]
    internal static partial nint android_ui_find_view_by_id(nint ui, int resource_id);

    // Post-layout hierarchy (android.h:388): parent walk needed to resolve the
    // scroll container of a hit-tested view (the hit is the DEEPEST view; the
    // ScrollView ancestor is what accepts set_scroll_offset).
    [LibraryImport(Dll)]
    internal static partial nint android_view_get_parent(nint view);

    // ScrollView/ListView/RecyclerView offset (android.h:588): accepts only
    // scroll-container classes, clamps negatives, range is clamped by layout.
    [LibraryImport(Dll)]
    internal static partial int android_view_set_scroll_offset(nint view, float x, float y);

    [LibraryImport(Dll)]
    internal static partial nint android_ui_hit_test(nint ui, nint root, float x, float y);

    // --- frame pipeline (measure/layout/record; presentation via surface) ---

    [LibraryImport(Dll)]
    internal static partial int android_ui_measure(nint ui, nint root, float width_px, float height_px);

    [LibraryImport(Dll)]
    internal static partial int android_ui_layout(nint ui, nint root, float x, float y, float width_px, float height_px);

    [LibraryImport(Dll)]
    internal static partial int android_ui_record(nint ui, nint root, out nint display_list);

    [LibraryImport(Dll)]
    internal static partial int android_ui_render(nint ui, nint display_list);

    [LibraryImport(Dll)]
    internal static partial void android_ui_set_surface(nint ui, nint surface);

    [LibraryImport(Dll)]
    internal static partial void display_list_destroy(nint display_list);

    [LibraryImport(Dll)]
    internal static partial int display_list_get_count(nint display_list);

    // --- presentation surface (finished frame handed back, no interpretation) ---

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint viewruntime_surface_create(string? font_path);

    [LibraryImport(Dll)]
    internal static partial void viewruntime_surface_destroy(nint surface);

    [LibraryImport(Dll)]
    internal static partial void viewruntime_surface_resize(nint surface, int pixel_width, int pixel_height, float density);

    [LibraryImport(Dll)]
    internal static partial void viewruntime_surface_pixels(
        nint surface, out nint out_pixels, out int out_pitch, out int out_width, out int out_height);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void string_free(nint text);

    /// <summary>Picks a real Windows TrueType font for the native surface so
    /// draw_text renders actual glyphs (Phase 1's helper, recreated for Phase 2 —
    /// a null font makes ViewRuntime paint a solid block instead of text).</summary>
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

    // --- Toast (android.widget.Toast — exact AOSP port; state owned by ViewRuntime) ---

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int android_toast_make_text(nint ui, string? text, int duration);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int android_toast_set_text(nint ui, string? text);

    [LibraryImport(Dll)]
    internal static partial int android_toast_set_duration(nint ui, int duration);

    [LibraryImport(Dll)]
    internal static partial int android_toast_get_duration(nint ui);

    [LibraryImport(Dll)]
    internal static partial int android_toast_show(nint ui);

    [LibraryImport(Dll)]
    internal static partial int android_toast_cancel(nint ui);

    [LibraryImport(Dll)]
    internal static partial byte android_toast_is_active(nint ui);

    [LibraryImport(Dll)]
    internal static partial void android_toast_render(nint ui);

    // --- Input events (android.h:689-752 — exact AOSP gesture machine) ---

    // MotionEvent action constants (MotionEvent.java ACTION_*).
    internal const int AndroidActionDown = 0;
    internal const int AndroidActionUp = 1;
    internal const int AndroidActionMove = 2;
    internal const int AndroidActionCancel = 3;

    // KeyEvent action / key codes (KeyEvent.java).
    internal const int AndroidKeyActionDown = 0;
    internal const int AndroidKeyActionUp = 1;
    internal const int AndroidKeyCodeEnter = 66;
    internal const int AndroidKeyCodeDpadCenter = 23;
    internal const int AndroidKeyCodeSpace = 62;

    [LibraryImport(Dll)]
    internal static partial int android_ui_dispatch_touch(nint ui, nint root, int action, float x, float y);

    [LibraryImport(Dll)]
    internal static partial int android_ui_dispatch_key(nint ui, nint root, int action, int key_code);

    /// <summary>Native → managed click channel (android.h:738): ViewRuntime
    /// decides performClick/performLongClick and hands the view's resource id
    /// back; the host runs the guest DEX listener. Invoked synchronously from
    /// inside the dispatch/poll call on the caller's thread.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void AndroidUiClickCallback(int resource_id, nint user_data);

    [LibraryImport(Dll)]
    internal static partial void android_ui_set_click_callback(
        nint ui, nint on_click, nint on_long_click, nint user_data);

    [LibraryImport(Dll)]
    internal static partial int android_ui_gesture_poll(nint ui);

    [LibraryImport(Dll)]
    internal static partial int android_ui_gesture_active(nint ui);

    /// <summary>Marks a view CLICKABLE (android.h:429, View.java:7868
    /// setOnClickListener semantics). bool_t in the native ABI is int32_t.</summary>
    [LibraryImport(Dll)]
    internal static partial int android_view_set_clickable(nint view, int clickable);
}

[StructLayout(LayoutKind.Sequential)]
internal struct AndroidUiOptions
{
    public float density;
    public float scaled_density;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AndroidColorRgba
{
    public float r, g, b, a;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AndroidSizeF
{
    public float width, height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AndroidRectF
{
    public float x, y, width, height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AndroidThicknessF
{
    public float left, top, right, bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AndroidLayoutParamsNative
{
    public int width_kind;
    public float width_value_dp;
    public int height_kind;
    public float height_value_dp;
    public float margin_left, margin_top, margin_right, margin_bottom;
    public int gravity;
    public float weight;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AndroidRawValueNative
{
    public int kind;
    public nint string_value;
    public uint ref_id;
    public float float_value;
    public int unit;
    public int int_value;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AndroidAttrNative
{
    public nint name;
    public uint name_id;
    public AndroidRawValueNative value;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AndroidNodeNative
{
    public nint class_name;
    public int resource_id;
    public int parent_index;
    public uint theme_style_id;
    public int attr_count;
    public nint attrs;
}
