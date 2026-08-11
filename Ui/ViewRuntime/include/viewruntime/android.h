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

/* Gravity constants mirror android.view.Gravity semantics. START/END carry
 * the RELATIVE_LAYOUT_DIRECTION axis bit exactly like AOSP (0x00800000), so
 * the numeric values match the real framework. */
#define ANDROID_GRAVITY_NO_GRAVITY     0x0000

/* LayoutParams.gravity sentinel: AOSP LinearLayout.LayoutParams and
 * FrameLayout.LayoutParams default gravity to -1 (FrameLayout names it
 * UNSPECIFIED_GRAVITY), which means "inherit the container's gravity"
 * (LinearLayout: gravity < 0 -> minorGravity, LinearLayout.java:1702) or
 * "use DEFAULT_CHILD_GRAVITY" (FrameLayout.layoutChildren). This is distinct
 * from ANDROID_GRAVITY_NO_GRAVITY (0), which is a valid explicit gravity. */
#define ANDROID_GRAVITY_UNSPECIFIED   (-1)
#define ANDROID_GRAVITY_CENTER_HORIZONTAL 0x0001
#define ANDROID_GRAVITY_LEFT            0x0003
#define ANDROID_GRAVITY_RIGHT           0x0005
#define ANDROID_GRAVITY_CLIP_HORIZONTAL 0x0008
#define ANDROID_GRAVITY_CENTER_VERTICAL 0x0010
#define ANDROID_GRAVITY_CENTER          0x0011
#define ANDROID_GRAVITY_TOP             0x0030
#define ANDROID_GRAVITY_BOTTOM          0x0050
#define ANDROID_GRAVITY_FILL_HORIZONTAL 0x0007
#define ANDROID_GRAVITY_CLIP_VERTICAL   0x0080
#define ANDROID_GRAVITY_FILL_VERTICAL   0x0070
#define ANDROID_GRAVITY_FILL            0x0077
/* RELATIVE_LAYOUT_DIRECTION axis (android.view.Gravity). */
#define ANDROID_GRAVITY_RELATIVE_LAYOUT_DIRECTION 0x00800000
#define ANDROID_GRAVITY_START           (ANDROID_GRAVITY_RELATIVE_LAYOUT_DIRECTION | ANDROID_GRAVITY_LEFT)
#define ANDROID_GRAVITY_END             (ANDROID_GRAVITY_RELATIVE_LAYOUT_DIRECTION | ANDROID_GRAVITY_RIGHT)

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

/* GradientDrawable shapes (GradientDrawable.java:111-126). */
#define ANDROID_SHAPE_RECTANGLE 0
#define ANDROID_SHAPE_OVAL      1
#define ANDROID_SHAPE_LINE      2
#define ANDROID_SHAPE_RING      3

/* GradientDrawable gradient types (GradientDrawable.java:136-146). */
#define ANDROID_GRADIENT_LINEAR 0
#define ANDROID_GRADIENT_RADIAL 1
#define ANDROID_GRADIENT_SWEEP  2

/* ImageView scale types — realigned to AOSP ImageView.ScaleType ordering
 * (MATRIX=0, FIT_XY=1, FIT_START=2, FIT_CENTER=3, FIT_END=4, CENTER=5,
 * CENTER_CROP=6, CENTER_INSIDE=7). */
