#include <viewruntime/android.h>
#include <viewruntime/viewruntime_backend.h>

#include <cstdio>
#include <cstdlib>
#include <cstring>

/* Phase 2 bridge: inflate from a parsed element tree + resource callbacks +
 * forwarding getters + style/theme chain resolution. */

namespace {

int g_failures = 0;

void expect(bool condition, const char* message) {
    if (!condition) {
        std::fprintf(stderr, "FAIL: %s\n", message);
        ++g_failures;
    }
}

void expect_ok(status_t st, const char* message) {
    if (st != OK) {
        std::fprintf(stderr, "FAIL: %s (status %d)\n", message, st);
        ++g_failures;
    }
}

android_attr_t attr_lit(const char* name, const android_raw_value_t& v) {
    return {name, 0, v};
}

bool_t stub_resolve_resource(uint32_t id, android_raw_value_t* out, void* ud) {
    (void)ud;
    if (id == 0x7f010000) {
        out->kind = ANDROID_RAW_TYPE_INT_COLOR;
        out->int_value = static_cast<int32_t>(0xFF008577);
        return true;
    }
    if (id == 0x7f010001) {
        out->kind = ANDROID_RAW_TYPE_STRING;
        out->string_value = "hello";
        return true;
    }
    if (id == 0x7f010002) {
        out->kind = ANDROID_RAW_TYPE_DIMENSION;
        out->float_value = 12.f;
        out->unit = ANDROID_DIMEN_UNIT_DIP;
        return true;
    }
    return false;
}

/* Style table used by the theme tests:
 *   style 0x7f020000 "Widget.AppCompat.Button.Colored"
 *     parent 0x7f020001; textColor=#FFFFFFFF, textSize=18sp
 *   style 0x7f020001 "Base.Widget.AppCompat.Button.Colored"
 *     parent 0x7f020002; background=?attr/colorAccent
 *   style 0x7f020002 "Base.Widget.AppCompat.Button"
 *     parent 0; textColor=#FF000000
 *   style 0x7f020010 (theme root) parent 0; colorAccent=#FFFF0000 */
struct StyleTable {
    static const android_attr_t kColoredAttrs[2];
    static const android_attr_t kBaseColoredAttrs[1];
    static const android_attr_t kBaseAttrs[1];
    static const android_attr_t kThemeAttrs[1];
};

const android_attr_t StyleTable::kColoredAttrs[2] = {
    {"android:textColor", 0x01010098,
     {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFFFFFFFF)}},
    {"android:textSize", 0x01010095,
     {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 18.f, ANDROID_DIMEN_UNIT_SP, 0}},
};
const android_attr_t StyleTable::kBaseColoredAttrs[1] = {
    {"android:background", 0x010100d4,
     {ANDROID_RAW_TYPE_ATTRIBUTE, nullptr, 0x01010436, 0.f, 0, 0}}, /* ?attr/colorAccent */
};
const android_attr_t StyleTable::kBaseAttrs[1] = {
    {"android:textColor", 0x01010098,
     {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF000000)}},
};
const android_attr_t StyleTable::kThemeAttrs[1] = {
    {"android:colorAccent", 0x01010436,
     {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFFFF0000)}},
};

/* Shape drawable 0x7f020020: <shape><solid android:color="#FF00FF00"/></shape>
 * (solid green). Walked like a style bag. */
const android_attr_t kDrawableAttrs[1] = {
    {"android:color", 0x01010098,
     {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF00FF00)}},
};

/* Theme with windowBackground: style 0x7f020011 -> windowBackground =
 * @drawable/0x7f020020 (solid green via drawable bag). */
const android_attr_t kWindowThemeAttrs[1] = {
    {"android:windowBackground", 0x01010054,
     {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f020020, 0.f, 0, 0}},
};

