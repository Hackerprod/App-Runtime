#include "android_types.h"
#include "../rendering/display_list_types.h"
#include "../include/viewruntime/viewruntime_backend.h"

#include <cmath>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <new>
#include <vector>

/* Display-list recorder: walks the measured/layout Android view tree and emits
 * the shared ViewRuntime paint-command model. Commands appended to the display
 * list transfer ownership of heap members to the list. */

namespace viewruntime::android {

const char* display_text(const android_view_s* view);
void record_view(const android_view_s* view, display_list_s* list,
                 const android_ui_s* ui);
void record_list(const android_view_s* view, display_list_s* list,
                 const android_ui_s* ui);

char* dup_string(const char* text) {
    if (!text) return nullptr;
    const size_t n = std::strlen(text);
    auto* out = static_cast<char*>(std::malloc(n + 1));
    if (!out) return nullptr;
    std::memcpy(out, text, n + 1);
    return out;
}

void emit_fill_rounded_rect(display_list_s* list, rectf rect,
                            corner_radii radii, color_rgba color) {
    paint_command_t cmd{};
    cmd.tag = PAINT_FILL_ROUNDED_RECT;
    cmd.data.fill_rounded_rect.rect = rect;
    cmd.data.fill_rounded_rect.radii = radii;
    cmd.data.fill_rounded_rect.color = color;
    list->commands.push_back(cmd);
}

/* OVAL / LINE fill emitters — GradientDrawable shapes java:839-851. These
 * reuse PAINT_FILL_ROUNDED_RECT with the shape in the radii? No — they need
 * dedicated tags. Use the existing tags with the radii encoding the shape:
 * the executor interprets radii.top_left_x == -1 as OVAL and -2 as LINE
 * (sentinel). */
void emit_fill_oval(display_list_s* list, rectf rect, color_rgba color) {
    paint_command_t cmd{};
    cmd.tag = PAINT_FILL_ROUNDED_RECT;
    cmd.data.fill_rounded_rect.rect = rect;
    cmd.data.fill_rounded_rect.radii = {-1.f, -1.f, -1.f, -1.f, -1.f, -1.f, -1.f, -1.f};
    cmd.data.fill_rounded_rect.color = color;
    list->commands.push_back(cmd);
}

void emit_fill_line(display_list_s* list, rectf rect, float width_px,
                    color_rgba color) {
    paint_command_t cmd{};
    cmd.tag = PAINT_FILL_ROUNDED_RECT;
    cmd.data.fill_rounded_rect.rect = rect;
    cmd.data.fill_rounded_rect.radii = {-2.f, -2.f, -2.f, -2.f, -2.f, -2.f, -2.f, -2.f};
    cmd.data.fill_rounded_rect.radii.top_left_y = width_px; /* line thickness */
    cmd.data.fill_rounded_rect.color = color;
    list->commands.push_back(cmd);
}

/* Emit a rounded rect filled with a LINEAR gradient between two colors along
 * the AOSP angle axis (GradientDrawable.java:1822-1851). Uses the existing
 * PAINT_FILL_ROUNDED_RECT_GRADIENT command with two stops; the executor
 * rasterizes via the angle→orientation shader. The angle (degrees) rides in
 * gradient.direction_x — the command has no dedicated angle field and the
 * executor converts it to the AOSP orientation endpoints. The stops are
 * malloc'ed because the display list OWNS them (copy_gradient deep-copies and
 * linear_gradient_free frees — a static/thread_local array would crash). */
void emit_fill_rounded_rect_gradient(display_list_s* list, rectf rect,
                                     corner_radii radii, int32_t angle_deg,
                                     color_rgba start, color_rgba end) {
    gradient_stop* stops = static_cast<gradient_stop*>(
        std::malloc(sizeof(gradient_stop) * 2));
    if (!stops) return;
    stops[0].color = start; stops[0].offset = 0.f; stops[0].has_position = TRUE;
    stops[1].color = end;   stops[1].offset = 1.f; stops[1].has_position = TRUE;
    paint_command_t cmd{};
    cmd.tag = PAINT_FILL_ROUNDED_RECT_GRADIENT;
    cmd.data.fill_rounded_rect_gradient.rect = rect;
    cmd.data.fill_rounded_rect_gradient.radii = radii;
    cmd.data.fill_rounded_rect_gradient.gradient_rect = rect;
    cmd.data.fill_rounded_rect_gradient.opacity = 1.f;
    cmd.data.fill_rounded_rect_gradient.gradient.direction_x =
        static_cast<float>(angle_deg);
    cmd.data.fill_rounded_rect_gradient.gradient.direction_y = 1.f;
    cmd.data.fill_rounded_rect_gradient.gradient.stop_count = 2;
    cmd.data.fill_rounded_rect_gradient.gradient.stops = stops;
    list->commands.push_back(cmd);
}

void emit_stroke_rounded_rect(display_list_s* list, rectf rect,
                              corner_radii radii, float stroke_width,
                              color_rgba color) {
    paint_command_t cmd{};
    cmd.tag = PAINT_STROKE_ROUNDED_RECT;
    cmd.data.stroke_rounded_rect.rect = rect;
    cmd.data.stroke_rounded_rect.radii = radii;
    cmd.data.stroke_rounded_rect.stroke_width = stroke_width;
    cmd.data.stroke_rounded_rect.color = color;
    list->commands.push_back(cmd);
}

void emit_draw_text(display_list_s* list, rectf rect, const char* text,
                    float font_size_px, int32_t text_align, color_rgba color,
                    bool wrap, bool bold = false) {
    char* owned = dup_string(text);
    if (!owned) return;
    paint_command_t cmd{};
    cmd.tag = PAINT_DRAW_TEXT;
    cmd.data.draw_text.text = owned;
    cmd.data.draw_text.rect = rect;
    cmd.data.draw_text.font_size = font_size_px;
    cmd.data.draw_text.font_weight = bold ? FONT_WEIGHT_BOLD : FONT_WEIGHT_NORMAL;
    cmd.data.draw_text.text_align = text_align;
    cmd.data.draw_text.color = color;
    cmd.data.draw_text.wrap = wrap ? TRUE : FALSE;
    list->commands.push_back(cmd);
}

void emit_clip(display_list_s* list, rectf rect, bool push) {
    paint_command_t cmd{};
    cmd.tag = push ? PAINT_PUSH_CLIP : PAINT_POP_CLIP;
    cmd.data.push_clip.rect = rect;
    cmd.data.push_clip.radii = {};
    list->commands.push_back(cmd);
}

void emit_translate(display_list_s* list, float dx, float dy, bool push) {
    paint_command_t cmd{};
    cmd.tag = push ? PAINT_PUSH_TRANSFORM : PAINT_POP_TRANSFORM;
    cmd.data.push_transform.matrix = {1.f, 0.f, 0.f, 1.f, dx, dy};
    list->commands.push_back(cmd);
}

corner_radii zero_radii() { return {}; }

void paint_background(const android_view_s* view, display_list_s* list,
                      const android_ui_s* ui) {
    if (!view->has_background) return;
    color_rgba c = view->background_color;
    color_rgba stroke_c = view->stroke_color;
    /* Drawable-backed backgrounds re-resolve for the current interaction
     * state (pressed/hovered) — the stateless default otherwise. AOSP
     * onStateChange also re-resolves the stroke color (GradientDrawable.java:
     * 1144-1155 mStrokeColors.getColorForState). */
    if (view->background_drawable_id != 0 &&
        (view->pressed || view->hovered)) {
        color_rgba state_c{};
        if (resolve_background_for_state(ui, view, &state_c, &stroke_c)) c = state_c;
    }
    /* Rounded corners from the drawable's <corners android:radius>
     * (GradientDrawable.setCornerRadius, GradientDrawable.java:302) or the
     * per-corner array (java:1668-1685); the backend clamps each to
     * min(radius, min(w,h)*0.5) like AOSP:823. */
    const float r = dp(ui, view->background_corner_radius_dp);
    corner_radii radii{};
    if (view->has_corner_radii) {
        /* AOSP clockwise order (java:1679-1684): tl, tl, tr, tr, br, br, bl, bl.
         * The backend rasterizes a single uniform radius; when the per-corner
         * values differ, the average is used as an honest approximation of the
         * radius array (AOSP Skia scales the array; exact per-corner arcs are
         * not modeled). */
        const float half = std::min(view->bounds.width, view->bounds.height) * 0.5f;
        const float tl = std::min(dp(ui, view->corner_radius_tl_dp), half);
        const float tr = std::min(dp(ui, view->corner_radius_tr_dp), half);
        const float br = std::min(dp(ui, view->corner_radius_br_dp), half);
        const float bl = std::min(dp(ui, view->corner_radius_bl_dp), half);
        const float avg = (tl + tr + br + bl) * 0.25f;
        radii = {avg, avg, avg, avg, avg, avg, avg, avg};
    } else if (r > 0.f) {
        const float half = std::min(view->bounds.width, view->bounds.height) * 0.5f;
        const float rad = std::min(r, half);
        radii = {rad, rad, rad, rad, rad, rad, rad, rad};
    }
    /* GradientDrawable shape dispatch (java:809-860). */
    if (view->gradient_shape == ANDROID_SHAPE_OVAL) {
        /* AOSP insets the oval by strokeWidth/2 when a stroke is declared
         * (ensureValidRect, java:1280-1287) — the fill ends at the stroke's
         * inner edge. */
        const float half_inset = view->has_stroke ? dp(ui, view->stroke_width_dp) * 0.5f : 0.f;
        rectf oval_bounds = view->bounds;
        if (half_inset > 0.f) {
            oval_bounds.x += half_inset;
            oval_bounds.y += half_inset;
            oval_bounds.width = std::max(0.f, oval_bounds.width - 2.f * half_inset);
            oval_bounds.height = std::max(0.f, oval_bounds.height - 2.f * half_inset);
        }
        if (view->has_gradient) {
            /* OVAL with a gradient: AOSP draws the ellipse with the
             * LinearGradient shader (java:840 drawOval(mRect, mFillPaint)).
             * Sentinel radii top_left_x == -1 routes the executor to
             * viewruntime_draw_fill_oval_gradient — a REAL ellipse, not a
             * rounded rect (radius min(w,h)/2 degenerates into a stadium for
             * w != h). */
            corner_radii oval_radii{-1.f, -1.f, -1.f, -1.f, -1.f, -1.f, -1.f, -1.f};
            emit_fill_rounded_rect_gradient(list, oval_bounds, oval_radii,
                                            view->gradient_angle,
                                            view->gradient_start_color,
                                            view->gradient_end_color);
        } else {
            emit_fill_oval(list, oval_bounds, c);
        }
        if (view->has_stroke) {
            /* AOSP strokes the SAME ellipse (java:841-843 drawOval +
             * mStrokePaint). The oval stroke is a band between two ellipses
             * inset by the stroke width — approximate with the rounded-rect
             * stroke at the ellipse radius (the band geometry matches only
             * for circles; for stretched ovals it is the honest rounded-rect
             * band). */
            const float half = std::min(view->bounds.width, view->bounds.height) * 0.5f;
            corner_radii oval_radii{half, half, half, half, half, half, half, half};
            emit_stroke_rounded_rect(list, view->bounds, oval_radii,
                                     dp(ui, view->stroke_width_dp),
                                     stroke_c);
        }
        return;
    }
    if (view->gradient_shape == ANDROID_SHAPE_LINE) {
        /* AOSP draws the LINE only when a stroke is declared, with the STROKE
         * color and width (java:754-755 haveStroke, java:845-851). */
        if (view->has_stroke) {
            emit_fill_line(list, view->bounds, dp(ui, view->stroke_width_dp),
                           stroke_c);
        }
        return;
    }
    /* RECTANGLE (0) and RING (3): RING needs innerRadius/thickness — not
     * parsed yet; falls back to RECTANGLE fill (honest: no ring geometry). */
    /* AOSP insets the fill rect by strokeWidth/2 when a stroke is declared
     * (ensureValidRect, GradientDrawable.java:1280-1287): mRect is inset and
     * BOTH the fill and the stroke use the inset rect. The stroke command
     * below re-derives its own centerline; the fill must therefore paint the
     * inset rect (gradient axis and corner clamp over the inset dims). */
    rectf fill_bounds = view->bounds;
    if (view->has_stroke) {
        const float sw = dp(ui, view->stroke_width_dp);
        const float half = sw * 0.5f;
        if (half > 0.f) {
            fill_bounds.x += half;
            fill_bounds.y += half;
            fill_bounds.width = std::max(0.f, fill_bounds.width - 2.f * half);
            fill_bounds.height = std::max(0.f, fill_bounds.height - 2.f * half);
        }
    }
    if (view->has_gradient) {
        emit_fill_rounded_rect_gradient(list, fill_bounds, radii,
                                        view->gradient_angle,
                                        view->gradient_start_color,
                                        view->gradient_end_color);
    } else {
        emit_fill_rounded_rect(list, fill_bounds, radii, c);
    }
    /* GradientDrawable <stroke>: painted OVER the fill with the same corner
     * radius (drawRoundRect(mRect, rad, rad, mStrokePaint),
     * GradientDrawable.java:825-827). */
    if (view->has_stroke) {
        const float sw = dp(ui, view->stroke_width_dp);
        emit_stroke_rounded_rect(list, view->bounds, radii, sw,
                                 stroke_c);
    }
}

/* Computes the text cell (bounds minus padding) and vertical placement. */
rectf text_rect(const android_view_s* view, const android_ui_s* ui,
                       float text_height_px, float* out_text_align) {
    const float l = dp(ui, view->padding_left_dp), t = dp(ui, view->padding_top_dp);
    const float r = dp(ui, view->padding_right_dp), b = dp(ui, view->padding_bottom_dp);
    const rectf cell = {view->bounds.x + l, view->bounds.y + t,
                               std::max(0.f, view->bounds.width - l - r),
                               std::max(0.f, view->bounds.height - t - b)};
    const int32_t g = gravity_normalize_ltr(view->text_gravity);
    /* Mask + equality, exactly like AOSP Gravity.apply â€” never bitwise `&`
     * on the raw value: CENTER (0x11) shares the 0x01 bit with RIGHT (0x05),
     * so `g & RIGHT` is true for CENTER and misaligns the text to the right
     * (the same class of bug fixed in apply_gravity). */
    float align = TEXT_ALIGN_LEFT;
    const int32_t hgrav = g & ANDROID_GRAVITY_FILL_HORIZONTAL;
    if (hgrav == ANDROID_GRAVITY_RIGHT) align = TEXT_ALIGN_RIGHT;
    else if (hgrav == ANDROID_GRAVITY_CENTER_HORIZONTAL) align = TEXT_ALIGN_CENTER;
    *out_text_align = align;

    float y = cell.y;
    const int32_t vgrav = g & ANDROID_GRAVITY_FILL_VERTICAL;
    if (vgrav == ANDROID_GRAVITY_BOTTOM) y = cell.y + std::max(0.f, cell.height - text_height_px);
    else if (vgrav == ANDROID_GRAVITY_CENTER_VERTICAL) y = cell.y + std::max(0.f, cell.height - text_height_px) * 0.5f;
    return {cell.x, y, cell.width, std::max(0.f, cell.height - (y - cell.y))};
}

void paint_text_view(const android_view_s* view, display_list_s* list,
                     const android_ui_s* ui) {
    paint_background(view, list, ui);
    const char* text = display_text(view);
    if (!text || !*text) return;
    const float size_px = sp(ui, view->text_size_sp);
    const bool wrap = !view->single_line;
    /* Measure with the SAME wrap width the draw pass uses (the text cell), so
     * the line counts agree. Measuring with FLT_MAX but drawing with wrap made
     * the draw produce N wrapped lines while metrics.height counted 1 → the
     * vertical centering in text_rect used the wrong height and the clip cut
     * the extra lines. */
    const float cell_w = std::max(0.f,
        view->bounds.width - dp(ui, view->padding_left_dp) - dp(ui, view->padding_right_dp));
    const android_text_metrics_t metrics = measure_text(ui, text, size_px,
        wrap ? cell_w : std::numeric_limits<float>::max());
    float align = TEXT_ALIGN_LEFT;
    const rectf rect = text_rect(view, ui, metrics.height, &align);
    emit_draw_text(list, rect, text, size_px, static_cast<int32_t>(align),
                   view->text_color, wrap, view->text_bold);
}

void paint_checkable(const android_view_s* view, display_list_s* list,
                     const android_ui_s* ui) {
    paint_background(view, list, ui);
    const bool radio = view->cls == ANDROID_VIEW_RADIO_BUTTON;
    const float size = dp(ui, 16.f);
    const float gap = dp(ui, 8.f);
    const float cy = view->bounds.y + view->bounds.height * 0.5f;
    const rectf indicator = {
        view->bounds.x + dp(ui, view->padding_left_dp), cy - size * 0.5f, size, size};
    if (view->checked) {
        emit_fill_rounded_rect(list, indicator,
            radio ? corner_radii{size * 0.5f, size * 0.5f, size * 0.5f, size * 0.5f,
                                        size * 0.5f, size * 0.5f, size * 0.5f, size * 0.5f}
                  : zero_radii(),
            view->progress_color);
    } else {
        emit_stroke_rounded_rect(list, indicator,
            radio ? corner_radii{size * 0.5f, size * 0.5f, size * 0.5f, size * 0.5f,
                                        size * 0.5f, size * 0.5f, size * 0.5f, size * 0.5f}
                  : zero_radii(),
            dp(ui, 1.5f), {1.f, 0.55f, 0.55f, 0.55f});
    }
    const char* text = display_text(view);
    if (!text || !*text) return;
    const float size_px = sp(ui, view->text_size_sp);
    const android_text_metrics_t metrics = measure_text(ui, text, size_px,
        std::numeric_limits<float>::max());
    const float text_x = indicator.x + indicator.width + gap;
    const float text_top = view->bounds.y + dp(ui, view->padding_top_dp);
    emit_draw_text(list, {text_x, text_top,
                          std::max(0.f, view->bounds.x + view->bounds.width -
                              dp(ui, view->padding_right_dp) - text_x),
                          std::max(0.f, view->bounds.height - dp(ui, view->padding_top_dp) -
                              dp(ui, view->padding_bottom_dp))},
                   text, size_px, TEXT_ALIGN_LEFT, view->text_color, false);
}

void paint_image(const android_view_s* view, display_list_s* list,
                 const android_ui_s* ui) {
    paint_background(view, list, ui);
    if (view->image_source.empty() || !view->image_has_geometry) return;
    /* The geometry (AOSP configureBounds) is content-relative; offset it into
     * the view box (bounds + padding). */
    const float ox = view->bounds.x + dp(ui, view->padding_left_dp);
    const float oy = view->bounds.y + dp(ui, view->padding_top_dp);
    char* source = dup_string(view->image_source.c_str());
    if (!source) return;
    paint_command_t cmd{};
    cmd.tag = PAINT_DRAW_IMAGE;
    cmd.data.draw_image.source = source;
    cmd.data.draw_image.source_rect = view->image_src_rect;
    cmd.data.draw_image.destination_rect = {
        ox + view->image_dst_rect.x, oy + view->image_dst_rect.y,
        view->image_dst_rect.width, view->image_dst_rect.height};
    list->commands.push_back(cmd);
}

void paint_progress(const android_view_s* view, display_list_s* list,
                    const android_ui_s* ui) {
    paint_background(view, list, ui);
    const float l = dp(ui, view->padding_left_dp), t = dp(ui, view->padding_top_dp);
    const float r = dp(ui, view->padding_right_dp), b = dp(ui, view->padding_bottom_dp);
    const rectf track = {view->bounds.x + l, view->bounds.y + t,
                                std::max(0.f, view->bounds.width - l - r),
                                std::max(0.f, view->bounds.height - t - b)};
    if (track.width <= 0.f || track.height <= 0.f) return;
    emit_fill_rounded_rect(list, track, zero_radii(), view->track_color);
    const int32_t span = view->progress_max - view->progress_min;
    if (span <= 0 || view->progress_value <= view->progress_min) return;
    const float fraction = static_cast<float>(view->progress_value - view->progress_min) /
                           static_cast<float>(span);
    const rectf fill = {track.x, track.y, track.width * fraction, track.height};
    emit_fill_rounded_rect(list, fill, zero_radii(), view->progress_color);
}

void record_children(const android_view_s* view, display_list_s* list,
                     const android_ui_s* ui) {
    for (const android_view_s* child : view->children) {
        record_view(child, list, ui);
    }
}

/* ListView / RecyclerView: clip to the viewport, record children (already in
 * screen space), then draw the ListView dividers per AOSP dispatchDraw. */
void record_list(const android_view_s* view, display_list_s* list, const android_ui_s* ui) {
    paint_background(view, list, ui);
    emit_clip(list, view->bounds, true);
    const bool is_list = view->cls == ANDROID_VIEW_LIST_VIEW;
    const bool vertical = is_list || view->orientation == ANDROID_VERTICAL;
    const float pad_left = dp(ui, view->padding_left_dp);
    const float pad_top = dp(ui, view->padding_top_dp);
    const float pad_right = dp(ui, view->padding_right_dp);
    const float pad_bottom = dp(ui, view->padding_bottom_dp);
    const size_t n = view->children.size();
    for (size_t i = 0; i < n; ++i) {
        const android_view_s* child = view->children[i];
        if (child->visibility != ANDROID_VISIBLE) continue;
        record_view(child, list, ui);
        if (!is_list || !view->divider_enabled || !child->enabled) continue;
        if (i + 1 < n && !view->children[i + 1]->enabled) continue;
        const float bottom = vertical
            ? child->bounds.y + child->bounds.height
            : child->bounds.x + child->bounds.width;
        const float limit = vertical
            ? (view->bounds.y + view->bounds.height - pad_bottom)
            : (view->bounds.x + view->bounds.width - pad_right);
        if (bottom >= limit) continue;
        const float dv = dp(ui, view->divider_height_dp);
        const rectf r = vertical
            ? rectf{view->bounds.x + pad_left, bottom,
                    std::max(0.f, view->bounds.width - pad_left - pad_right), dv}
            : rectf{bottom, view->bounds.y + pad_top, dv,
                    std::max(0.f, view->bounds.height - pad_top - pad_bottom)};
        emit_fill_rounded_rect(list, r, zero_radii(), view->divider_color);
    }
    emit_clip(list, view->bounds, false);
}

void record_view(const android_view_s* view, display_list_s* list,
                 const android_ui_s* ui) {
    if (view->visibility != ANDROID_VISIBLE) return;
    switch (view->cls) {
        case ANDROID_VIEW_TEXT_VIEW:
        case ANDROID_VIEW_BUTTON:
        case ANDROID_VIEW_EDIT_TEXT:
            paint_text_view(view, list, ui);
            return;
        case ANDROID_VIEW_CHECK_BOX:
        case ANDROID_VIEW_RADIO_BUTTON:
            paint_checkable(view, list, ui);
            return;
        case ANDROID_VIEW_IMAGE_VIEW:
            paint_image(view, list, ui);
            return;
        case ANDROID_VIEW_PROGRESS_BAR:
            paint_progress(view, list, ui);
            return;
        case ANDROID_VIEW_SCROLL_VIEW:
            paint_background(view, list, ui);
            emit_clip(list, view->bounds, true);
            /* Children are laid out in screen space (bounds already include the
             * clamped scroll), so only clipping is needed â€” never translate. */
            record_children(view, list, ui);
            emit_clip(list, view->bounds, false);
            return;
        case ANDROID_VIEW_LIST_VIEW:
        case ANDROID_VIEW_RECYCLER_VIEW:
            record_list(view, list, ui);
            return;
        case ANDROID_VIEW_GRID_LAYOUT:
        case ANDROID_VIEW_RELATIVE_LAYOUT:
            paint_background(view, list, ui);
            record_children(view, list, ui);
            return;
        default:
            break;
    }
    paint_background(view, list, ui);
    record_children(view, list, ui);
}

} // namespace viewruntime::android

