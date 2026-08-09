#pragma once
#include <viewruntime/viewruntime.h>
#include <viewruntime/android.h>

#include <string>
#include <vector>
#include <unordered_map>

/* Internal Android view-model state. The public ABI exposes only opaque
 * handles; every layout/measure decision lives here. */

namespace viewruntime::android::constraint {
struct ConstraintWidget;
}

struct android_view_s {
    android_ui_s* ui = nullptr;
    android_view_class_t cls = ANDROID_VIEW_VIEW;
    int32_t resource_id = 0;
    android_view_s* parent = nullptr;
    std::vector<android_view_s*> children;

    android_layout_params_t lp{};
    int32_t gravity = 0;            /* container gravity (LinearLayout/FrameLayout) */
    int32_t visibility = ANDROID_VISIBLE;
    bool enabled = true;

    /* LinearLayout advanced (AOSP LinearLayout fields). */
    int32_t layout_direction = ANDROID_LAYOUT_DIRECTION_LTR;
    bool use_largest_child = false;         /* measureWithLargestChild */
    int32_t show_dividers = ANDROID_SHOW_DIVIDER_NONE;
    float divider_thickness_px = 0.f;       /* intrinsic size along the main axis */
    float divider_padding_px = 0.f;
    color_rgba linear_divider_color{0.f, 0.f, 0.f, 0.f};
    bool use_default_margins = false;       /* useDefaultMargins (pre-2.0 API) */
    /* Resolved divider bounds for rendering (filled by the linear layout pass). */
    std::vector<rectf> divider_rects;

    /* RelativeLayout rules (AOSP RelativeLayout.LayoutParams.mRules): indexed
     * by android_relative_rule_t; value is the target resource id, TRUE(-1)
     * for parent rules, or 0 when unset. */
    int32_t relative_rules[ANDROID_RELATIVE_VERB_COUNT] = {};
    bool relative_align_with_parent = false;
    /* Resolved bounds during the RelativeLayout measure pass (AOSP
     * LayoutParams.mLeft/mRight/mTop/mBottom, VALUE_NOT_SET when free). */
    float rl_left = 0.f, rl_right = 0.f, rl_top = 0.f, rl_bottom = 0.f;

    /* Temporary solver widget during ConstraintLayout layout; owned by the
     * layout pass and freed there (null otherwise). */
    viewruntime::android::constraint::ConstraintWidget* constraint_widget = nullptr;

    /* Barrier (virtual helper child of a ConstraintLayout). */
    std::vector<int32_t> barrier_references;
    int32_t barrier_type = 0;          /* ANDROID_BARRIER_* */
    float barrier_margin_dp = 0.f;
    bool barrier_allows_gone = true;

    color_rgba background_color{1.f, 1.f, 1.f, 1.f};
    bool has_background = false;

    /* Style/theme: raw style id the view was inflated with (0 = none); the
     * parent chain is walked through the resource bridge at resolution time,
     * never cached here (the resolved state lives in the fields above). */
    uint32_t style_id = 0;

    float padding_left_dp = 0.f, padding_top_dp = 0.f;
    float padding_right_dp = 0.f, padding_bottom_dp = 0.f;
    float min_width_dp = 0.f, min_height_dp = 0.f;

    std::string content_description;
    std::string click_handler;

    rectf bounds{0, 0, 0, 0};
    android_measured_size_t measured{0, 0};
    float measured_baseline = -1.f; /* -1 => no baseline (matches View.getBaseline) */

    int32_t orientation = ANDROID_VERTICAL;
    bool baseline_aligned = true;          /* LinearLayout: align child baselines */
    float weight_sum = 0.f;                /* 0 => computed from children */
    float baseline_ascent[4] = {-1.f, -1.f, -1.f, -1.f};   /* per vertical-gravity bucket */
    float baseline_descent[4] = {-1.f, -1.f, -1.f, -1.f};

    /* TextView family */
    std::string text;
    std::string hint;
    bool has_hint = false;
    float text_size_sp = 16.f;
    color_rgba text_color{1.f, 0.125f, 0.125f, 0.125f};
    bool single_line = false;
    int32_t text_gravity = 0;

    /* ImageView */
    std::string image_source;
    int32_t scale_type = ANDROID_SCALE_FIT_CENTER;
    bool adjust_view_bounds = false;
    float max_width_dp = 0.f;  /* 0 = unbounded (AOSP Integer.MAX_VALUE) */
    float max_height_dp = 0.f;
    /* Resolved draw geometry (AOSP configureBounds): the intrinsic image is
     * mapped from image_src_rect into image_dst_rect, clipped to the view box. */
    rectf image_src_rect{};
    rectf image_dst_rect{};
    bool image_has_geometry = false;

    /* CheckBox / RadioButton */
    bool checked = false;

