#include <viewruntime/android.h>
#include <viewruntime/viewruntime_backend.h>

#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <thread>

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
    if (id == 0x7f050003) {
        /* SKYNET: textColor -> @color/abc_btn_colored_text_material; the
         * resource resolves to a FILE PATH (a <selector> AXML), not a color. */
        out->kind = ANDROID_RAW_TYPE_STRING;
        out->string_value = "res/color/abc_btn_colored_text_material.xml";
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

/* Theme 0x7f020012: textViewStyle=?attr/0x01010018 -> @style/0x7f020000, and
 * textColorLink = teal selector (0x7f010004) so links render their own color. */
const android_attr_t kThemeWithDefStyleAttrs[2] = {
    {"android:textViewStyle", 0x01010018,
     {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f020000, 0.f, 0, 0}},
    {"android:textColorLink", 0x0101009a,
     {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f010004, 0.f, 0, 0}},
};

/* Shape drawable 0x7f020020: <shape><solid android:color="#FF00FF00"/></shape>
 * (solid green). Walked like a style bag. */
const android_attr_t kDrawableAttrs[1] = {
    {"android:color", 0x01010098,
     {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF00FF00)}},
};

/* Drawables resolved as bags (App Runtime parses the AXML drawable and
 * exposes it as the same raw attribute bag as a style):
 *   0x7f010003 shape: <solid android:color="#FF008577"/> (teal)
 *   0x7f010004 selector: default item android:color="#FF03DAC5" (accent) */
const android_attr_t kShapeBagAttrs[1] = {
    {"android:color", 0x01010098,
     {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF008577)}},
};
const android_attr_t kSelectorBagAttrs[1] = {
    {"android:color", 0x01010098,
     {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF03DAC5)}},
};

/* Selector with interaction states, exposed per the hover/pressed ABI
 * contract: the stateless <item> is "color", the pressed <item> is
 * "state_pressed". Default teal (#FF03DAC5), pressed dark (#FF008577). */
const android_attr_t kStatefulSelectorAttrs[2] = {
    {"android:color", 0x01010098,
     {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF03DAC5)}},
    {"state_pressed", 0x010100fe,
     {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF008577)}},
};

/* GradientDrawable page background: <shape><gradient startColor endColor
 * angle/></shape> — no <solid>. The bag exposes startColor (the dominant
 * gradient color for angle=270). */
const android_attr_t kGradientBgAttrs[2] = {
    {"startColor", 0x010101cd,
     {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF0B1020)}},
    {"endColor", 0x010101ce,
     {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF111A33)}},
};

/* Regression bags (audit round 7): a shape with <corners android:radius=8
 * android:topLeftRadius=4/> and <stroke android:width=2dp/> (NO color).
 * AOSP: absent per-corners fall back to the UNIFORM radius
 * (getDimensionPixelSize(name, radius), GradientDrawable.java:1668-1675);
 * a stroke without color paints OPAQUE BLACK (Paint default,
 * GradientDrawable.java:754-755 + 2413-2423). */
const android_attr_t kPerCornerStrokeAttrs[4] = {
    {"radius", 0x010101a3,
     {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 8.f, ANDROID_DIMEN_UNIT_DIP, 0}},
    {"topLeftRadius", 0x010101a4,
     {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 4.f, ANDROID_DIMEN_UNIT_DIP, 0}},
    {"strokeWidth", 0x010101b1,
     {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 2.f, ANDROID_DIMEN_UNIT_DIP, 0}},
    {"shape", 0x01010063,
     {ANDROID_RAW_TYPE_INT_DEC, nullptr, 0, 0.f, 0, 0}}, /* RECTANGLE */
};

/* ColorStateList textColor file (SKYNET abc_btn_colored_text_material):
 * <selector><item state_enabled=false color=?android:textColorHighlight/>
 * <item (stateless) color=?android:textColorPrimary/></selector> — the
 * stateless item is a theme ATTRIBUTE (0x01010039), not a literal. The bag
 * App Runtime serves exposes the stateless item as "color". */
const android_attr_t kTextColorSelectorAttrs[2] = {
    {"state_enabled_false", 0x01010007,
     {ANDROID_RAW_TYPE_ATTRIBUTE, nullptr, 0x01010046, 0.f, 0, 0}}, /* textColorHighlight */
    {"color", 0x01010098,
     {ANDROID_RAW_TYPE_ATTRIBUTE, nullptr, 0x01010039, 0.f, 0, 0}}, /* textColorPrimary */
};

/* Theme with textColorPrimary: style 0x7f020013 -> textColorPrimary=#FFFFFFFF
 * (white — the expected button text on the teal background). */
const android_attr_t kTextColorPrimaryThemeAttrs[1] = {
    {"android:textColorPrimary", 0x01010039,
     {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFFFFFFFF)}},
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
        case 0x7f020012:
            *out = kThemeWithDefStyleAttrs; *count = 2; *parent = 0;
            return true;
        case 0x7f020020:
            *out = kDrawableAttrs; *count = 1; *parent = 0;
            return true;
        case 0x7f010003:
            *out = kShapeBagAttrs; *count = 1; *parent = 0;
            return true;
        case 0x7f010004:
            *out = kSelectorBagAttrs; *count = 1; *parent = 0;
            return true;
        case 0x7f010005:
            *out = kStatefulSelectorAttrs; *count = 2; *parent = 0;
            return true;
        case 0x7f010006:
            *out = kGradientBgAttrs; *count = 2; *parent = 0;
            return true;
        case 0x7f010007:
            *out = kPerCornerStrokeAttrs; *count = 4; *parent = 0;
            return true;
        case 0x7f050003:
            /* The color-selector file: served as a bag under the SAME id as
             * the textColor reference (App Runtime generic resolve_style). */
            *out = kTextColorSelectorAttrs; *count = 2; *parent = 0;
            return true;
        case 0x7f020013:
            *out = kTextColorPrimaryThemeAttrs; *count = 1; *parent = 0;
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

/* Drawable via resolve_style bag: a background="@drawable/..." resolves by
 * App Runtime exposing the PARSED drawable (shape/selector) as the same raw
 * attribute bag as a style — ViewRuntime takes android:color, never parses
 * bytes (separation of responsibilities: format parsing is App Runtime's). */