extern "C" {

API status_t android_ui_record(
    android_ui_t ui, android_view_t root, display_list_t* out_list) {
    if (!ui || !root || !out_list || root->ui != ui) return ERROR_NULL_ARG;
    *out_list = nullptr;
    auto* list = new (std::nothrow) display_list_s();
    if (!list) return ERROR_OUT_OF_MEMORY;
    viewruntime::android::record_view(root, list, ui);
    *out_list = list;
    return OK;
}

/* Execute a recorded display list onto the session's registered render
 * surface. This is the ONLY path that turns recorded commands into pixels â€”
 * the host never interprets commands itself (Phase 2 ownership). Commands
 * without a backend mapping are skipped, never approximated. */
API status_t android_ui_render(
    android_ui_t ui, display_list_t list) {
    if (!ui || !list || !ui->surface) return ERROR_NULL_ARG;
    viewruntime_frame_begin(ui->surface);
    const int32_t count = display_list_get_count(list);
    for (int32_t i = 0; i < count; ++i) {
        paint_command_t cmd{};
        if (display_list_get_command(list, i, &cmd) != OK) continue;
        switch (cmd.tag) {
            case PAINT_PUSH_CLIP: {
                const rectf& r = cmd.data.push_clip.rect;
                viewruntime_clip_push(ui->surface, r.x, r.y, r.width, r.height);
                break;
            }
            case PAINT_POP_CLIP: {
                viewruntime_clip_pop(ui->surface);
                break;
            }
            case PAINT_FILL_ROUNDED_RECT_GRADIENT: {
                const auto& d = cmd.data.fill_rounded_rect_gradient;
                const rectf& r = d.rect;
                const float rad = d.radii.top_left_x;
                const int32_t angle = static_cast<int32_t>(d.gradient.direction_x);
                /* Two stops: start (offset 0) and end (offset 1) — the AOSP
                 * GradientDrawable startColor/endColor pair. */
                color_rgba s0{1.f,1.f,1.f,1.f}, s1{0.f,0.f,0.f,0.f};
                if (d.gradient.stop_count >= 1) s0 = d.gradient.stops[0].color;
                if (d.gradient.stop_count >= 2) s1 = d.gradient.stops[1].color;
                if (rad == -1.f) {
                    /* Sentinel radii (emit_fill_rounded_rect_gradient from
                     * the OVAL branch): a REAL ellipse with the gradient,
                     * NOT a rounded rect (which would be a stadium for
                     * w != h — GradientDrawable.java:840 drawOval). */
                    viewruntime_draw_fill_oval_gradient(
                        ui->surface, r.x, r.y, r.width, r.height, angle,
                        static_cast<uint8_t>(s0.a * 255.f),
                        static_cast<uint8_t>(s0.r * 255.f),
                        static_cast<uint8_t>(s0.g * 255.f),
                        static_cast<uint8_t>(s0.b * 255.f),
                        static_cast<uint8_t>(s1.a * 255.f),
                        static_cast<uint8_t>(s1.r * 255.f),
                        static_cast<uint8_t>(s1.g * 255.f),
                        static_cast<uint8_t>(s1.b * 255.f), 0);
                } else {
                    viewruntime_draw_fill_rounded_rect_gradient(
                        ui->surface, r.x, r.y, r.width, r.height, rad, angle,
                        static_cast<uint8_t>(s0.a * 255.f),
                        static_cast<uint8_t>(s0.r * 255.f),
                        static_cast<uint8_t>(s0.g * 255.f),
                        static_cast<uint8_t>(s0.b * 255.f),
                        static_cast<uint8_t>(s1.a * 255.f),
                        static_cast<uint8_t>(s1.r * 255.f),
                        static_cast<uint8_t>(s1.g * 255.f),
                        static_cast<uint8_t>(s1.b * 255.f), 0);
                }
                break;
            }
            case PAINT_FILL_ROUNDED_RECT: {
                const color_rgba& c = cmd.data.fill_rounded_rect.color;
                const rectf& r = cmd.data.fill_rounded_rect.rect;
                const float rad0 = cmd.data.fill_rounded_rect.radii.top_left_x;
                /* Sentinel shapes (emit_fill_oval / emit_fill_line):
                 * -1 = OVAL, -2 = LINE (thickness in top_left_y). */
                if (rad0 == -1.f) {
                    viewruntime_draw_fill_oval(ui->surface, r.x, r.y, r.width, r.height,
                        static_cast<uint8_t>(c.a * 255.f),
                        static_cast<uint8_t>(c.r * 255.f),
                        static_cast<uint8_t>(c.g * 255.f),
                        static_cast<uint8_t>(c.b * 255.f), 0);
                } else if (rad0 == -2.f) {
                    viewruntime_draw_fill_line(ui->surface, r.x, r.y, r.width, r.height,
                        cmd.data.fill_rounded_rect.radii.top_left_y,
                        static_cast<uint8_t>(c.a * 255.f),
                        static_cast<uint8_t>(c.r * 255.f),
                        static_cast<uint8_t>(c.g * 255.f),
                        static_cast<uint8_t>(c.b * 255.f), 0);
                } else if (rad0 > 0.f) {
                    viewruntime_draw_fill_rounded_rect(ui->surface, r.x, r.y, r.width, r.height,
                        rad0,
                        static_cast<uint8_t>(c.a * 255.f),
                        static_cast<uint8_t>(c.r * 255.f),
                        static_cast<uint8_t>(c.g * 255.f),
                        static_cast<uint8_t>(c.b * 255.f), 0);
                } else {
                    viewruntime_draw_fill_rect(ui->surface, r.x, r.y, r.width, r.height,
                        static_cast<uint8_t>(c.a * 255.f),
                        static_cast<uint8_t>(c.r * 255.f),
                        static_cast<uint8_t>(c.g * 255.f),
                        static_cast<uint8_t>(c.b * 255.f), 0);
                }
                break;
            }
            case PAINT_STROKE_ROUNDED_RECT: {
                const auto& d = cmd.data.stroke_rounded_rect;
                const rectf& r = d.rect;
                const color_rgba& c = d.color;
                viewruntime_draw_stroke_rounded_rect(
                    ui->surface, r.x, r.y, r.width, r.height,
                    d.radii.top_left_x, d.stroke_width, 0.f, 0.f,
                    static_cast<uint8_t>(c.a * 255.f),
                    static_cast<uint8_t>(c.r * 255.f),
                    static_cast<uint8_t>(c.g * 255.f),
                    static_cast<uint8_t>(c.b * 255.f), 0);
                break;
            }
            case PAINT_DRAW_TEXT: {
                const auto& d = cmd.data.draw_text;
                const rectf& r = d.rect;
                /* UTF-16 width is bounded by the text byte length; the backend
                 * needs a uint16 buffer, so convert the UTF-8 command text. */
                const size_t len = std::strlen(d.text ? d.text : "");
                std::vector<uint16_t> utf16(len + 1);
                size_t n = 0;
                const char* p = d.text;
                while (p && *p) {
                    unsigned int cp = 0;
                    const unsigned char c0 = static_cast<unsigned char>(*p);
                    if (c0 < 0x80) { cp = c0; ++p; }
                    else if ((c0 & 0xE0) == 0xC0 && p[1]) {
                        cp = ((c0 & 0x1F) << 6) | (p[1] & 0x3F); p += 2;
                    } else if ((c0 & 0xF0) == 0xE0 && p[1] && p[2]) {
                        cp = ((c0 & 0x0F) << 12) | ((p[1] & 0x3F) << 6) | (p[2] & 0x3F); p += 3;
                    } else if ((c0 & 0xF8) == 0xF0 && p[1] && p[2] && p[3]) {
                        const uint32_t v = ((c0 & 0x07) << 18) | ((p[1] & 0x3F) << 12) |
                                           ((p[2] & 0x3F) << 6) | (p[3] & 0x3F);
                        cp = v;
                        p += 4; /* ALWAYS advance — see surrogate branch below */
                        if (cp >= 0x10000) {
                            /* supplementary plane: emit a UTF-16 surrogate
                             * pair. p was advanced above; do NOT continue
                             * without advancing (the old code skipped p += 4
                             * → infinite loop + heap overflow on any
                             * character >= 0x10000). */
                            utf16[n++] = static_cast<uint16_t>(0xD800 + ((cp - 0x10000) >> 10));
                            utf16[n++] = static_cast<uint16_t>(0xDC00 + ((cp - 0x10000) & 0x3FF));
                            continue;
                        }
                    } else { cp = 0xFFFD; ++p; }
                    if (cp >= 0x10000) continue; /* handled above */
                    utf16[n++] = static_cast<uint16_t>(cp);
                }
                viewruntime_draw_text(ui->surface, r.x, r.y, r.width, r.height,
                                      utf16.data(), static_cast<int32_t>(n),
                                      d.font_size,
                                      static_cast<uint8_t>(d.color.a * 255.f),
                                      static_cast<uint8_t>(d.color.r * 255.f),
                                      static_cast<uint8_t>(d.color.g * 255.f),
                                      static_cast<uint8_t>(d.color.b * 255.f), 0,
                                      d.text_align,
                                      d.font_weight == FONT_WEIGHT_BOLD ? 1 : 0,
                                      d.wrap);
                break;
            }
            case PAINT_DRAW_IMAGE: {
                const auto& d = cmd.data.draw_image;
                const rectf& sr = d.source_rect;
                const rectf& dr = d.destination_rect;
                viewruntime_draw_image(ui->surface, d.source ? d.source : "",
                                       sr.x, sr.y, sr.width, sr.height,
                                       dr.x, dr.y, dr.width, dr.height, 0);
                break;
            }
            default:
                /* Metadata / unbacked commands are skipped, never invented. */
                break;
        }
        paint_command_free(&cmd);
    }
    viewruntime_frame_end(ui->surface);
    return OK;
}

} // extern "C"