bool_t stub_resolve_style(uint32_t id, const android_attr_t** out,
                          int32_t* count, uint32_t* parent, void* ud) {
    (void)ud;
    switch (id) {
        case 0x7f020000:
            *out = StyleTable::kColoredAttrs; *count = 2; *parent = 0x7f020001;
            return true;
        case 0x7f020001:
            *out = StyleTable::kBaseColoredAttrs; *count = 1; *parent = 0x7f020002;
            return true;
        case 0x7f020002:
            *out = StyleTable::kBaseAttrs; *count = 1; *parent = 0;
            return true;
        case 0x7f020010:
            *out = StyleTable::kThemeAttrs; *count = 1; *parent = 0;
            return true;
        case 0x7f020011:
            *out = kWindowThemeAttrs; *count = 1; *parent = 0;
            return true;
        case 0x7f020020:
            *out = kDrawableAttrs; *count = 1; *parent = 0;
            return true;
        default:
            *out = nullptr; *count = 0; *parent = 0;
            return false;
    }
}

bool_t stub_fetch_file(const char* path, const uint8_t** out, int32_t* size,
                       void* ud) {
    (void)ud;
    /* "red" serves a real 1x1 red PNG embedded here. */
    if (path && std::strcmp(path, "red") == 0) {
        static const uint8_t kPng[] = {
            0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,
            0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
            0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
            0x08,0x02,0x00,0x00,0x00,0x90,0x77,0x53,
            0xDE, 0x00,0x00,0x00,0x0C,0x49,0x44,0x41,0x54,
            0x08,0xD7,0x63,0xF8,0xCF,0xC0,0x00,0x00,
            0x00,0x03,0x00,0x01,0x25,0x4F,0x4D,0xE7,
            0x00,0x00,0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,0x42,0x60,0x82};
        *out = kPng;
        *size = static_cast<int32_t>(sizeof(kPng));
        return true;
    }
    *out = nullptr;
    *size = 0;
    return false;
}

void test_inflate_builds_tree() {
    android_ui_options_t opts{};
    opts.density = 3.f;
    opts.scaled_density = 3.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");
    android_ui_set_resource_bridge(ui, stub_resolve_resource,
                                   stub_resolve_style, stub_fetch_file, nullptr);

    /* LinearLayout (root) -> TextView + Button (weight 1). Raw values:
     * width/height as literal strings; margins/padding as raw DIP; colors
     * as literal INT_COLOR; text as STRING. */
    android_attr_t root_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
        attr_lit("android:orientation",
                 {ANDROID_RAW_TYPE_STRING, "vertical", 0, 0.f, 0, 0}),
    };
    android_attr_t text_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:text", {ANDROID_RAW_TYPE_STRING, "Label", 0, 0.f, 0, 0}),
        attr_lit("android:textColor",
                 {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF000000)}),
        attr_lit("android:textSize",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 16.f, ANDROID_DIMEN_UNIT_SP, 0}),
        attr_lit("android:layout_margin",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 8.f, ANDROID_DIMEN_UNIT_DIP, 0}),
    };
    android_attr_t button_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:layout_weight",
                 {ANDROID_RAW_TYPE_FLOAT, nullptr, 0, 1.f, 0, 0}),
        attr_lit("android:background",
                 {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f010000, 0.f, 0, 0}),
    };

    android_node_t nodes[] = {
        {"LinearLayout", 0x7f0f0000, -1, 0, 3, root_attrs},
        {"TextView", 0x7f0f0001, 0, 0, 6, text_attrs},
        {"Button", 0x7f0f0002, 0, 0, 4, button_attrs},
    };

    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, nodes, 3, &root), "inflate");
    expect(root != nullptr, "root returned");
    expect(android_view_get_class(root) == ANDROID_VIEW_LINEAR_LAYOUT,
           "root is LinearLayout");
    expect(android_view_get_child_count(root) == 2, "root has 2 children");

    android_view_t tv = android_ui_find_view_by_id(ui, 0x7f0f0001);
    expect(tv != nullptr, "find TextView by id");
    const char* text = nullptr;
    expect_ok(android_view_get_text(tv, &text), "get_text");
    expect(text && std::strcmp(text, "Label") == 0, "text == Label");

    color_rgba c{};
    expect_ok(android_view_get_text_color(tv, &c), "get_text_color");
    expect(c.r == 0.f && c.g == 0.f && c.b == 0.f && c.a == 1.f,
           "text color black");

    android_view_t btn = android_ui_find_view_by_id(ui, 0x7f0f0002);
    expect(btn != nullptr, "find Button by id");
    color_rgba bg{};
    expect_ok(android_view_get_background_color(btn, &bg), "get_background_color");
    /* #FF008577: r=0x00,g=0x85,b=0x77,a=0xFF */
    expect(bg.a == 1.f && bg.g > 0.5f && bg.b > 0.4f && bg.r == 0.f,
           "button background teal from resource ref");

    android_ui_destroy(ui);
}

