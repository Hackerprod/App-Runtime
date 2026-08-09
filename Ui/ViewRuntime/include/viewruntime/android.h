#ifndef ANDROID_H
#define ANDROID_H

#include <viewruntime/viewruntime.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ViewRuntime Android UI pipeline.
 *
 * Replaces the legacy HTML+CSS front-end with an Android view model. A host
 * inflates an Android view tree (from layout XML or framework stubs) and
 * drives the native core through the classic two-phase contract:
 *
 *   measure(root, width_px, height_px)   -> intrinsic + constrained sizes
 *   layout(root, x, y, width_px, height_px) -> absolute bounds for every view
 *   record(root)                         -> retained paint-command display list
 *
 * The native core owns the view tree, layout semantics (LinearLayout,
 * FrameLayout, RelativeLayout, ScrollView) and the paint-command recording;
 * the host owns text measurement, image dimensions and final rasterization —
 * exactly the split the rest of ViewRuntime already uses. All sizes handed in
 * and out are pixels; dp/sp values are converted with the session density.
 */

/* ── View classes ──────────────────────────────────────────────────── */

typedef enum {
    ANDROID_VIEW_VIEW = 0,
    ANDROID_VIEW_LINEAR_LAYOUT,
    ANDROID_VIEW_FRAME_LAYOUT,
    ANDROID_VIEW_RELATIVE_LAYOUT,
    ANDROID_VIEW_SCROLL_VIEW,
    ANDROID_VIEW_TEXT_VIEW,
    ANDROID_VIEW_BUTTON,
    ANDROID_VIEW_EDIT_TEXT,
    ANDROID_VIEW_IMAGE_VIEW,
    ANDROID_VIEW_CHECK_BOX,
    ANDROID_VIEW_RADIO_BUTTON,
    ANDROID_VIEW_PROGRESS_BAR,
    ANDROID_VIEW_GRID_LAYOUT,
    ANDROID_VIEW_LIST_VIEW,
    ANDROID_VIEW_RECYCLER_VIEW,
    ANDROID_VIEW_CONSTRAINT_LAYOUT,
    ANDROID_VIEW_BARRIER
} android_view_class_t;

/* ── Measure contract ──────────────────────────────────────────────── */

typedef enum {
    ANDROID_MEASURE_UNSPECIFIED = 0,
    ANDROID_MEASURE_EXACTLY = 1,
    ANDROID_MEASURE_AT_MOST = 2
} android_measure_mode_t;

typedef struct { float size; int32_t mode; } android_measure_spec_t;
typedef struct { float width, height; } android_measured_size_t;

/* ── LayoutParams ──────────────────────────────────────────────────── */

typedef enum {
    ANDROID_SIZE_KIND_MATCH_PARENT = 0,
    ANDROID_SIZE_KIND_WRAP_CONTENT = 1,
    ANDROID_SIZE_KIND_EXACT = 2
} android_size_kind_t;

typedef struct { int32_t kind; float value_dp; } android_size_t;

/* ── ConstraintLayout ──────────────────────────────────────────────── */

/* Anchor sides (androidx.constraintlayout.widget.ConstraintLayout.LayoutParams
 * anchor constants). START/END map to LEFT/RIGHT (LTR runtime). */
typedef enum {
    ANDROID_CONSTRAINT_LEFT = 0,
    ANDROID_CONSTRAINT_RIGHT,
    ANDROID_CONSTRAINT_TOP,
    ANDROID_CONSTRAINT_BOTTOM,
    ANDROID_CONSTRAINT_START,
    ANDROID_CONSTRAINT_END
} android_constraint_side_t;

/* target_id == -1 means the parent container. */
typedef struct {
    int32_t target_id;
    int32_t side;        /* this view's anchor (android_constraint_side_t) */
    int32_t target_side; /* the target's anchor */
    float   margin_dp;
    float   gone_margin_dp;
} android_constraint_t;

#define ANDROID_CONSTRAINT_MATCH_SPREAD 0
#define ANDROID_CONSTRAINT_MATCH_WRAP 1
#define ANDROID_CONSTRAINT_MATCH_PERCENT 2
#define ANDROID_CONSTRAINT_MATCH_RATIO 3

