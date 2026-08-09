#ifndef H
#define H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#ifdef _WIN32
    #ifdef EXPORTS
        #define API __declspec(dllexport)
    #else
        #define API __declspec(dllimport)
    #endif
#else
    #define API __attribute__((visibility("default")))
#endif

/* ── Status & Lifecycle ────────────────────────────────────────────── */

/* ABI introspection
 *
 * The packed version uses 8 bits for major, 8 bits for minor, and 16 bits for
 * patch. Before ABI 1.0, minor releases may contain breaking changes. After
 * ABI 1.0, a major-version change denotes an incompatible ABI.
 */
#define ABI_VERSION_MAJOR 0u
#define ABI_VERSION_MINOR 31u
#define ABI_VERSION_PATCH 0u

#define ABI_VERSION_MAKE(major, minor, patch) \
    ((((uint32_t)(major) & UINT32_C(0xff)) << 24) | \
     (((uint32_t)(minor) & UINT32_C(0xff)) << 16) | \
      ((uint32_t)(patch) & UINT32_C(0xffff)))
#define ABI_VERSION_GET_MAJOR(version) (((uint32_t)(version) >> 24) & UINT32_C(0xff))
#define ABI_VERSION_GET_MINOR(version) (((uint32_t)(version) >> 16) & UINT32_C(0xff))
#define ABI_VERSION_GET_PATCH(version) ((uint32_t)(version) & UINT32_C(0xffff))
#define ABI_VERSION_CURRENT \
    ABI_VERSION_MAKE(ABI_VERSION_MAJOR, ABI_VERSION_MINOR, ABI_VERSION_PATCH)

typedef uint64_t capabilities_t;

/* Each bit represents a tested public pipeline, not the mere presence of a
   symbol. */
#define CAPABILITY_DISPLAY_LIST  (UINT64_C(1) << 3)
#define CAPABILITY_RENDER_PLAN   (UINT64_C(1) << 4)
#define CAPABILITY_ANDROID_UI    (UINT64_C(1) << 5)

API uint32_t              abi_version(void);
API capabilities_t abi_capabilities(void);

typedef int32_t status_t;
#define OK                   0
#define ERROR_NULL_ARG      -1
#define ERROR_INVALID_STATE -2
#define ERROR_OUT_OF_MEMORY -3
#define ERROR_DISPOSED      -4
#define ERROR_PARSE_FAILED  -5

typedef int32_t bool_t;
#define TRUE  1
#define FALSE 0

API const char* status_message(status_t status);
API void        string_free(char* str);

/* ── Primitives ────────────────────────────────────────────────────── */

typedef struct { float x, y; }                  pointf;
typedef struct { float width, height; }         sizef;
typedef struct { float x, y, width, height; }   rectf;
typedef struct { float left, top, right, bottom; } thicknessf;

API bool_t rectf_contains(rectf r, pointf p);
API rectf  rectf_deflate(rectf r, thicknessf t);
API rectf  rectf_inflate(rectf r, float v);
API rectf  rectf_offset(rectf r, float dx, float dy);
API rectf  rectf_from_edges(float left, float top, float right, float bottom);
API float         thicknessf_horizontal(thicknessf t);
API float         thicknessf_vertical(thicknessf t);

typedef struct { float r, g, b, a; }            color_rgba;
typedef struct { color_rgba left, top, right, bottom; } color_edges;

/* Border styles are kept per edge. The values follow the CSS keyword family;
   they describe paint style only, not the cascade. */
typedef enum {
    BORDER_STYLE_NONE = 0,
    BORDER_STYLE_HIDDEN,
    BORDER_STYLE_DOTTED,
    BORDER_STYLE_DASHED,
    BORDER_STYLE_SOLID,
    BORDER_STYLE_DOUBLE,
    BORDER_STYLE_GROOVE,
    BORDER_STYLE_RIDGE,
    BORDER_STYLE_INSET,
    BORDER_STYLE_OUTSET
} border_style_t;

typedef struct { int32_t left, top, right, bottom; } border_style_edges;

typedef struct {
    float top_left_x, top_left_y, top_right_x, top_right_y;
    float bottom_right_x, bottom_right_y, bottom_left_x, bottom_left_y;
} corner_radii;

/* A single paint shadow. A list owns its contiguous item storage; use the
   shadow-list helpers below to copy, compare, or release it. */
typedef struct {
    float offset_x;
    float offset_y;
    float blur_radius;
    float spread_radius;
    color_rgba color;
    bool_t     inset;
} shadow;

typedef struct {
    shadow* items;
    int32_t        count;
} shadow_list;

/* A copy leaves its destination as a deep copy on success and as the canonical
   empty list on failure. A list with count zero must have a null items pointer. */