    /* ProgressBar */
    int32_t progress_min = 0, progress_max = 100, progress_value = 0;
    color_rgba track_color{1.f, 0.85f, 0.85f, 0.85f};
    color_rgba progress_color{1.f, 0.20f, 0.55f, 0.95f};

    /* GridLayout (child specs; the container keeps counts + orientation) */
    struct grid_spec_s {
        int start = ANDROID_GRID_UNDEFINED;
        int size = 1;
        int alignment = 0; /* grid alignment kind, see grid_layout.cpp */
        float weight = 0.f;
        bool start_defined() const { return start != ANDROID_GRID_UNDEFINED; }
        int end() const { return start + size; }
    };
    grid_spec_s grid_row, grid_column;
    int grid_row_count = ANDROID_GRID_UNDEFINED;
    int grid_column_count = ANDROID_GRID_UNDEFINED;

    /* ScrollView / ListView / RecyclerView */
    float scroll_x = 0.f, scroll_y = 0.f;
    scroll_metrics_t scroll_metrics{};

    /* ListView divider (Material default: 1dp, colorListDivider ~ black 12%) */
    float divider_height_dp = 1.f;
    color_rgba divider_color{0.f, 0.f, 0.f, 0.12f};
    bool divider_enabled = true;
};

struct android_ui_s {
    float density = 1.f;
    float scaled_density = 1.f;

    android_text_measurer_fn text_measurer = nullptr;
    void* text_measurer_data = nullptr;
    /* Real font measurement (stb_truetype): opaque face + owned TTF bytes. */
    void* font_face = nullptr;
    uint8_t* font_data = nullptr;
    size_t font_data_size = 0;
    android_image_dimensions_fn image_dimensions = nullptr;
    void* image_dimensions_data = nullptr;

    /* Phase 2 resource bridge (App Runtime as provider). */
    android_resolve_resource_fn resolve_resource = nullptr;
    android_resolve_style_fn resolve_style = nullptr;
    android_fetch_file_fn fetch_file = nullptr;
    void* bridge_data = nullptr;
    /* Active theme's root style id (from the last inflate's root node). */
    uint32_t theme_style_id = 0;

    /* Render surface for decoded-image uploads (host registers it). */
    void* surface = nullptr;

    /* Decoded image cache: source key -> owned ARGB8888 pixels. */
    struct DecodedImage {
        int width = 0;
        int height = 0;
        std::vector<uint8_t> argb; /* straight ARGB8888, row-major */
    };
    std::unordered_map<std::string, DecodedImage> decoded_images;

    std::vector<android_view_s*> roots;
    std::vector<android_view_s*> all_views;
    std::unordered_map<int32_t, android_view_s*> id_index;
};