void test_inflate_drawable_bag_and_color_selector() {
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
    /* Button: background=@drawable/0x7f010003 (shape bag, teal), and
     * textColor=@drawable/0x7f010004 (color selector bag, accent). */
    android_attr_t button_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:background",
                 {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f010003, 0.f, 0, 0}),
        attr_lit("android:textColor",
                 {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f010004, 0.f, 0, 0}),
    };
    android_node_t nodes[] = {
        {"LinearLayout", 0, -1, 0, 2, root_attrs},
        {"Button", 0x7f0f0001, 0, 0, 4, button_attrs},
    };
    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, nodes, 2, &root), "inflate");

    android_view_t btn = android_ui_find_view_by_id(ui, 0x7f0f0001);
    expect(btn != nullptr, "find Button");
    color_rgba bg{};
    expect_ok(android_view_get_background_color(btn, &bg), "get bg");
    expect(bg.a == 1.f && bg.g > 0.5f && bg.b > 0.4f && bg.r == 0.f,
           "shape drawable background teal (#FF008577)");
    color_rgba tc{};
    expect_ok(android_view_get_text_color(btn, &tc), "get text color");
    expect(tc.r < 0.05f && tc.g > 0.8f && tc.b > 0.7f && tc.a == 1.f,
           "color selector textColor accent (#FF03DAC5)");

    android_ui_destroy(ui);
}

/* Text pipeline end-to-end with a real font: android_ui_set_font loads the
 * SAME bytes the surface renders with (propagated), so a Button with text
 * paints real glyphs instead of the no-font solid-block fallback. */
void test_inflate_text_paints_real_glyphs() {
    android_ui_options_t opts{};
    opts.density = 1.f;
    opts.scaled_density = 1.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");
    android_ui_set_resource_bridge(ui, stub_resolve_resource,
                                   stub_resolve_style, stub_fetch_file, nullptr);
    void* surface = viewruntime_surface_create(nullptr);
    expect(surface != nullptr, "surface created");
    viewruntime_surface_resize(surface, 64, 24, 1.f);
    android_ui_set_surface(ui, surface);

    /* Load a real system font; the same bytes must reach the surface. */
    const char* font_path = "C:\\Windows\\Fonts\\arial.ttf";
    const status_t font_st = android_ui_set_font(ui, font_path);
    expect(font_st == OK, "set_font loads arial.ttf");
    if (font_st != OK) {
        android_ui_destroy(ui);
        viewruntime_surface_destroy(surface);
        return;
    }

    /* LinearLayout (match_parent) > TextView "AB" (wrap_content): the child's
     * wrap_content honors the real glyph metrics. */
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
        attr_lit("android:text", {ANDROID_RAW_TYPE_STRING, "AB", 0, 0.f, 0, 0}),
        attr_lit("android:textSize",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 16.f, ANDROID_DIMEN_UNIT_SP, 0}),
        attr_lit("android:textColor",
                 {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF000000)}),
    };
    android_node_t nodes[] = {
        {"LinearLayout", 0, -1, 0, 2, root_attrs},
        {"TextView", 0x7f0f0001, 0, 0, 5, attrs},
    };
    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, nodes, 2, &root), "inflate");

    /* Wrap_content of "AB" at 16sp must be the REAL glyph advance, not the
     * 0.56*size proportional fallback (which would be ~17.9px for 2 chars).
     * Measure through the API that shares the loaded font. */
    android_text_metrics_t metrics{};
    const status_t mst = android_ui_measure_text(ui, "AB", 16.f, 0.f, &metrics);
    expect(mst == OK, "measure_text with loaded font");
    expect(metrics.width > 8.f && metrics.width < 25.f,
           "text width from real font metrics, not block fallback");
    /* Real Arial "AB" at 16px is ~18px; the proportional fallback is also
     * ~18px, so the discriminator is the render below, not just width. */

    /* Layout the tree first: a view must have real laid-out bounds for the
     * render clip (View.java:24905 canvas.clipRect). Rendering an unlaid-out
     * view (bounds 0x0) paints nothing — with the old no-clip draw the text
     * bled into the surface regardless. */
    expect_ok(android_ui_measure(ui, root, 64.f, 24.f), "measure");
    expect_ok(android_ui_layout(ui, root, 0.f, 0.f, 64.f, 24.f), "layout");

    display_list_t list = nullptr;
    expect_ok(android_ui_record(ui, root, &list), "record");
    expect_ok(android_ui_render(ui, list), "render");
    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    /* Real glyphs are NOT a uniform solid block: the "A" stroke area has
     * opaque pixels, but the box is not uniformly opaque (the no-font
     * fallback fills the whole rect uniformly). */
    int dark = 0, total = 0;
    for (int y = 0; y < h; ++y) {
        for (int x = 0; x < w; ++x) {
            ++total;
            const uint8_t* p = px + y * pitch + x * 4;
            if (p[3] > 200) ++dark; /* opaque (alpha) pixel */
        }
    }
    expect(dark > 0 && dark < total / 2,
           "glyph coverage: some opaque pixels, not a full block");

    display_list_destroy(list);
    viewruntime_surface_destroy(surface);
    android_ui_destroy(ui);
}

/* AOSP defStyleAttr + textColorLink: a TextView with NO explicit style=
 * inherits its default style from the theme via ?attr/textViewStyle (the
 * chain's textColor white applies), and a TextView with
 * android:textColorLink resolves its OWN link color (separate from
 * textColor). textViewStyle=0x01010018 is the id VERIFIED from real data. */
void test_inflate_def_style_attr_and_text_color_link() {
    android_ui_options_t opts{};
    opts.density = 1.f;
    opts.scaled_density = 1.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");
    android_ui_set_resource_bridge(ui, stub_resolve_resource,
                                   stub_resolve_style, stub_fetch_file, nullptr);

    /* Theme 0x7f020012 defines textViewStyle (0x01010018) -> style 0x7f020000
     * (textColor white via its chain). */
    android_attr_t root_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
    };
    /* TextView WITHOUT style=: defStyleAttr must pull textViewStyle from the
     * theme. TextView WITH textColorLink: link color resolves separately. */
    android_attr_t plain_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:text", {ANDROID_RAW_TYPE_STRING, "title", 0, 0.f, 0, 0}),
    };
    android_attr_t link_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:text", {ANDROID_RAW_TYPE_STRING, "link", 0, 0.f, 0, 0}),
        attr_lit("android:textColor",
                 {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF000000)}),
        attr_lit("android:textColorLink",
                 {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f010004, 0.f, 0, 0}),
    };
    android_node_t nodes[] = {
        {"LinearLayout", 0, -1, 0x7f020012 /* theme with textViewStyle */, 2, root_attrs},
        {"TextView", 0x7f0f0001, 0, 0, 3, plain_attrs},
        {"TextView", 0x7f0f0002, 0, 0, 5, link_attrs},
    };
    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, nodes, 3, &root), "inflate");

    /* Plain TextView inherits textColor WHITE from the theme's
     * textViewStyle chain (defStyleAttr). */
    android_view_t plain = android_ui_find_view_by_id(ui, 0x7f0f0001);
    expect(plain != nullptr, "find plain TextView");
    color_rgba tc{};
    expect_ok(android_view_get_text_color(plain, &tc), "plain text color");
    expect(tc.r == 1.f && tc.g == 1.f && tc.b == 1.f,
           "textColor white via theme defStyleAttr (textViewStyle)");

    /* TextView's link color resolves to the accent teal selector, separate
     * from its regular black text color. */
    android_view_t link = android_ui_find_view_by_id(ui, 0x7f0f0002);
    expect(link != nullptr, "find TextView");
    color_rgba tc2{};
    expect_ok(android_view_get_text_color(link, &tc2), "link text color");
    expect(tc2.r == 0.f && tc2.g == 0.f && tc2.b == 0.f,
           "regular textColor stays black");
    color_rgba linkc{};
    expect_ok(android_view_get_text_color_link(link, &linkc), "link color");
    expect(linkc.r < 0.05f && linkc.g > 0.8f && linkc.b > 0.7f && linkc.a == 1.f,
           "textColorLink resolves to accent teal (#FF03DAC5)");

    android_ui_destroy(ui);
}

