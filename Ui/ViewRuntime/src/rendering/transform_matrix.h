#pragma once

#include <viewruntime/viewruntime.h>

/* Resolves percentage-dependent transform values against a post-layout border
   box.  The returned matrix is local to that box; ancestor matrices remain
   independent Display List brackets. */
transform_matrix transform_matrix_resolve(
    const transform_list& transforms,
    transform_origin origin,
    rectf border_box,
    float font_size,
    float root_font_size,
    sizef viewport);

pointf transform_matrix_map_point(transform_matrix matrix, pointf point);