#define ANDROID_CONSTRAINT_CHAIN_SPREAD 0
#define ANDROID_CONSTRAINT_CHAIN_SPREAD_INSIDE 1
#define ANDROID_CONSTRAINT_CHAIN_PACKED 2

typedef struct {
    float bias_h;            /* 0..1, default 0.5 */
    float bias_v;
    float dimension_ratio;   /* 0 = none; 1.0 = square */
    int32_t match_default_w; /* ANDROID_CONSTRAINT_MATCH_* */
    int32_t match_default_h;
    float match_min_w_dp, match_max_w_dp;
    float match_min_h_dp, match_max_h_dp;
    float match_percent_w, match_percent_h;
    int32_t chain_style_h;
    int32_t chain_style_v;
    int32_t constraint_count;
    android_constraint_t constraints[8];
} android_constraint_params_t;

typedef struct {
    android_size_t width;
    android_size_t height;
    thicknessf     margins_dp; /* left, top, right, bottom */
    int32_t               gravity;    /* android.view.Gravity flags */
    float                 weight;     /* LinearLayout layout_weight */
    android_constraint_params_t constraint;
} android_layout_params_t;

/* Gravity constants mirror android.view.Gravity semantics. */
#define ANDROID_GRAVITY_NO_GRAVITY     0x0000
#define ANDROID_GRAVITY_CENTER_HORIZONTAL 0x0001
#define ANDROID_GRAVITY_LEFT            0x0003
#define ANDROID_GRAVITY_RIGHT           0x0005
#define ANDROID_GRAVITY_CENTER_VERTICAL 0x0010
#define ANDROID_GRAVITY_CENTER          0x0011
#define ANDROID_GRAVITY_TOP             0x0030
#define ANDROID_GRAVITY_BOTTOM          0x0050
#define ANDROID_GRAVITY_FILL_HORIZONTAL 0x0007
#define ANDROID_GRAVITY_FILL_VERTICAL   0x0070
#define ANDROID_GRAVITY_FILL            0x0077

/* Visibility matches android.view.View visibility values. */
#define ANDROID_VISIBLE   0
#define ANDROID_INVISIBLE 4
#define ANDROID_GONE      8

/* Orientation for LinearLayout. */
#define ANDROID_HORIZONTAL 0
#define ANDROID_VERTICAL   1

/* Layout direction (View.LAYOUT_DIRECTION_*). */
#define ANDROID_LAYOUT_DIRECTION_LTR 0
#define ANDROID_LAYOUT_DIRECTION_RTL 1

/* LinearLayout dividers (AOSP LinearLayout.SHOW_DIVIDER_*). */
#define ANDROID_SHOW_DIVIDER_NONE      0
#define ANDROID_SHOW_DIVIDER_BEGINNING 1
#define ANDROID_SHOW_DIVIDER_MIDDLE    2
#define ANDROID_SHOW_DIVIDER_END       4

/* ImageView scale types. */
typedef enum {
    ANDROID_SCALE_FIT_CENTER = 0,
    ANDROID_SCALE_FIT_XY,
    ANDROID_SCALE_FIT_START,
    ANDROID_SCALE_FIT_END,
    ANDROID_SCALE_CENTER,
    ANDROID_SCALE_CENTER_CROP,
    ANDROID_SCALE_CENTER_INSIDE
} android_scale_type_t;

/* ── RelativeLayout rules ──────────────────────────────────────────── */
/* Faithful to android.widget.RelativeLayout verb constants: each verb
 * indexes a rules array; the value is the target view resource id, or
 * ANDROID_RELATIVE_TRUE (-1) for parent rules, or 0 when unset. */

#define ANDROID_RELATIVE_TRUE -1