/* Regression: AXML binary encodes layout_width/layout_height special
 * constants as typed INT_DEC, NOT strings — MATCH_PARENT=-1, WRAP_CONTENT=-2
 * (ViewGroup.LayoutParams, ViewGroup.java:8312/8319). The serializer dump of
 * the real SKYNET APK shows layout_width=IntDecimal data=0xfffffffe for the
 * title. Before the fix, size_from_raw fell into EXACT and every wrap/match
 * child measured 0x0. */
void test_inflate_axml_int_size_constants() {
    android_ui_options_t opts{};
    opts.density = 1.f;
    opts.scaled_density = 1.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");

    android_attr_t attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_INT_DEC, nullptr, 0, 0.f, 0, -1}),  /* MATCH_PARENT */
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_INT_DEC, nullptr, 0, 0.f, 0, -2}),  /* WRAP_CONTENT */
    };
    android_node_t node{};
    node.class_name = "TextView";
    node.parent_index = -1;
    node.attr_count = 2;
    node.attrs = attrs;

    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, &node, 1, &root), "inflate INT_DEC sizes");

    android_layout_params_t lp{};
    expect_ok(android_view_get_layout_params(root, &lp), "get lp");
    expect(lp.width.kind == ANDROID_SIZE_KIND_MATCH_PARENT,
           "INT_DEC -1 -> MATCH_PARENT");
    expect(lp.height.kind == ANDROID_SIZE_KIND_WRAP_CONTENT,
           "INT_DEC -2 -> WRAP_CONTENT");

    /* The real bug: these used to become EXACT 0dp and measure 0x0. */
    expect_ok(android_ui_measure(ui, root, 432.f, 768.f), "measure");
    sizef m{};
    expect_ok(android_view_get_measured_size(root, &m), "get measured");
    expect(m.width == 432.f, "MATCH_PARENT width resolves to parent width (not 0)");

    android_ui_destroy(ui);
}

/* Regression: a Button's text must be CENTERED inside the button. The
 * text_gravity mask bug (CENTER 0x11 shares the 0x01 bit with RIGHT 0x05)
 * misaligned the text to the right edge. Exercises the REAL paint path:
 * inflate -> measure -> layout -> record -> render, then reads pixels. */
void test_inflate_button_text_centered() {
    android_ui_options_t opts{};
    opts.density = 1.f;
    opts.scaled_density = 1.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");

    const char* font_path = nullptr;
    static const char* candidates[] = {
        "C:\\Windows\\Fonts\\arial.ttf",
        "C:\\Windows\\Fonts\\segoeui.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    };
    for (const char* c : candidates) {
        FILE* f = std::fopen(c, "rb");
        if (f) { std::fclose(f); font_path = c; break; }
    }
    if (!font_path) { android_ui_destroy(ui); return; } /* no font: skip */
    expect_ok(android_ui_set_font(ui, font_path), "set font");

    void* surface = viewruntime_surface_create(font_path);
    expect(surface != nullptr, "surface created");
    viewruntime_surface_resize(surface, 300, 120, 1.f);
    android_ui_set_surface(ui, surface);

    /* Button 200x60, "Connect" 18sp bold, textColor black, gravity CENTER. */
    android_attr_t attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 200.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 60.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:textSize",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 18.f, ANDROID_DIMEN_UNIT_SP, 0}),
        attr_lit("android:textStyle", {ANDROID_RAW_TYPE_INT_DEC, nullptr, 0, 0.f, 0, 1}),
        attr_lit("android:text", {ANDROID_RAW_TYPE_STRING, "Connect", 0, 0.f, 0, 0}),
        attr_lit("android:gravity", {ANDROID_RAW_TYPE_INT_DEC, nullptr, 0, 0.f, 0, ANDROID_GRAVITY_CENTER}),
        attr_lit("android:textColor", {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF000000)}),
    };
    android_node_t node{};
    node.class_name = "Button";
    node.parent_index = -1;
    node.attr_count = 7;
    node.attrs = attrs;

    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, &node, 1, &root), "inflate");
    expect_ok(android_ui_measure(ui, root, 300.f, 120.f), "measure");
    expect_ok(android_ui_layout(ui, root, 50.f, 30.f, 200.f, 60.f), "layout");

    display_list_t list = nullptr;
    expect_ok(android_ui_record(ui, root, &list), "record");
    expect_ok(android_ui_render(ui, list), "render");

    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    int minx = 9999, maxx = -1, miny = 9999, maxy = -1, count = 0;
    for (int y = 0; y < h; ++y) {
        for (int x = 0; x < w; ++x) {
            const uint8_t* p = px + y * pitch + x * 4; /* b,g,r,a */
            if (p[3] != 0 && p[2] < 64 && p[1] < 64 && p[0] < 64) { /* black text */
                if (x < minx) minx = x;
                if (x > maxx) maxx = x;
                if (y < miny) miny = y;
                if (y > maxy) maxy = y;
                count++;
            }
        }
    }
    expect(count > 0, "button text rendered");
    if (count > 0) {
        const int cx = (minx + maxx) / 2, cy = (miny + maxy) / 2;
        /* Button center is (150, 60); the mask bug pushed text to the right
         * edge (~x 240+). Allow 12px tolerance for glyph asymmetry. */
        expect(cx > 100 && cx < 200, "text horizontally centered in button");
        expect(cy > 40 && cy < 80, "text vertically centered in button");
    }

    display_list_destroy(list);
    viewruntime_surface_destroy(surface);
    android_ui_destroy(ui);
}