namespace viewruntime::android {

/* Defined in android_measure_layout.cpp. */
android_measured_size_t measure_view(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui);
void layout_view(android_view_s* view, float x, float y, float w, float h,
                 const android_ui_s* ui);

/* Defined in stb_text_measurer.cpp. */
void android_ui_release_font(android_ui_s* ui);

inline float dp(const android_ui_s* ui, float v) { return v * ui->density; }
inline float sp(const android_ui_s* ui, float v) { return v * ui->scaled_density; }

inline float resolve_size(float desired, android_measure_spec_t spec) {
    switch (spec.mode) {
        case ANDROID_MEASURE_EXACTLY: return spec.size;
        case ANDROID_MEASURE_AT_MOST: return desired < spec.size ? desired : spec.size;
        default: return desired;
    }
}

inline float margin_h(const android_layout_params_t& lp, const android_ui_s* ui) {
    return dp(ui, lp.margins_dp.left) + dp(ui, lp.margins_dp.right);
}
inline float margin_v(const android_layout_params_t& lp, const android_ui_s* ui) {
    return dp(ui, lp.margins_dp.top) + dp(ui, lp.margins_dp.bottom);
}
inline float padding_h(const android_view_s* v, const android_ui_s* ui) {
    return dp(ui, v->padding_left_dp) + dp(ui, v->padding_right_dp);
}
inline float padding_v(const android_view_s* v, const android_ui_s* ui) {
    return dp(ui, v->padding_top_dp) + dp(ui, v->padding_bottom_dp);
}

inline bool gravity_has(int32_t gravity, int32_t flags) {
    return (gravity & flags) == flags;
}

/* LTR normalization: START resolves to LEFT and END to RIGHT, exactly like
 * AOSP View.getLayoutDirection applied to gravity (RELATIVE_LAYOUT_DIRECTION
 * bits are cleared after resolution). Non-relative bits pass through. Uses
 * gravity_has so a plain LEFT (0x3) never matches END (0x00800005) through
 * their shared low bit. */
inline int32_t gravity_normalize_ltr(int32_t gravity) {
    int32_t g = gravity & ~ANDROID_GRAVITY_RELATIVE_LAYOUT_DIRECTION;
    if (gravity_has(gravity, ANDROID_GRAVITY_END)) g |= ANDROID_GRAVITY_RIGHT;
    return g;
}

inline bool is_group(android_view_class_t cls) {
    switch (cls) {
        case ANDROID_VIEW_LINEAR_LAYOUT:
        case ANDROID_VIEW_FRAME_LAYOUT:
        case ANDROID_VIEW_RELATIVE_LAYOUT:
        case ANDROID_VIEW_SCROLL_VIEW:
        case ANDROID_VIEW_GRID_LAYOUT:
        case ANDROID_VIEW_LIST_VIEW:
        case ANDROID_VIEW_RECYCLER_VIEW:
        case ANDROID_VIEW_CONSTRAINT_LAYOUT:
            return true;
        default:
            return false;
    }
}

inline bool is_text_like(android_view_class_t cls) {
    switch (cls) {
        case ANDROID_VIEW_TEXT_VIEW:
        case ANDROID_VIEW_BUTTON:
        case ANDROID_VIEW_EDIT_TEXT:
        case ANDROID_VIEW_CHECK_BOX:
        case ANDROID_VIEW_RADIO_BUTTON:
            return true;
        default:
            return false;
    }
}

/* Defined in stb_text_measurer.cpp. */
void android_ui_release_font(android_ui_s* ui);

/* ── ViewGroup measure helpers ─────────────────────────────────────── */

/* Defined in android_grid_layout.cpp. */
android_measured_size_t measure_grid(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui);
void layout_grid(android_view_s* view, float x, float y, float w, float h,
                 const android_ui_s* ui);

/* Defined in android_list.cpp. */
android_measured_size_t measure_list(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui);
void layout_list(android_view_s* view, float x, float y, float w, float h,
                 const android_ui_s* ui);

/* Faithful port of android.view.ViewGroup.getChildMeasureSpec: the child's
 * dimension is resolved against the parent's spec after subtracting padding
 * and used space (margins). EXACTLY stays exact; MATCH_PARENT inherits the
 * parent mode; WRAP_CONTENT degrades to AT_MOST under a bounded parent and
 * stays unbounded under UNSPECIFIED. */
inline android_measure_spec_t get_child_measure_spec(
    android_measure_spec_t parent_spec, float padding_used,
    android_size_t child_dim, const android_ui_s* ui) {
    const float size = std::max(0.f, parent_spec.size - padding_used);
    const int32_t mode = parent_spec.mode;
    if (child_dim.kind == ANDROID_SIZE_KIND_EXACT) {
        return {dp(ui, child_dim.value_dp), ANDROID_MEASURE_EXACTLY};
    }
    if (child_dim.kind == ANDROID_SIZE_KIND_MATCH_PARENT) {
        switch (mode) {
            case ANDROID_MEASURE_AT_MOST:
                return {size, ANDROID_MEASURE_AT_MOST};
            case ANDROID_MEASURE_UNSPECIFIED:
                return {size, ANDROID_MEASURE_UNSPECIFIED};
            default:
                return {size, ANDROID_MEASURE_EXACTLY};
        }
    }
    /* WRAP_CONTENT */
    switch (mode) {
        case ANDROID_MEASURE_UNSPECIFIED:
            return {0.f, ANDROID_MEASURE_UNSPECIFIED};
        default:
            return {size, ANDROID_MEASURE_AT_MOST};
    }
}

/* ViewGroup.measureChildWithMargins: the shared primitive every container uses
 * to measure a direct child. */
inline android_measured_size_t measure_child_with_margins(
    const android_view_s* parent, android_view_s* child,
    android_measure_spec_t spec_w, android_measure_spec_t spec_h,
    const android_ui_s* ui) {
    const float used_w = margin_h(child->lp, ui);
    const float used_h = margin_v(child->lp, ui);
    const android_measure_spec_t cw =
        get_child_measure_spec(spec_w, used_w, child->lp.width, ui);
    const android_measure_spec_t ch =
        get_child_measure_spec(spec_h, used_h, child->lp.height, ui);
    child->measured = measure_view(child, cw, ch, ui);
    return child->measured;
}

/* Text measurement through the host callback; missing callback degrades to a
 * deterministic monospace-ish approximation so the core never crashes. */
android_text_metrics_t measure_text(
    const android_ui_s* ui, const char* text, float size_px, float max_width);

/* Defined in android_image_decode.cpp (stb_image PNG/JPEG decode). */
bool decode_and_cache_image(android_ui_s* ui, const std::string& source);
bool image_dimensions_from_cache(const android_ui_s* ui,
                                 const std::string& source,
                                 float* out_w, float* out_h);
const android_ui_s::DecodedImage* find_decoded_image(
    const android_ui_s* ui, const std::string& source);

} // namespace viewruntime::android
