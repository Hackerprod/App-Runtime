#include "abi.h"

#include <cstdlib>
#include <cstring>
#include <limits>

namespace {

bool gradients_equal(const linear_gradient& left, const linear_gradient& right) {
    if (left.direction_x != right.direction_x || left.direction_y != right.direction_y ||
        left.stop_count != right.stop_count) return false;
    if (left.stop_count < 0 || (left.stop_count > 0 && (!left.stops || !right.stops))) return false;
    return left.stop_count == 0 || std::memcmp(left.stops, right.stops,
        sizeof(gradient_stop) * static_cast<size_t>(left.stop_count)) == 0;
}

bool copy_gradient(const linear_gradient& source, linear_gradient* destination) {
    if (!destination || source.stop_count < 0 || (source.stop_count > 0 && !source.stops)) return false;
    *destination = source;
    destination->stops = nullptr;
    if (source.stop_count == 0) return true;
    const size_t count = static_cast<size_t>(source.stop_count);
    if (count > (std::numeric_limits<size_t>::max)() / sizeof(gradient_stop)) return false;
    destination->stops = static_cast<gradient_stop*>(std::malloc(sizeof(gradient_stop) * count));
    if (!destination->stops) return false;
    std::memcpy(destination->stops, source.stops, sizeof(gradient_stop) * count);
    return true;
}

char* copy_string(const char* source) {
    if (!source) return nullptr;
    const size_t length = std::strlen(source);
    if (length == (std::numeric_limits<size_t>::max)()) return nullptr;
    auto* destination = static_cast<char*>(std::malloc(length + 1));
    if (!destination) return nullptr;
    std::memcpy(destination, source, length + 1);
    return destination;
}

void free_background_layer(background_layer* layer) {
    if (!layer) return;
    linear_gradient_free(&layer->gradient);
    std::free(layer->image_source);
    *layer = {};
}

bool copy_background_layer(const background_layer& source, background_layer* destination) {
    if (!destination) return false;
    *destination = source;
    destination->gradient.stops = nullptr;
    destination->image_source = nullptr;
    if (source.kind == BACKGROUND_LAYER_LINEAR_GRADIENT) {
        return copy_gradient(source.gradient, &destination->gradient);
    }
    if (source.kind == BACKGROUND_LAYER_IMAGE_URL && source.image_source) {
        destination->image_source = copy_string(source.image_source);
        return destination->image_source != nullptr;
    }
    return false;
}

bool background_layers_equal(const background_layer& left, const background_layer& right) {
    if (left.kind != right.kind ||
        std::memcmp(&left.position_x, &right.position_x, sizeof(css_length)) != 0 ||
        std::memcmp(&left.position_y, &right.position_y, sizeof(css_length)) != 0 ||
        left.size_kind != right.size_kind ||
        std::memcmp(&left.size_x, &right.size_x, sizeof(css_length)) != 0 ||
        std::memcmp(&left.size_y, &right.size_y, sizeof(css_length)) != 0 ||
        left.repeat_x != right.repeat_x || left.repeat_y != right.repeat_y) return false;
    if (left.kind == BACKGROUND_LAYER_LINEAR_GRADIENT)
        return gradients_equal(left.gradient, right.gradient);
    if (left.kind == BACKGROUND_LAYER_IMAGE_URL)
        return left.image_source && right.image_source && std::strcmp(left.image_source, right.image_source) == 0;
    return false;
}

} // namespace