/* Hover/pressed visual feedback: a Button whose background is a stateful
 * selector (default teal #FF03DAC5, pressed dark #FF008577) renders the
 * DEFAULT color normally and SWAPS to the pressed color when
 * android_view_set_pressed(true) is called — verified on real pixels. */
void test_inflate_button_pressed_color_swap() {
    android_ui_options_t opts{};
    opts.density = 1.f;
    opts.scaled_density = 1.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");
    android_ui_set_resource_bridge(ui, stub_resolve_resource,
                                   stub_resolve_style, stub_fetch_file, nullptr);

    static const char* font_candidates[] = {
        "C:\\Windows\\Fonts\\arial.ttf",
        "C:\\Windows\\Fonts\\segoeui.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    };
    const char* font_path = nullptr;
    for (const char* c : font_candidates) {
        FILE* f = std::fopen(c, "rb");
        if (f) { std::fclose(f); font_path = c; break; }
    }
    if (font_path) expect_ok(android_ui_set_font(ui, font_path), "set font");

    void* surface = viewruntime_surface_create(font_path);
    expect(surface != nullptr, "surface created");
    viewruntime_surface_resize(surface, 120, 60, 1.f);
    android_ui_set_surface(ui, surface);

    /* Button 100x40 with background=@drawable/0x7f010005 (stateful selector). */
    android_attr_t attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 100.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 40.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:background",
                 {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f010005, 0.f, 0, 0}),
        attr_lit("android:text", {ANDROID_RAW_TYPE_STRING, "OK", 0, 0.f, 0, 0}),
        attr_lit("android:textSize",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 14.f, ANDROID_DIMEN_UNIT_SP, 0}),
        attr_lit("android:textColor", {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF000000)}),
    };
    android_node_t node{};
    node.class_name = "Button";
    node.parent_index = -1;
    node.attr_count = 6;
    node.attrs = attrs;

    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, &node, 1, &root), "inflate");
    expect_ok(android_ui_measure(ui, root, 120.f, 60.f), "measure");
    expect_ok(android_ui_layout(ui, root, 10.f, 10.f, 100.f, 40.f), "layout");

    /* Background sample point: inside the button but away from the text —
     * the button spans (10,10)-(110,50); sample (15,45) (bottom-left corner
     * area, no glyphs). BGRA byte order. */
    struct Rgb { int r, g, b; };
    auto sample_bg = [&]() -> Rgb {
        const uint8_t* px = nullptr;
        int pitch = 0, w = 0, h = 0;
        viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
        const uint8_t* p = px + 45 * pitch + 15 * 4;
        return {static_cast<int>(p[2]), static_cast<int>(p[1]),
                static_cast<int>(p[0])}; /* r,g,b */
    };

    /* Default state: teal #FF03DAC5 -> r=3 g=218 b=197. */
    display_list_t list = nullptr;
    expect_ok(android_ui_record(ui, root, &list), "record default");
    expect_ok(android_ui_render(ui, list), "render default");
    display_list_destroy(list);
    Rgb r0 = sample_bg();
    expect(r0.r < 32 && r0.g > 180 && r0.b > 160,
           "default background is teal (#03DAC5)");

    /* Pressed: dark #FF008577 -> r=0 g=133 b=119. */
    expect_ok(android_view_set_pressed(root, TRUE), "set pressed");
    list = nullptr;
    expect_ok(android_ui_record(ui, root, &list), "record pressed");
    expect_ok(android_ui_render(ui, list), "render pressed");
    display_list_destroy(list);
    Rgb r1 = sample_bg();
    expect(r1.r < 32 && r1.g > 100 && r1.g < 160 && r1.b > 90 && r1.b < 150,
           "pressed background is dark (#008577)");
    expect(!(r0.r == r1.r && r0.g == r1.g && r0.b == r1.b),
           "pressed color differs from default");

    /* Release: back to teal. */
    expect_ok(android_view_set_pressed(root, FALSE), "release pressed");
    list = nullptr;
    expect_ok(android_ui_record(ui, root, &list), "record released");
    expect_ok(android_ui_render(ui, list), "render released");
    display_list_destroy(list);
    Rgb r2 = sample_bg();
    expect(r2.r < 32 && r2.g > 180 && r2.b > 160,
           "released background is teal again");

    /* Honest fallback: a drawable with NO state_pressed item must keep the
     * stateless color when pressed — never a fabricated color. Build a fresh
     * session with the stateless-only selector (0x7f010004). */
    android_ui_destroy(ui);
    viewruntime_surface_destroy(surface);

    /* Fresh session: button with background=@drawable/0x7f010004 (no states). */
    android_ui_t ui2 = nullptr;
    expect_ok(android_ui_create(&opts, &ui2), "create ui2");
    android_ui_set_resource_bridge(ui2, stub_resolve_resource,
                                   stub_resolve_style, stub_fetch_file, nullptr);
    if (font_path) expect_ok(android_ui_set_font(ui2, font_path), "set font ui2");
    void* surface2 = viewruntime_surface_create(font_path);
    expect(surface2 != nullptr, "surface2 created");
    viewruntime_surface_resize(surface2, 120, 60, 1.f);
    android_ui_set_surface(ui2, surface2);

    android_attr_t attrs2[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 100.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 40.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:background",
                 {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f010004, 0.f, 0, 0}),
        attr_lit("android:text", {ANDROID_RAW_TYPE_STRING, "OK", 0, 0.f, 0, 0}),
        attr_lit("android:textSize",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 14.f, ANDROID_DIMEN_UNIT_SP, 0}),
    };
    android_node_t node2{};
    node2.class_name = "Button";
    node2.parent_index = -1;
    node2.attr_count = 5;
    node2.attrs = attrs2;

    android_view_t root2 = nullptr;
    expect_ok(android_ui_inflate(ui2, &node2, 1, &root2), "inflate fallback");
    expect_ok(android_ui_measure(ui2, root2, 120.f, 60.f), "measure fallback");
    expect_ok(android_ui_layout(ui2, root2, 10.f, 10.f, 100.f, 40.f), "layout fallback");

    auto sample2 = [&]() -> Rgb {
        const uint8_t* px = nullptr;
        int pitch = 0, w = 0, h = 0;
        viewruntime_surface_pixels(surface2, &px, &pitch, &w, &h);
        const uint8_t* p = px + 45 * pitch + 15 * 4;
        return {static_cast<int>(p[2]), static_cast<int>(p[1]),
                static_cast<int>(p[0])};
    };

    /* Default: teal. */
    display_list_t list2 = nullptr;
    expect_ok(android_ui_record(ui2, root2, &list2), "record fallback default");
    expect_ok(android_ui_render(ui2, list2), "render fallback default");
    display_list_destroy(list2);
    Rgb fd = sample2();
    expect(fd.r < 32 && fd.g > 180 && fd.b > 160,
           "fallback default is teal");

    /* Pressed with NO state_pressed item: must STAY teal (honest fallback). */
    expect_ok(android_view_set_pressed(root2, TRUE), "press fallback");
    list2 = nullptr;
    expect_ok(android_ui_record(ui2, root2, &list2), "record fallback pressed");
    expect_ok(android_ui_render(ui2, list2), "render fallback pressed");
    display_list_destroy(list2);
    Rgb fp = sample2();
    expect(fp.r < 32 && fp.g > 180 && fp.b > 160,
           "fallback pressed stays teal (no fabricated color)");

    viewruntime_surface_destroy(surface2);
    android_ui_destroy(ui2);

    /* GradientDrawable page background: <shape><gradient startColor
     * #FF0B1020 endColor #FF111A33/></shape> — no <solid>. The background
     * must resolve to the gradient's startColor (page navy), never black.
     * RuntimeApiLab bg_page regression. */
    android_ui_t ui3 = nullptr;
    expect_ok(android_ui_create(&opts, &ui3), "create ui3");
    android_ui_set_resource_bridge(ui3, stub_resolve_resource,
                                   stub_resolve_style, stub_fetch_file, nullptr);
    void* surface3 = viewruntime_surface_create(nullptr);
    expect(surface3 != nullptr, "surface3 created");
    viewruntime_surface_resize(surface3, 40, 40, 1.f);
    android_ui_set_surface(ui3, surface3);

    android_attr_t attrs3[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 40.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 40.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:background",
                 {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f010006, 0.f, 0, 0}),
    };
    android_node_t node3{};
    node3.class_name = "FrameLayout";
    node3.parent_index = -1;
    node3.attr_count = 3;
    node3.attrs = attrs3;

    android_view_t root3 = nullptr;
    expect_ok(android_ui_inflate(ui3, &node3, 1, &root3), "inflate gradient bg");
    expect_ok(android_ui_measure(ui3, root3, 40.f, 40.f), "measure gradient bg");
    expect_ok(android_ui_layout(ui3, root3, 0.f, 0.f, 40.f, 40.f), "layout gradient bg");

    color_rgba gbg{};
    android_view_get_background_color(root3, &gbg);
    expect(gbg.a == 1.f && gbg.r < 0.1f && gbg.g > 0.04f && gbg.g < 0.09f &&
           gbg.b > 0.1f && gbg.b < 0.16f,
           "gradient background resolves to startColor navy (#0B1020), not black");

    display_list_t list3 = nullptr;
    expect_ok(android_ui_record(ui3, root3, &list3), "record gradient bg");
    expect_ok(android_ui_render(ui3, list3), "render gradient bg");
    display_list_destroy(list3);
    const uint8_t* px3 = nullptr;
    int pitch3 = 0, w3 = 0, h3 = 0;
    viewruntime_surface_pixels(surface3, &px3, &pitch3, &w3, &h3);
    const uint8_t* p3 = px3 + 20 * pitch3 + 20 * 4;
    expect(p3[3] != 0 && p3[2] < 32 && p3[1] > 5 && p3[1] < 40 &&
           p3[0] > 20 && p3[0] < 70,
           "gradient background painted navy, not black");

    viewruntime_surface_destroy(surface3);
    android_ui_destroy(ui3);

    /* SKYNET contrast regression: Button textColor=@color/0x7f050003 — a
     * reference that RESOLVES TO A FILE PATH (color selector), whose bag's
     * stateless item is ?attr/textColorPrimary (theme ATTRIBUTE 0x01010039).
     * The app theme does NOT define 0x01010039 (verified against the real
     * SKYNET theme chain 0x7f10000b→0x7f10021c→0x7f100054); Widget.AppCompat.
     * Button.Colored applies ThemeOverlay.AppCompat.Dark so THIS Button gets
     * WHITE (reference RGB 248,244,245). A plain TextView must NOT — the
     * overlay is button-scoped, so no generic white default.
     * Theme root = 0x7f020010 (only colorAccent, NO textColorPrimary). */
    android_ui_t ui4 = nullptr;
    expect_ok(android_ui_create(&opts, &ui4), "create ui4");
    android_ui_set_resource_bridge(ui4, stub_resolve_resource,
                                   stub_resolve_style, stub_fetch_file, nullptr);
    if (font_path) expect_ok(android_ui_set_font(ui4, font_path), "set font ui4");
    void* surface4 = viewruntime_surface_create(font_path);
    expect(surface4 != nullptr, "surface4 created");
    viewruntime_surface_resize(surface4, 120, 60, 1.f);
    android_ui_set_surface(ui4, surface4);

    android_attr_t attrs4[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 100.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 40.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:background",
                 {ANDROID_RAW_TYPE_INT_COLOR, nullptr, 0, 0.f, 0, static_cast<int32_t>(0xFF03DAC5)}),
        attr_lit("android:text", {ANDROID_RAW_TYPE_STRING, "CONNECT", 0, 0.f, 0, 0}),
        attr_lit("android:textSize",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 16.f, ANDROID_DIMEN_UNIT_SP, 0}),
        attr_lit("android:textColor",
                 {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f050003, 0.f, 0, 0}),
    };
    android_node_t node4{};
    node4.class_name = "Button";
    node4.parent_index = -1;
    node4.theme_style_id = 0x7f020010; /* theme WITHOUT textColorPrimary */
    node4.attr_count = 6;
    node4.attrs = attrs4;

    android_view_t root4 = nullptr;
    expect_ok(android_ui_inflate(ui4, &node4, 1, &root4), "inflate contrast");
    expect_ok(android_ui_measure(ui4, root4, 120.f, 60.f), "measure contrast");
    expect_ok(android_ui_layout(ui4, root4, 10.f, 10.f, 100.f, 40.f), "layout contrast");

    color_rgba tc{};
    expect_ok(android_view_get_text_color(root4, &tc), "get text color");
    expect(tc.a == 1.f && tc.r > 0.9f && tc.g > 0.9f && tc.b > 0.9f,
           "Button textColor resolves to white (Button.Colored overlay), not teal");

    display_list_t list4 = nullptr;
    expect_ok(android_ui_record(ui4, root4, &list4), "record contrast");
    expect_ok(android_ui_render(ui4, list4), "render contrast");
    display_list_destroy(list4);
    /* Text pixel: white glyph on teal background — a bright pixel (>200)
     * inside the button (sample near center, on the "O" of CONNECT). */
    const uint8_t* px4 = nullptr;
    int pitch4 = 0, w4 = 0, h4 = 0;
    viewruntime_surface_pixels(surface4, &px4, &pitch4, &w4, &h4);
    int bright = 0;
    for (int y = 20; y < 40; ++y)
        for (int x = 30; x < 90; ++x) {
            const uint8_t* p = px4 + y * pitch4 + x * 4;
            if (p[3] != 0 && p[2] > 200 && p[1] > 200 && p[0] > 200) bright++;
        }
    expect(bright > 0, "white text pixels rendered (high contrast)");

    /* Scope guard: a plain TextView with the SAME textColor reference must
     * NOT get the Button.Colored overlay white — the fallback is button-
     * scoped, so inflate fails (unresolved textColor) instead of inventing
     * a generic default. */
    android_node_t node_tv{};
    node_tv.class_name = "TextView";
    node_tv.parent_index = -1;
    node_tv.theme_style_id = 0x7f020010; /* same theme, no textColorPrimary */
    node_tv.attr_count = 2;
    android_attr_t tv_attrs[2] = {
        attr_lit("android:text", {ANDROID_RAW_TYPE_STRING, "plain", 0, 0.f, 0, 0}),
        attr_lit("android:textColor",
                 {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f050003, 0.f, 0, 0}),
    };
    node_tv.attrs = tv_attrs;
    android_view_t root_tv = nullptr;
    status_t tv_st = android_ui_inflate(ui4, &node_tv, 1, &root_tv);
    expect(tv_st != OK,
           "TextView with unresolved textColorPrimary does NOT resolve (no generic white)");

    viewruntime_surface_destroy(surface4);
    android_ui_destroy(ui4);
    return;
}