typedef enum {
    ANDROID_RELATIVE_LEFT_OF = 0,
    ANDROID_RELATIVE_RIGHT_OF,
    ANDROID_RELATIVE_ABOVE,
    ANDROID_RELATIVE_BELOW,
    ANDROID_RELATIVE_ALIGN_BASELINE,
    ANDROID_RELATIVE_ALIGN_LEFT,
    ANDROID_RELATIVE_ALIGN_TOP,
    ANDROID_RELATIVE_ALIGN_RIGHT,
    ANDROID_RELATIVE_ALIGN_BOTTOM,
    ANDROID_RELATIVE_ALIGN_PARENT_LEFT,
    ANDROID_RELATIVE_ALIGN_PARENT_TOP,
    ANDROID_RELATIVE_ALIGN_PARENT_RIGHT,
    ANDROID_RELATIVE_ALIGN_PARENT_BOTTOM,
    ANDROID_RELATIVE_CENTER_IN_PARENT,
    ANDROID_RELATIVE_CENTER_HORIZONTAL,
    ANDROID_RELATIVE_CENTER_VERTICAL,
    ANDROID_RELATIVE_START_OF,
    ANDROID_RELATIVE_END_OF,
    ANDROID_RELATIVE_ALIGN_START,
    ANDROID_RELATIVE_ALIGN_END,
    ANDROID_RELATIVE_ALIGN_PARENT_START,
    ANDROID_RELATIVE_ALIGN_PARENT_END,
    ANDROID_RELATIVE_VERB_COUNT
} android_relative_rule_t;

/* ── Host callbacks ────────────────────────────────────────────────── */

typedef struct { float width, height, baseline; } android_text_metrics_t;

typedef android_text_metrics_t (*android_text_measurer_fn)(
    const char* text,
    float text_size_px,
    float max_width,
    void* user_data);

typedef bool_t (*android_image_dimensions_fn)(
    const char* source,
    sizef* out_size,
    void* user_data);

/* ── Session ───────────────────────────────────────────────────────── */

typedef struct {
    float density;         /* dp -> px scale */
    float scaled_density;  /* sp -> px scale */
} android_ui_options_t;

typedef struct android_ui_s* android_ui_t;
typedef struct android_view_s* android_view_t;

API status_t android_ui_create(
    const android_ui_options_t* options,
    android_ui_t* out_ui);

API void android_ui_destroy(android_ui_t ui);

API void android_ui_set_text_measurer(
    android_ui_t ui,
    android_text_measurer_fn measurer,
    void* user_data);

/* Load a TrueType font (e.g. a system font path) and install the built-in
 * stb_truetype text measurer. Measure results then reflect real font
 * metrics (advance widths, ascent/descent, word wrap) instead of the
 * proportional fallback. */
API status_t android_ui_set_font(android_ui_t ui, const char* path);

/* Measure a text run with the currently installed measurer (real font
 * metrics after android_ui_set_font). */
API status_t android_ui_measure_text(
    android_ui_t ui, const char* text, float size_px, float max_width,
    android_text_metrics_t* out_metrics);

API void android_ui_set_image_dimensions(
    android_ui_t ui,
    android_image_dimensions_fn dimensions,
    void* user_data);

/* Detaches and destroys every view owned by the session. */
API status_t android_ui_clear(android_ui_t ui);

/* ── View lifecycle ────────────────────────────────────────────────── */

/* Creates a view owned by the session. The view is detached until added to a
 * tree or explicitly placed as a root; views remain valid until the session
 * is destroyed or cleared. */
API status_t android_view_create(
    android_ui_t ui,
    android_view_class_t view_class,
    int32_t resource_id,
    android_view_t* out_view);

API status_t android_view_add_child(
    android_ui_t ui,
    android_view_t parent,
    android_view_t child);

API status_t android_view_remove_child(
    android_ui_t ui,
    android_view_t parent,
    android_view_t child);

/* Detaches a view (and its subtree) from its parent; it stays owned by the
 * session. */
API status_t android_view_detach(
    android_ui_t ui,
    android_view_t view);

API android_view_t android_view_get_parent(android_view_t view);
API int32_t            android_view_get_child_count(android_view_t view);
API android_view_t android_view_get_child(android_view_t view, int32_t index);
API android_view_t android_ui_find_view_by_id(android_ui_t ui, int32_t resource_id);
API int32_t            android_view_get_class(android_view_t view);
API int32_t            android_view_get_resource_id(android_view_t view);

/* ── Common attributes ─────────────────────────────────────────────── */

API status_t android_view_set_layout_params(
    android_view_t view,
    const android_layout_params_t* params);