void test_inflate_applies_dimensions_raw() {
    android_ui_options_t opts{};
    opts.density = 2.f;
    opts.scaled_density = 2.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");

    /* TextView child of a LinearLayout with explicit 40dp x 20dp and 4dp
     * padding all around: the child's LayoutParams are honored through
     * getChildMeasureSpec, and density is applied exactly once at measure. */
    android_attr_t root_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
    };
    android_attr_t attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 40.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 20.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:padding",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 4.f, ANDROID_DIMEN_UNIT_DIP, 0}),
    };
    android_node_t nodes[] = {
        {"LinearLayout", 0, -1, 0, 2, root_attrs},
        {"TextView", 0, 0, 0, 3, attrs},
    };
    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, nodes, 2, &root), "inflate");
    expect(root != nullptr, "root returned");
    android_view_t tv = android_view_get_child(root, 0);
    expect(tv != nullptr, "child returned");
    android_layout_params_t lp{};
    expect_ok(android_view_get_layout_params(tv, &lp), "get_layout_params");
    expect(lp.width.kind == ANDROID_SIZE_KIND_EXACT, "width exact");
    /* Raw dp preserved: no density applied at inflate time. */
    expect(lp.width.value_dp == 40.f, "width stored raw in dp");
    thicknessf pad{};
    expect_ok(android_view_get_padding_dp(tv, &pad), "get_padding_dp");
    expect(pad.left == 4.f, "padding stored raw in dp");

    /* After measure at 2x density, the 40dp child becomes 80px. */
    expect_ok(android_ui_measure(ui, root, 200.f, 100.f), "measure");
    sizef size{};
    expect_ok(android_view_get_measured_size(tv, &size), "measured size");
    expect(size.width == 80.f, "40dp measures to 80px at 2x density");

    android_ui_destroy(ui);
}

void test_inflate_rejects_missing_root() {
    android_ui_t ui = nullptr;
    android_ui_options_t opts{};
    expect_ok(android_ui_create(&opts, &ui), "create ui");

    android_attr_t a[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
    };
    /* child with parent_index 0 but node 0 is missing -> no root -> fail */
    android_node_t nodes[] = {
        {"TextView", 0, 0, 0, 1, a},
    };
    android_view_t root = nullptr;
    const status_t st = android_ui_inflate(ui, nodes, 1, &root);
    expect(st != OK, "inflate without root fails");
    expect(root == nullptr, "no root out");
    expect(android_ui_hit_test(ui, nullptr, 0.f, 0.f) == nullptr,
           "session still usable after failed inflate");

    android_ui_destroy(ui);
}

/* Style chain: Button with style=Widget.AppCompat.Button.Colored must resolve
 * textColor from the derived style (white), background=?attr/colorAccent
 * through the theme chain (red), and an explicit textColor on the XML wins. */
void test_inflate_style_chain_and_theme() {
    android_ui_options_t opts{};
    opts.density = 1.f;
    opts.scaled_density = 1.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");
    android_ui_set_resource_bridge(ui, stub_resolve_resource,
                                   stub_resolve_style, stub_fetch_file, nullptr);

    android_attr_t root_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
    };
    android_attr_t button_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("style",
                 {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f020000, 0.f, 0, 0}),
    };
    android_node_t nodes[] = {
        {"LinearLayout", 0x7f0f0000, -1, 0x7f020010 /* theme root */, 2, root_attrs},
        {"Button", 0x7f0f0001, 0, 0, 3, button_attrs},
    };
    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, nodes, 2, &root), "inflate");

    android_view_t btn = android_ui_find_view_by_id(ui, 0x7f0f0001);
    expect(btn != nullptr, "find Button");
    color_rgba tc{};
    expect_ok(android_view_get_text_color(btn, &tc), "get_text_color");
    expect(tc.r == 1.f && tc.g == 1.f && tc.b == 1.f,
           "textColor white from derived style");
    color_rgba bg{};
    expect_ok(android_view_get_background_color(btn, &bg), "get_background_color");
    expect(bg.r == 1.f && bg.g == 0.f && bg.b == 0.f && bg.a == 1.f,
           "background red from ?attr/colorAccent through theme");

    /* Explicit XML textColor overrides the style value. */
    android_attr_t override_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("style",
                 {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f020000, 0.f, 0, 0}),
        attr_lit("android:textColor",
                 {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF00FF00)}),
    };
    android_node_t nodes2[] = {
        {"LinearLayout", 0, -1, 0x7f020010, 2, root_attrs},
        {"Button", 0, 0, 0, 4, override_attrs},
    };
    android_ui_clear(ui);
    android_view_t root2 = nullptr;
    expect_ok(android_ui_inflate(ui, nodes2, 2, &root2), "inflate override");
    android_view_t btn2 = android_view_get_child(root2, 0);
    expect(btn2 != nullptr, "child2");
    color_rgba tc2{};
    expect_ok(android_view_get_text_color(btn2, &tc2), "get_text_color 2");
    expect(tc2.g == 1.f && tc2.r == 0.f && tc2.b == 0.f,
           "explicit XML textColor wins over style");

    android_ui_destroy(ui);
}