API bool_t shadow_list_copy(const shadow_list* source,
                                                  shadow_list* destination);
API bool_t shadow_list_equal(const shadow_list* left,
                                                   const shadow_list* right);
API void          shadow_list_free(shadow_list* list);

/* ── Shared Value Types ────────────────────────────────────────────── */

typedef enum {
    CSS_UNIT_AUTO = 0,
    CSS_UNIT_PX,
    CSS_UNIT_PERCENT,
    CSS_UNIT_EM,
    CSS_UNIT_REM,
    CSS_UNIT_VW,
    CSS_UNIT_VH,
    CSS_UNIT_LINEAR
} css_unit_t;

/* Symbolic length kept until the final coordinate system is known at paint
   time. `linear_percent`/`linear_em`/... cache resolved linear values. */
typedef struct {
    float value;
    css_unit_t unit;
    float linear_percent;
    float linear_em;
    float linear_rem;
    float linear_vw;
    float linear_vh;
} css_length;

/* A gradient stop keeps its authored <length-percentage> symbolic until the
   final gradient line is known at paint time. `offset` is the resolved 0..1
   value carried only by paint commands. */
typedef struct {
    color_rgba color;
    css_length position;
    float             offset;
    bool_t     has_position;
} gradient_stop;

typedef struct {
    float direction_x;
    float direction_y;
    gradient_stop* stops;
    int32_t               stop_count;
} linear_gradient;

API void linear_gradient_free(linear_gradient* g);

/* Backgrounds & borders: every corner has independent horizontal and vertical
   radii. Keep symbolic lengths until the border box is known. */
typedef struct { css_length horizontal, vertical; } corner_radius;
typedef struct {
    corner_radius top_left, top_right, bottom_right, bottom_left;
} border_radii;

API css_length css_length_auto(void);
API css_length css_length_zero(void);
API bool_t     css_length_is_auto(css_length l);
API float             css_length_resolve(css_length l, float reference,
                                float font_size, float root_font_size,
                                float viewport_width, float viewport_height);
API bool_t     css_length_try_parse(const char* input, css_length* out);

API bool_t     color_rgba_try_parse(const char* input, color_rgba* out);

/* Background layers are painted from the last declared layer to the first,
   above background-color. Resource loading remains outside the core: an image
   layer carries its normalized UTF-8 source for the binding to resolve. */
typedef enum {
    BACKGROUND_LAYER_LINEAR_GRADIENT = 0,
    BACKGROUND_LAYER_IMAGE_URL
} background_layer_kind_t;

typedef enum {
    BACKGROUND_SIZE_AUTO = 0,
    BACKGROUND_SIZE_EXPLICIT,
    BACKGROUND_SIZE_COVER,
    BACKGROUND_SIZE_CONTAIN
} background_size_kind_t;

typedef enum {
    BACKGROUND_REPEAT = 0,
    BACKGROUND_NO_REPEAT,
    BACKGROUND_SPACE,
    BACKGROUND_ROUND
} background_repeat_t;

typedef struct {
    int32_t                kind;
    linear_gradient gradient;
    char*                  image_source;
    css_length      position_x;
    css_length      position_y;
    int32_t                size_kind;
    css_length      size_x;
    css_length      size_y;
    int32_t                repeat_x;
    int32_t                repeat_y;
} background_layer;

typedef struct {
    background_layer* items;
    int32_t                  count;
} background_layer_list;

API bool_t background_layer_list_copy(const background_layer_list* source,
                                                              background_layer_list* destination);
API bool_t background_layer_list_equal(const background_layer_list* left,
                                                               const background_layer_list* right);
API void          background_layer_list_free(background_layer_list* list);

/* Backdrop filters preserve the complete ordered operation model. */
typedef enum {
    BACKDROP_FILTER_BLUR = 0,
    BACKDROP_FILTER_BRIGHTNESS,
    BACKDROP_FILTER_CONTRAST,
    BACKDROP_FILTER_SATURATE
} backdrop_filter_kind_t;

typedef struct {
    int32_t           kind;
    css_length length_amount;
    float             scalar_amount;
} backdrop_filter;

typedef struct {
    backdrop_filter* items;
    int32_t                 count;
} backdrop_filter_list;

API bool_t backdrop_filter_list_copy(const backdrop_filter_list* source,
                                                             backdrop_filter_list* destination);
API bool_t backdrop_filter_list_equal(const backdrop_filter_list* left,
                                                              const backdrop_filter_list* right);
API void          backdrop_filter_list_free(backdrop_filter_list* list);

/* Transforms retain their authored operations until paint time: percentage
   translations and transform-origin both need the element border-box. The
   values are normalized into a small 2D operation set. */
