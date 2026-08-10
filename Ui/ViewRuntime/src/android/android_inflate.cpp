#include "android_types.h"
#include "../include/viewruntime/viewruntime_backend.h"

#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

/* Phase 2 resource bridge + inflate.
 *
 * App Runtime hands over a parsed element tree (raw, unresolved attribute
 * values); ViewRuntime builds its own real view objects from it and decides
 * what every attribute means. Dimension values stay raw here — the session
 * density is applied exactly once, at measure/layout time, through the
 * dp()/sp() helpers. */

namespace viewruntime::android {
namespace {

/* Map an AXML element class name to the view class enum. Names arrive fully
 * qualified ("android.widget.LinearLayout") or short ("Button",
 * "androidx.appcompat.widget.AppCompatTextView"); we match the last
 * dot-segment against the known widget set, with AppCompat/Material
 * suffixes resolving to their base widget. */
android_view_class_t classify(const char* class_name) {
    if (!class_name || !*class_name) return ANDROID_VIEW_VIEW;
    const char* last_dot = std::strrchr(class_name, '.');
    const char* name = last_dot ? last_dot + 1 : class_name;

    struct entry { const char* suffix; android_view_class_t cls; };
    static const entry kEntries[] = {
        {"ConstraintLayout", ANDROID_VIEW_CONSTRAINT_LAYOUT},
        {"LinearLayout", ANDROID_VIEW_LINEAR_LAYOUT},
        {"FrameLayout", ANDROID_VIEW_FRAME_LAYOUT},
        {"RelativeLayout", ANDROID_VIEW_RELATIVE_LAYOUT},
        {"ScrollView", ANDROID_VIEW_SCROLL_VIEW},
        {"RecyclerView", ANDROID_VIEW_RECYCLER_VIEW},
        {"ListView", ANDROID_VIEW_LIST_VIEW},
        {"GridView", ANDROID_VIEW_LIST_VIEW},
        {"GridLayout", ANDROID_VIEW_GRID_LAYOUT},
        {"TextView", ANDROID_VIEW_TEXT_VIEW},
        {"EditText", ANDROID_VIEW_EDIT_TEXT},
        {"AutoCompleteTextView", ANDROID_VIEW_EDIT_TEXT},
        {"Button", ANDROID_VIEW_BUTTON},
        {"ImageButton", ANDROID_VIEW_BUTTON},
        {"ImageView", ANDROID_VIEW_IMAGE_VIEW},
        {"CheckBox", ANDROID_VIEW_CHECK_BOX},
        {"RadioButton", ANDROID_VIEW_RADIO_BUTTON},
        {"ProgressBar", ANDROID_VIEW_PROGRESS_BAR},
        {"SeekBar", ANDROID_VIEW_PROGRESS_BAR},
        {"Barrier", ANDROID_VIEW_BARRIER},
    };
    for (const entry& e : kEntries) {
        const size_t len = std::strlen(e.suffix);
        const size_t name_len = std::strlen(name);
        if (name_len >= len &&
            std::strcmp(name + (name_len - len), e.suffix) == 0) {
            return e.cls;
        }
    }
    return ANDROID_VIEW_VIEW;
}

/* Short attribute name: everything after the last ':' ("android:layout_width"
 * -> "layout_width", "style" -> "style"). */
const char* attr_short(const char* name) {
    if (!name) return "";
    const char* colon = std::strrchr(name, ':');
    return colon ? colon + 1 : name;
}

/* Parse a raw color (INT_COLOR/INT_HEX/INT_DEC) into color_rgba. Returns
 * false when the value is not a color-shaped integer. */
bool color_from_raw(const android_raw_value_t& v, color_rgba* out) {
    if (v.kind == ANDROID_RAW_TYPE_INT_COLOR ||
        v.kind == ANDROID_RAW_TYPE_INT_HEX ||
        v.kind == ANDROID_RAW_TYPE_INT_DEC) {
        const uint32_t argb = static_cast<uint32_t>(v.int_value);
        out->r = ((argb >> 16) & 0xFF) / 255.f;
        out->g = ((argb >> 8) & 0xFF) / 255.f;
        out->b = (argb & 0xFF) / 255.f;
        out->a = ((argb >> 24) & 0xFF) / 255.f;
        return true;
    }
    return false;
}

/* ── Style / theme resolution ────────────────────────────────────────
 *
 * A style is a chain of raw attribute bags (style -> parent -> ...). A theme
 * is structurally the same thing: App Runtime exposes the active theme's
 * root style id and ViewRuntime walks it with the same resolve_style
 * callback. ?attr/<id> resolves by walking the theme chain looking for a bag
 * entry whose name_id matches the attribute's resource id. */

constexpr int kMaxResolveDepth = 32;

/* Find an attribute by resource id inside one style bag. */
const android_attr_t* find_attr_by_id(const android_attr_t* attrs,
                                      int32_t count, uint32_t attr_id) {
    if (!attrs || count <= 0) return nullptr;
    for (int32_t i = 0; i < count; ++i) {
        if (attrs[i].name_id != 0 && attrs[i].name_id == attr_id) {
            return &attrs[i];
        }
    }
    return nullptr;
}

/* Resolve a raw value that may itself be a reference / theme attribute.
 * Literals pass through unchanged; REFERENCE asks resolve_resource; ATTRIBUTE
 * walks the theme chain. Bounded depth guards against reference cycles. */
bool resolve_value(const android_ui_s* ui, const android_raw_value_t& v,
                   android_raw_value_t* out, int depth = 0);

/* Walk the active theme's style chain looking for attr_id. Returns the raw
 * value found (possibly still a reference; resolve_value unwraps it). */
bool resolve_theme_attr(const android_ui_s* ui, uint32_t attr_id,
                        android_raw_value_t* out) {
    if (!ui->resolve_style || ui->theme_style_id == 0) return false;
    uint32_t style_id = ui->theme_style_id;
    int depth = 0;
    while (style_id != 0 && depth < kMaxResolveDepth) {
        const android_attr_t* attrs = nullptr;
        int32_t count = 0;
        uint32_t parent = 0;
        if (!ui->resolve_style(style_id, &attrs, &count, &parent,
                               ui->bridge_data)) {
            return false;
        }
        if (const android_attr_t* hit = find_attr_by_id(attrs, count, attr_id)) {
            *out = hit->value;
            return true;
        }
        style_id = parent;
        ++depth;
    }
    return false;
}

bool resolve_value(const android_ui_s* ui, const android_raw_value_t& v,
                   android_raw_value_t* out, int depth) {
    if (depth > kMaxResolveDepth) return false;
    switch (v.kind) {
        case ANDROID_RAW_TYPE_REFERENCE:
            if (!ui->resolve_resource) return false;
            if (!ui->resolve_resource(v.ref_id, out, ui->bridge_data)) {
                return false;
            }
            /* A reference may resolve to another reference / theme attr. */
            if (out->kind == ANDROID_RAW_TYPE_REFERENCE ||
                out->kind == ANDROID_RAW_TYPE_ATTRIBUTE) {
                return resolve_value(ui, *out, out, depth + 1);
            }
            return true;
        case ANDROID_RAW_TYPE_ATTRIBUTE:
            if (!resolve_theme_attr(ui, v.ref_id, out)) return false;
            if (out->kind == ANDROID_RAW_TYPE_REFERENCE ||
                out->kind == ANDROID_RAW_TYPE_ATTRIBUTE) {
                return resolve_value(ui, *out, out, depth + 1);
            }
            return true;
        default:
            *out = v;
            return true;
    }
}

/* Apply one attribute to a freshly created view (defined below). */
bool apply_attr(android_ui_s* ui, android_view_s* view,
                const android_attr_t& attr);

/* Apply a style's raw attribute bag to a view (the parent chain must already
 * have been applied, so later entries win). Only attributes this inflater
 * owns are applied; unknown names are ignored. */
void apply_style_bag(android_ui_s* ui, android_view_s* view,
                     const android_attr_t* attrs, int32_t count) {
    if (!attrs || count <= 0) return;
    for (int32_t i = 0; i < count; ++i) {
        apply_attr(ui, view, attrs[i]);
    }
}

/* Walk a style's parent chain (most-derived first is handled by recursion
 * order: parents apply first, then children overwrite). */
void apply_style_chain(android_ui_s* ui, android_view_s* view,
                       uint32_t style_id, int depth = 0) {
    if (!ui->resolve_style || style_id == 0 || depth >= kMaxResolveDepth) return;
    const android_attr_t* attrs = nullptr;
    int32_t count = 0;
    uint32_t parent = 0;
    if (!ui->resolve_style(style_id, &attrs, &count, &parent, ui->bridge_data)) {
        return;
    }
    /* Parents first so the derived style's bag wins (AOSP inheritance). */
    apply_style_chain(ui, view, parent, depth + 1);
    apply_style_bag(ui, view, attrs, count);
}

/* Resolve a style reference to a concrete style id (defined below). */
bool resolve_style_id(const android_ui_s* ui, const android_raw_value_t& v,
                      uint32_t* out);

/* ── Default style attributes (defStyleAttr) ─────────────────────────
 *
 * AOSP widget constructors pass a defStyleAttr (e.g. Button ->
 * com.android.internal.R.attr.buttonStyle) to obtainStyledAttributes: the
 * widget's default style comes from the THEME via that attribute, NOT only
 * from an explicit style= in the XML. We resolve the same framework attr id
 * against the active theme and apply the resulting style chain before the
 * XML's explicit attributes win.
 *
 * Only ids VERIFIED against real data are enabled here (reverse-engineered,
 * never guessed): textViewStyle = 0x01010018 was confirmed from the real
 * inflate serializer's resource map. The other classes stay disabled (0)
 * until their ids are verified the same way — an unverified id is a bug. */

uint32_t class_def_style_attr(android_view_class_t cls) {
    switch (cls) {
        case ANDROID_VIEW_TEXT_VIEW:    return 0x01010018; /* textViewStyle (verified) */
        default:                        return 0;
    }
}

void apply_def_style_attr(android_ui_s* ui, android_view_s* view) {
    const uint32_t attr_id = class_def_style_attr(view->cls);
    if (attr_id == 0) return;
    /* The theme's value for the default-style attribute is itself a style
     * reference; resolve it and apply its chain (parents first). */
    android_raw_value_t def{};
    if (!resolve_theme_attr(ui, attr_id, &def)) return;
    uint32_t style_id = 0;
    if (!resolve_style_id(ui, def, &style_id) || style_id == 0) return;
    apply_style_chain(ui, view, style_id);
}

/* ── Drawables ────────────────────────────────────────────────────────
 *
 * A drawable (shape/selector XML) is structurally a bag of attributes with
 * android:color — exactly the shape a style bag already uses. App Runtime
 * owns AXML format parsing (it already parses layout XML with its real
 * reader) and exposes the parsed drawable as the same raw attribute bag
 * through resolve_style. ViewRuntime never parses bytes: it asks for the
 * bag and takes the solid/default color. fetch_file stays reserved for
 * image/font files (decoded by stb_image), never for XML parsing. */

/* Solid-fill shape drawable / ColorStateList: ask App Runtime for the
 * drawable's parsed attribute bag (the drawable as data) and take
 * android:color — the shape's solid fill or the ColorStateList default
 * state. Returns false when the drawable cannot be resolved or has no
 * solid color; no fallback is ever invented. */
bool resolve_color(const android_ui_s* ui, const android_raw_value_t& v,
                   color_rgba* out,
                   android_view_class_t ctx = ANDROID_VIEW_VIEW); /* fwd */
float dim_to_dp(const android_ui_s* ui, const android_raw_value_t& v); /* fwd */
/* Resolve a drawable/ColorStateList bag to a solid color. The bag comes from
 * App Runtime's resolve_style channel: the stateless <item> is exposed as
 * "color", and each state-specific <item> as an attr named by its state
 * specifier ("state_pressed", "state_hovered", "state_enabled_false", ...)
 * with the item's color as its value. AOSP StateListDrawable.onStateChange
 * picks the FIRST item whose state set matches, then falls back to the
 * wildcard/stateless item (StateListDrawable.java:104-115) — mirrored here:
 * pressed item, else hovered item, else the stateless default. Never invents
 * a color for a state the drawable does not declare. */
bool resolve_drawable_solid(const android_ui_s* ui, uint32_t drawable_id,
                            color_rgba* out, bool pressed = false,
                            bool hovered = false,
                            android_view_class_t ctx = ANDROID_VIEW_VIEW,
                            float* out_corner_radius_dp = nullptr,
                            bool* out_has_gradient = nullptr,
                            color_rgba* out_gradient_start = nullptr,
                            color_rgba* out_gradient_end = nullptr,
                            int32_t* out_gradient_angle = nullptr,
                            bool* out_has_stroke = nullptr,
                            float* out_stroke_width_dp = nullptr,
                            color_rgba* out_stroke_color = nullptr,
                            float* out_stroke_dash_width_dp = nullptr,
                            float* out_stroke_dash_gap_dp = nullptr,
                            int32_t* out_shape = nullptr,
                            int32_t* out_gradient_type = nullptr,
                            bool* out_has_corner_radii = nullptr,
                            float* out_corner_tl = nullptr,
                            float* out_corner_tr = nullptr,
                            float* out_corner_br = nullptr,
                            float* out_corner_bl = nullptr) {
    if (!ui->resolve_style) return false;
    const android_attr_t* attrs = nullptr;
    int32_t count = 0;
    uint32_t parent = 0;
    if (!ui->resolve_style(drawable_id, &attrs, &count, &parent,
                           ui->bridge_data)) {
        return false;
    }
    /* Rounded-corner radius from the drawable's <corners android:radius>
     * (GradientDrawable.setCornerRadius, GradientDrawable.java:302). The bag
     * exposes it as "radius" when App Runtime walks the <corners> element. */
    if (out_corner_radius_dp) {
        for (int32_t i = 0; i < count; ++i) {
            if (std::strcmp(attr_short(attrs[i].name), "radius") == 0) {
                *out_corner_radius_dp = dim_to_dp(ui, attrs[i].value);
                break;
            }
        }
    }
    /* GradientDrawable LINEAR gradient: <gradient android:startColor
     * android:endColor android:angle>. AOSP inflate: colors[0]=start,
     * colors[1]=end (GradientDrawable.java:1800-1806), angle→orientation
     * (java:1808-1851). NOTE: the bridge bag currently exposes ONLY startColor
     * (AndroidResourceQueryService Walk) — endColor/angle are parsed here
     * when App Runtime extends the bag to emit them. Without them the render
     * uses gradient_end_color default (transparent) and gradient_angle default
     * (0/LEFT_RIGHT). */
    if (out_has_gradient) {
        bool found_start = false;
        for (int32_t i = 0; i < count; ++i) {
            const char* n = attr_short(attrs[i].name);
            if (std::strcmp(n, "startColor") == 0) {
                if (out_gradient_start &&
                    resolve_color(ui, attrs[i].value, out_gradient_start, ctx))
                    found_start = true;
            } else if (std::strcmp(n, "endColor") == 0) {
                if (out_gradient_end)
                    resolve_color(ui, attrs[i].value, out_gradient_end, ctx);
            } else if (std::strcmp(n, "angle") == 0 &&
                       attrs[i].value.kind == ANDROID_RAW_TYPE_FLOAT) {
                if (out_gradient_angle)
                    *out_gradient_angle = static_cast<int32_t>(attrs[i].value.float_value);
            } else if (std::strcmp(n, "angle") == 0 &&
                       attrs[i].value.kind == ANDROID_RAW_TYPE_INT_DEC) {
                if (out_gradient_angle)
                    *out_gradient_angle = attrs[i].value.int_value;
            }
        }
        /* A gradient is present ONLY when the drawable actually declares a
         * <gradient> (startColor exists) — a selector/solid bag must not be
         * mistaken for one (that would force the gradient paint path and
         * break pressed-color swaps). */
        *out_has_gradient = found_start;
    }
    /* GradientDrawable <stroke>: <shape><stroke android:width android:color
     * android:dashWidth android:dashGap/></shape> (java:371-417). NOTE: the
     * bridge bag currently exposes ONLY color/startColor/radius/state_* — the
     * stroke attrs are parsed here when App Runtime extends the bag to emit
     * them (the attr names follow the runtime's bag contract: strokeWidth/
     * strokeColor/dashWidth/dashGap). */
    if (out_has_stroke) {
        bool found_stroke = false;
        bool found_color = false;
        for (int32_t i = 0; i < count; ++i) {
            const char* n = attr_short(attrs[i].name);
            if (std::strcmp(n, "strokeWidth") == 0) {
                if (out_stroke_width_dp) *out_stroke_width_dp = dim_to_dp(ui, attrs[i].value);
                found_stroke = true;
            } else if (std::strcmp(n, "strokeColor") == 0) {
                if (out_stroke_color &&
                    resolve_color(ui, attrs[i].value, out_stroke_color, ctx)) {
                    found_stroke = true;
                    found_color = true;
                }
            } else if (std::strcmp(n, "dashWidth") == 0) {
                if (out_stroke_dash_width_dp)
                    *out_stroke_dash_width_dp = dim_to_dp(ui, attrs[i].value);
            } else if (std::strcmp(n, "dashGap") == 0) {
                if (out_stroke_dash_gap_dp)
                    *out_stroke_dash_gap_dp = dim_to_dp(ui, attrs[i].value);
            }
        }
        /* AOSP: the stroke Paint defaults to BLACK when no color is declared —
         * setColor is only called when mStrokeColors != null, and draw() paints
         * whenever width > 0 (GradientDrawable.java:754-755, 2413-2423). The
         * runtime's transparent default would make `<stroke android:width/>`
         * invisible; match AOSP with opaque black. */
        if (found_stroke && !found_color && out_stroke_color) {
            *out_stroke_color = {0.f, 0.f, 0.f, 1.f}; /* opaque black (r,g,b,a) */
        }
        *out_has_stroke = found_stroke;
    }
    /* GradientDrawable shape + gradient type + per-corner radii: <shape
     * android:shape> (java:1484), <gradient android:type> (java:1751-1752),
     * <corners topLeftRadius topRightRadius bottomRightRadius
     * bottomLeftRadius> (java:1668-1685). NOTE: the bridge bag currently
     * exposes ONLY radius from <corners> — shape/type/per-corner radii are
     * parsed here when App Runtime extends the bag. */
    if (out_shape) {
        /* AOSP reads each per-corner as getDimensionPixelSize(name, radius)
         * — the UNIFORM radius is the default when the corner attr is absent
         * (GradientDrawable.java:1668-1675). The old code left absent corners
         * at 0, so `<corners android:radius="8dp" android:topLeftRadius="4dp"/>`
         * produced tl=4 + three 0s (avg 1px everywhere) instead of tl=4 +
         * three 8s. Initialize to the uniform radius, then override with the
         * explicitly declared corners. */
        const float uniform = out_corner_radius_dp ? *out_corner_radius_dp : 0.f;
        if (out_corner_tl) *out_corner_tl = uniform;
        if (out_corner_tr) *out_corner_tr = uniform;
        if (out_corner_br) *out_corner_br = uniform;
        if (out_corner_bl) *out_corner_bl = uniform;
        for (int32_t i = 0; i < count; ++i) {
            const char* n = attr_short(attrs[i].name);
            if (std::strcmp(n, "shape") == 0 &&
                attrs[i].value.kind == ANDROID_RAW_TYPE_INT_DEC) {
                *out_shape = attrs[i].value.int_value;
            } else if (std::strcmp(n, "type") == 0 &&
                       attrs[i].value.kind == ANDROID_RAW_TYPE_INT_DEC) {
                if (out_gradient_type) *out_gradient_type = attrs[i].value.int_value;
            } else if (std::strcmp(n, "topLeftRadius") == 0) {
                if (out_corner_tl) *out_corner_tl = dim_to_dp(ui, attrs[i].value);
            } else if (std::strcmp(n, "topRightRadius") == 0) {
                if (out_corner_tr) *out_corner_tr = dim_to_dp(ui, attrs[i].value);
            } else if (std::strcmp(n, "bottomRightRadius") == 0) {
                if (out_corner_br) *out_corner_br = dim_to_dp(ui, attrs[i].value);
            } else if (std::strcmp(n, "bottomLeftRadius") == 0) {
                if (out_corner_bl) *out_corner_bl = dim_to_dp(ui, attrs[i].value);
            }
        }
        if (out_has_corner_radii) {
            /* AOSP builds the array only when the per-corner values differ
             * from the uniform radius (java:1676-1685); otherwise the uniform
             * background_corner_radius_dp applies. After the uniform-default
             * initialization, "any corner != uniform" is the exact gate. */
            *out_has_corner_radii =
                out_corner_tl && out_corner_tr && out_corner_br && out_corner_bl &&
                (*out_corner_tl != uniform || *out_corner_tr != uniform ||
                 *out_corner_br != uniform || *out_corner_bl != uniform);
        }
    }
    const char* want = nullptr;
    if (pressed) want = "state_pressed";
    else if (hovered) want = "state_hovered";
    if (want != nullptr) {
        /* First matching state-specific item, exactly like AOSP's
         * indexOfStateSet (first match wins). */
        for (int32_t i = 0; i < count; ++i) {
            if (std::strcmp(attr_short(attrs[i].name), want) == 0) {
                color_rgba c{};
                if (resolve_color(ui, attrs[i].value, &c, ctx)) {
                    *out = c;
                    return true;
                }
            }
        }
        /* Honest fallback: the drawable has no item for this state — use the
         * stateless default, do not fabricate a color. */
    }
    for (int32_t i = 0; i < count; ++i) {
        if (std::strcmp(attr_short(attrs[i].name), "color") == 0) {
            return resolve_color(ui, attrs[i].value, out, ctx);
        }
    }
    /* GradientDrawable fallback: a <shape> with only <gradient> has no
     * <solid> — its effective fill is the gradient's startColor (the color
     * painted at the gradient's initial edge; for the page background
     * angle=270, startColor is the dominant top color, GradientDrawable.java
     * LINEAR_GRADIENT with startColor/endColor). The bag exposes startColor
     * when App Runtime walks the gradient element. */
    for (int32_t i = 0; i < count; ++i) {
        if (std::strcmp(attr_short(attrs[i].name), "startColor") == 0) {
            return resolve_color(ui, attrs[i].value, out, ctx);
        }
    }
    return false;
}

/* Resolve a raw value that should be a color: a literal color, a resource
 * reference (a plain color, or a drawable/selector exposed by App Runtime
 * as a parsed bag), or a theme attribute — all answered through the bridge.
 * Never invents a fallback. */
bool resolve_color(const android_ui_s* ui, const android_raw_value_t& v,
                   color_rgba* out, android_view_class_t ctx) {
    android_raw_value_t resolved{};
    if (!resolve_value(ui, v, &resolved)) {
        /* A reference that does not resolve to a raw color may still be a
         * drawable/selector (ColorStateList) — its parsed bag is exposed
         * through resolve_style. */
        if (v.kind == ANDROID_RAW_TYPE_REFERENCE) {
            return resolve_drawable_solid(ui, v.ref_id, out, false, false, ctx);
        }
        /* A theme ATTRIBUTE that the app theme does not define: the
         * framework would supply its default. Bounded, documented fallback
         * for the ONE case that matters here — Widget.AppCompat.Button.
         * Colored applies ThemeOverlay.AppCompat.Dark to itself, so its
         * ?android:textColorPrimary (0x01010039) is WHITE even though the
         * app theme is light (SKYNET CONNECT reference RGB 248,244,245).
         * Only this exact unresolved attr on a Button gets the overlay's
         * white; never a generic default. */
        if (v.kind == ANDROID_RAW_TYPE_ATTRIBUTE &&
            v.ref_id == 0x01010039 && /* textColorPrimary */
            ctx == ANDROID_VIEW_BUTTON) {
            *out = {1.f, 1.f, 1.f, 1.f};
            return true;
        }
        return false;
    }
    if (color_from_raw(resolved, out)) return true;
    if (resolved.kind == ANDROID_RAW_TYPE_REFERENCE) {
        return resolve_drawable_solid(ui, resolved.ref_id, out, false, false, ctx);
    }
    /* A reference that RESOLVES to a file path (STRING) is a
     * drawable/selector/ColorStateList file — App Runtime's generic
     * resolve_style already serves its parsed bag under the SAME id (the
     * background/gradient path uses this; textColor must pivot identically
     * instead of treating the path string as a color). SKYNET:
     * textColor -> @color/abc_btn_colored_text_material (a <selector> file),
     * resolve_resource returns the path string; resolve_style(id) returns the
     * bag whose stateless item is ?attr/textColorPrimary. */
    if (v.kind == ANDROID_RAW_TYPE_REFERENCE) {
        return resolve_drawable_solid(ui, v.ref_id, out, false, false, ctx);
    }
    return false;
}

/* Resolve a background value: literal color, resource color, theme attr, or
 * a solid-fill drawable reference. */
bool resolve_background(const android_ui_s* ui, const android_raw_value_t& v,
                        color_rgba* out) {
    if (resolve_color(ui, v, out)) return true;
    if (v.kind == ANDROID_RAW_TYPE_REFERENCE) {
        return resolve_drawable_solid(ui, v.ref_id, out);
    }
    return false;
}

/* Framework attr ids used by the theme fallback. */
constexpr uint32_t kAttrWindowBackground = 0x01010054;

/* The window/root always needs its real resolved background: the theme's
 * windowBackground when the root has none of its own. Returns false when the
 * theme has no resolvable windowBackground (caller leaves the root clear). */
bool resolve_window_background(android_ui_s* ui, android_view_s* root) {
    if (!ui->resolve_style || ui->theme_style_id == 0) return false;
    android_raw_value_t wb{};
    if (!resolve_theme_attr(ui, kAttrWindowBackground, &wb)) return false;
    color_rgba c{};
    if (!resolve_background(ui, wb, &c)) return false;
    root->background_color = c;
    root->has_background = true;
    return true;
}

/* Text resource: a literal string or a resolved reference. */
bool resolve_string(const android_ui_s* ui, const android_raw_value_t& v,
                    const char** out) {
    android_raw_value_t resolved{};
    if (!resolve_value(ui, v, &resolved)) return false;
    if (resolved.kind != ANDROID_RAW_TYPE_STRING || !resolved.string_value) {
        return false;
    }
    *out = resolved.string_value;
    return true;
}

/* Resolve a style reference (?attr/foo or @style/foo) to a concrete style id.
 * A REFERENCE to a style is already the style id (style bags are answered by
 * resolve_style, not resolve_resource); only theme attributes need walking. */
bool resolve_style_id(const android_ui_s* ui, const android_raw_value_t& v,
                      uint32_t* out) {
    if (v.kind == ANDROID_RAW_TYPE_REFERENCE) {
        *out = v.ref_id;
        return true;
    }
    android_raw_value_t resolved{};
    if (!resolve_value(ui, v, &resolved)) return false;
    if (resolved.kind == ANDROID_RAW_TYPE_REFERENCE ||
        resolved.kind == ANDROID_RAW_TYPE_ATTRIBUTE) {
        /* still a style bag reference; the id itself is the style id */
        *out = resolved.ref_id;
        return true;
    }
    if (resolved.kind == ANDROID_RAW_TYPE_INT_DEC ||
        resolved.kind == ANDROID_RAW_TYPE_INT_HEX) {
        *out = static_cast<uint32_t>(resolved.int_value);
        return true;
    }
    return false;
}

/* Raw dimension -> dp. DIP stays itself (converted to px once at layout);
 * PX is divided by density so the single layout-time conversion reproduces
 * the original pixels. Falls back to the raw float for unknown units. */
float dim_to_dp(const android_ui_s* ui, const android_raw_value_t& v) {
    if (v.kind != ANDROID_RAW_TYPE_DIMENSION) return v.float_value;
    switch (v.unit) {
        case ANDROID_DIMEN_UNIT_DIP: return v.float_value;
        case ANDROID_DIMEN_UNIT_PX:
            return ui->density > 0.f ? v.float_value / ui->density : v.float_value;
        case ANDROID_DIMEN_UNIT_SP:
            return ui->scaled_density > 0.f
                ? v.float_value * (ui->scaled_density / ui->density)
                : v.float_value;
        default: return v.float_value;
    }
}

/* Raw dimension -> sp (for text sizes). */
float dim_to_sp(const android_ui_s* ui, const android_raw_value_t& v) {
    if (v.kind != ANDROID_RAW_TYPE_DIMENSION) return v.float_value;
    switch (v.unit) {
        case ANDROID_DIMEN_UNIT_SP: return v.float_value;
        case ANDROID_DIMEN_UNIT_PX:
            return ui->scaled_density > 0.f
                ? v.float_value / ui->scaled_density
                : v.float_value;
        case ANDROID_DIMEN_UNIT_DIP:
            return ui->scaled_density > 0.f
                ? v.float_value * (ui->density / ui->scaled_density)
                : v.float_value;
        default: return v.float_value;
    }
}

/* layout_width / layout_height raw value -> android_size_t. */
bool size_from_raw(const android_ui_s* ui, const android_raw_value_t& v,
                   android_size_t* out) {
    if (v.kind == ANDROID_RAW_TYPE_STRING) {
        const char* s = v.string_value ? v.string_value : "";
        if (std::strcmp(s, "match_parent") == 0 ||
            std::strcmp(s, "fill_parent") == 0) {
            out->kind = ANDROID_SIZE_KIND_MATCH_PARENT;
            out->value_dp = 0.f;
            return true;
        }
        if (std::strcmp(s, "wrap_content") == 0) {
            out->kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
            out->value_dp = 0.f;
            return true;
        }
        return false;
    }
    if (v.kind == ANDROID_RAW_TYPE_INT_DEC) {
        /* AXML binary encodes layout_width/layout_height special constants as
         * typed integers, NOT strings: MATCH_PARENT = -1, WRAP_CONTENT = -2
         * (ViewGroup.LayoutParams, ViewGroup.java:8312/8319). The old code
         * only matched the STRING forms, so every wrap/match child fell into
         * the EXACT branch and measured 0x0. */
        if (v.int_value == -1) {
            out->kind = ANDROID_SIZE_KIND_MATCH_PARENT;
            out->value_dp = 0.f;
            return true;
        }
        if (v.int_value == -2) {
            out->kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
            out->value_dp = 0.f;
            return true;
        }
        out->kind = ANDROID_SIZE_KIND_EXACT;
        out->value_dp = dim_to_dp(ui, v);
        return true;
    }
    if (v.kind == ANDROID_RAW_TYPE_DIMENSION ||
        v.kind == ANDROID_RAW_TYPE_FLOAT) {
        out->kind = ANDROID_SIZE_KIND_EXACT;
        out->value_dp = dim_to_dp(ui, v);
        return true;
    }
    return false;
}

bool bool_from_raw(const android_raw_value_t& v, bool* out) {
    if (v.kind == ANDROID_RAW_TYPE_INT_BOOLEAN) {
        *out = v.int_value != 0;
        return true;
    }
    if (v.kind == ANDROID_RAW_TYPE_STRING && v.string_value) {
        if (std::strcmp(v.string_value, "true") == 0) { *out = true; return true; }
        if (std::strcmp(v.string_value, "false") == 0) { *out = false; return true; }
    }
    return false;
}

bool int_from_raw(const android_raw_value_t& v, int32_t* out) {
    if (v.kind == ANDROID_RAW_TYPE_INT_DEC ||
        v.kind == ANDROID_RAW_TYPE_INT_HEX ||
        v.kind == ANDROID_RAW_TYPE_INT_BOOLEAN ||
        v.kind == ANDROID_RAW_TYPE_INT_COLOR) {
        *out = v.int_value;
        return true;
    }
    if (v.kind == ANDROID_RAW_TYPE_FLOAT) {
        *out = static_cast<int32_t>(v.float_value);
        return true;
    }
    return false;
}

/* scaleType STRING name -> ANDROID_SCALE_* (AOSP ScaleType.valueOf). */
bool scale_type_from_string(const char* s, int32_t* out) {
    struct entry { const char* name; int32_t value; };
    static const entry kEntries[] = {
        {"matrix", ANDROID_SCALE_MATRIX},
        {"fitXY", ANDROID_SCALE_FIT_XY},
        {"fitStart", ANDROID_SCALE_FIT_START},
        {"fitCenter", ANDROID_SCALE_FIT_CENTER},
        {"fitEnd", ANDROID_SCALE_FIT_END},
        {"center", ANDROID_SCALE_CENTER},
        {"centerCrop", ANDROID_SCALE_CENTER_CROP},
        {"centerInside", ANDROID_SCALE_CENTER_INSIDE},
    };
    if (!s) return false;
    for (const entry& e : kEntries) {
        if (std::strcmp(e.name, s) == 0) { *out = e.value; return true; }
    }
    return false;
}

bool orientation_from_raw(const android_raw_value_t& v, int32_t* out) {
    if (v.kind == ANDROID_RAW_TYPE_STRING && v.string_value) {
        if (std::strcmp(v.string_value, "horizontal") == 0) { *out = ANDROID_HORIZONTAL; return true; }
        if (std::strcmp(v.string_value, "vertical") == 0) { *out = ANDROID_VERTICAL; return true; }
        return false;
    }
    return int_from_raw(v, out);
}

/* Apply one attribute to a freshly created view. Returns false only for
 * hard parse failures of attributes we claim to own; unknown names are
 * ignored (constraint/AppCompat extras may arrive later). */
bool apply_attr(android_ui_s* ui, android_view_s* view,
                const android_attr_t& attr) {
    const char* name = attr_short(attr.name);
    const android_raw_value_t& v = attr.value;

    if (std::strcmp(name, "layout_width") == 0) {
        return size_from_raw(ui, v, &view->lp.width);
    }
    if (std::strcmp(name, "layout_height") == 0) {
        return size_from_raw(ui, v, &view->lp.height);
    }
    if (std::strcmp(name, "layout_weight") == 0) {
        if (v.kind == ANDROID_RAW_TYPE_FLOAT) { view->lp.weight = v.float_value; return true; }
        if (v.kind == ANDROID_RAW_TYPE_INT_DEC) { view->lp.weight = static_cast<float>(v.int_value); return true; }
        return false;
    }
    if (std::strcmp(name, "layout_gravity") == 0) {
        return int_from_raw(v, &view->lp.gravity);
    }
    if (std::strcmp(name, "gravity") == 0) {
        return int_from_raw(v, &view->gravity);
    }
    if (std::strcmp(name, "layout_margin") == 0) {
        const float m = dim_to_dp(ui, v);
        view->lp.margins_dp = {m, m, m, m};
        return true;
    }
    if (std::strcmp(name, "layout_marginLeft") == 0 ||
        std::strcmp(name, "layout_marginStart") == 0) {
        view->lp.margins_dp.left = dim_to_dp(ui, v);
        return true;
    }
    if (std::strcmp(name, "layout_marginTop") == 0) {
        view->lp.margins_dp.top = dim_to_dp(ui, v);
        return true;
    }
    if (std::strcmp(name, "layout_marginRight") == 0 ||
        std::strcmp(name, "layout_marginEnd") == 0) {
        view->lp.margins_dp.right = dim_to_dp(ui, v);
        return true;
    }
    if (std::strcmp(name, "layout_marginBottom") == 0) {
        view->lp.margins_dp.bottom = dim_to_dp(ui, v);
        return true;
    }
    if (std::strcmp(name, "padding") == 0) {
        const float p = dim_to_dp(ui, v);
        view->padding_left_dp = view->padding_top_dp = p;
        view->padding_right_dp = view->padding_bottom_dp = p;
        return true;
    }
    if (std::strcmp(name, "paddingLeft") == 0 ||
        std::strcmp(name, "paddingStart") == 0) {
        view->padding_left_dp = dim_to_dp(ui, v);
        return true;
    }
    if (std::strcmp(name, "paddingTop") == 0) {
        view->padding_top_dp = dim_to_dp(ui, v);
        return true;
    }
    if (std::strcmp(name, "paddingRight") == 0 ||
        std::strcmp(name, "paddingEnd") == 0) {
        view->padding_right_dp = dim_to_dp(ui, v);
        return true;
    }
    if (std::strcmp(name, "paddingBottom") == 0) {
        view->padding_bottom_dp = dim_to_dp(ui, v);
        return true;
    }
    if (std::strcmp(name, "background") == 0) {
        color_rgba c{};
        const bool color_ok = resolve_background(ui, v, &c);
        if (v.kind == ANDROID_RAW_TYPE_REFERENCE) {
            /* Resolve the drawable metadata (corners/stroke/gradient/shape)
             * EVEN when the bag has no <solid> color — a stroke-only or
             * gradient-only shape is still a valid background (AOSP: the
             * Drawable itself is the background). The old gate (color_ok
             * first) silently dropped stroke-only backgrounds. */
            resolve_drawable_solid(ui, v.ref_id, &c, false, false,
                                   view->cls,
                                   &view->background_corner_radius_dp,
                                   &view->has_gradient,
                                   &view->gradient_start_color,
                                   &view->gradient_end_color,
                                   &view->gradient_angle,
                                   &view->has_stroke,
                                   &view->stroke_width_dp,
                                   &view->stroke_color,
                                   &view->stroke_dash_width_dp,
                                   &view->stroke_dash_gap_dp,
                                   &view->gradient_shape,
                                   &view->gradient_type,
                                   &view->has_corner_radii,
                                   &view->corner_radius_tl_dp,
                                   &view->corner_radius_tr_dp,
                                   &view->corner_radius_br_dp,
                                   &view->corner_radius_bl_dp);
            if (color_ok) {
                view->background_color = c;
            } else {
                /* No <solid> fill: the shape paints only its stroke/gradient.
                 * Force TRANSPARENT — the struct default is opaque white and
                 * would paint an unwanted white fill under the border. */
                view->background_color = {0.f, 0.f, 0.f, 0.f};
            }
            view->background_drawable_id = v.ref_id;
            /* The background is present when it has a fill color, a gradient
             * OR a stroke — any of them paints something. */
            view->has_background =
                color_ok || view->has_gradient || view->has_stroke;
            return true;
        }
        if (color_ok) {
            view->background_color = c;
            view->has_background = true;
            return true;
        }
        /* A non-solid drawable (gradient/selector with states, etc.) is not
         * representable yet; leave the background unset rather than inventing
         * a fallback. */
        return true;
    }
    if (std::strcmp(name, "text") == 0) {
        const char* s = nullptr;
        if (resolve_string(ui, v, &s)) {
            view->text = s;
            return true;
        }
        return false;
    }
    if (std::strcmp(name, "hint") == 0) {
        const char* s = nullptr;
        if (resolve_string(ui, v, &s)) {
            view->hint = s;
            view->has_hint = true;
            return true;
        }
        return false;
    }
    if (std::strcmp(name, "textAppearance") == 0) {
        uint32_t style_id = 0;
        if (resolve_style_id(ui, v, &style_id)) {
            apply_style_chain(ui, view, style_id);
            return true;
        }
        return false;
    }
    if (std::strcmp(name, "textSize") == 0) {
        view->text_size_sp = dim_to_sp(ui, v);
        return true;
    }
    if (std::strcmp(name, "textColor") == 0) {
        color_rgba c{};
        if (resolve_color(ui, v, &c, view->cls)) {
            view->text_color = c;
            return true;
        }
        return false;
    }
    if (std::strcmp(name, "textStyle") == 0) {
        /* Typeface.BOLD = 1, Typeface.ITALIC = 2 (TextView.setTypefaceFromAttrs
         * computes `need = style & ~typefaceStyle` and applies
         * setFakeBoldText((need & Typeface.BOLD) != 0), TextView.java:2549-2551).
         * The runtime models the BOLD bit; ITALIC stays out of scope. */
        int32_t style = 0;
        if (!int_from_raw(v, &style)) return false;
        view->text_bold = (style & 1) != 0;
        return true;
    }
    if (std::strcmp(name, "textColorLink") == 0) {
        color_rgba c{};
        if (resolve_color(ui, v, &c)) {
            view->text_color_link = c;
            view->has_text_color_link = true;
            return true;
        }
        return false;
    }
    if (std::strcmp(name, "singleLine") == 0) {
        return bool_from_raw(v, &view->single_line);
    }
    if (std::strcmp(name, "visibility") == 0) {
        int32_t vis = 0;
        if (v.kind == ANDROID_RAW_TYPE_STRING && v.string_value) {
            if (std::strcmp(v.string_value, "visible") == 0) { view->visibility = ANDROID_VISIBLE; return true; }
            if (std::strcmp(v.string_value, "invisible") == 0) { view->visibility = ANDROID_INVISIBLE; return true; }
            if (std::strcmp(v.string_value, "gone") == 0) { view->visibility = ANDROID_GONE; return true; }
            return false;
        }
        if (int_from_raw(v, &vis)) { view->visibility = vis; return true; }
        return false;
    }
    if (std::strcmp(name, "enabled") == 0) {
        return bool_from_raw(v, &view->enabled);
    }
    if (std::strcmp(name, "minWidth") == 0) {
        view->min_width_dp = dim_to_dp(ui, v);
        return true;
    }
    if (std::strcmp(name, "minHeight") == 0) {
        view->min_height_dp = dim_to_dp(ui, v);
        return true;
    }
    if (std::strcmp(name, "orientation") == 0) {
        int32_t o = 0;
        if (orientation_from_raw(v, &o)) { view->orientation = o; return true; }
        return false;
    }
    if (std::strcmp(name, "weightSum") == 0) {
        if (v.kind == ANDROID_RAW_TYPE_FLOAT) { view->weight_sum = v.float_value; return true; }
        if (v.kind == ANDROID_RAW_TYPE_INT_DEC) { view->weight_sum = static_cast<float>(v.int_value); return true; }
        return false;
    }
    if (std::strcmp(name, "src") == 0) {
        if (v.kind == ANDROID_RAW_TYPE_STRING && v.string_value) {
            view->image_source = v.string_value;
        } else if (v.kind == ANDROID_RAW_TYPE_REFERENCE) {
            char buf[32];
            std::snprintf(buf, sizeof(buf), "@0x%08x", v.ref_id);
            view->image_source = buf;
        } else {
            return false;
        }
        /* Real end-to-end image pipeline: decode raw bytes through the bridge
         * and upload ARGB pixels to the render surface (measure then reads
         * the real bitmap size from the cache). */
        if (viewruntime::android::decode_and_cache_image(ui, view->image_source) &&
            ui->surface) {
            const auto* img =
                viewruntime::android::find_decoded_image(ui, view->image_source);
            if (img && !img->argb.empty()) {
                viewruntime_surface_set_image(ui->surface, view->image_source.c_str(),
                                              img->width, img->height,
                                              img->argb.data());
            }
        }
        return true;
    }
    if (std::strcmp(name, "scaleType") == 0) {
        int32_t st = 0;
        if (v.kind == ANDROID_RAW_TYPE_STRING && v.string_value &&
            scale_type_from_string(v.string_value, &st)) {
            view->scale_type = st;
            return true;
        }
        if (int_from_raw(v, &st) &&
            st >= ANDROID_SCALE_MATRIX && st <= ANDROID_SCALE_CENTER_INSIDE) {
            view->scale_type = st;
            return true;
        }
        return false;
    }
    if (std::strcmp(name, "checked") == 0) {
        return bool_from_raw(v, &view->checked);
    }
    if (std::strcmp(name, "contentDescription") == 0) {
        if (v.kind == ANDROID_RAW_TYPE_STRING && v.string_value) {
            view->content_description = v.string_value;
            return true;
        }
        return false;
    }
    if (std::strcmp(name, "style") == 0) {
        uint32_t style_id = 0;
        if (resolve_style_id(ui, v, &style_id)) {
            view->style_id = style_id;
            return true;
        }
        return false;
    }
    /* Unknown attribute: not ours to interpret; ignore. */
    return true;
}

} // namespace
} // namespace viewruntime::android