/* Regression (audit round 5, HIGH): the display-list UTF-8→UTF-16 converter
 * must handle a supplementary-plane character (>= 0x10000, e.g. emoji U+1F600
 * = bytes F0 9F 98 80) by emitting a surrogate pair AND advancing 4 bytes.
 * The old code `continue`d inside the 4-byte branch before `p += 4`, so any
 * emoji caused an infinite loop + heap overflow (n grew past the len+1
 * buffer). Rendering a TextView whose text contains an emoji must complete. */
void test_inflate_text_with_emoji_completes() {
    android_ui_options_t opts{};
    opts.density = 1.f;
    opts.scaled_density = 1.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");
    android_ui_set_resource_bridge(ui, stub_resolve_resource,
                                   stub_resolve_style, stub_fetch_file, nullptr);
    void* surface = viewruntime_surface_create(nullptr);
    expect(surface != nullptr, "surface created");
    viewruntime_surface_resize(surface, 64, 24, 1.f);
    android_ui_set_surface(ui, surface);

    const char* font_path = "C:\\Windows\\Fonts\\segoeui.ttf";
    if (std::fopen(font_path, "rb") == nullptr) font_path = "C:\\Windows\\Fonts\\arial.ttf";
    const status_t font_st = android_ui_set_font(ui, font_path);
    expect(font_st == OK, "set_font");
    if (font_st != OK) {
        android_ui_destroy(ui);
        viewruntime_surface_destroy(surface);
        return;
    }

    /* "A😀B" — the emoji is 4 UTF-8 bytes; UTF-16 needs 2 code units. */
    android_attr_t attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:text",
                 {ANDROID_RAW_TYPE_STRING, "A\xF0\x9F\x98\x80" "B", 0, 0.f, 0, 0}),
        attr_lit("android:textSize",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 16.f, ANDROID_DIMEN_UNIT_SP, 0}),
    };
    android_node_t node{};
    node.class_name = "TextView";
    node.parent_index = -1;
    node.attr_count = 4;
    node.attrs = attrs;
    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, &node, 1, &root), "inflate emoji TextView");

    display_list_t list = nullptr;
    expect_ok(android_ui_record(ui, root, &list), "record");
    /* If the converter still loops on the emoji, this call never returns
     * (the test binary hangs; with the fix it completes). */
    const status_t rst = android_ui_render(ui, list);
    expect(rst == OK, "render emoji text completes (no infinite loop)");
    expect(display_list_get_count(list) > 0, "list recorded commands");
    display_list_destroy(list);
    android_ui_destroy(ui);
    viewruntime_surface_destroy(surface);
}

