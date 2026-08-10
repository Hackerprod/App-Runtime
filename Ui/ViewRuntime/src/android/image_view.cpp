#include "android_types.h"

#include <algorithm>
#include <cmath>

namespace viewruntime::android {

/* Java Math.round(float) = (int) Math.floor(a + 0.5f): it rounds toward
 * +INFINITY, unlike C++ std::round which rounds half away from zero. ImageView
 * uses Math.round for the CENTER/CENTER_CROP/CENTER_INSIDE translate offsets
 * (ImageView.java:1319-1320/1336/1350-1351), so an x.5 NEGATIVE offset (image
 * larger than the view) differs by one pixel. */
static float round_java(float x) { return std::floor(x + 0.5f); }

/* AOSP ImageView.resolveAdjustedSize (ImageView.java:1247): clamp the desired
 * size to the parent spec and the max size imposed on ourselves. The runtime
 * models "no max" as max_size == 0 (AOSP mMaxWidth = Integer.MAX_VALUE). */
static float image_resolve_adjusted_size(float desired_size, float max_size,
                                         android_measure_spec_t spec) {
    switch (spec.mode) {
        case ANDROID_MEASURE_UNSPECIFIED:
            return max_size > 0.f ? std::min(desired_size, max_size) : desired_size;
        case ANDROID_MEASURE_AT_MOST:
            return std::min(std::min(desired_size, spec.size),
                            max_size > 0.f ? max_size : spec.size);
        case ANDROID_MEASURE_EXACTLY:
        default:
            return spec.size;
    }
}

/* AOSP ImageView.configureBounds (ImageView.java:1281): compute the
 * source/destination mapping for the given scale type. The intrinsic image
 * (dwidth x dheight) is placed into the content area (vwidth x vheight)
 * exactly like Android's drawMatrix. */
static void image_configure_bounds(android_view_s* view, float dwidth, float dheight,
                                   float vwidth, float vheight) {
    view->image_src_rect = {0.f, 0.f, dwidth, dheight};
    view->image_has_geometry = true;

    const bool fits = (dwidth <= 0.f || vwidth == dwidth) &&
                      (dheight <= 0.f || vheight == dheight);

    if (dwidth <= 0.f || dheight <= 0.f ||
        view->scale_type == ANDROID_SCALE_FIT_XY) {
        /* no intrinsic size or FIT_XY: fill the view */
        view->image_dst_rect = {0.f, 0.f, vwidth, vheight};
        return;
    }

    if (fits || view->scale_type == ANDROID_SCALE_MATRIX) {
        /* no transform needed (identity matrix) */
        view->image_dst_rect = {0.f, 0.f, dwidth, dheight};
        return;
    }

    switch (view->scale_type) {
        case ANDROID_SCALE_CENTER: {
            /* AOSP Math.round((vwidth - dwidth) * 0.5f) (ImageView.java:1319-1320). */
            const float dx = round_java((vwidth - dwidth) * 0.5f);
            const float dy = round_java((vheight - dheight) * 0.5f);
            view->image_dst_rect = {dx, dy, dwidth, dheight};
            break;
        }
        case ANDROID_SCALE_CENTER_CROP: {
            float scale, dx = 0.f, dy = 0.f;
            if (dwidth * vheight > vwidth * dheight) {
                scale = vheight / dheight;
                dx = (vwidth - dwidth * scale) * 0.5f;
            } else {
                scale = vwidth / dwidth;
                dy = (vheight - dheight * scale) * 0.5f;
            }
            /* AOSP postTranslate(Math.round(dx), Math.round(dy))
             * (ImageView.java:1336). */
            view->image_dst_rect = {round_java(dx), round_java(dy),
                                    dwidth * scale, dheight * scale};
            break;
        }
        case ANDROID_SCALE_CENTER_INSIDE: {
            float scale = 1.f;
            if (dwidth > vwidth || dheight > vheight) {
                scale = std::min(vwidth / dwidth, vheight / dheight);
            }
            /* AOSP Math.round((vwidth - dwidth*scale) * 0.5f)
             * (ImageView.java:1350-1351). */
            const float dx = round_java((vwidth - dwidth * scale) * 0.5f);
            const float dy = round_java((vheight - dheight * scale) * 0.5f);
            view->image_dst_rect = {dx, dy, dwidth * scale, dheight * scale};
            break;
        }
        case ANDROID_SCALE_FIT_START:
        case ANDROID_SCALE_FIT_CENTER:
        case ANDROID_SCALE_FIT_END: {
            /* Matrix.ScaleToFit START/CENTER/END on the rectToRect mapping */
            const float scale = std::min(vwidth / dwidth, vheight / dheight);
            float dx = 0.f, dy = 0.f;
            if (view->scale_type == ANDROID_SCALE_FIT_CENTER) {
                dx = (vwidth - dwidth * scale) * 0.5f;
                dy = (vheight - dheight * scale) * 0.5f;
            } else if (view->scale_type == ANDROID_SCALE_FIT_END) {
                dx = vwidth - dwidth * scale;
                dy = vheight - dheight * scale;
            }
            view->image_dst_rect = {dx, dy, dwidth * scale, dheight * scale};
            break;
        }
        default:
            view->image_dst_rect = {0.f, 0.f, vwidth, vheight};
            break;
    }
}

/* Port of AOSP ImageView.onMeasure (ImageView.java:1129): intrinsic size
 * from the host dimensions hook, adjustViewBounds aspect-ratio fitting,
 * maxWidth/maxHeight clamps. */
android_measured_size_t measure_image(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    sizef intrinsic{0.f, 0.f};
    bool has_size = false;
    /* Prefer the real decoded bitmap size (Phase 2 pipeline: bytes fetched
     * through the bridge and decoded by ViewRuntime). */
    if (!view->image_source.empty()) {
        float iw = 0.f, ih = 0.f;
        if (viewruntime::android::image_dimensions_from_cache(
                ui, view->image_source, &iw, &ih) && iw > 0.f && ih > 0.f) {
            intrinsic.width = iw;
            intrinsic.height = ih;
            has_size = true;
        }
    }
    if (!has_size && ui->image_dimensions && !view->image_source.empty()) {
        has_size = ui->image_dimensions(view->image_source.c_str(), &intrinsic,
                                        ui->image_dimensions_data) == TRUE;
    }

    float w = has_size ? intrinsic.width : 0.f;
    float h = has_size ? intrinsic.height : 0.f;
    const float max_w = dp(ui, view->max_width_dp);
    const float max_h = dp(ui, view->max_height_dp);

    float desired_aspect = 0.f;
    bool resize_width = false;
    bool resize_height = false;

    if (has_size) {
        if (w <= 0.f) w = 1.f;
        if (h <= 0.f) h = 1.f;
        if (view->adjust_view_bounds) {
            resize_width = spec_w.mode != ANDROID_MEASURE_EXACTLY;
            resize_height = spec_h.mode != ANDROID_MEASURE_EXACTLY;
            desired_aspect = w / h;
        }
    } else {
        w = h = 0.f;
    }

    const float pleft = dp(ui, view->padding_left_dp), pright = dp(ui, view->padding_right_dp);
    const float ptop = dp(ui, view->padding_top_dp), pbottom = dp(ui, view->padding_bottom_dp);

    float width_size;
    float height_size;

    if (resize_width || resize_height) {
        /* AOSP resolveAdjustedSize with the imposed max */
        width_size = image_resolve_adjusted_size(w + pleft + pright, max_w, spec_w);
        height_size = image_resolve_adjusted_size(h + ptop + pbottom, max_h, spec_h);

        if (desired_aspect != 0.f) {
            const float actual_aspect =
                (width_size - pleft - pright) / (height_size - ptop - pbottom);
            if (std::fabs(actual_aspect - desired_aspect) > 0.0000001f) {
                bool done = false;
                if (resize_width) {
                    /* AOSP: int newWidth = (int)(desiredAspect * (heightSize -
                     * ptop - pbottom)) + pleft + pright; (ImageView.java:1198) —
                     * the (int) cast truncates toward zero before the padding. */
                    const float new_width =
                        static_cast<int>(desired_aspect * (height_size - ptop - pbottom)) + pleft + pright;
                    /* AOSP: when the height is fixed the width may outgrow its
                     * original estimate, so re-resolve it against the spec and
                     * the max before the clamp (ImageView.java:1202-1204;
                     * sCompatAdjustViewBounds defaults to false). */
                    if (!resize_height) {
                        width_size = image_resolve_adjusted_size(new_width, max_w, spec_w);
                    }
                    if (new_width <= width_size) {
                        width_size = new_width;
                        done = true;
                    }
                }
                if (!done && resize_height) {
                    /* AOSP: int newHeight = (int)((widthSize - pleft - pright) /
                     * desiredAspect) + ptop + pbottom; (ImageView.java:1214). */
                    const float new_height =
                        static_cast<int>((width_size - pleft - pright) / desired_aspect) + ptop + pbottom;
                    /* AOSP: when the width is fixed, re-resolve the height
                     * against the spec and the max (ImageView.java:1218-1220). */
                    if (!resize_width) {
                        height_size = image_resolve_adjusted_size(new_height, max_h, spec_h);
                    }
                    if (new_height <= height_size) {
                        height_size = new_height;
                    }
                }
            }
        }
    } else {
        w += pleft + pright;
        h += ptop + pbottom;
        w = std::max(w, dp(ui, view->min_width_dp));
        h = std::max(h, dp(ui, view->min_height_dp));
        width_size = resolve_size(w, spec_w);
        height_size = resolve_size(h, spec_h);
    }

    /* resolve the draw geometry against the measured content box */
    if (has_size) {
        image_configure_bounds(view, intrinsic.width, intrinsic.height,
                               std::max(0.f, width_size - pleft - pright),
                               std::max(0.f, height_size - ptop - pbottom));
    } else {
        view->image_has_geometry = false;
    }

    view->measured_baseline = -1.f;
    return {width_size, height_size};
}

} // namespace viewruntime::android