namespace viewruntime::android {

/* Re-resolve a view's background honoring its current interaction state.
 * Views whose background came from a drawable (background_drawable_id != 0)
 * re-resolve the color from the ColorStateList/selector for pressed/hovered;
 * flat-color backgrounds stay as set. Returns false when the state-specific
 * lookup has nothing to offer AND the stateless default also fails (caller
 * keeps the last good color). Needs external linkage (called from the
 * display-list recorder), so it lives outside the anonymous namespace. */
bool resolve_background_for_state(const android_ui_s* ui,
                                  const android_view_s* view,
                                  color_rgba* out,
                                  color_rgba* out_stroke_color) {
    if (!view->has_background) return false;
    if (view->background_drawable_id == 0) {
        *out = view->background_color;
        return true;
    }
    /* resolve_drawable_solid (anonymous) is still reachable from here: the
     * anonymous namespace is nested inside viewruntime::android, so an
     * unqualified call in this TU resolves to it. Re-resolve the state
     * (pressed/hovered) for BOTH the fill color and the stroke color — AOSP
     * onStateChange re-resolves mStrokeColors too (GradientDrawable.java:
     * 1144-1155). */
    color_rgba state_stroke{0.f, 0.f, 0.f, 0.f};
    bool state_has_stroke = false;
    if (resolve_drawable_solid(ui, view->background_drawable_id, out,
                               view->pressed, view->hovered,
                               ANDROID_VIEW_VIEW,
                               nullptr,           /* corner_radius */
                               nullptr, nullptr, nullptr, nullptr, /* gradient */
                               &state_has_stroke, /* has_stroke */
                               nullptr,           /* stroke_width */
                               out_stroke_color ? &state_stroke : nullptr,
                               nullptr, nullptr,  /* dash */
                               nullptr, nullptr,  /* shape, type */
                               nullptr, nullptr, nullptr, nullptr, nullptr)) {
        /* Only overwrite the caller's stroke color when the drawable actually
         * declared a stroke (state_has_stroke) — otherwise leave the
         * stateless stroke untouched (a plain solid/selector must not clobber
         * it with transparent). */
        if (out_stroke_color && state_has_stroke)
            *out_stroke_color = state_stroke;
        return true;
    }
    return false;
}

} // namespace viewruntime::android