typedef enum {
    TRANSFORM_TRANSLATE = 0,
    TRANSFORM_SCALE,
    TRANSFORM_ROTATE,
    TRANSFORM_SKEW_X,
    TRANSFORM_SKEW_Y,
    TRANSFORM_MATRIX
} transform_operation_kind_t;

typedef struct {
    int32_t           kind;
    css_length x;
    css_length y;
    float             scalar_x;
    float             scalar_y;
    float             angle_radians;
    float             matrix_a;
    float             matrix_b;
    float             matrix_c;
    float             matrix_d;
    float             matrix_e;
    float             matrix_f;
} transform_operation;

typedef struct {
    transform_operation* items;
    int32_t                     count;
} transform_list;

typedef struct {
    css_length x;
    css_length y;
} transform_origin;

/* Row-vector affine matrix matching the native transform primitives used by
   Direct2D, Skia and CoreGraphics adapters: x' = x*m11 + y*m21 + dx and
   y' = x*m12 + y*m22 + dy. */
typedef struct {
    float m11, m12;
    float m21, m22;
    float dx, dy;
} transform_matrix;

API bool_t transform_list_copy(const transform_list* source,
                                                      transform_list* destination);
API bool_t transform_list_equal(const transform_list* left,
                                                       const transform_list* right);
API void          transform_list_free(transform_list* list);

/* ── Paint value enums ─────────────────────────────────────────────── */

typedef enum {
    TEXT_ALIGN_START = 0,
    TEXT_ALIGN_CENTER,
    TEXT_ALIGN_END,
    TEXT_ALIGN_LEFT,
    TEXT_ALIGN_RIGHT,
    TEXT_ALIGN_JUSTIFY
} text_align_t;

typedef enum {
    FONT_WEIGHT_THIN       = 100,
    FONT_WEIGHT_EXTRA_LIGHT = 200,
    FONT_WEIGHT_LIGHT      = 300,
    FONT_WEIGHT_NORMAL     = 400,
    FONT_WEIGHT_MEDIUM     = 500,
    FONT_WEIGHT_SEMI_BOLD  = 600,
    FONT_WEIGHT_BOLD       = 700,
    FONT_WEIGHT_EXTRA_BOLD = 800,
    FONT_WEIGHT_BLACK      = 900
} font_weight_t;

/* ── Rendering (DisplayList) ───────────────────────────────────────── */

typedef enum {
    PAINT_FILL_ROUNDED_RECT = 0,
    PAINT_DRAW_BOX_SHADOW,
    PAINT_FILL_ROUNDED_RECT_GRADIENT,
    PAINT_STROKE_ROUNDED_RECT,
    PAINT_DRAW_TEXT,
    PAINT_DRAW_TEXT_GRADIENT,
    PAINT_PUSH_CLIP,
    PAINT_POP_CLIP,
    PAINT_FILL_BORDER_SIDE,
    PAINT_DRAW_TEXT_SHADOW,
    PAINT_DRAW_BACKGROUND_IMAGE,
    /* ImageView content: map source_rect of the source image into
     * destination_rect (AOSP ImageView.configureBounds geometry), clipped to
     * the painted view box. */
    PAINT_DRAW_IMAGE,
    PAINT_APPLY_BACKDROP_FILTER,
    PAINT_PUSH_TRANSFORM,
    PAINT_POP_TRANSFORM,
    /* Dynamic retained-composition scope. Bindings resolve this stable id
       against their live scroll state; no layout pointer crosses the ABI. */
    PAINT_PUSH_SCROLL_OFFSET,
    PAINT_POP_SCROLL_OFFSET,
    PAINT_DRAW_SCROLLBAR_CHROME,
    /* Retained-paint metadata. Bindings use these brackets to replace and
       damage only the paint subtree whose state changed. Renderers must not
       paint them. */
    PAINT_BEGIN_PAINT_CHUNK,
    PAINT_END_PAINT_CHUNK,
} paint_command_tag_t;

