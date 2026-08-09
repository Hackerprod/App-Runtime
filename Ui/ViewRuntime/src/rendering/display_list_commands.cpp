/* Display-list command management: the retained paint-command list owns the
 * storage for every heap-backed command it carries. This translation unit is
 * the single owner of list destruction, indexed command reads (deep copies),
 * and command release — used by the Android recorder and every binding. */

#include "display_list_types.h"
#include "../abi/abi.h"

#include <cstdlib>
#include <cstring>
#include <new>

namespace viewruntime::android_commands {

linear_gradient copy_gradient(const linear_gradient& src) {
    linear_gradient g = src;
    if (src.stop_count > 0 && src.stops) {
        g.stops = static_cast<gradient_stop*>(std::malloc(sizeof(gradient_stop) * src.stop_count));
        if (g.stops) std::memcpy(g.stops, src.stops, sizeof(gradient_stop) * src.stop_count);
    }
    return g;
}

} // namespace viewruntime::android_commands

extern "C" {

API void display_list_destroy(display_list_t list) {
    delete list;
}

API int32_t display_list_get_count(display_list_t list) {
    if (!list) return 0;
    return static_cast<int32_t>(list->commands.size());
}

API status_t display_list_get_command(
    display_list_t list, int32_t index, paint_command_t* out_cmd)
{
    if (!list || !out_cmd) return ERROR_NULL_ARG;
    std::memset(out_cmd, 0, sizeof(*out_cmd));
    if (index < 0 || index >= static_cast<int32_t>(list->commands.size())) {
        return ERROR_INVALID_STATE;
    }

    const auto& src = list->commands[static_cast<size_t>(index)];
    out_cmd->tag = src.tag;

    switch (src.tag) {
        case PAINT_FILL_ROUNDED_RECT:
            out_cmd->data.fill_rounded_rect = src.data.fill_rounded_rect;
            break;
        case PAINT_DRAW_BOX_SHADOW:
            out_cmd->data.draw_box_shadow = src.data.draw_box_shadow;
            break;
        case PAINT_FILL_ROUNDED_RECT_GRADIENT:
            out_cmd->data.fill_rounded_rect_gradient.rect = src.data.fill_rounded_rect_gradient.rect;
            out_cmd->data.fill_rounded_rect_gradient.radii = src.data.fill_rounded_rect_gradient.radii;
            out_cmd->data.fill_rounded_rect_gradient.gradient_rect = src.data.fill_rounded_rect_gradient.gradient_rect;
            out_cmd->data.fill_rounded_rect_gradient.opacity = src.data.fill_rounded_rect_gradient.opacity;
            out_cmd->data.fill_rounded_rect_gradient.gradient =
                viewruntime::android_commands::copy_gradient(src.data.fill_rounded_rect_gradient.gradient);
            break;
        case PAINT_STROKE_ROUNDED_RECT:
            out_cmd->data.stroke_rounded_rect = src.data.stroke_rounded_rect;
            break;
        case PAINT_FILL_BORDER_SIDE:
            out_cmd->data.fill_border_side = src.data.fill_border_side;
            break;
        case PAINT_DRAW_TEXT:
            out_cmd->data.draw_text.text = viewruntime::strdup(src.data.draw_text.text);
            out_cmd->data.draw_text.rect = src.data.draw_text.rect;
            std::strncpy(out_cmd->data.draw_text.font_family, src.data.draw_text.font_family,
                         sizeof(out_cmd->data.draw_text.font_family) - 1);
            out_cmd->data.draw_text.font_size = src.data.draw_text.font_size;
            out_cmd->data.draw_text.font_weight = src.data.draw_text.font_weight;
            out_cmd->data.draw_text.text_align = src.data.draw_text.text_align;
            out_cmd->data.draw_text.color = src.data.draw_text.color;
            out_cmd->data.draw_text.wrap = src.data.draw_text.wrap;
            break;
        case PAINT_DRAW_TEXT_GRADIENT:
            out_cmd->data.draw_text_gradient.text = viewruntime::strdup(src.data.draw_text_gradient.text);
            out_cmd->data.draw_text_gradient.rect = src.data.draw_text_gradient.rect;
            std::strncpy(out_cmd->data.draw_text_gradient.font_family, src.data.draw_text_gradient.font_family,
                         sizeof(out_cmd->data.draw_text_gradient.font_family) - 1);
            out_cmd->data.draw_text_gradient.font_size = src.data.draw_text_gradient.font_size;
            out_cmd->data.draw_text_gradient.font_weight = src.data.draw_text_gradient.font_weight;
            out_cmd->data.draw_text_gradient.text_align = src.data.draw_text_gradient.text_align;
            out_cmd->data.draw_text_gradient.gradient =
                viewruntime::android_commands::copy_gradient(src.data.draw_text_gradient.gradient);
            out_cmd->data.draw_text_gradient.opacity = src.data.draw_text_gradient.opacity;
            break;
        case PAINT_DRAW_TEXT_SHADOW:
            out_cmd->data.draw_text_shadow.text = viewruntime::strdup(src.data.draw_text_shadow.text);
            out_cmd->data.draw_text_shadow.rect = src.data.draw_text_shadow.rect;
            std::strncpy(out_cmd->data.draw_text_shadow.font_family, src.data.draw_text_shadow.font_family,
                         sizeof(out_cmd->data.draw_text_shadow.font_family) - 1);
            out_cmd->data.draw_text_shadow.font_size = src.data.draw_text_shadow.font_size;
            out_cmd->data.draw_text_shadow.font_weight = src.data.draw_text_shadow.font_weight;
            out_cmd->data.draw_text_shadow.text_align = src.data.draw_text_shadow.text_align;
            out_cmd->data.draw_text_shadow.shadow = src.data.draw_text_shadow.shadow;
            out_cmd->data.draw_text_shadow.wrap = src.data.draw_text_shadow.wrap;
            break;
        case PAINT_PUSH_CLIP:
            out_cmd->data.push_clip = src.data.push_clip;
            break;
        case PAINT_DRAW_BACKGROUND_IMAGE:
            out_cmd->data.draw_background_image = src.data.draw_background_image;
            out_cmd->data.draw_background_image.source = viewruntime::strdup(src.data.draw_background_image.source);
            break;
        case PAINT_APPLY_BACKDROP_FILTER:
            out_cmd->data.apply_backdrop_filter.rect = src.data.apply_backdrop_filter.rect;
            out_cmd->data.apply_backdrop_filter.radii = src.data.apply_backdrop_filter.radii;
            (void)backdrop_filter_list_copy(&src.data.apply_backdrop_filter.filters,
                                                   &out_cmd->data.apply_backdrop_filter.filters);
            break;
        case PAINT_PUSH_TRANSFORM:
            out_cmd->data.push_transform = src.data.push_transform;
            break;
        case PAINT_PUSH_SCROLL_OFFSET:
            out_cmd->data.push_scroll_offset = src.data.push_scroll_offset;
            break;
        case PAINT_DRAW_SCROLLBAR_CHROME:
            out_cmd->data.draw_scrollbar_chrome = src.data.draw_scrollbar_chrome;
            break;
        case PAINT_BEGIN_PAINT_CHUNK:
        case PAINT_END_PAINT_CHUNK:
            out_cmd->data.paint_chunk = src.data.paint_chunk;
            break;
        case PAINT_POP_CLIP:
        case PAINT_POP_TRANSFORM:
        case PAINT_POP_SCROLL_OFFSET:
            break;
    }
    return OK;
}

API void paint_command_free(paint_command_t* cmd) {
    if (!cmd) return;
    switch (cmd->tag) {
        case PAINT_DRAW_TEXT:
            std::free(cmd->data.draw_text.text);
            cmd->data.draw_text.text = nullptr;
            break;
        case PAINT_DRAW_TEXT_GRADIENT:
            std::free(cmd->data.draw_text_gradient.text);
            cmd->data.draw_text_gradient.text = nullptr;
            linear_gradient_free(&cmd->data.draw_text_gradient.gradient);
            break;
        case PAINT_DRAW_TEXT_SHADOW:
            std::free(cmd->data.draw_text_shadow.text);
            cmd->data.draw_text_shadow.text = nullptr;
            break;
        case PAINT_FILL_ROUNDED_RECT_GRADIENT:
            linear_gradient_free(&cmd->data.fill_rounded_rect_gradient.gradient);
            break;
        case PAINT_DRAW_BACKGROUND_IMAGE:
            std::free(cmd->data.draw_background_image.source);
            cmd->data.draw_background_image.source = nullptr;
            break;
        case PAINT_APPLY_BACKDROP_FILTER:
            backdrop_filter_list_free(&cmd->data.apply_backdrop_filter.filters);
            break;
        default: break;
    }
}

} // extern "C"