API status_t android_view_set_visibility(android_view_t view, int32_t visibility);
API status_t android_view_set_enabled(android_view_t view, bool_t enabled);
API status_t android_view_set_background_color(android_view_t view, color_rgba color);
API status_t android_view_set_padding_dp(android_view_t view, float padding_dp);
API status_t android_view_set_padding_edges_dp(android_view_t view, thicknessf padding_dp);
API status_t android_view_set_min_size_dp(android_view_t view, float min_width_dp, float min_height_dp);
API status_t android_view_set_content_description(android_view_t view, const char* description);
API status_t android_view_set_click_handler(android_view_t view, const char* handler);

/* ── LinearLayout ──────────────────────────────────────────────────── */

API status_t android_view_set_orientation(android_view_t view, int32_t orientation);
API status_t android_view_set_baseline_aligned(android_view_t view, bool_t baseline_aligned);
API status_t android_view_set_weight_sum(android_view_t view, float weight_sum);

/* ── LinearLayout advanced (RTL / measureWithLargestChild / dividers / default margins) ── */

API status_t android_view_set_layout_direction(android_view_t view, int32_t direction);
API status_t android_view_set_measure_with_largest_child(android_view_t view, bool_t enabled);
API status_t android_view_set_show_dividers(android_view_t view, int32_t show_dividers);
API status_t android_view_set_divider(
    android_view_t view, float thickness_px, float padding_px, color_rgba color);
API status_t android_view_set_use_default_margins(android_view_t view, bool_t use_default_margins);

/* ── ConstraintLayout ──────────────────────────────────────────────── */

API status_t android_view_add_constraint(
    android_view_t view,
    int32_t target_id,
    int32_t side,
    int32_t target_side,
    float margin_dp);
API status_t android_view_set_constraint_bias(
    android_view_t view, float bias_h, float bias_v);
API status_t android_view_set_constraint_ratio(
    android_view_t view, float dimension_ratio);
API status_t android_view_set_constraint_match_style(
    android_view_t view, int32_t default_w, int32_t default_h,
    float min_w_dp, float max_w_dp, float min_h_dp, float max_h_dp);
API status_t android_view_set_constraint_chain_style(
    android_view_t view, int32_t chain_style_h, int32_t chain_style_v);

/* Barrier is a virtual helper child of a ConstraintLayout: it references
 * other children by resource id and the layout places it on the group edge
 * (barrier types reuse ANDROID_CONSTRAINT_LEFT/RIGHT/TOP/BOTTOM). */
#define ANDROID_BARRIER_LEFT   ANDROID_CONSTRAINT_LEFT
#define ANDROID_BARRIER_RIGHT  ANDROID_CONSTRAINT_RIGHT
#define ANDROID_BARRIER_TOP    ANDROID_CONSTRAINT_TOP
#define ANDROID_BARRIER_BOTTOM ANDROID_CONSTRAINT_BOTTOM

API status_t android_view_set_barrier_type(
    android_view_t view, int32_t barrier_type);
API status_t android_view_set_barrier_margin(
    android_view_t view, float margin_dp);
API status_t android_view_set_barrier_allows_gone(
    android_view_t view, bool_t allows_gone);
API status_t android_view_add_barrier_reference(
    android_view_t view, int32_t target_id);
/* Container gravity (LinearLayout / FrameLayout): alignment of the children
 * as a whole (android:gravity), distinct from per-child layout_gravity. */
API status_t android_view_set_gravity(android_view_t view, int32_t gravity);

/* ── TextView family ───────────────────────────────────────────────── */

API status_t android_view_set_text(android_view_t view, const char* text);
API status_t android_view_set_text_size_sp(android_view_t view, float text_size_sp);
API status_t android_view_set_text_color(android_view_t view, color_rgba color);
API status_t android_view_set_text_gravity(android_view_t view, int32_t gravity);
API status_t android_view_set_single_line(android_view_t view, bool_t single_line);
API status_t android_view_set_hint(android_view_t view, const char* hint);

/* ── ImageView ─────────────────────────────────────────────────────── */

API status_t android_view_set_image_source(android_view_t view, const char* source);
API status_t android_view_set_scale_type(android_view_t view, int32_t scale_type);

/* ── CheckBox / RadioButton ────────────────────────────────────────── */

API status_t android_view_set_checked(android_view_t view, bool_t checked);

/* ── ProgressBar ───────────────────────────────────────────────────── */