/* Drawables + window background: a background="@drawable/shape_solid" resolves
 * through the drawable bag, and a root without any background inherits the
 * theme's windowBackground. */
void test_inflate_drawable_and_window_background() {
    android_ui_options_t opts{};
    opts.density = 1.f;
    opts.scaled_density = 1.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");
    android_ui_set_resource_bridge(ui, stub_resolve_resource,
                                   stub_resolve_style, stub_fetch_file, nullptr);

    /* Button with background=@drawable/0x7f020020 (solid green). */
    android_attr_t root_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
    };
    android_attr_t button_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:background",
                 {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f020020, 0.f, 0, 0}),
    };
    android_node_t nodes[] = {
        {"LinearLayout", 0x7f0f0000, -1, 0, 2, root_attrs},
        {"Button", 0x7f0f0001, 0, 0, 3, button_attrs},
    };
    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, nodes, 2, &root), "inflate");

    android_view_t btn = android_ui_find_view_by_id(ui, 0x7f0f0001);
    color_rgba bg{};
    expect_ok(android_view_get_background_color(btn, &bg), "button bg");
    expect(bg.g == 1.f && bg.r == 0.f && bg.b == 0.f && bg.a == 1.f,
           "button background solid green from drawable bag");

    /* Root has no own background; theme 0x7f020011's windowBackground
     * (@drawable/0x7f020020, green) must be applied to it. */
    color_rgba root_bg{};
    expect_ok(android_view_get_background_color(root, &root_bg), "root bg");
    expect(root_bg.g == 1.f && root_bg.a == 1.f,
           "root inherited windowBackground from theme");

    android_ui_destroy(ui);
}

/* ImageView end-to-end: src="red" is decoded by ViewRuntime from the raw
 * bytes fetched through the bridge (real 1x1 red PNG), the real bitmap size
 * drives measure, and the ARGB pixels are uploaded to the render surface. */
void test_inflate_image_decode_pipeline() {
    android_ui_options_t opts{};
    opts.density = 1.f;
    opts.scaled_density = 1.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");
    android_ui_set_resource_bridge(ui, stub_resolve_resource,
                                   stub_resolve_style, stub_fetch_file, nullptr);
    void* surface = viewruntime_surface_create(nullptr);
    expect(surface != nullptr, "surface created");
    viewruntime_surface_resize(surface, 8, 8, 1.f);
    android_ui_set_surface(ui, surface);

    /* LinearLayout (match_parent) > ImageView (wrap_content, src="red"):
     * the child's wrap_content honors the 1x1 intrinsic bitmap. */
    android_attr_t root_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
    };
    android_attr_t attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:src", {ANDROID_RAW_TYPE_STRING, "red", 0, 0.f, 0, 0}),
    };
    android_node_t nodes[] = {
        {"LinearLayout", 0, -1, 0, 2, root_attrs},
        {"ImageView", 0x7f0f0001, 0, 0, 3, attrs},
    };
    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, nodes, 2, &root), "inflate");

    expect_ok(android_ui_measure(ui, root, 100.f, 100.f), "measure");
    android_view_t img = android_ui_find_view_by_id(ui, 0x7f0f0001);
    expect(img != nullptr, "find ImageView");
    sizef size{};
    expect_ok(android_view_get_measured_size(img, &size), "measured size");
    expect(size.width == 1.f && size.height == 1.f,
           "1x1 bitmap measures 1x1 at density 1");

    /* Record, then let ViewRuntime execute its own display list onto the
     * surface (the generic render path — the host never interprets
     * commands), and verify the uploaded pixel reached the buffer. */
    display_list_t list = nullptr;
    expect_ok(android_ui_record(ui, root, &list), "record");
    expect_ok(android_ui_render(ui, list), "render");
    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    /* red PNG: R=255,G=0,B=0,A=255; backend stores b,g,r,a byte order. */
    expect(px[0 * pitch + 0 * 4 + 2] == 255 &&
           px[0 * pitch + 0 * 4 + 0] == 0 &&
           px[0 * pitch + 0 * 4 + 3] == 255,
           "decoded red pixel painted at image origin");

    display_list_destroy(list);
    viewruntime_surface_destroy(surface);
    android_ui_destroy(ui);
}