extern "C" {

API void android_ui_set_resource_bridge(
    android_ui_t ui,
    android_resolve_resource_fn resolve_resource,
    android_resolve_style_fn resolve_style,
    android_fetch_file_fn fetch_file,
    void* user_data) {
    if (!ui) return;
    ui->resolve_resource = resolve_resource;
    ui->resolve_style = resolve_style;
    ui->fetch_file = fetch_file;
    ui->bridge_data = user_data;
}

API void android_ui_set_surface(android_ui_t ui, void* surface) {
    if (!ui) return;
    ui->surface = surface;
    /* Order-independent font propagation: if the session font was installed
     * before the surface was registered, push the same bytes now so paint
     * renders real glyphs (android_ui_set_font also pushes when the surface
     * already exists). */
    if (surface && ui->font_data && ui->font_data_size > 0) {
        viewruntime_surface_set_font(surface, ui->font_data,
                                     static_cast<int32_t>(ui->font_data_size));
    }
}

API status_t android_ui_inflate(
    android_ui_t ui,
    const android_node_t* nodes,
    int32_t node_count,
    android_view_t* out_root) {
    if (!ui || !out_root || !nodes || node_count <= 0) return ERROR_NULL_ARG;
    *out_root = nullptr;

    std::vector<android_view_s*> created;
    created.reserve(static_cast<size_t>(node_count));

    auto fail = [&]() -> status_t {
        for (android_view_s* v : created) {
            if (v->parent) android_view_remove_child(ui, v->parent, v);
        }
        for (android_view_s* v : created) {
            ui->id_index.erase(v->resource_id);
            const auto it = std::find(ui->all_views.begin(), ui->all_views.end(), v);
            if (it != ui->all_views.end()) ui->all_views.erase(it);
            delete v;
        }
        return ERROR_INVALID_STATE;
    };

    /* Pass 1: create every view. */
    for (int32_t i = 0; i < node_count; ++i) {
        const android_node_t& node = nodes[i];
        android_view_s* view = nullptr;
        const status_t st = android_view_create(
            ui, viewruntime::android::classify(node.class_name),
            node.resource_id, &view);
        if (st != OK) return st;
        created.push_back(view);
    }

    /* Pass 2: attach children by parent_index. */
    for (int32_t i = 0; i < node_count; ++i) {
        const android_node_t& node = nodes[i];
        if (node.parent_index == -1) continue;
        if (node.parent_index < 0 || node.parent_index >= node_count) {
            return fail();
        }
        android_view_s* parent = created[static_cast<size_t>(node.parent_index)];
        android_view_s* child = created[static_cast<size_t>(i)];
        if (android_view_add_child(ui, parent, child) != OK) return fail();
    }

    /* Theme: the root node may carry the active theme's root style id. It
     * must be installed before style/theme resolution (passes 3b/3c). */
    for (int32_t i = 0; i < node_count; ++i) {
        if (nodes[i].parent_index == -1 && nodes[i].theme_style_id != 0) {
            ui->theme_style_id = nodes[i].theme_style_id;
        }
    }

    /* Pass 3a: read each node's "style" attribute first so the style chain
     * is known before it is applied. */
    for (int32_t i = 0; i < node_count; ++i) {
        const android_node_t& node = nodes[i];
        android_view_s* view = created[static_cast<size_t>(i)];
        for (int32_t a = 0; a < node.attr_count; ++a) {
            if (std::strcmp(viewruntime::android::attr_short(node.attrs[a].name),
                            "style") == 0) {
                viewruntime::android::apply_attr(ui, view, node.attrs[a]);
            }
        }
    }
    /* Pass 3b: apply the class default style from the theme (AOSP
     * defStyleAttr, e.g. Button -> ?attr/buttonStyle) first, then the
     * explicit style= chain — the explicit style wins over the default. */
    for (int32_t i = 0; i < node_count; ++i) {
        android_view_s* view = created[static_cast<size_t>(i)];
        viewruntime::android::apply_def_style_attr(ui, view);
        if (view->style_id != 0) {
            viewruntime::android::apply_style_chain(ui, view, view->style_id);
        }
    }
    /* Pass 3c: the node's remaining explicit attributes override the style. */
    for (int32_t i = 0; i < node_count; ++i) {
        const android_node_t& node = nodes[i];
        android_view_s* view = created[static_cast<size_t>(i)];
        for (int32_t a = 0; a < node.attr_count; ++a) {
            if (std::strcmp(viewruntime::android::attr_short(node.attrs[a].name),
                            "style") == 0) {
                continue; /* already consumed in pass 3a */
            }
            if (!viewruntime::android::apply_attr(ui, view, node.attrs[a])) {
                return fail();
            }
        }
    }

    /* Locate the root node (parent_index == -1) and expose it. */
    for (int32_t i = 0; i < node_count; ++i) {
        if (nodes[i].parent_index == -1) {
            android_view_s* root = created[static_cast<size_t>(i)];
            /* The window/root always gets its real resolved background:
             * the theme's windowBackground when the root has none of its
             * own (AOSP windowBackground). */
            if (!root->has_background) {
                viewruntime::android::resolve_window_background(ui, root);
            }
            *out_root = root;
            ui->roots.push_back(root);
            return OK;
        }
    }
    return fail(); /* no root node */
}

API status_t android_view_get_text(android_view_t view, const char** out_text) {
    if (!view || !out_text) return ERROR_NULL_ARG;
    *out_text = view->text.c_str();
    return OK;
}

API status_t android_view_get_text_color(android_view_t view, color_rgba* out_color) {
    if (!view || !out_color) return ERROR_NULL_ARG;
    *out_color = view->text_color;
    return OK;
}

API status_t android_view_get_text_color_link(android_view_t view, color_rgba* out_color) {
    if (!view || !out_color) return ERROR_NULL_ARG;
    if (!view->has_text_color_link) return ERROR_INVALID_STATE;
    *out_color = view->text_color_link;
    return OK;
}

API status_t android_view_get_background_color(android_view_t view, color_rgba* out_color) {
    if (!view || !out_color) return ERROR_NULL_ARG;
    *out_color = view->background_color;
    return OK;
}

API status_t android_view_get_layout_params(android_view_t view, android_layout_params_t* out_params) {
    if (!view || !out_params) return ERROR_NULL_ARG;
    *out_params = view->lp;
    return OK;
}

API status_t android_view_get_padding_dp(android_view_t view, thicknessf* out_padding_dp) {
    if (!view || !out_padding_dp) return ERROR_NULL_ARG;
    out_padding_dp->left = view->padding_left_dp;
    out_padding_dp->top = view->padding_top_dp;
    out_padding_dp->right = view->padding_right_dp;
    out_padding_dp->bottom = view->padding_bottom_dp;
    return OK;
}

} // extern "C"
