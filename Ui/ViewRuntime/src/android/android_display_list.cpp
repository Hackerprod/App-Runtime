#include "android_types.h"
#include "../rendering/display_list_types.h"

#include <cmath>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <new>

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
                    bool wrap) {
    char* owned = dup_string(text);
    if (!owned) return;
    paint_command_t cmd{};
    cmd.tag = PAINT_DRAW_TEXT;
    cmd.data.draw_text.text = owned;
    cmd.data.draw_text.rect = rect;
    cmd.data.draw_text.font_size = font_size_px;
    cmd.data.draw_text.font_weight = FONT_WEIGHT_NORMAL;
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

void paint_background(const android_view_s* view, display_list_s* list) {
    if (!view->has_background) return;
    emit_fill_rounded_rect(list, view->bounds, zero_radii(), view->background_color);
}

/* Computes the text cell (bounds minus padding) and vertical placement. */
rectf text_rect(const android_view_s* view, const android_ui_s* ui,
                       float text_height_px, float* out_text_align) {
    const float l = dp(ui, view->padding_left_dp), t = dp(ui, view->padding_top_dp);
    const float r = dp(ui, view->padding_right_dp), b = dp(ui, view->padding_bottom_dp);
    const rectf cell = {view->bounds.x + l, view->bounds.y + t,
                               std::max(0.f, view->bounds.width - l - r),
                               std::max(0.f, view->bounds.height - t - b)};
    const int32_t g = view->text_gravity;
    float align = TEXT_ALIGN_LEFT;
    if (g & (ANDROID_GRAVITY_RIGHT)) align = TEXT_ALIGN_RIGHT;
    else if (g & (ANDROID_GRAVITY_CENTER_HORIZONTAL | ANDROID_GRAVITY_CENTER)) align = TEXT_ALIGN_CENTER;
    *out_text_align = align;

    float y = cell.y;
    if (g & (ANDROID_GRAVITY_BOTTOM)) y = cell.y + std::max(0.f, cell.height - text_height_px);
    else if (g & (ANDROID_GRAVITY_CENTER_VERTICAL | ANDROID_GRAVITY_CENTER)) y = cell.y + std::max(0.f, cell.height - text_height_px) * 0.5f;
    return {cell.x, y, cell.width, std::max(0.f, cell.height - (y - cell.y))};
}

void paint_text_view(const android_view_s* view, display_list_s* list,
                     const android_ui_s* ui) {
    paint_background(view, list);
    const char* text = display_text(view);
    if (!text || !*text) return;
    const float size_px = sp(ui, view->text_size_sp);
    const android_text_metrics_t metrics = measure_text(ui, text, size_px,
        std::numeric_limits<float>::max());
    float align = TEXT_ALIGN_LEFT;
    const rectf rect = text_rect(view, ui, metrics.height, &align);
    emit_draw_text(list, rect, text, size_px, static_cast<int32_t>(align),
                   view->text_color, !view->single_line);
}

void paint_checkable(const android_view_s* view, display_list_s* list,
                     const android_ui_s* ui) {
    paint_background(view, list);
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
    paint_background(view, list);
    if (view->image_source.empty() || !ui->image_dimensions) return;
    sizef intrinsic{0.f, 0.f};
    if (ui->image_dimensions(view->image_source.c_str(), &intrinsic, ui->image_dimensions_data) != TRUE) {
        return;
    }
    if (intrinsic.width <= 0.f || intrinsic.height <= 0.f) return;
    const float l = dp(ui, view->padding_left_dp), t = dp(ui, view->padding_top_dp);
    const float r = dp(ui, view->padding_right_dp), b = dp(ui, view->padding_bottom_dp);
    const rectf avail = {view->bounds.x + l, view->bounds.y + t,
                                std::max(0.f, view->bounds.width - l - r),
                                std::max(0.f, view->bounds.height - t - b)};
    float dest_w = 0.f, dest_h = 0.f, dest_x = avail.x, dest_y = avail.y;
    const float scale_x = avail.width / intrinsic.width;
    const float scale_y = avail.height / intrinsic.height;
    switch (view->scale_type) {
        case ANDROID_SCALE_FIT_XY:
            dest_w = avail.width; dest_h = avail.height; break;
        case ANDROID_SCALE_CENTER:
            dest_w = intrinsic.width; dest_h = intrinsic.height;
            dest_x = avail.x + (avail.width - dest_w) * 0.5f;
            dest_y = avail.y + (avail.height - dest_h) * 0.5f;
            break;
        case ANDROID_SCALE_CENTER_INSIDE: {
            const float scale = scale_x < scale_y ? scale_x : scale_y;
            const float s = scale < 1.f ? scale : 1.f;
            dest_w = intrinsic.width * s; dest_h = intrinsic.height * s;
            dest_x = avail.x + (avail.width - dest_w) * 0.5f;
            dest_y = avail.y + (avail.height - dest_h) * 0.5f;
            break;
        }
        case ANDROID_SCALE_CENTER_CROP: {
            const float scale = scale_x > scale_y ? scale_x : scale_y;
            dest_w = intrinsic.width * scale; dest_h = intrinsic.height * scale;
            dest_x = avail.x + (avail.width - dest_w) * 0.5f;
            dest_y = avail.y + (avail.height - dest_h) * 0.5f;
            break;
        }
        case ANDROID_SCALE_FIT_START: {
            const float scale = scale_x < scale_y ? scale_x : scale_y;
            dest_w = intrinsic.width * scale; dest_h = intrinsic.height * scale;
            break;
        }
        case ANDROID_SCALE_FIT_END: {
            const float scale = scale_x < scale_y ? scale_x : scale_y;
            dest_w = intrinsic.width * scale; dest_h = intrinsic.height * scale;
            dest_x = avail.x + (avail.width - dest_w);
            dest_y = avail.y + (avail.height - dest_h);
            break;
        }
        default: { /* FIT_CENTER */
            const float scale = scale_x < scale_y ? scale_x : scale_y;
            dest_w = intrinsic.width * scale; dest_h = intrinsic.height * scale;
            dest_x = avail.x + (avail.width - dest_w) * 0.5f;
            dest_y = avail.y + (avail.height - dest_h) * 0.5f;
            break;
        }
    }
    char* source = dup_string(view->image_source.c_str());
    if (!source) return;
    paint_command_t cmd{};
    cmd.tag = PAINT_DRAW_BACKGROUND_IMAGE;
    cmd.data.draw_background_image.source = source;
    cmd.data.draw_background_image.paint_rect = {dest_x, dest_y, dest_w, dest_h};
    cmd.data.draw_background_image.position_x = css_length_zero();
    cmd.data.draw_background_image.position_y = css_length_zero();
    cmd.data.draw_background_image.size_kind = BACKGROUND_SIZE_EXPLICIT;
    cmd.data.draw_background_image.size_x = css_length_zero();
    cmd.data.draw_background_image.size_y = css_length_zero();
    cmd.data.draw_background_image.repeat_x = BACKGROUND_NO_REPEAT;
    cmd.data.draw_background_image.repeat_y = BACKGROUND_NO_REPEAT;
    cmd.data.draw_background_image.opacity = 1.f;
    cmd.data.draw_background_image.source_rect = {0.f, 0.f, intrinsic.width, intrinsic.height};
    cmd.data.draw_background_image.destination_rect = {dest_x, dest_y, dest_w, dest_h};
    cmd.data.draw_background_image.has_resolved_geometry = TRUE;
    list->commands.push_back(cmd);
}

void paint_progress(const android_view_s* view, display_list_s* list,
                    const android_ui_s* ui) {
    paint_background(view, list);
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
    paint_background(view, list);
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
            paint_background(view, list);
            emit_clip(list, view->bounds, true);
            /* Children are laid out in screen space (bounds already include the
             * clamped scroll), so only clipping is needed — never translate. */
            record_children(view, list, ui);
            emit_clip(list, view->bounds, false);
            return;
        case ANDROID_VIEW_LIST_VIEW:
        case ANDROID_VIEW_RECYCLER_VIEW:
            record_list(view, list, ui);
            return;
        case ANDROID_VIEW_GRID_LAYOUT:
        case ANDROID_VIEW_RELATIVE_LAYOUT:
            paint_background(view, list);
            record_children(view, list, ui);
            return;
        default:
            break;
    }
    paint_background(view, list);
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

} // extern "C"