/* LTR gravity/margin relativity: layout_gravity=END must right-align a child
 * in a horizontal LinearLayout, and layout_marginEnd must land on the right
 * edge in LTR. */
void test_inflate_ltr_relative_gravity_and_margin() {
    android_ui_options_t opts{};
    opts.density = 1.f;
    opts.scaled_density = 1.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");

    android_attr_t root_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
    };
    /* Two wrap_content TextViews; the second has layout_gravity=END and
     * layout_marginEnd=4dp (both relative, resolved as RIGHT in LTR). */
    android_attr_t a_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:text", {ANDROID_RAW_TYPE_STRING, "A", 0, 0.f, 0, 0}),
    };
    android_attr_t b_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:text", {ANDROID_RAW_TYPE_STRING, "B", 0, 0.f, 0, 0}),
        attr_lit("android:layout_gravity",
                 {ANDROID_RAW_TYPE_INT_DEC, nullptr, 0, 0.f, 0, ANDROID_GRAVITY_END}),
        attr_lit("android:layout_marginEnd",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 4.f, ANDROID_DIMEN_UNIT_DIP, 0}),
    };
    android_node_t nodes[] = {
        {"LinearLayout", 0, -1, 0, 2, root_attrs},
        {"TextView", 0x7f0f0010, 0, 0, 3, a_attrs},
        {"TextView", 0x7f0f0011, 0, 0, 5, b_attrs},
    };
    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, nodes, 3, &root), "inflate");
    expect_ok(android_ui_measure(ui, root, 200.f, 50.f), "measure");
    expect_ok(android_ui_layout(ui, root, 0.f, 0.f, 200.f, 50.f), "layout");

    android_view_t b = android_ui_find_view_by_id(ui, 0x7f0f0011);
    expect(b != nullptr, "find B");
    rectf bounds{};
    expect_ok(android_view_get_bounds(b, &bounds), "get bounds B");
    /* END in LTR = right edge, minus marginEnd (4). B's measured width is
     * one glyph ~0.56*16 ≈ 9px, so x ≈ 200 - 4 - 9 ≈ 187. */
    expect(bounds.x >= 180.f && bounds.x <= 195.f,
           "END gravity right-aligns with marginEnd in LTR");
    /* marginEnd stored on the right edge (LTR). */
    android_layout_params_t lp{};
    expect_ok(android_view_get_layout_params(b, &lp), "get layout params B");
    expect(lp.margins_dp.right == 4.f && lp.margins_dp.left == 0.f,
           "layout_marginEnd maps to right margin in LTR");

    android_ui_destroy(ui);
}

} // namespace

int main() {
    test_inflate_builds_tree();
    test_inflate_applies_dimensions_raw();
    test_inflate_rejects_missing_root();
    test_inflate_style_chain_and_theme();
    test_inflate_drawable_and_window_background();
    test_inflate_image_decode_pipeline();
    test_inflate_ltr_relative_gravity_and_margin();
    if (g_failures != 0) {
        std::fprintf(stderr, "%d FAILURES\n", g_failures);
        return 1;
    }
    std::printf("OK\n");
    return 0;
}