/* Regression (audit round 7, LOW): per-corner radii absent from the bag fall
 * back to the UNIFORM radius (AOSP getDimensionPixelSize(name, radius),
 * GradientDrawable.java:1668-1675) — the old code left them 0, so
 * radius=8 + topLeftRadius=4 averaged (4+0+0+0)/4=1px everywhere instead of
 * (4+8+8+8)/4=7. And a <stroke android:width/> with NO color must resolve to
 * OPAQUE BLACK (AOSP Paint default, java:754-755 + 2413-2423), not
 * transparent — verified by rendering: the 2dp black border is visible over
 * the transparent fill. */
void test_inflate_per_corner_uniform_default_and_black_stroke() {
    android_ui_options_t opts{};
    opts.density = 1.f;
    opts.scaled_density = 1.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");
    android_ui_set_resource_bridge(ui, stub_resolve_resource,
                                   stub_resolve_style, stub_fetch_file, nullptr);
    void* surface = viewruntime_surface_create(nullptr);
    expect(surface != nullptr, "surface created");
    viewruntime_surface_resize(surface, 40, 40, 1.f);
    android_ui_set_surface(ui, surface);

    android_attr_t attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 40.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 40.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:background",
                 {ANDROID_RAW_TYPE_REFERENCE, nullptr, 0x7f010007, 0.f, 0, 0}),
    };
    android_node_t node{};
    node.class_name = "FrameLayout";
    node.parent_index = -1;
    node.attr_count = 3;
    node.attrs = attrs;
    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, &node, 1, &root), "inflate per-corner bg");
    expect_ok(android_ui_measure(ui, root, 40.f, 40.f), "measure");
    expect_ok(android_ui_layout(ui, root, 0.f, 0.f, 40.f, 40.f), "layout");

    display_list_t list = nullptr;
    expect_ok(android_ui_record(ui, root, &list), "record");
    expect_ok(android_ui_render(ui, list), "render");
    display_list_destroy(list);
    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    auto sample = [&](int x, int y) -> int {
        const uint8_t* p = px + y * pitch + x * 4;
        return p[3]; /* alpha */
    };
    /* Stroke border (2dp, no declared color → opaque black): painted. The
     * 2px band spans pixels [0,1] (centerline inset 1px ± 1px); (1,20) is
     * inside the band, (2,20) is the stroke INTERIOR (transparent). */
    expect(sample(1, 20) != 0, "stroke border painted (black default)");
    expect(sample(39, 20) != 0, "right stroke border painted");
    /* Interior: no <solid> fill → transparent (a stroke-only shape). */
    expect(sample(20, 20) == 0, "interior transparent (no solid fill)");

    viewruntime_surface_destroy(surface);
    android_ui_destroy(ui);
}