extern "C" {

API uint32_t abi_version(void) {
    return ABI_VERSION_CURRENT;
}

API capabilities_t abi_capabilities(void) {
    return CAPABILITY_DISPLAY_LIST |
           CAPABILITY_RENDER_PLAN |
           CAPABILITY_ANDROID_UI;
}

API size_t paint_command_size(void) {
    return sizeof(paint_command_t);
}

API const char* status_message(status_t status) {
    switch (status) {
        case OK:                   return "OK";
        case ERROR_NULL_ARG:       return "Null argument";
        case ERROR_INVALID_STATE:  return "Invalid state";
        case ERROR_OUT_OF_MEMORY:  return "Out of memory";
        case ERROR_DISPOSED:       return "Object disposed";
        case ERROR_PARSE_FAILED:   return "Parse failed";
        default:                          return "Unknown error";
    }
}

API void string_free(char* str) {
    free(str);
}

API void linear_gradient_free(linear_gradient* g) {
    if (g && g->stops) {
        free(g->stops);
        g->stops = nullptr;
        g->stop_count = 0;
    }
}

API void shadow_list_free(shadow_list* list) {
    if (!list) return;
    free(list->items);
    list->items = nullptr;
    list->count = 0;
}

API void background_layer_list_free(background_layer_list* list) {
    if (!list) return;
    if (list->items && list->count > 0) {
        for (int32_t index = 0; index < list->count; ++index) free_background_layer(&list->items[index]);
    }
    std::free(list->items);
    list->items = nullptr;
    list->count = 0;
}

API void backdrop_filter_list_free(backdrop_filter_list* list) {
    if (!list) return;
    std::free(list->items);
    list->items = nullptr;
    list->count = 0;
}

API void transform_list_free(transform_list* list) {
    if (!list) return;
    std::free(list->items);
    list->items = nullptr;
    list->count = 0;
}

API bool_t transform_list_copy(const transform_list* source,
                                                      transform_list* destination) {
    if (!destination) return FALSE;
    if (source == destination) return TRUE;
    if (!source || source->count < 0 || (source->count > 0 && !source->items)) {
        transform_list_free(destination);
        return FALSE;
    }
    transform_list replacement{};
    if (source->count > 0) {
        const size_t count = static_cast<size_t>(source->count);
        if (count > (std::numeric_limits<size_t>::max)() / sizeof(transform_operation)) {
            transform_list_free(destination);
            return FALSE;
        }
        replacement.items = static_cast<transform_operation*>(
            std::malloc(sizeof(transform_operation) * count));
        if (!replacement.items) {
            transform_list_free(destination);
            return FALSE;
        }
        std::memcpy(replacement.items, source->items, sizeof(transform_operation) * count);
        replacement.count = source->count;
    }
    transform_list_free(destination);
    *destination = replacement;
    return TRUE;
}

API bool_t transform_list_equal(const transform_list* left,
                                                       const transform_list* right) {
    if (!left || !right || left->count < 0 || right->count < 0 || left->count != right->count)
        return FALSE;
    if (left->count == 0) return TRUE;
    if (!left->items || !right->items) return FALSE;
    return std::memcmp(left->items, right->items,
        sizeof(transform_operation) * static_cast<size_t>(left->count)) == 0 ? TRUE : FALSE;
}

API bool_t backdrop_filter_list_copy(const backdrop_filter_list* source,
                                                            backdrop_filter_list* destination) {
    if (!destination) return FALSE;
    if (source == destination) return TRUE;
    if (!source || source->count < 0 || (source->count > 0 && !source->items)) {
        backdrop_filter_list_free(destination);
        return FALSE;
    }
    backdrop_filter_list replacement{};
    if (source->count > 0) {
        const auto count = static_cast<size_t>(source->count);
        if (count > (std::numeric_limits<size_t>::max)() / sizeof(backdrop_filter)) {
            backdrop_filter_list_free(destination);
            return FALSE;
        }
        replacement.items = static_cast<backdrop_filter*>(std::malloc(sizeof(backdrop_filter) * count));
        if (!replacement.items) { backdrop_filter_list_free(destination); return FALSE; }
        std::memcpy(replacement.items, source->items, sizeof(backdrop_filter) * count);
        replacement.count = source->count;
    }
    backdrop_filter_list_free(destination);
    *destination = replacement;
    return TRUE;
}

API bool_t backdrop_filter_list_equal(const backdrop_filter_list* left,
                                                             const backdrop_filter_list* right) {
    if (!left || !right || left->count < 0 || right->count < 0 || left->count != right->count) return FALSE;
    if (left->count == 0) return TRUE;
    if (!left->items || !right->items) return FALSE;
    return std::memcmp(left->items, right->items,
        sizeof(backdrop_filter) * static_cast<size_t>(left->count)) == 0 ? TRUE : FALSE;
}

API bool_t background_layer_list_copy(const background_layer_list* source,
                                                            background_layer_list* destination) {
    if (!destination) return FALSE;
    if (source == destination) return TRUE;
    if (!source || source->count < 0 || (source->count > 0 && !source->items)) {
        background_layer_list_free(destination);
        return FALSE;
    }

    background_layer_list replacement{};
    if (source->count > 0) {
        const size_t count = static_cast<size_t>(source->count);
        if (count > (std::numeric_limits<size_t>::max)() / sizeof(background_layer)) {
            background_layer_list_free(destination);
            return FALSE;
        }
        replacement.items = static_cast<background_layer*>(std::calloc(count, sizeof(background_layer)));
        if (!replacement.items) {
            background_layer_list_free(destination);
            return FALSE;
        }
        replacement.count = source->count;
        for (int32_t index = 0; index < source->count; ++index) {
            if (!copy_background_layer(source->items[index], &replacement.items[index])) {
                background_layer_list_free(&replacement);
                background_layer_list_free(destination);
                return FALSE;
            }
        }
    }

    background_layer_list_free(destination);
    *destination = replacement;
    return TRUE;
}

API bool_t background_layer_list_equal(const background_layer_list* left,
                                                             const background_layer_list* right) {
    if (!left || !right || left->count < 0 || right->count < 0 || left->count != right->count)
        return FALSE;
    if (left->count == 0) return TRUE;
    if (!left->items || !right->items) return FALSE;
    for (int32_t index = 0; index < left->count; ++index) {
        if (!background_layers_equal(left->items[index], right->items[index])) return FALSE;
    }
    return TRUE;
}

API bool_t shadow_list_copy(const shadow_list* source,
                                                  shadow_list* destination) {
    if (!destination) return FALSE;
    if (source == destination) return TRUE;
    if (!source || source->count < 0 || (source->count > 0 && !source->items)) {
        shadow_list_free(destination);
        return FALSE;
    }

    shadow_list replacement{};
    if (source->count > 0) {
        const size_t count = static_cast<size_t>(source->count);
        if (count > (std::numeric_limits<size_t>::max)() / sizeof(shadow)) {
            shadow_list_free(destination);
            return FALSE;
        }
        const size_t bytes = sizeof(shadow) * count;
        replacement.items = static_cast<shadow*>(malloc(bytes));
        if (!replacement.items) {
            shadow_list_free(destination);
            return FALSE;
        }
        memcpy(replacement.items, source->items, bytes);
        replacement.count = source->count;
    }

    shadow_list_free(destination);
    *destination = replacement;
    return TRUE;
}

API bool_t shadow_list_equal(const shadow_list* left,
                                                   const shadow_list* right) {
    if (!left || !right || left->count < 0 || right->count < 0) return FALSE;
    if (left->count != right->count) return FALSE;
    if (left->count == 0) return TRUE;
    if (!left->items || !right->items) return FALSE;
    return memcmp(left->items, right->items,
                  sizeof(shadow) * static_cast<size_t>(left->count)) == 0
        ? TRUE : FALSE;
}

} // extern "C"
