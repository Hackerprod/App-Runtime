#pragma once
#include <viewruntime/viewruntime.h>
#include <viewruntime/android.h>

#include <string>
#include <vector>
#include <unordered_map>
#include <mutex>

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

    /* gravity = -1 (ANDROID_GRAVITY_UNSPECIFIED): the AOSP LayoutParams
     * default (LinearLayout.LayoutParams.gravity = -1, FrameLayout
     * UNSPECIFIED_GRAVITY = -1), NOT Gravity.NO_GRAVITY (0). Views inflated
     * without android:layout_gravity keep this sentinel and inherit the
     * container's gravity at layout time. */
    android_layout_params_t lp{
        android_size_t{},              /* width */
        android_size_t{},              /* height */
        thicknessf{},                  /* margins_dp */
        ANDROID_GRAVITY_UNSPECIFIED,   /* gravity = -1 */
        0.f,                           /* weight */
        {}                             /* constraint */
    };
    int32_t gravity = 0;            /* container gravity (LinearLayout/FrameLayout) */
    int32_t visibility = ANDROID_VISIBLE;
    bool enabled = true;
    /* View.mViewFlags CLICKABLE (View.java:7868 setOnClickListener sets it):
     * a clickable view consumes touch and performs click on UP. The host sets
     * this when a guest listener (programmatic or XML onClick) is attached. */
    bool clickable = false;

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
    /* AOSP LayoutParams.mInitialRules: the immutable snapshot of the rules as
     * inflated. resolveRules re-copies from it before every resolution
     * (RelativeLayout.java:1553), so START/END verbs are re-resolved from the
     * original rules each pass and the live mRules array never back-feeds the
     * resolution. Kept in sync by android_view_set_relative_rule. */
    int32_t relative_rules_initial[ANDROID_RELATIVE_VERB_COUNT] = {};
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
    /* Drawable resource id the background came from (0 = flat color set
     * directly). When non-zero, the render pass re-resolves the color from
     * the drawable's ColorStateList/selector honoring pressed/hovered. */
    uint32_t background_drawable_id = 0;
    /* Rounded-corner radius (dp) from the drawable's <corners android:radius>
     * (GradientDrawable.setCornerRadius, GradientDrawable.java:302). 0 = square.
     * The render clamps it to min(radius, min(w,h)*0.5) like AOSP
     * (GradientDrawable.java:823-825). */
    float background_corner_radius_dp = 0.f;
    /* GradientDrawable LINEAR gradient (GradientDrawable.java:1302-1340):
     * startColor/endColor interpolated along the orientation axis given by
     * <gradient android:angle> (java:1822-1851). angle 270 = TOP_BOTTOM
     * (vertical). has_gradient is set when the drawable declares a gradient;
     * the render then interpolates instead of using a flat color. */
    bool has_gradient = false;
    color_rgba gradient_start_color{1.f, 1.f, 1.f, 1.f};
    /* AOSP default endColor is TRANSPARENT (0): inflate reads
     * android:endColor with default 0 (GradientDrawable.java:1758-1766,
     * mGradientColors[1] = valueOf(endColor) where endColor=0 → transparent).
     * A gradient with only startColor fades start → transparent, never white. */
    color_rgba gradient_end_color{0.f, 0.f, 0.f, 0.f};
    /* AOSP default angle when <gradient android:angle> is absent: st.mAngle
     * defaults to 0 → LEFT_RIGHT (GradientDrawable.java:1808 reads
     * a.getFloat(angle, st.mAngle) with st.mAngle=0, java:2012; orientation
     * 0 = LEFT_RIGHT java:1824-1825). The render wraps %360. */
    int32_t gradient_angle = 0; /* degrees; 0 = LEFT_RIGHT */
    /* GradientDrawable <stroke android:width android:color> (java:371-417):
     * an optional border stroked over the same rect as the fill, width px,
     * optionally dashed (dashWidth/dashGap). has_stroke is set when declared;
     * dash 0 = solid border. */
    bool has_stroke = false;
    float stroke_width_dp = 0.f;
    color_rgba stroke_color{0.f, 0.f, 0.f, 0.f};
    float stroke_dash_width_dp = 0.f; /* 0 = no dash (solid) */
    float stroke_dash_gap_dp = 0.f;
    /* GradientDrawable shape (java:111-126): RECTANGLE=0, OVAL=1, LINE=2,
     * RING=3. The <shape android:shape> attr; default RECTANGLE (java:1484). */
    int32_t gradient_shape = 0; /* ANDROID_SHAPE_RECTANGLE */
    /* Gradient type (java:136-146): LINEAR=0, RADIAL=1, SWEEP=2. The
     * <gradient android:type> attr; default LINEAR (java:1800-1806 two colors
     * = linear). RADIAL needs centerX/Y + gradientRadius; SWEEP is a full
     * rotation — both rare in the APKs; LINEAR is the common case. */
    int32_t gradient_type = 0; /* ANDROID_GRADIENT_LINEAR */
    /* Per-corner radii (java:1668-1685): when the <corners> declares
     * topLeft/topRight/bottomRight/bottomLeft radii differing from the uniform
     * radius, AOSP builds a radius array (clockwise, 2 values per corner).
     * 0 = use the uniform background_corner_radius_dp. */
    bool has_corner_radii = false;
    float corner_radius_tl_dp = 0.f, corner_radius_tr_dp = 0.f;
    float corner_radius_br_dp = 0.f, corner_radius_bl_dp = 0.f;
    /* Interaction visual state, set by the host (android_view_set_pressed /
     * android_view_set_hovered). Only affects background resolution. */
    bool pressed = false;
    bool hovered = false;

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

    /* AOSP LinearLayout.mTotalLength (content extent WITHOUT padding) at the
     * end of measure; layout_linear reads it for the major-gravity offset
     * instead of re-summing (the value differs when the weight pass or the
     * useLargestChild re-sum ran: AOSP mTotalLength semantics, LinearLayout
     * layoutVertical:1674-1689). */
    float linear_measured_main = 0.f;

    /* TextView family */
    std::string text;
    std::string hint;
    bool has_hint = false;
    float text_size_sp = 16.f;
    color_rgba text_color{1.f, 0.125f, 0.125f, 0.125f};
    /* Link color (android:textColorLink, AOSP mTextColorLink): separate from
     * the regular text color; used only for link spans (autoLink/text links). */
    color_rgba text_color_link{1.f, 0.125f, 0.125f, 0.125f};
    bool has_text_color_link = false;
    bool single_line = false;
    int32_t text_gravity = 0;
    /* android:textStyle BOLD bit (Typeface.BOLD = 1). AOSP applies this as
     * algorithmic fake-bold on the paint (TextView.java:2551
     * setFakeBoldText((need & Typeface.BOLD) != 0)) — the runtime mirrors it
     * in the renderer with a synthetic second pass. */
    bool text_bold = false;

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

    /* Toast state (android.widget.Toast, exact AOSP semantics — owned here,
     * not in the host). Guarded by toast_mutex; active_toast_deadline_ms is
     * the steady-clock deadline at which TN would send HIDE (SHORT=4000ms,
     * LONG=7000ms, ToastPresenter.java). The host polls android_toast_is_active
     * each frame and renders android_toast_render over the app frame. */
    std::mutex toast_mutex;
    std::string toast_text;
    int32_t toast_duration = ANDROID_TOAST_LENGTH_SHORT;
    bool toast_active = false;
    uint64_t toast_deadline_ms = 0;
    bool has_toast = false; /* makeText was called (state exists) */

    /* Touch gesture state (View.dispatchTouchEvent / View.onTouchEvent /
     * ViewGroup.dispatchTouchEvent exact port, android_input.cpp). Only one
     * gesture at a time — the host dispatches ACTION_DOWN/UP/MOVE/CANCEL. */
    struct {
        /* The view that received ACTION_DOWN (ViewGroup.mFirstTouchTarget):
         * the gesture is delivered to THIS view until UP/CANCEL, never
         * re-hit-tested (ViewGroup.java:2675, 2717-2766). */
        android_view_s* touch_target = nullptr;
        bool pressed = false;        /* View.PFLAG_PRESSED */
        bool prepressed = false;     /* View.PFLAG_PREPRESSED (scrolling container) */
        bool has_performed_long_press = false; /* View.mHasPerformedLongPress */
        bool ignore_next_up = false; /* View.mIgnoreNextUpEvent */
        float down_x = 0.f, down_y = 0.f;
        float last_x = 0.f, last_y = 0.f;
        uint64_t down_ms = 0;
        uint64_t long_press_deadline_ms = 0; /* 0 = no long-press pending */
        bool long_press_pending = false;
        uint64_t tap_deadline_ms = 0;        /* 0 = no tap (prepressed) pending */
        bool tap_pending = false;
        /* Focused view for key dispatch (Enter/Space → performClick). */
        android_view_s* focused = nullptr;
        /* Click dispatch channel (C++ decides click → host runs guest DEX). */
        void (*on_click)(int32_t resource_id, void* user_data) = nullptr;
        void (*on_long_click)(int32_t resource_id, void* user_data) = nullptr;
        void* click_user_data = nullptr;
    } gesture;
};