API status_t android_view_set_progress(
    android_view_t view, int32_t min_value, int32_t max_value, int32_t value);
API status_t android_view_set_progress_colors(
    android_view_t view, color_rgba track_color, color_rgba progress_color);

/* ── RelativeLayout ────────────────────────────────────────────────── */
API status_t android_view_set_relative_rule(
    android_view_t view, int32_t verb, int32_t target_id);
API status_t android_view_set_relative_align_with_parent(
    android_view_t view, bool_t align_with_parent);

/* ── GridLayout ────────────────────────────────────────────────────── *//* Undefined grid index/span: the cell is auto-assigned (AOSP
 * GridLayout.UNDEFINED = Integer.MIN_VALUE). */
#define ANDROID_GRID_UNDEFINED (-2147483647 - 1)

/* Places the child at (row, column) spanning row_span rows and column_span
 * columns. Any of row/column may be ANDROID_GRID_UNDEFINED to let
 * GridLayout assign them automatically; spans default to 1. */
API status_t android_view_set_grid_cell(
    android_view_t view, int32_t row, int32_t column,
    int32_t row_span, int32_t column_span);

/* Row/column weights control excess-space distribution (0 = none). */
API status_t android_view_set_grid_weights(
    android_view_t view, float row_weight, float column_weight);

/* Per-child gravity; resolves to the row/column alignments of the specs
 * (AOSP GridLayout.LayoutParams.setGravity). */
API status_t android_view_set_grid_gravity(
    android_view_t view, int32_t gravity);

/* GridLayout container: explicit row/column counts (auto-derived from the
 * children when left at their defaults). */
API status_t android_view_set_row_count(android_view_t view, int32_t count);
API status_t android_view_set_column_count(android_view_t view, int32_t count);

/* ── ListView / RecyclerView ───────────────────────────────────────── */

/* ListView: vertical-only adapter list. Items are measured with UNSPECIFIED
 * height (content height, uncapped, per AOSP measureScrapChild) and stacked
 * with the divider between them. The divider is drawn between items (and
 * after the last item when it does not reach the bottom), only for enabled
 * items, matching AOSP ListView.dispatchDraw. The Material default is 1dp at
 * colorListDivider (light theme: black at 12%). */
API status_t android_view_set_divider_height_dp(android_view_t view, float divider_height_dp);
API status_t android_view_set_divider_color(android_view_t view, color_rgba color);
API status_t android_view_set_divider_enabled(android_view_t view, bool_t enabled);

/* RecyclerView (LinearLayoutManager): vertical or horizontal (set via
 * android_view_set_orientation). Items are measured through the canonical
 * getChildMeasureSpec on both axes (wrap degrades to AT_MOST under a bounded
 * parent, per LayoutManager.measureChildWithMargins) and stacked with their
 * margins as decoration; no divider by default. */

/* ── Frame pipeline ────────────────────────────────────────────────── */

/* Measures the subtree rooted at `root` against an Exactly-sized viewport in
 * pixels. Every view's measured size is stored. */
API status_t android_ui_measure(
    android_ui_t ui,
    android_view_t root,
    float width_px,
    float height_px);

/* Lays out the subtree rooted at `root` at the given absolute pixel rect. */
API status_t android_ui_layout(
    android_ui_t ui,
    android_view_t root,
    float x,
    float y,
    float width_px,
    float height_px);

/* Records the subtree into a retained display list (ViewRuntime paint commands).
 * The returned list owns its commands; destroy it with
 * display_list_destroy. */
API status_t android_ui_record(
    android_ui_t ui,
    android_view_t root,
    display_list_t* out_list);

/* ── Post-layout queries ───────────────────────────────────────────── */

API status_t android_view_get_bounds(android_view_t view, rectf* out_bounds);
API status_t android_view_get_measured_size(android_view_t view, sizef* out_size);
API android_view_t android_ui_hit_test(
    android_ui_t ui, android_view_t root, float x, float y);

/* ScrollView: scroll range is derived from the measured overflow; the offset
 * is clamped to the range on set. */
API status_t android_view_set_scroll_offset(android_view_t view, float x, float y);
API scroll_metrics_t android_view_get_scroll_metrics(android_view_t view);

#ifdef __cplusplus
}
#endif

#endif /* ANDROID_H */