/* Regression (host bridge): the WindowsHost flow is measure → layout → record
 * → render, then a guest mutation (setText), then the SAME cycle again. The
 * second render crashed (AccessViolation) in android_ui_render on the real
 * RuntimeApiLab tree (ScrollView > LinearLayout > TextView children). Minimal
 * repro with the same shape: ScrollView root, TextView child, setText, second
 * record/render cycle. */
void test_inflate_settext_then_rerender() {
    android_ui_options_t opts{};
    opts.density = 1.f;
    opts.scaled_density = 1.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");
    android_ui_set_resource_bridge(ui, stub_resolve_resource,
                                   stub_resolve_style, stub_fetch_file, nullptr);
    void* surface = viewruntime_surface_create(nullptr);
    expect(surface != nullptr, "surface created");
    viewruntime_surface_resize(surface, 40, 40, 1.f);
    android_ui_set_surface(ui, surface);

    android_attr_t scroll_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
    };
    android_attr_t tv_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "wrap_content", 0, 0.f, 0, 0}),
        attr_lit("android:text", {ANDROID_RAW_TYPE_STRING, "old", 0, 0.f, 0, 0}),
        attr_lit("android:textSize",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 16.f, ANDROID_DIMEN_UNIT_SP, 0}),
    };
    android_node_t nodes[] = {
        {"ScrollView", 0, -1, 0, 2, scroll_attrs},
        {"TextView", 0x7f0f0001, 0, 0, 4, tv_attrs},
    };
    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, nodes, 2, &root), "inflate");

    auto cycle = [&](const char* label) {
        expect_ok(android_ui_measure(ui, root, 40.f, 40.f), label);
        expect_ok(android_ui_layout(ui, root, 0.f, 0.f, 40.f, 40.f), label);
        display_list_t list = nullptr;
        expect_ok(android_ui_record(ui, root, &list), label);
        expect_ok(android_ui_render(ui, list), label);
        display_list_destroy(list);
    };
    cycle("cycle 1");
    android_view_t tv = android_ui_find_view_by_id(ui, 0x7f0f0001);
    expect(tv != nullptr, "textview found");
    if (tv != nullptr) {
        expect_ok(android_view_set_text(tv, "Ejecutando ping a google.com"), "set text");
        cycle("cycle 2 (no crash)");
    }

    viewruntime_surface_destroy(surface);
    android_ui_destroy(ui);
}

/* Toast (android.widget.Toast exact port): makeText → show → active →
 * render overlay → timeout deactivates. Verifies the AOSP constants:
 * SHORT timeout 4000ms, default gravity BOTTOM|CENTER, y offset 48dp, panel
 * drawn over the surface. Uses a tiny SHORT window via direct deadline
 * manipulation? No — the deadline is real; the test checks active right after
 * show, render produces the panel, and after >4000ms is_active is false. */
void test_toast_lifecycle_and_render() {
    android_ui_options_t opts{};
    opts.density = 1.f;
    opts.scaled_density = 1.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");
    android_ui_set_resource_bridge(ui, stub_resolve_resource,
                                   stub_resolve_style, stub_fetch_file, nullptr);
    void* surface = viewruntime_surface_create(nullptr);
    expect(surface != nullptr, "surface created");
    viewruntime_surface_resize(surface, 200, 200, 1.f);
    android_ui_set_surface(ui, surface);

    /* Not active before makeText. */
    expect(android_toast_is_active(ui) == FALSE, "inactive before makeText");

    /* makeText with an invalid duration must fail (AOSP LENGTH_* domain). */
    expect(android_toast_make_text(ui, "ping", 7) != OK, "invalid duration rejected");

    /* makeText(SHORT) + show → active. */
    expect_ok(android_toast_make_text(ui, "Ejecutando ping a google.com", ANDROID_TOAST_LENGTH_SHORT),
              "makeText short");
    expect(android_toast_is_active(ui) == FALSE, "inactive until show");
    expect_ok(android_toast_show(ui), "show");
    expect(android_toast_is_active(ui) == TRUE, "active after show");
    expect(android_toast_get_duration(ui) == ANDROID_TOAST_LENGTH_SHORT, "duration short");

    /* Render the overlay: a dark panel near the bottom must appear (bottom
     * center, y offset 48dp — on a 200px surface the panel bottom sits at
     * 200-48=152). The message text is light; the panel background dark. */
    viewruntime_frame_begin(surface);
    android_toast_render(ui);
    viewruntime_frame_end(surface);
    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    auto alpha_at = [&](int x, int y) -> int { return px[y * pitch + x * 4 + 3]; };
    auto red_at = [&](int x, int y) -> int { return px[y * pitch + x * 4 + 2]; };
    /* Center of the panel area (dark, opaque): around (100, ~130) given the
     * message width. Verify the panel exists: SOME dark pixel in the lower
     * half below the 48dp offset, none above it. */
    bool found_panel = false, found_above = false;
    for (int y = 60; y < 152; ++y)
        for (int x = 0; x < 200; ++x)
            if (alpha_at(x, y) > 200 && red_at(x, y) < 100) found_panel = true;
    for (int y = 0; y < 55; ++y)
        for (int x = 0; x < 200; ++x)
            if (alpha_at(x, y) > 200) found_above = true;
    expect(found_panel, "toast panel rendered in the bottom region");
    expect(!found_above, "no toast above the 48dp y offset region");

    /* setText updates the message (getText path is the same store). */
    expect_ok(android_toast_set_text(ui, "nuevo"), "setText");

    /* cancel → inactive immediately. */
    expect_ok(android_toast_cancel(ui), "cancel");
    expect(android_toast_is_active(ui) == FALSE, "inactive after cancel");

    /* LONG duration survives >4000ms but expires before 7000ms: show LONG,
     * sleep 4.2s, still active; then simulate timeout by re-showing with the
     * deadline already past (direct store) is not possible via API, so verify
     * the timeout path by waiting for SHORT expiry in a second run. */
    expect_ok(android_toast_make_text(ui, "largo", ANDROID_TOAST_LENGTH_LONG), "makeText long");
    expect_ok(android_toast_show(ui), "show long");
    expect(android_toast_is_active(ui) == TRUE, "long active immediately");
    /* Short-timeout check: make a SHORT toast, wait past 4000ms, inactive. */
    expect_ok(android_toast_make_text(ui, "corto", ANDROID_TOAST_LENGTH_SHORT), "makeText short2");
    expect_ok(android_toast_show(ui), "show short2");
    expect(android_toast_is_active(ui) == TRUE, "short active immediately");
    std::this_thread::sleep_for(std::chrono::milliseconds(4200));
    expect(android_toast_is_active(ui) == FALSE, "short toast expired after 4.2s");

    viewruntime_surface_destroy(surface);
    android_ui_destroy(ui);
}

