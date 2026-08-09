#include "transform_matrix.h"

#include <cmath>

namespace {

transform_matrix identity() { return {1.f, 0.f, 0.f, 1.f, 0.f, 0.f}; }

/* Returns the operation produced by applying first and then second.  This is
   explicit about our row-vector convention and keeps CSS's right-to-left
   transform-list semantics out of renderer bindings. */
transform_matrix compose(transform_matrix first, transform_matrix second) {
    return {
        first.m11 * second.m11 + first.m12 * second.m21,
        first.m11 * second.m12 + first.m12 * second.m22,
        first.m21 * second.m11 + first.m22 * second.m21,
        first.m21 * second.m12 + first.m22 * second.m22,
        first.dx * second.m11 + first.dy * second.m21 + second.dx,
        first.dx * second.m12 + first.dy * second.m22 + second.dy,
    };
}

float resolve_length(css_length value, float reference, float font_size, float root_font_size,
                     sizef viewport) {
    return css_length_resolve(value, reference, font_size, root_font_size,
                                     viewport.width, viewport.height);
}

transform_matrix operation_matrix(const transform_operation& operation,
                                          rectf border_box, float font_size,
                                          float root_font_size, sizef viewport) {
    switch (operation.kind) {
        case TRANSFORM_TRANSLATE:
            return {1.f, 0.f, 0.f, 1.f,
                    resolve_length(operation.x, border_box.width, font_size, root_font_size, viewport),
                    resolve_length(operation.y, border_box.height, font_size, root_font_size, viewport)};
        case TRANSFORM_SCALE:
            return {operation.scalar_x, 0.f, 0.f, operation.scalar_y, 0.f, 0.f};
        case TRANSFORM_ROTATE: {
            const float cosine = std::cos(operation.angle_radians);
            const float sine = std::sin(operation.angle_radians);
            return {cosine, sine, -sine, cosine, 0.f, 0.f};
        }
        case TRANSFORM_SKEW_X:
            return {1.f, 0.f, std::tan(operation.angle_radians), 1.f, 0.f, 0.f};
        case TRANSFORM_SKEW_Y:
            return {1.f, std::tan(operation.angle_radians), 0.f, 1.f, 0.f, 0.f};
        case TRANSFORM_MATRIX:
            return {operation.matrix_a, operation.matrix_b, operation.matrix_c,
                    operation.matrix_d, operation.matrix_e, operation.matrix_f};
        default:
            return identity();
    }
}

} // namespace

transform_matrix transform_matrix_resolve(
    const transform_list& transforms,
    transform_origin origin,
    rectf border_box,
    float font_size,
    float root_font_size,
    sizef viewport) {
    transform_matrix list_matrix = identity();
    if (transforms.items && transforms.count > 0) {
        // CSS applies the rightmost function first. With row vectors that is
        // the product of the operations visited in reverse declaration order.
        for (int32_t index = transforms.count - 1; index >= 0; --index) {
            list_matrix = compose(list_matrix,
                operation_matrix(transforms.items[index], border_box, font_size, root_font_size, viewport));
        }
    }
    const float origin_x = border_box.x + resolve_length(origin.x, border_box.width, font_size, root_font_size, viewport);
    const float origin_y = border_box.y + resolve_length(origin.y, border_box.height, font_size, root_font_size, viewport);
    return compose(compose({1.f, 0.f, 0.f, 1.f, -origin_x, -origin_y}, list_matrix),
                   {1.f, 0.f, 0.f, 1.f, origin_x, origin_y});
}

pointf transform_matrix_map_point(transform_matrix matrix, pointf point) {
    return {
        point.x * matrix.m11 + point.y * matrix.m21 + matrix.dx,
        point.x * matrix.m12 + point.y * matrix.m22 + matrix.dy,
    };
}