typedef struct {
    paint_command_tag_t tag;
    union {
        struct { rectf rect; corner_radii radii; color_rgba color; } fill_rounded_rect;
        struct { rectf rect; corner_radii radii; shadow shadow; } draw_box_shadow;
        /* rect is the painted area; gradient_rect owns the 0%-100% gradient coordinate system.
           They differ for clipped/repeated gradient tiles. */
        struct { rectf rect; corner_radii radii; rectf gradient_rect; linear_gradient gradient; float opacity; } fill_rounded_rect_gradient;
        struct { rectf rect; corner_radii radii; float stroke_width; color_rgba color; } stroke_rounded_rect;
        struct { char* text; rectf rect; char font_family[64]; float font_size; int32_t font_weight; int32_t text_align; color_rgba color; bool_t wrap; } draw_text;
        struct { char* text; rectf rect; char font_family[64]; float font_size; int32_t font_weight; int32_t text_align; linear_gradient gradient; float opacity; } draw_text_gradient;
        struct { char* text; rectf rect; char font_family[64]; float font_size; int32_t font_weight; int32_t text_align; shadow shadow; bool_t wrap; } draw_text_shadow;
        struct { rectf rect; corner_radii radii; } push_clip;
        struct { rectf outer_rect; corner_radii outer_radii;
                 rectf inner_rect; corner_radii inner_radii;
                 int32_t side; int32_t style; color_rgba color; } fill_border_side;
        struct { char* source; rectf paint_rect; css_length position_x; css_length position_y;
                 int32_t size_kind; css_length size_x; css_length size_y;
                 int32_t repeat_x; int32_t repeat_y; float opacity;
                 rectf source_rect; rectf destination_rect;
                 bool_t has_resolved_geometry; } draw_background_image;
        struct { char* source; rectf source_rect; rectf destination_rect; } draw_image;
        struct { rectf rect; corner_radii radii;
                 backdrop_filter_list filters; } apply_backdrop_filter;
        struct { transform_matrix matrix; } push_transform;
        struct { int64_t scroll_container_id; } push_scroll_offset;
        struct { int64_t scroll_container_id; } draw_scrollbar_chrome;
        struct { int64_t style_node_id; } paint_chunk;
    } data;
} paint_command_t;

/* Returns sizeof(paint_command_t) for the loaded native library.
   FFI consumers must use this instead of hard-coding a command-buffer size. */
API size_t paint_command_size(void);

typedef struct display_list_s* display_list_t;

API void display_list_destroy(display_list_t list);
API int32_t display_list_get_count(display_list_t list);
API status_t display_list_get_command(display_list_t list, int32_t index, paint_command_t* out_cmd);

API void paint_command_free(paint_command_t* cmd);

/* -- Render planning -------------------------------------------------------
 *
 * Bindings own platform rendering backends, but the engine owns the policy
 * that maps UI invalidation into the next frame's required work. Keep this
 * decision here so each binding does not reimplement it.
 */

typedef enum {
    STYLE_IMPACT_NONE        = 0,
    STYLE_IMPACT_LAYOUT      = 1 << 0,
    STYLE_IMPACT_PAINT       = 1 << 1,
    STYLE_IMPACT_HITTEST     = 1 << 2,
    STYLE_IMPACT_COMPOSITE   = 1 << 3,
    STYLE_IMPACT_TEXT_LAYOUT = 1 << 4,
    STYLE_IMPACT_CURSOR      = 1 << 5,
    STYLE_IMPACT_INHERITED   = 1 << 6
} style_impact_public_t;

typedef enum {
    VISUAL_INVALIDATION_NONE         = 0,
    VISUAL_INVALIDATION_STYLE        = 1 << 0,
    VISUAL_INVALIDATION_LAYOUT       = 1 << 1,
    VISUAL_INVALIDATION_DISPLAY_LIST = 1 << 2,
    VISUAL_INVALIDATION_PAINT_CHUNKS = 1 << 3,
    VISUAL_INVALIDATION_SCROLL       = 1 << 4
} visual_invalidation_t;

typedef struct {
    uint32_t invalidation;
    bool_t markup_dirty;
    bool_t has_pending_pointer_move;
    bool_t display_list_available;
    bool_t display_list_contains_patched_chunks;
    bool_t renderer_supports_incremental;
} render_plan_input_t;

typedef struct {
    uint32_t normalized_invalidation;
    bool_t requires_style;
    bool_t requires_layout;
    bool_t requires_display_list;
    bool_t requires_paint_chunks;
    bool_t pointer_only_render;
    bool_t scroll_only_render;
    bool_t allow_incremental_render;
    bool_t use_scroll_composition;
} render_plan_t;

API uint32_t visual_invalidation_from_style_impact(uint32_t impact);
API uint32_t visual_invalidation_for_scroll(bool_t display_list_contains_patched_chunks);
API status_t render_plan_evaluate(
    const render_plan_input_t* input,
    render_plan_t* out_plan);

/* ── Scroll state (Android pipeline) ───────────────────────────────── */

/* Scroll state is intentionally two-dimensional.  Overflow is resolved per
   axis and a host may preserve either offset independently. */
typedef struct scroll_offset_s {
    float x;
    float y;
} scroll_offset_t;

typedef struct scroll_metrics_s {
    float scrollable_overflow_x;
    float scrollable_overflow_y;
    float scroll_offset_x;
    float scroll_offset_y;
} scroll_metrics_t;

#ifdef __cplusplus
}
#endif

#endif /* H */