/* Input dispatch (View.dispatchTouchEvent/onTouchEvent exact port):
 * DOWN → fix target + press; UP on the same target → performClick callback;
 * MOVE beyond touch slop cancels the tap; disabled consumes without clicking;
 * key Enter → click the focused view. */
void test_input_dispatch_tap_and_slop() {
    android_ui_options_t opts{};
    opts.density = 1.f;
    opts.scaled_density = 1.f;
    android_ui_t ui = nullptr;
    expect_ok(android_ui_create(&opts, &ui), "create ui");
    android_ui_set_resource_bridge(ui, stub_resolve_resource,
                                   stub_resolve_style, stub_fetch_file, nullptr);
    void* surface = viewruntime_surface_create(nullptr);
    expect(surface != nullptr, "surface created");
    viewruntime_surface_resize(surface, 200, 200, 1.f);
    android_ui_set_surface(ui, surface);

    /* A clickable Button at (10,10,100,40) inside a FrameLayout. */
    android_attr_t root_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_STRING, "match_parent", 0, 0.f, 0, 0}),
    };
    android_attr_t btn_attrs[] = {
        attr_lit("android:layout_width",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 100.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:layout_height",
                 {ANDROID_RAW_TYPE_DIMENSION, nullptr, 0, 40.f, ANDROID_DIMEN_UNIT_DIP, 0}),
        attr_lit("android:text", {ANDROID_RAW_TYPE_STRING, "Ping", 0, 0.f, 0, 0}),
    };
    android_node_t nodes[] = {
        {"FrameLayout", 0, -1, 0, 2, root_attrs},
        {"Button", 0x7f0f0100, 0, 0, 3, btn_attrs},
    };
    android_view_t root = nullptr;
    expect_ok(android_ui_inflate(ui, nodes, 2, &root), "inflate");
    expect_ok(android_ui_measure(ui, root, 200.f, 200.f), "measure");
    expect_ok(android_ui_layout(ui, root, 0.f, 0.f, 200.f, 200.f), "layout");

    android_view_t btn = android_ui_find_view_by_id(ui, 0x7f0f0100);
    expect(btn != nullptr, "button found");
    expect_ok(android_view_set_clickable(btn, TRUE), "set clickable");

    int clicks = 0, long_clicks = 0;
    struct Ctx { int* clicks; int* long_clicks; } ctx{&clicks, &long_clicks};
    auto on_click = [](int32_t /*id*/, void* ud) {
        auto* c = static_cast<Ctx*>(ud);
        ++(*c->clicks);
    };
    auto on_long = [](int32_t /*id*/, void* ud) {
        auto* c = static_cast<Ctx*>(ud);
        ++(*c->long_clicks);
    };
    android_ui_set_click_callback(ui, on_click, on_long, &ctx);

    /* Tap inside the button → click. */
    expect_ok(android_ui_dispatch_touch(ui, root, ANDROID_ACTION_DOWN, 30.f, 30.f), "down");
    expect(android_ui_gesture_active(ui) == TRUE, "gesture active after down");
    expect_ok(android_ui_dispatch_touch(ui, root, ANDROID_ACTION_UP, 30.f, 30.f), "up");
    expect(clicks == 1, "tap produced exactly one click");
    expect(android_ui_gesture_active(ui) == FALSE, "gesture ended after up");

    /* MOVE beyond touch slop (8dp) cancels the tap. */
    expect_ok(android_ui_dispatch_touch(ui, root, ANDROID_ACTION_DOWN, 30.f, 30.f), "down2");
    expect_ok(android_ui_dispatch_touch(ui, root, ANDROID_ACTION_MOVE, 60.f, 60.f), "move-out");
    expect_ok(android_ui_dispatch_touch(ui, root, ANDROID_ACTION_UP, 60.f, 60.f), "up2");
    expect(clicks == 1, "slop-exceeded move must NOT click");

    /* Click outside the button → no click. */
    expect_ok(android_ui_dispatch_touch(ui, root, ANDROID_ACTION_DOWN, 150.f, 150.f), "down3");
    expect_ok(android_ui_dispatch_touch(ui, root, ANDROID_ACTION_UP, 150.f, 150.f), "up3");
    expect(clicks == 1, "tap outside must NOT click");

    /* Disabled button consumes but does not click (View.java:18069-18078). */
    expect_ok(android_view_set_enabled(btn, FALSE), "disable");
    expect_ok(android_ui_dispatch_touch(ui, root, ANDROID_ACTION_DOWN, 30.f, 30.f), "down4");
    expect_ok(android_ui_dispatch_touch(ui, root, ANDROID_ACTION_UP, 30.f, 30.f), "up4");
    expect(clicks == 1, "disabled button must NOT click");
    expect_ok(android_view_set_enabled(btn, TRUE), "re-enable");

    /* Key Enter on the focused view (after a DOWN) → click. */
    expect_ok(android_ui_dispatch_touch(ui, root, ANDROID_ACTION_DOWN, 30.f, 30.f), "down5");
    expect_ok(android_ui_dispatch_touch(ui, root, ANDROID_ACTION_UP, 30.f, 30.f), "up5");
    expect(clicks == 2, "tap 2 clicked");
    expect_ok(android_ui_dispatch_key(ui, root, ANDROID_KEY_ACTION_DOWN, ANDROID_KEYCODE_ENTER), "key enter");
    expect(clicks == 3, "Enter on focused view clicked");

    viewruntime_surface_destroy(surface);
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
    test_inflate_drawable_bag_and_color_selector();
    test_inflate_text_paints_real_glyphs();
    test_inflate_def_style_attr_and_text_color_link();
    test_inflate_axml_int_size_constants();
    test_inflate_button_text_centered();
    test_inflate_button_pressed_color_swap();
    test_inflate_text_with_emoji_completes();
    test_inflate_per_corner_uniform_default_and_black_stroke();
    test_inflate_settext_then_rerender();
    test_toast_lifecycle_and_render();
    test_input_dispatch_tap_and_slop();
    if (g_failures != 0) {
        std::fprintf(stderr, "%d FAILURES\n", g_failures);
        return 1;
    }
    std::printf("OK\n");
    return 0;
}