namespace viewruntime::android {

/* Defined in measure_core.cpp. */
android_measured_size_t measure_view(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui);
void layout_view(android_view_s* view, float x, float y, float w, float h,
                 const android_ui_s* ui);
/* Defined in android_inflate.cpp: re-resolve a view's background for its
 * current pressed/hovered state (drawable ColorStateList/selector lookup);
 * optionally also returns the state-resolved stroke color. */
bool resolve_background_for_state(const android_ui_s* ui,
                                  const android_view_s* view,
                                  color_rgba* out,
                                  color_rgba* out_stroke_color = nullptr);
void apply_gravity(int32_t gravity, float child_w, float child_h,
                   float container_w, float container_h,
                   float* out_x, float* out_y);
const char* display_text(const android_view_s* view);
android_measured_size_t measure_base(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui);
android_view_s* hit_test(android_view_s* view, float px, float py);

/* Defined in text_view.cpp. */
android_measured_size_t measure_text_view(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui);
android_measured_size_t measure_checkable(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui);

/* Defined in image_view.cpp. */
android_measured_size_t measure_image(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui);

/* Defined in progress_bar.cpp. */
android_measured_size_t measure_progress(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui);

/* Defined in linear_layout.cpp. */
android_measured_size_t measure_linear(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui);
void layout_linear(android_view_s* view, float x, float y, float w, float h,
                   const android_ui_s* ui);

/* Defined in frame_layout.cpp. */
android_measured_size_t measure_frame(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui);
void layout_frame(android_view_s* view, float x, float y, float w, float h,
                  const android_ui_s* ui);

/* Defined in relative_layout.cpp. */
android_measured_size_t measure_relative(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui);
void layout_relative(android_view_s* view, float x, float y, float w, float h,
                     const android_ui_s* ui);

/* Defined in scroll_view.cpp. */
android_measured_size_t measure_scroll(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui);
void layout_scroll(android_view_s* view, float x, float y, float w, float h,
                   const android_ui_s* ui);

/* Defined in constraint_layout.cpp. */
android_measured_size_t measure_constraint(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui);
void layout_constraint(android_view_s* view, float x, float y, float w, float h,
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
            /* AOSP getChildMeasureSpec: WRAP_CONTENT under UNSPECIFIED keeps
             * resultSize = size (the parent's size minus padding_used) and the
             * UNSPECIFIED mode (ViewGroup.java:7105-7110); returning size 0
             * collapsed cross-axis WRAP children in UNSPECIFIED containers. */
            return {size, ANDROID_MEASURE_UNSPECIFIED};
        default:
            return {size, ANDROID_MEASURE_AT_MOST};
    }
}

/* ViewGroup.measureChildWithMargins: the shared primitive every container uses
 * to measure a direct child. */
inline android_measured_size_t measure_child_with_margins(
    android_view_s* child,
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