typedef enum {
    ANDROID_SCALE_MATRIX = 0,
    ANDROID_SCALE_FIT_XY,
    ANDROID_SCALE_FIT_START,
    ANDROID_SCALE_FIT_CENTER,
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

/* ── Phase 2 resource bridge ─────────────────────────────────────────
 *
 * App Runtime is the resource-and-behavior PROVIDER; ViewRuntime owns every
 * visual decision. Values cross the bridge RAW (as declared by the AXML),
 * never pre-resolved or pre-scaled: ViewRuntime interprets references,
 * walks style parent chains, applies density exactly once. */

/* Typed raw value kinds — mirrors android.util.TypedValue as it appears in
 * binary AXML, unresolved. */
typedef enum {
    ANDROID_RAW_TYPE_STRING = 0,      /* string_value */
    ANDROID_RAW_TYPE_REFERENCE,       /* "@color/x": ref_id */
    ANDROID_RAW_TYPE_ATTRIBUTE,       /* "?attr/x": ref_id = attr id */
    ANDROID_RAW_TYPE_DIMENSION,       /* float_value + unit */
    ANDROID_RAW_TYPE_FLOAT,           /* float_value */
    ANDROID_RAW_TYPE_INT_BOOLEAN,     /* int_value 0/1 */
    ANDROID_RAW_TYPE_INT_DEC,         /* int_value signed */
    ANDROID_RAW_TYPE_INT_HEX,         /* int_value unsigned */
    ANDROID_RAW_TYPE_INT_COLOR        /* int_value ARGB */
} android_raw_value_kind_t;

typedef enum {
    ANDROID_DIMEN_UNIT_PX = 0,
    ANDROID_DIMEN_UNIT_DIP,
    ANDROID_DIMEN_UNIT_SP,
    ANDROID_DIMEN_UNIT_PT,
    ANDROID_DIMEN_UNIT_IN,
    ANDROID_DIMEN_UNIT_MM
} android_dimen_unit_t;

typedef struct {
    int32_t kind;            /* android_raw_value_kind_t */
    const char* string_value;/* STRING */
    uint32_t ref_id;         /* REFERENCE / ATTRIBUTE */
    float float_value;       /* FLOAT / DIMENSION raw, NOT scaled */
    int32_t unit;            /* DIMENSION only (android_dimen_unit_t) */
    int32_t int_value;       /* INT_* */
} android_raw_value_t;

/* One layout attribute: namespaced name + raw, unresolved value. name_id is
 * the attribute's resource id when the provider knows it (0 otherwise) —
 * needed to match "?attr/<id>" theme references against style bags. */
typedef struct {
    const char* name;        /* "android:layout_width", "app:...", "style" */
    uint32_t name_id;        /* attribute resource id, 0 if unknown */
    android_raw_value_t value;
} android_attr_t;

/* One element of the parsed layout tree. parent_index == -1 is the root;
 * the root may carry the active theme's root style id (0 = none). */
typedef struct {
    const char* class_name;  /* "LinearLayout", "TextView", "Button", ... */
    int32_t resource_id;     /* android:id resolved id, 0 if none */
    int32_t parent_index;    /* -1 = root */
    uint32_t theme_style_id; /* root only: active theme's root style id */
    int32_t attr_count;
    const android_attr_t* attrs;
} android_node_t;

/* Resolve a resource reference to its raw typed value. ViewRuntime decides
 * what the value means; App Runtime only hands back the raw parsed data.
 * Returns FALSE when the reference cannot be resolved. */
typedef bool_t (*android_resolve_resource_fn)(
    uint32_t resource_id,
    android_raw_value_t* out_value,
    void* user_data);

/* Return one style's raw attribute bag plus its parent style id (0 = no
 * parent). ViewRuntime walks the parent chain and decides which attribute
 * wins; App Runtime never applies styles itself. */
typedef bool_t (*android_resolve_style_fn)(
    uint32_t style_id,
    const android_attr_t** out_attrs,
    int32_t* out_attr_count,
    uint32_t* out_parent_style_id,
    void* user_data);

/* Fetch raw file bytes for a resource path (image/font). The bytes are owned
 * by App Runtime and valid only for the duration of the call. */
typedef bool_t (*android_fetch_file_fn)(
    const char* path,
    const uint8_t** out_bytes,
    int32_t* out_size,
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
/* Interaction visual state (desktop-host addition over AOSP touch model):
 * the host reports mouse-down (pressed) and mouse-enter (hovered) per
 * HitTest result and re-requests a frame; ViewRuntime re-resolves the
 * background from the drawable's ColorStateList/selector for that state
 * instead of always the stateless default. No dispatch involvement. */
API status_t android_view_set_pressed(android_view_t view, bool_t pressed);
API status_t android_view_set_hovered(android_view_t view, bool_t hovered);
API status_t android_view_set_padding_dp(android_view_t view, float padding_dp);
API status_t android_view_set_padding_edges_dp(android_view_t view, thicknessf padding_dp);
API status_t android_view_set_min_size_dp(android_view_t view, float min_width_dp, float min_height_dp);
API status_t android_view_set_content_description(android_view_t view, const char* description);
API status_t android_view_set_click_handler(android_view_t view, const char* handler);
/* Mark a view CLICKABLE (View.java:7868 setOnClickListener sets the flag).
 * The host calls this when a guest OnClickListener / XML onClick is attached;
 * the input dispatch then performs click on UP for this view. */
API status_t android_view_set_clickable(android_view_t view, bool_t clickable);

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
API status_t android_view_set_adjust_view_bounds(android_view_t view, bool_t adjust);
API status_t android_view_set_max_image_size_dp(
    android_view_t view, float max_width_dp, float max_height_dp);

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

/* Executes a recorded display list onto the session's registered render
 * surface (android_ui_set_surface). This is the only host-visible paint path:
 * ViewRuntime turns its own commands into pixels; the host never interprets
 * them. Commands without a backend mapping are skipped, never approximated. */
API status_t android_ui_render(android_ui_t ui, display_list_t list);

/* ── Post-layout queries ───────────────────────────────────────────── */

API status_t android_view_get_bounds(android_view_t view, rectf* out_bounds);
API status_t android_view_get_measured_size(android_view_t view, sizef* out_size);
API android_view_t android_ui_hit_test(
    android_ui_t ui, android_view_t root, float x, float y);

/* ScrollView: scroll range is derived from the measured overflow; the offset
 * is clamped to the range on set. */
API status_t android_view_set_scroll_offset(android_view_t view, float x, float y);
API scroll_metrics_t android_view_get_scroll_metrics(android_view_t view);

/* ── Phase 2 resource bridge + inflate ─────────────────────────────── */

/* Register the resource-query channel ViewRuntime uses to ask App Runtime
 * for raw resource data. Any callback may be null (that capability then
 * fails closed with no fallback value). Call before inflate. */
API void android_ui_set_resource_bridge(
    android_ui_t ui,
    android_resolve_resource_fn resolve_resource,
    android_resolve_style_fn resolve_style,
    android_fetch_file_fn fetch_file,
    void* user_data);

/* Register the render surface ViewRuntime uses to upload decoded images
 * (ImageViews decode raw file bytes through the bridge, then hand the
 * surface its ARGB pixels). Call after surface creation, before inflate. */
API void android_ui_set_surface(android_ui_t ui, void* surface);

/* Build a native view tree from a parsed element tree. App Runtime has
 * already parsed the binary AXML into this generic form; ViewRuntime owns
 * every attribute's meaning (dimensions stay raw, converted with the
 * session density exactly once). Copies all input data; the caller may free
 * the node array after the call. */
API status_t android_ui_inflate(
    android_ui_t ui,
    const android_node_t* nodes,
    int32_t node_count,
    android_view_t* out_root);

/* API-binding forwarding queries: real answers from ViewRuntime's actual
 * view state (the guest bytecode asks App Runtime, App Runtime relays here). */
API status_t android_view_get_text(android_view_t view, const char** out_text);
API status_t android_view_get_text_color(android_view_t view, color_rgba* out_color);
API status_t android_view_get_text_color_link(android_view_t view, color_rgba* out_color);
API status_t android_view_get_background_color(android_view_t view, color_rgba* out_color);
/* View.getLayoutParams / getPaddingLeft equivalents (raw dp, unconverted). */
API status_t android_view_get_layout_params(android_view_t view, android_layout_params_t* out_params);
API status_t android_view_get_padding_dp(android_view_t view, thicknessf* out_padding_dp);

/* ── Toast (android.widget.Toast, exact AOSP semantics) ─────────────── */

/* Duration constants — Toast.LENGTH_SHORT / LENGTH_LONG (Toast.java). */
#define ANDROID_TOAST_LENGTH_SHORT 0
#define ANDROID_TOAST_LENGTH_LONG 1

/* Timeouts — ToastPresenter.java SHORT_DURATION_TIMEOUT / LONG_DURATION_TIMEOUT. */
#define ANDROID_TOAST_SHORT_TIMEOUT_MS 4000
#define ANDROID_TOAST_LONG_TIMEOUT_MS 7000

/* Default gravity — config_toastDefaultGravity = 0x51 = CENTER_HORIZONTAL |
 * BOTTOM (AOSP config.xml). */
#define ANDROID_TOAST_DEFAULT_GRAVITY 0x51

/* Notification the host polls at its own pace (e.g. each frame):
 * 0 = no toast active, nonzero = a toast is showing. The host should render a
 * fresh frame while active and keep polling; ViewRuntime hides the toast
 * itself after the SHORT/LONG timeout exactly like AOSP's TN handler
 * (ToastPresenter.java SHORT=4000ms / LONG=7000ms). */
API bool_t android_toast_is_active(android_ui_t ui);

/* Toast.makeText(Context, CharSequence, int): creates the toast state; text
 * copied (the session owns it), duration validated (0=SHORT, 1=LONG). */
API status_t android_toast_make_text(android_ui_t ui, const char* text, int32_t duration);

/* Toast.setText(CharSequence) / getText — the transient_notification TextView
 * message (Toast.java setText: tv.setText(s)). */
API status_t android_toast_set_text(android_ui_t ui, const char* text);

/* Toast.setDuration / getDuration (Toast.java). */
API status_t android_toast_set_duration(android_ui_t ui, int32_t duration);
API int32_t android_toast_get_duration(android_ui_t ui);

/* Toast.show(): activates the toast; ViewRuntime starts its SHORT/LONG
 * timeout (the TN SHOW message + ToastPresenter timeout). */
API status_t android_toast_show(android_ui_t ui);

/* Toast.cancel(): deactivates immediately (the TN CANCEL message). */
API status_t android_toast_cancel(android_ui_t ui);

/* Render the active toast overlay (transient_notification.xml geometry) into
 * the registered render surface AFTER the app frame. No-op when inactive.
 * Only meaningful with a surface registered via android_ui_set_surface. */
API void android_toast_render(android_ui_t ui);

/* ── Input events (android.view.MotionEvent / View / ViewGroup exact port) ── */

/* MotionEvent action constants (MotionEvent.java ACTION_* masked values). */
#define ANDROID_ACTION_DOWN 0
#define ANDROID_ACTION_UP 1
#define ANDROID_ACTION_MOVE 2
#define ANDROID_ACTION_CANCEL 3

/* KeyEvent action constants (KeyEvent.java). */
#define ANDROID_KEY_ACTION_DOWN 0
#define ANDROID_KEY_ACTION_UP 1
#define ANDROID_KEYCODE_ENTER 66
#define ANDROID_KEYCODE_DPAD_CENTER 23
#define ANDROID_KEYCODE_SPACE 62

/* ViewConfiguration constants (ViewConfiguration.java): PRESSED_STATE_DURATION
 * =64ms, DEFAULT_LONG_PRESS_TIMEOUT=400ms, TAP_TIMEOUT=100ms, TOUCH_SLOP=8dp
 * (scaled by density — the runtime applies ui->density). */
#define ANDROID_VIEW_CONFIG_PRESSED_STATE_DURATION_MS 64
#define ANDROID_VIEW_CONFIG_LONG_PRESS_TIMEOUT_MS 400
#define ANDROID_VIEW_CONFIG_TAP_TIMEOUT_MS 100
#define ANDROID_VIEW_CONFIG_TOUCH_SLOP_DP 8.f

/* Dispatch a touch event into the view tree, exactly like
 * ViewRootImpl.deliverPointerEvent → ViewGroup.dispatchTouchEvent →
 * View.onTouchEvent (ViewGroup.java:2647, View.java:16551/18059):
 *   DOWN   → hit-test, fix mFirstTouchTarget, pressed (+prepressed in
 *            scrolling containers + tap timeout), schedule long-press.
 *   MOVE   → touch-slop check: leaving the slop cancels pressed + long-press
 *            (View.java:18207-18245).
 *   UP     → if pressed and no long-press performed → performClick (callback),
 *            then unpress after pressed-state duration (View.java:18087-18150).
 *   CANCEL → unpress + cancel timers (View.java:18195-18205).
 * Disabled views CONSUME but do not respond (View.java:18069-18078); a view
 * consumes only when clickable (CLICKABLE|LONG_CLICKABLE|CONTEXT_CLICKABLE). */
API status_t android_ui_dispatch_touch(
    android_ui_t ui, android_view_t root, int32_t action, float x, float y);

/* Dispatch a key event (Enter/Space on the focused view → performClick),
 * like ViewRootImpl.deliverKeyEvent → View.dispatchKeyEvent (Enter/Space
 * handling matches the runtime's previous focused-view click). */
API status_t android_ui_dispatch_key(
    android_ui_t ui, android_view_t root, int32_t action, int32_t key_code);

/* Register the click dispatch channel: when View.performClick decides to call
 * the OnClickListener (View.java:8072), ViewRuntime invokes on_click with the
 * view's resource id so the HOST runs the guest DEX listener. Long-press
 * (View.performLongClick, java:8118) invokes on_long_click. Either callback
 * may be null (that click then does nothing, like a view with no listener). */
typedef void (*android_on_click_fn)(int32_t resource_id, void* user_data);
API void android_ui_set_click_callback(
    android_ui_t ui,
    android_on_click_fn on_click,
    android_on_click_fn on_long_click,
    void* user_data);

/* Poll gesture timers (long-press 400ms / tap 100ms / pressed-state 64ms).
 * The host calls this from its frame loop (like the toast deadline poll);
 * ViewRuntime fires long-press when its deadline passes. Returns nonzero when
 * a timer fired (a frame refresh is worth it). */
API int32_t android_ui_gesture_poll(android_ui_t ui);

/* True while a touch gesture is active (a view is pressed/targeted). */
API bool_t android_ui_gesture_active(android_ui_t ui);

#ifdef __cplusplus
}
#endif

#endif /* ANDROID_H */
