#include "android_types.h"

#include "constraint_widget.h"

#include <algorithm>
#include <cmath>
#include <limits>

namespace viewruntime::android {

void layout_view(android_view_s* view, float x, float y, float w, float h,
                 const android_ui_s* ui);
android_measured_size_t measure_constraint(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui);

/* ── Gravity ───────────────────────────────────────────────────────── */

/* Places a child box of size (child_w, child_h) inside a container of size
 * (container_w, container_h) according to gravity flags. Margins are already
 * excluded from the container size by callers. Returns the top-left offset. */
void apply_gravity(int32_t gravity, float child_w, float child_h,
                   float container_w, float container_h,
                   float* out_x, float* out_y) {
    /* LTR runtime: START resolves to LEFT, END to RIGHT (AOSP applies the
     * layout direction to the relative bits before layout). */
    const int32_t g = gravity_normalize_ltr(gravity);
    float x = 0.f, y = 0.f;
    if (gravity_has(g, ANDROID_GRAVITY_RIGHT)) {
        x = container_w - child_w;
    } else if (gravity_has(g, ANDROID_GRAVITY_CENTER_HORIZONTAL) ||
               gravity_has(g, ANDROID_GRAVITY_CENTER)) {
        x = (container_w - child_w) * 0.5f;
    }
    if (gravity_has(g, ANDROID_GRAVITY_BOTTOM)) {
        y = container_h - child_h;
    } else if (gravity_has(g, ANDROID_GRAVITY_CENTER_VERTICAL) ||
               gravity_has(g, ANDROID_GRAVITY_CENTER)) {
        y = (container_h - child_h) * 0.5f;
    }
    *out_x = x < 0.f ? 0.f : x;
    *out_y = y < 0.f ? 0.f : y;
}

/* ── Display text ──────────────────────────────────────────────────── */

const char* display_text(const android_view_s* view) {
    if (view->has_hint && view->text.empty()) return view->hint.c_str();
    return view->text.c_str();
}

/* ── Measure: leaf widgets ─────────────────────────────────────────── */

android_measured_size_t measure_base(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    const float desired_w = dp(ui, view->min_width_dp) + padding_h(view, ui);
    const float desired_h = dp(ui, view->min_height_dp) + padding_v(view, ui);
    const android_measured_size_t result{resolve_size(desired_w, spec_w),
                                                resolve_size(desired_h, spec_h)};
    view->measured_baseline = -1.f; /* View.getBaseline() returns -1 */
    return result;
}

android_measured_size_t measure_text_view(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    const float size_px = sp(ui, view->text_size_sp);
    const float avail_w = spec_w.mode == ANDROID_MEASURE_UNSPECIFIED
        ? (std::numeric_limits<float>::max)()
        : std::max(0.f, spec_w.size - padding_h(view, ui));
    const android_text_metrics_t metrics =
        measure_text(ui, display_text(view), size_px, avail_w);
    float desired_w = metrics.width + padding_h(view, ui);
    float desired_h = metrics.height + padding_v(view, ui);
    desired_w = std::max(desired_w, dp(ui, view->min_width_dp));
    desired_h = std::max(desired_h, dp(ui, view->min_height_dp));
    const android_measured_size_t result{resolve_size(desired_w, spec_w),
                                                resolve_size(desired_h, spec_h)};
    view->measured_baseline = dp(ui, view->padding_top_dp) + metrics.baseline;
    return result;
}

android_measured_size_t measure_checkable(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    const float indicator = dp(ui, 16.f) + dp(ui, 8.f); /* box + gap before text */
    const float size_px = sp(ui, view->text_size_sp);
    const float avail_w = spec_w.mode == ANDROID_MEASURE_UNSPECIFIED
        ? (std::numeric_limits<float>::max)()
        : std::max(0.f, spec_w.size - padding_h(view, ui) - indicator);
    const android_text_metrics_t metrics =
        measure_text(ui, display_text(view), size_px, avail_w);
    float desired_w = metrics.width + indicator + padding_h(view, ui);
    float desired_h = metrics.height + padding_v(view, ui);
    desired_w = std::max(desired_w, dp(ui, view->min_width_dp));
    desired_h = std::max(desired_h, dp(ui, view->min_height_dp));
    const android_measured_size_t result{resolve_size(desired_w, spec_w),
                                                resolve_size(desired_h, spec_h)};
    view->measured_baseline = dp(ui, view->padding_top_dp) + metrics.baseline;
    return result;
}

/* AOSP ImageView.resolveAdjustedSize: clamp the desired size to the imposed
 * max and the parent spec. */
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

/* AOSP ImageView.configureBounds: compute the source/destination mapping for
 * the given scale type. The intrinsic image (dwidth x dheight) is placed into
 * the content area (vwidth x vheight) exactly like Android's drawMatrix. */
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
            const float dx = std::round((vwidth - dwidth) * 0.5f);
            const float dy = std::round((vheight - dheight) * 0.5f);
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
            view->image_dst_rect = {std::round(dx), std::round(dy),
                                    dwidth * scale, dheight * scale};
            break;
        }
        case ANDROID_SCALE_CENTER_INSIDE: {
            float scale = 1.f;
            if (dwidth > vwidth || dheight > vheight) {
                scale = std::min(vwidth / dwidth, vheight / dheight);
            }
            const float dx = std::round((vwidth - dwidth * scale) * 0.5f);
            const float dy = std::round((vheight - dheight * scale) * 0.5f);
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

/* Port of AOSP ImageView.onMeasure: intrinsic size from the host dimensions
 * hook, adjustViewBounds aspect-ratio fitting, maxWidth/maxHeight clamps. */
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
                    const float new_width =
                        desired_aspect * (height_size - ptop - pbottom) + pleft + pright;
                    if (new_width <= width_size) {
                        width_size = new_width;
                        done = true;
                    }
                }
                if (!done && resize_height) {
                    const float new_height =
                        (width_size - pleft - pright) / desired_aspect + ptop + pbottom;
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

android_measured_size_t measure_progress(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    float desired_w = std::max(dp(ui, 100.f), dp(ui, view->min_width_dp)) + padding_h(view, ui);
    float desired_h = std::max(dp(ui, 16.f), dp(ui, view->min_height_dp)) + padding_v(view, ui);
    const android_measured_size_t result{resolve_size(desired_w, spec_w),
                                                resolve_size(desired_h, spec_h)};
    view->measured_baseline = -1.f;
    return result;
}

/* ── Measure: view groups ──────────────────────────────────────────── */

/* Faithful port of AOSP LinearLayout.measureVertical/measureHorizontal
 * (frameworks/base/core/java/android/widget/LinearLayout.java). Weighted
 * children receive their measured size plus their share of the remaining
 * (possibly negative) excess; 0-dimension weighted children receive the share
 * alone. The share is distributed sequentially so rounding never orphans
 * pixels. */
/* ── LinearLayout ──────────────────────────────────────────────────── */

/* AOSP LinearLayout.hasDividerBeforeChildAt (master semantics: BEGINNING
 * before the first non-GONE view, MIDDLE between non-GONE views, END after
 * the last). */
static bool linear_has_divider_before(const android_view_s* view, int child_index) {
    if (view->show_dividers == ANDROID_SHOW_DIVIDER_NONE) return false;
    const int count = static_cast<int>(view->children.size());
    if (child_index == count) {
        return (view->show_dividers & ANDROID_SHOW_DIVIDER_END) != 0;
    }
    bool all_gone_before = true;
    for (int i = child_index - 1; i >= 0; --i) {
        if (view->children[static_cast<size_t>(i)]->visibility != ANDROID_GONE) {
            all_gone_before = false;
            break;
        }
    }
    if (all_gone_before) {
        return (view->show_dividers & ANDROID_SHOW_DIVIDER_BEGINNING) != 0;
    }
    return (view->show_dividers & ANDROID_SHOW_DIVIDER_MIDDLE) != 0;
}

/* AOSP LinearLayout.hasDividerAfterChildAt (used by RTL layout). */
static bool linear_has_divider_after(const android_view_s* view, int child_index) {
    if (view->show_dividers == ANDROID_SHOW_DIVIDER_NONE) return false;
    const int count = static_cast<int>(view->children.size());
    bool all_gone_after = true;
    for (int i = child_index + 1; i < count; ++i) {
        if (view->children[static_cast<size_t>(i)]->visibility != ANDROID_GONE) {
            all_gone_after = false;
            break;
        }
    }
    if (all_gone_after) {
        return (view->show_dividers & ANDROID_SHOW_DIVIDER_END) != 0;
    }
    return (view->show_dividers & ANDROID_SHOW_DIVIDER_MIDDLE) != 0;
}

/* useDefaultMargins: a child without an explicit margin on the main axis gets
 * the divider thickness when MIDDLE dividers are shown (AOSP getDefaultMargin;
 * the pre-2.0 platform default margin is not modeled by this runtime). */
static float linear_default_main_margin(const android_view_s* view) {
    return (view->show_dividers & ANDROID_SHOW_DIVIDER_MIDDLE) != 0
        ? view->divider_thickness_px : 0.f;
}

android_measured_size_t measure_linear(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    const bool vertical = view->orientation == ANDROID_VERTICAL;
    const int32_t main_mode = vertical ? spec_h.mode : spec_w.mode;
    const int32_t cross_mode = vertical ? spec_w.mode : spec_h.mode;
    const float content_w = std::max(0.f, spec_w.size - padding_h(view, ui));
    const float content_h = std::max(0.f, spec_h.size - padding_v(view, ui));

    float total_length = 0.f;
    float max_cross = 0.f;
    float total_weight = 0.f;
    bool all_fill_parent = true;
    bool match_axis = false;
    bool skipped_measure = false;
    float alternative_max = 0.f;
    float weighted_max = 0.f;

    float max_ascent[4] = {-1.f, -1.f, -1.f, -1.f};
    float max_descent[4] = {-1.f, -1.f, -1.f, -1.f};

    struct pass1_t { android_view_s* view; };
    std::vector<pass1_t> measured;
    measured.reserve(view->children.size());

    float largest_main = -(std::numeric_limits<float>::max)();
    int non_skipped_child_count = 0;
    const float default_main_margin = linear_default_main_margin(view);

    for (size_t idx = 0; idx < view->children.size(); ++idx) {
        android_view_s* child = view->children[idx];
        if (child->visibility == ANDROID_GONE) continue;
        measured.push_back({child});
        non_skipped_child_count++;
        total_weight += child->lp.weight;

        if (linear_has_divider_before(view, static_cast<int>(idx))) {
            total_length += view->divider_thickness_px;
        }

        float mh = margin_h(child->lp, ui);
        float mv = margin_v(child->lp, ui);
        if (view->use_default_margins) {
            /* apply the default margin to the main axis when none was set */
            if (vertical && mv == 0.f) mv = default_main_margin;
            if (!vertical && mh == 0.f) mh = default_main_margin;
        }
        const bool use_excess = child->lp.weight > 0.f &&
            ((vertical && child->lp.height.kind == ANDROID_SIZE_KIND_EXACT &&
              child->lp.height.value_dp == 0.f) ||
             (!vertical && child->lp.width.kind == ANDROID_SIZE_KIND_EXACT &&
              child->lp.width.value_dp == 0.f));

        if (use_excess && main_mode == ANDROID_MEASURE_EXACTLY) {
            /* AOSP optimization: skip measuring 0-dimension weighted children
             * under an EXACTLY parent; only their margins take space. */
            total_length += vertical ? mv : mh;
            skipped_measure = true;
        } else {
            const float used_main = total_weight == 0.f ? total_length : 0.f;
            android_measure_spec_t cw, ch;
            if (vertical) {
                cw = get_child_measure_spec({content_w, cross_mode}, mh, child->lp.width, ui);
                if (use_excess) {
                    /* temporary WRAP_CONTENT measure to learn the intrinsic */
                    const android_size_t wrap{ANDROID_SIZE_KIND_WRAP_CONTENT, 0.f};
                    ch = get_child_measure_spec({content_h, main_mode}, mv + used_main, wrap, ui);
                } else {
                    ch = get_child_measure_spec({content_h, main_mode}, mv + used_main, child->lp.height, ui);
                }
            } else {
                if (use_excess) {
                    const android_size_t wrap{ANDROID_SIZE_KIND_WRAP_CONTENT, 0.f};
                    cw = get_child_measure_spec({content_w, main_mode}, mh + used_main, wrap, ui);
                } else {
                    cw = get_child_measure_spec({content_w, main_mode}, mh + used_main, child->lp.width, ui);
                }
                ch = get_child_measure_spec({content_h, cross_mode}, mv, child->lp.height, ui);
            }
            child->measured = measure_view(child, cw, ch, ui);
            const float main_size = vertical ? child->measured.height : child->measured.width;
            total_length += main_size + (vertical ? mv : mh);
            if (view->use_largest_child) {
                largest_main = std::max(largest_main, main_size);
            }
        }

        const bool match_locally = cross_mode != ANDROID_MEASURE_EXACTLY &&
            ((vertical && child->lp.width.kind == ANDROID_SIZE_KIND_MATCH_PARENT) ||
             (!vertical && child->lp.height.kind == ANDROID_SIZE_KIND_MATCH_PARENT));
        if (match_locally) match_axis = true;

        const float margin_cross = vertical ? mh : mv;
        const float cross_size = (vertical ? child->measured.width : child->measured.height) + margin_cross;
        max_cross = std::max(max_cross, cross_size);
        all_fill_parent = all_fill_parent &&
            ((vertical && child->lp.width.kind == ANDROID_SIZE_KIND_MATCH_PARENT) ||
             (!vertical && child->lp.height.kind == ANDROID_SIZE_KIND_MATCH_PARENT));
        if (child->lp.weight > 0.f) weighted_max = std::max(weighted_max, match_locally ? margin_cross : cross_size);
        else alternative_max = std::max(alternative_max, match_locally ? margin_cross : cross_size);

        /* Baseline buckets are per vertical-gravity class, horizontal only. */
        if (!vertical && view->baseline_aligned && child->measured_baseline >= 0.f) {
            const int32_t gravity = child->lp.gravity != ANDROID_GRAVITY_NO_GRAVITY
                ? child->lp.gravity : view->gravity;
            const int bucket = ((gravity >> 4) & ~1) >> 1; /* 0 CV, 1 TOP, 2 BOTTOM, 3 FILL */
            const float child_height_with_margins = child->measured.height + mv;
            max_ascent[bucket] = std::max(max_ascent[bucket], child->measured_baseline);
            max_descent[bucket] = std::max(max_descent[bucket], child_height_with_margins - child->measured_baseline);
        }
    }

    if (non_skipped_child_count > 0 &&
        linear_has_divider_before(view, static_cast<int>(view->children.size()))) {
        total_length += view->divider_thickness_px;
    }

    /* measureWithLargestChild: every visible child contributes the size of
     * the largest one (AOSP re-sums mTotalLength with largestChildHeight). */
    if (view->use_largest_child && main_mode != ANDROID_MEASURE_EXACTLY &&
        non_skipped_child_count > 0) {
        total_length = 0.f;
        for (android_view_s* child : view->children) {
            if (child->visibility == ANDROID_GONE) continue;
            float mh = margin_h(child->lp, ui);
            float mv = margin_v(child->lp, ui);
            if (view->use_default_margins) {
                if (vertical && mv == 0.f) mv = default_main_margin;
                if (!vertical && mh == 0.f) mh = default_main_margin;
            }
            total_length += largest_main + (vertical ? mv : mh);
        }
    }

    /* Add padding, then resolve the main size BEFORE the weight pass; the
     * resolved value is what the layout reports. */
    total_length += vertical ? padding_v(view, ui) : padding_h(view, ui);
    const float main_resolved = resolve_size(
        std::max(total_length, vertical ? dp(ui, view->min_height_dp) : dp(ui, view->min_width_dp)),
        vertical ? spec_h : spec_w);

    /* AOSP P+: weighted children are always re-measured when any weight exists
     * (sRemeasureWeightedChildren), distributing positive AND negative excess. */
    if (skipped_measure || total_weight > 0.f) {
        float remaining_excess = main_resolved - total_length;
        float remaining_weight_sum = view->weight_sum > 0.f ? view->weight_sum : total_weight;
        float new_total = 0.f;

        for (android_view_s* child : view->children) {
            if (child->visibility == ANDROID_GONE) continue;
            const float mh = margin_h(child->lp, ui);
            const float mv = margin_v(child->lp, ui);
            const float child_weight = child->lp.weight;
            if (child_weight > 0.f && remaining_weight_sum > 0.f) {
                const float share = child_weight * remaining_excess / remaining_weight_sum;
                remaining_excess -= share;
                remaining_weight_sum -= child_weight;

                const bool zero_dim = vertical
                    ? (child->lp.height.kind == ANDROID_SIZE_KIND_EXACT && child->lp.height.value_dp == 0.f)
                    : (child->lp.width.kind == ANDROID_SIZE_KIND_EXACT && child->lp.width.value_dp == 0.f);
                float child_main;
                if (view->use_largest_child && main_mode != ANDROID_MEASURE_EXACTLY) {
                    /* AOSP: weighted children get the largest child's size */
                    child_main = largest_main;
                } else {
                    child_main = std::max(0.f,
                        zero_dim ? share : (vertical ? child->measured.height : child->measured.width) + share);
                }

                android_measure_spec_t cw, ch;
                if (vertical) {
                    cw = get_child_measure_spec({content_w, cross_mode}, mh, child->lp.width, ui);
                    ch = {child_main, ANDROID_MEASURE_EXACTLY};
                } else {
                    cw = {child_main, ANDROID_MEASURE_EXACTLY};
                    ch = get_child_measure_spec({content_h, cross_mode}, mv, child->lp.height, ui);
                }
                child->measured = measure_view(child, cw, ch, ui);

                /* Recompute the cross metrics and baseline buckets for the
                 * re-measured child (AOSP does the same in its weight pass). */
                const float cross_size = (vertical ? child->measured.width : child->measured.height) +
                    (vertical ? mh : mv);
                max_cross = std::max(max_cross, cross_size);
                all_fill_parent = all_fill_parent &&
                    ((vertical && child->lp.width.kind == ANDROID_SIZE_KIND_MATCH_PARENT) ||
                     (!vertical && child->lp.height.kind == ANDROID_SIZE_KIND_MATCH_PARENT));
                weighted_max = std::max(weighted_max, cross_size);
                if (!vertical && view->baseline_aligned && child->measured_baseline >= 0.f) {
                    const int32_t gravity = child->lp.gravity != ANDROID_GRAVITY_NO_GRAVITY
                        ? child->lp.gravity : view->gravity;
                    const int bucket = ((gravity >> 4) & ~1) >> 1;
                    const float child_height_with_margins = child->measured.height + mv;
                    max_ascent[bucket] = std::max(max_ascent[bucket], child->measured_baseline);
                    max_descent[bucket] = std::max(max_descent[bucket], child_height_with_margins - child->measured_baseline);
                }
            }
            new_total += (vertical ? child->measured.height : child->measured.width) + (vertical ? mv : mh);
        }
        total_length = new_total + (vertical ? padding_v(view, ui) : padding_h(view, ui));
    } else if (view->use_largest_child && main_mode != ANDROID_MEASURE_EXACTLY) {
        /* AOSP: no excess to distribute, so make all weighted views as large
         * as the largest child (they were measured once already). */
        for (android_view_s* child : view->children) {
            if (child->visibility == ANDROID_GONE) continue;
            if (child->lp.weight <= 0.f) continue;
            android_measure_spec_t cw, ch;
            if (vertical) {
                cw = {child->measured.width, ANDROID_MEASURE_EXACTLY};
                ch = {largest_main, ANDROID_MEASURE_EXACTLY};
            } else {
                cw = {largest_main, ANDROID_MEASURE_EXACTLY};
                ch = {child->measured.height, ANDROID_MEASURE_EXACTLY};
            }
            child->measured = measure_view(child, cw, ch, ui);
        }
    }

    for (int i = 0; i < 4; ++i) {
        view->baseline_ascent[i] = max_ascent[i];
        view->baseline_descent[i] = max_descent[i];
    }

    if (!all_fill_parent && cross_mode != ANDROID_MEASURE_EXACTLY) {
        max_cross = alternative_max;
    }
    max_cross += vertical ? padding_h(view, ui) : padding_v(view, ui);
    max_cross = std::max(max_cross, vertical ? dp(ui, view->min_width_dp) : dp(ui, view->min_height_dp));

    android_measured_size_t result{};
    if (vertical) {
        result = {resolve_size(max_cross, spec_w), main_resolved};
    } else {
        result = {main_resolved, resolve_size(max_cross, spec_h)};
    }

    /* forceUniformWidth/Height: children declared MATCH_PARENT on the cross
     * axis are re-measured against the final resolved size when the parent was
     * not EXACTLY (AOSP ViewGroup.forceUniformWidth). */
    if (match_axis) {
        const float uniform = vertical ? result.width : result.height;
        for (android_view_s* child : view->children) {
            if (child->visibility == ANDROID_GONE) continue;
            const bool wants_match = vertical
                ? child->lp.width.kind == ANDROID_SIZE_KIND_MATCH_PARENT
                : child->lp.height.kind == ANDROID_SIZE_KIND_MATCH_PARENT;
            if (!wants_match) continue;
            const float mh = margin_h(child->lp, ui);
            const float mv = margin_v(child->lp, ui);
            android_measure_spec_t cw, ch;
            if (vertical) {
                cw = get_child_measure_spec({uniform, ANDROID_MEASURE_EXACTLY}, mh,
                                            child->lp.width, ui);
                ch = get_child_measure_spec({content_h, main_mode}, mv, child->lp.height, ui);
            } else {
                cw = get_child_measure_spec({content_w, main_mode}, mh, child->lp.width, ui);
                ch = get_child_measure_spec({uniform, ANDROID_MEASURE_EXACTLY}, mv,
                                            child->lp.height, ui);
            }
            child->measured = measure_view(child, cw, ch, ui);
        }
    }
    return result;
}

android_measured_size_t measure_frame(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    const float content_w = std::max(0.f, spec_w.size - padding_h(view, ui));
    const float content_h = std::max(0.f, spec_h.size - padding_v(view, ui));
    float desired_w = 0.f, desired_h = 0.f;
    std::vector<android_view_s*> match_parent_children;
    const bool measure_match_parent =
        spec_w.mode != ANDROID_MEASURE_EXACTLY ||
        spec_h.mode != ANDROID_MEASURE_EXACTLY;
    for (android_view_s* child : view->children) {
        if (child->visibility == ANDROID_GONE) continue;
        measure_child_with_margins(view, child,
            {content_w, spec_w.mode}, {content_h, spec_h.mode}, ui);
        desired_w = std::max(desired_w, child->measured.width + margin_h(child->lp, ui));
        desired_h = std::max(desired_h, child->measured.height + margin_v(child->lp, ui));
        if (measure_match_parent &&
            (child->lp.width.kind == ANDROID_SIZE_KIND_MATCH_PARENT ||
             child->lp.height.kind == ANDROID_SIZE_KIND_MATCH_PARENT)) {
            match_parent_children.push_back(child);
        }
    }
    const float resolved_w = resolve_size(desired_w + padding_h(view, ui), spec_w);
    const float resolved_h = resolve_size(desired_h + padding_v(view, ui), spec_h);

    /* AOSP FrameLayout.onMeasure second pass: when the parent was not EXACTLY
     * and more than one child declared MATCH_PARENT, re-measure those children
     * against the final resolved size. */
    if (match_parent_children.size() > 1) {
        for (android_view_s* child : match_parent_children) {
            android_measure_spec_t cw, ch;
            if (child->lp.width.kind == ANDROID_SIZE_KIND_MATCH_PARENT) {
                cw = {std::max(0.f, resolved_w - padding_h(view, ui) - margin_h(child->lp, ui)),
                      ANDROID_MEASURE_EXACTLY};
            } else {
                cw = get_child_measure_spec({content_w, spec_w.mode}, margin_h(child->lp, ui),
                                            child->lp.width, ui);
            }
            if (child->lp.height.kind == ANDROID_SIZE_KIND_MATCH_PARENT) {
                ch = {std::max(0.f, resolved_h - padding_v(view, ui) - margin_v(child->lp, ui)),
                      ANDROID_MEASURE_EXACTLY};
            } else {
                ch = get_child_measure_spec({content_h, spec_h.mode}, margin_v(child->lp, ui),
                                            child->lp.height, ui);
            }
            child->measured = measure_view(child, cw, ch, ui);
        }
    }
    return {resolved_w, resolved_h};
}

/* ── RelativeLayout ────────────────────────────────────────────────── */
/* Faithful port of android.widget.RelativeLayout: dependency graph with
 * topological sort (AOSP DependencyGraph), two-pass measure
 * (horizontal rules then vertical rules), VALUE_NOT_SET edge semantics. */

namespace {

constexpr float RL_UNSET = -(std::numeric_limits<float>::max)();

/* AOSP RULES_VERTICAL / RULES_HORIZONTAL: the verbs that create dependencies
 * for each sort pass. */
const int kRulesVertical[] = {
    ANDROID_RELATIVE_ABOVE, ANDROID_RELATIVE_BELOW, ANDROID_RELATIVE_ALIGN_BASELINE,
    ANDROID_RELATIVE_ALIGN_TOP, ANDROID_RELATIVE_ALIGN_BOTTOM};
const int kRulesHorizontal[] = {
    ANDROID_RELATIVE_LEFT_OF, ANDROID_RELATIVE_RIGHT_OF, ANDROID_RELATIVE_ALIGN_LEFT,
    ANDROID_RELATIVE_ALIGN_RIGHT, ANDROID_RELATIVE_START_OF, ANDROID_RELATIVE_END_OF,
    ANDROID_RELATIVE_ALIGN_START, ANDROID_RELATIVE_ALIGN_END};

struct RelativeNode {
    android_view_s* view = nullptr;
    std::vector<RelativeNode*> dependents;   /* nodes needing this node first */
    std::vector<std::pair<int32_t, RelativeNode*>> dependencies; /* (target_id, node) */
};

/* Port of AOSP DependencyGraph (RelativeLayout). The graph is rebuilt per
 * measure pass; node pointers are stable because nodes are reserved up front. */
struct RelativeGraph {
    std::vector<RelativeNode> nodes;
    std::unordered_map<int32_t, RelativeNode*> key_nodes; /* by resource id */

    void clear() {
        nodes.clear();
        key_nodes.clear();
    }
    void add(android_view_s* view) {
        nodes.push_back(RelativeNode{view});
        if (view->resource_id != 0) {
            key_nodes[view->resource_id] = &nodes.back();
        }
    }

    /* AOSP findRoots: builds dependents/dependencies from the filter rules
     * and returns the roots (nodes with no dependencies). */
    std::vector<RelativeNode*> find_roots(const int* filter, int filter_count) {
        for (RelativeNode& node : nodes) {
            node.dependents.clear();
            node.dependencies.clear();
        }
        for (RelativeNode& node : nodes) {
            for (int j = 0; j < filter_count; ++j) {
                const int32_t target_id = node.view->relative_rules[filter[j]];
                if (target_id <= 0) continue; /* parent rules are TRUE (-1) */
                auto it = key_nodes.find(target_id);
                if (it == key_nodes.end()) continue;
                RelativeNode* dep = it->second;
                if (dep == &node) continue; /* skip self dependencies */
                if (std::find(dep->dependents.begin(), dep->dependents.end(), &node) ==
                    dep->dependents.end()) {
                    dep->dependents.push_back(&node);
                }
                node.dependencies.emplace_back(target_id, dep);
            }
        }
        std::vector<RelativeNode*> roots;
        for (RelativeNode& node : nodes) {
            if (node.dependencies.empty()) roots.push_back(&node);
        }
        return roots;
    }

    /* AOSP getSortedViews: Kahn's algorithm over the roots (LIFO). */
    std::vector<android_view_s*> get_sorted_views(const int* filter, int filter_count) {
        std::vector<android_view_s*> sorted;
        std::vector<RelativeNode*> stack = find_roots(filter, filter_count);
        while (!stack.empty()) {
            RelativeNode* node = stack.back();
            stack.pop_back();
            sorted.push_back(node->view);
            const int32_t key = node->view->resource_id;
            for (RelativeNode* dependent : node->dependents) {
                auto& deps = dependent->dependencies;
                deps.erase(std::remove_if(deps.begin(), deps.end(),
                    [key](const auto& kv) { return kv.first == key; }), deps.end());
                if (deps.empty()) {
                    stack.push_back(dependent);
                }
            }
        }
        return sorted;
    }
};

/* AOSP getRelatedView: the target of a rule, skipping GONE views up the chain
 * (re-resolving the same verb on each GONE view). */
android_view_s* relative_get_related_view(RelativeGraph& graph,
                                          const int* rules, int relation) {
    const int32_t id = rules[relation];
    if (id == 0) return nullptr;
    auto it = graph.key_nodes.find(id);
    if (it == graph.key_nodes.end()) return nullptr;
    android_view_s* v = it->second->view;
    while (v->visibility == ANDROID_GONE) {
        rules = v->relative_rules;
        it = graph.key_nodes.find(rules[relation]);
        if (it == graph.key_nodes.end() || v == it->second->view) return nullptr;
        v = it->second->view;
    }
    return v;
}

/* AOSP LayoutParams margin accessor (0=left, 1=top, 2=right, 3=bottom). */
float relative_margin(const android_layout_params_t& lp, int which, const android_ui_s* ui) {
    switch (which) {
        case 0: return dp(ui, lp.margins_dp.left);
        case 1: return dp(ui, lp.margins_dp.top);
        case 2: return dp(ui, lp.margins_dp.right);
        default: return dp(ui, lp.margins_dp.bottom);
    }
}

/* AOSP applyHorizontalSizeRules: resolve child.rl_left/rl_right from the
 * rules; edges stay RL_UNSET ("soft requirement") when not fixed. */
void relative_apply_h_size_rules(android_view_s* child, RelativeGraph& graph,
                                 float my_width, const android_ui_s* ui) {
    android_view_s* parent = child->parent;
    const int* rules = child->relative_rules;
    const float left_margin = dp(ui, child->lp.margins_dp.left);
    const float right_margin = dp(ui, child->lp.margins_dp.right);
    const float pad_left = dp(ui, parent->padding_left_dp);
    const float pad_right = dp(ui, parent->padding_right_dp);
    child->rl_left = RL_UNSET;
    child->rl_right = RL_UNSET;

    android_view_s* anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_LEFT_OF);
    if (anchor != nullptr) {
        child->rl_right = anchor->rl_left - (relative_margin(anchor->lp, 2, ui) + right_margin);
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_LEFT_OF] != 0) {
        if (my_width >= 0.f) child->rl_right = my_width - pad_right - right_margin;
    }

    anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_RIGHT_OF);
    if (anchor != nullptr) {
        child->rl_left = anchor->rl_right + (relative_margin(anchor->lp, 2, ui) + left_margin);
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_RIGHT_OF] != 0) {
        child->rl_left = pad_left + left_margin;
    }

    anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_ALIGN_LEFT);
    if (anchor != nullptr) {
        child->rl_left = anchor->rl_left + left_margin;
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_ALIGN_LEFT] != 0) {
        child->rl_left = pad_left + left_margin;
    }

    anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_ALIGN_RIGHT);
    if (anchor != nullptr) {
        child->rl_right = anchor->rl_right - right_margin;
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_ALIGN_RIGHT] != 0) {
        if (my_width >= 0.f) child->rl_right = my_width - pad_right - right_margin;
    }

    if (rules[ANDROID_RELATIVE_ALIGN_PARENT_LEFT] != 0) {
        child->rl_left = pad_left + left_margin;
    }
    if (rules[ANDROID_RELATIVE_ALIGN_PARENT_RIGHT] != 0) {
        if (my_width >= 0.f) child->rl_right = my_width - pad_right - right_margin;
    }
}

/* AOSP getRelatedViewBaselineOffset for ALIGN_BASELINE; -1 when no baseline
 * target exists. */
int relative_get_related_view_baseline_offset(RelativeGraph& graph, const int* rules) {
    android_view_s* v = relative_get_related_view(graph, rules, ANDROID_RELATIVE_ALIGN_BASELINE);
    if (v == nullptr) return -1;
    const float baseline = v->measured_baseline;
    if (baseline < 0.f) return -1;
    return static_cast<int>(v->rl_top + baseline);
}

/* AOSP applyVerticalSizeRules: baseline alignment overrides explicit
 * top/bottom; otherwise resolve rl_top/rl_bottom from the rules. */
void relative_apply_v_size_rules(android_view_s* child, RelativeGraph& graph,
                                 float my_height, float my_baseline, const android_ui_s* ui) {
    android_view_s* parent = child->parent;
    const int* rules = child->relative_rules;
    const float top_margin = dp(ui, child->lp.margins_dp.top);
    const float bottom_margin = dp(ui, child->lp.margins_dp.bottom);
    const float pad_top = dp(ui, parent->padding_top_dp);
    const float pad_bottom = dp(ui, parent->padding_bottom_dp);

    const int baseline_offset = relative_get_related_view_baseline_offset(graph, rules);
    if (baseline_offset != -1) {
        float offset = static_cast<float>(baseline_offset);
        if (my_baseline != -1.f) offset -= my_baseline;
        child->rl_top = offset;
        child->rl_bottom = RL_UNSET;
        return;
    }

    child->rl_top = RL_UNSET;
    child->rl_bottom = RL_UNSET;

    android_view_s* anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_ABOVE);
    if (anchor != nullptr) {
        child->rl_bottom = anchor->rl_top - (relative_margin(anchor->lp, 1, ui) + bottom_margin);
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_ABOVE] != 0) {
        if (my_height >= 0.f) child->rl_bottom = my_height - pad_bottom - bottom_margin;
    }

    anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_BELOW);
    if (anchor != nullptr) {
        child->rl_top = anchor->rl_bottom + (relative_margin(anchor->lp, 1, ui) + top_margin);
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_BELOW] != 0) {
        child->rl_top = pad_top + top_margin;
    }

    anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_ALIGN_TOP);
    if (anchor != nullptr) {
        child->rl_top = anchor->rl_top + top_margin;
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_ALIGN_TOP] != 0) {
        child->rl_top = pad_top + top_margin;
    }

    anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_ALIGN_BOTTOM);
    if (anchor != nullptr) {
        child->rl_bottom = anchor->rl_bottom - bottom_margin;
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_ALIGN_BOTTOM] != 0) {
        if (my_height >= 0.f) child->rl_bottom = my_height - pad_bottom - bottom_margin;
    }

    if (rules[ANDROID_RELATIVE_ALIGN_PARENT_TOP] != 0) {
        child->rl_top = pad_top + top_margin;
    }
    if (rules[ANDROID_RELATIVE_ALIGN_PARENT_BOTTOM] != 0) {
        if (my_height >= 0.f) child->rl_bottom = my_height - pad_bottom - bottom_margin;
    }
}

/* AOSP getChildMeasureSpec: size constraints from the resolved edges, the
 * child's desired size, margins and padding. my_size < 0 means UNSPECIFIED. */
android_measure_spec_t relative_get_child_measure_spec(
    float child_start, float child_end, const android_size_t& child_size,
    float start_margin, float end_margin, float start_padding, float end_padding,
    float my_size, const android_ui_s* ui) {
    const bool is_unspecified = my_size < 0.f;
    /* AOSP LayoutParams size: >=0 exact px, MATCH_PARENT = -1, WRAP = -2 */
    float child_size_px = 0.f;
    if (child_size.kind == ANDROID_SIZE_KIND_EXACT) {
        child_size_px = dp(ui, child_size.value_dp);
    } else if (child_size.kind == ANDROID_SIZE_KIND_WRAP_CONTENT) {
        child_size_px = -2.f;
    } else {
        child_size_px = -1.f; /* MATCH_PARENT */
    }

    if (is_unspecified) {
        if (child_start != RL_UNSET && child_end != RL_UNSET) {
            return {std::max(0.f, child_end - child_start), ANDROID_MEASURE_EXACTLY};
        }
        if (child_size_px >= 0.f) {
            return {child_size_px, ANDROID_MEASURE_EXACTLY};
        }
        return {0.f, ANDROID_MEASURE_UNSPECIFIED};
    }

    const float temp_start =
        child_start != RL_UNSET ? child_start : start_padding + start_margin;
    const float temp_end =
        child_end != RL_UNSET ? child_end : my_size - end_padding - end_margin;
    const float max_available = temp_end - temp_start;

    if (child_start != RL_UNSET && child_end != RL_UNSET) {
        return {std::max(0.f, max_available), ANDROID_MEASURE_EXACTLY};
    }
    if (child_size_px >= 0.f) {
        if (max_available >= 0.f) {
            return {std::min(max_available, child_size_px), ANDROID_MEASURE_EXACTLY};
        }
        return {child_size_px, ANDROID_MEASURE_EXACTLY};
    }
    if (child_size_px == -1.f) { /* MATCH_PARENT */
        return {std::max(0.f, max_available), ANDROID_MEASURE_EXACTLY};
    }
    /* WRAP_CONTENT */
    if (max_available >= 0.f) {
        return {max_available, ANDROID_MEASURE_AT_MOST};
    }
    return {0.f, ANDROID_MEASURE_UNSPECIFIED};
}

/* AOSP measureChildHorizontal: width from the horizontal rules; height is
 * AT_MOST (EXACTLY when MATCH_PARENT) bounded by the parent. */
void relative_measure_child_horizontal(android_view_s* child, float my_width,
                                       float my_height, const android_ui_s* ui) {
    android_view_s* parent = child->parent;
    const android_layout_params_t& lp = child->lp;
    const android_measure_spec_t cw = relative_get_child_measure_spec(
        child->rl_left, child->rl_right, lp.width,
        dp(ui, lp.margins_dp.left), dp(ui, lp.margins_dp.right),
        dp(ui, parent->padding_left_dp), dp(ui, parent->padding_right_dp), my_width, ui);

    android_measure_spec_t ch;
    if (my_height < 0.f) {
        if (lp.height.kind == ANDROID_SIZE_KIND_EXACT) {
            ch = {dp(ui, lp.height.value_dp), ANDROID_MEASURE_EXACTLY};
        } else {
            ch = {0.f, ANDROID_MEASURE_UNSPECIFIED};
        }
    } else {
        const float max_height = std::max(0.f, my_height
            - dp(ui, parent->padding_top_dp) - dp(ui, parent->padding_bottom_dp)
            - dp(ui, lp.margins_dp.top) - dp(ui, lp.margins_dp.bottom));
        const int32_t mode = lp.height.kind == ANDROID_SIZE_KIND_MATCH_PARENT
            ? ANDROID_MEASURE_EXACTLY : ANDROID_MEASURE_AT_MOST;
        ch = {max_height, mode};
    }

    child->measured = measure_view(child, cw, ch, ui);
}

/* AOSP measureChild: both axes from getChildMeasureSpec (vertical pass). */
void relative_measure_child(android_view_s* child, float my_width, float my_height,
                            const android_ui_s* ui) {
    android_view_s* parent = child->parent;
    const android_layout_params_t& lp = child->lp;
    const android_measure_spec_t cw = relative_get_child_measure_spec(
        child->rl_left, child->rl_right, lp.width,
        dp(ui, lp.margins_dp.left), dp(ui, lp.margins_dp.right),
        dp(ui, parent->padding_left_dp), dp(ui, parent->padding_right_dp), my_width, ui);
    const android_measure_spec_t ch = relative_get_child_measure_spec(
        child->rl_top, child->rl_bottom, lp.height,
        dp(ui, lp.margins_dp.top), dp(ui, lp.margins_dp.bottom),
        dp(ui, parent->padding_top_dp), dp(ui, parent->padding_bottom_dp), my_height, ui);

    child->measured = measure_view(child, cw, ch, ui);
}

void relative_center_horizontal(android_view_s* child, float my_width) {
    const float left = (my_width - child->measured.width) / 2.f;
    child->rl_left = left;
    child->rl_right = left + child->measured.width;
}

void relative_center_vertical(android_view_s* child, float my_height) {
    const float top = (my_height - child->measured.height) / 2.f;
    child->rl_top = top;
    child->rl_bottom = top + child->measured.height;
}

void relative_position_at_edge(android_view_s* child, const android_ui_s* ui) {
    android_view_s* parent = child->parent;
    child->rl_left = dp(ui, parent->padding_left_dp) + dp(ui, child->lp.margins_dp.left);
    child->rl_right = child->rl_left + child->measured.width;
}

/* AOSP positionChildHorizontal; returns true when the axis was offset
 * (wrap-content re-centering will be needed). */
bool relative_position_child_horizontal(android_view_s* child, float my_width,
                                        bool wrap_content, const android_ui_s* ui) {
    const int* rules = child->relative_rules;
    if (child->rl_left == RL_UNSET && child->rl_right != RL_UNSET) {
        child->rl_left = child->rl_right - child->measured.width;
    } else if (child->rl_left != RL_UNSET && child->rl_right == RL_UNSET) {
        child->rl_right = child->rl_left + child->measured.width;
    } else if (child->rl_left == RL_UNSET && child->rl_right == RL_UNSET) {
        if (rules[ANDROID_RELATIVE_CENTER_IN_PARENT] != 0 ||
            rules[ANDROID_RELATIVE_CENTER_HORIZONTAL] != 0) {
            if (!wrap_content) {
                relative_center_horizontal(child, my_width);
            } else {
                relative_position_at_edge(child, ui);
            }
            return true;
        }
        relative_position_at_edge(child, ui);
    }
    return rules[ANDROID_RELATIVE_ALIGN_PARENT_END] != 0;
}

/* AOSP positionChildVertical. */
bool relative_position_child_vertical(android_view_s* child, float my_height,
                                      bool wrap_content, const android_ui_s* ui) {
    const int* rules = child->relative_rules;
    if (child->rl_top == RL_UNSET && child->rl_bottom != RL_UNSET) {
        child->rl_top = child->rl_bottom - child->measured.height;
    } else if (child->rl_top != RL_UNSET && child->rl_bottom == RL_UNSET) {
        child->rl_bottom = child->rl_top + child->measured.height;
    } else if (child->rl_top == RL_UNSET && child->rl_bottom == RL_UNSET) {
        if (rules[ANDROID_RELATIVE_CENTER_IN_PARENT] != 0 ||
            rules[ANDROID_RELATIVE_CENTER_VERTICAL] != 0) {
            if (!wrap_content) {
                relative_center_vertical(child, my_height);
            } else {
                child->rl_top = dp(ui, child->parent->padding_top_dp) + dp(ui, child->lp.margins_dp.top);
                child->rl_bottom = child->rl_top + child->measured.height;
            }
            return true;
        }
        child->rl_top = dp(ui, child->parent->padding_top_dp) + dp(ui, child->lp.margins_dp.top);
        child->rl_bottom = child->rl_top + child->measured.height;
    }
    return rules[ANDROID_RELATIVE_ALIGN_PARENT_BOTTOM] != 0;
}

android_measured_size_t measure_relative(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    const float my_width = spec_w.mode == ANDROID_MEASURE_UNSPECIFIED ? -1.f : spec_w.size;
    const float my_height = spec_h.mode == ANDROID_MEASURE_UNSPECIFIED ? -1.f : spec_h.size;

    float width = 0.f, height = 0.f;
    if (spec_w.mode == ANDROID_MEASURE_EXACTLY) width = my_width;
    if (spec_h.mode == ANDROID_MEASURE_EXACTLY) height = my_height;

    const bool is_wrap_content_width = spec_w.mode != ANDROID_MEASURE_EXACTLY;
    const bool is_wrap_content_height = spec_h.mode != ANDROID_MEASURE_EXACTLY;

    RelativeGraph graph;
    graph.nodes.reserve(view->children.size());
    for (android_view_s* child : view->children) {
        graph.add(child);
    }
    const std::vector<android_view_s*> sorted_h =
        graph.get_sorted_views(kRulesHorizontal, 8);
    const std::vector<android_view_s*> sorted_v =
        graph.get_sorted_views(kRulesVertical, 5);

    for (android_view_s* child : sorted_h) {
        if (child->visibility == ANDROID_GONE) continue;
        relative_apply_h_size_rules(child, graph, my_width, ui);
        relative_measure_child_horizontal(child, my_width, my_height, ui);
        relative_position_child_horizontal(child, my_width, is_wrap_content_width, ui);
    }

    float left = (std::numeric_limits<float>::max)();
    float top = (std::numeric_limits<float>::max)();
    float right = -(std::numeric_limits<float>::max)();
    float bottom = -(std::numeric_limits<float>::max)();

    for (android_view_s* child : sorted_v) {
        if (child->visibility == ANDROID_GONE) continue;
        relative_apply_v_size_rules(child, graph, my_height, child->measured_baseline, ui);
        relative_measure_child(child, my_width, my_height, ui);
        relative_position_child_vertical(child, my_height, is_wrap_content_height, ui);

        if (is_wrap_content_width) {
            width = std::max(width, child->rl_right + dp(ui, child->lp.margins_dp.right));
        }
        if (is_wrap_content_height) {
            height = std::max(height, child->rl_bottom + dp(ui, child->lp.margins_dp.bottom));
        }
        left = std::min(left, child->rl_left - dp(ui, child->lp.margins_dp.left));
        top = std::min(top, child->rl_top - dp(ui, child->lp.margins_dp.top));
        right = std::max(right, child->rl_right + dp(ui, child->lp.margins_dp.right));
        bottom = std::max(bottom, child->rl_bottom + dp(ui, child->lp.margins_dp.bottom));
    }

    if (is_wrap_content_width) {
        width += dp(ui, view->padding_right_dp);
        width = std::max(width, dp(ui, view->min_width_dp));
        width = resolve_size(width, spec_w);
        for (android_view_s* child : sorted_v) {
            if (child->visibility == ANDROID_GONE) continue;
            const int* rules = child->relative_rules;
            if (rules[ANDROID_RELATIVE_CENTER_IN_PARENT] != 0 ||
                rules[ANDROID_RELATIVE_CENTER_HORIZONTAL] != 0) {
                relative_center_horizontal(child, width);
            } else if (rules[ANDROID_RELATIVE_ALIGN_PARENT_RIGHT] != 0) {
                child->rl_left = width - dp(ui, view->padding_right_dp) - child->measured.width;
                child->rl_right = child->rl_left + child->measured.width;
            }
        }
    }

    if (is_wrap_content_height) {
        height += dp(ui, view->padding_bottom_dp);
        height = std::max(height, dp(ui, view->min_height_dp));
        height = resolve_size(height, spec_h);
        for (android_view_s* child : sorted_v) {
            if (child->visibility == ANDROID_GONE) continue;
            const int* rules = child->relative_rules;
            if (rules[ANDROID_RELATIVE_CENTER_IN_PARENT] != 0 ||
                rules[ANDROID_RELATIVE_CENTER_VERTICAL] != 0) {
                relative_center_vertical(child, height);
            } else if (rules[ANDROID_RELATIVE_ALIGN_PARENT_BOTTOM] != 0) {
                child->rl_top = height - dp(ui, view->padding_bottom_dp) - child->measured.height;
                child->rl_bottom = child->rl_top + child->measured.height;
            }
        }
    }

    /* Gravity pass: offset the whole group inside the content bounds
     * (AOSP Gravity.apply after relative positioning). Default gravity 0
     * (START|TOP equivalent) produces no offset. */
    if (view->gravity != 0) {
        const float content_w = std::max(0.f, width - padding_h(view, ui));
        const float content_h = std::max(0.f, height - padding_v(view, ui));
        const float box_w = std::max(0.f, right - left);
        const float box_h = std::max(0.f, bottom - top);
        float ox = 0.f, oy = 0.f;
        apply_gravity(view->gravity, box_w, box_h, content_w, content_h, &ox, &oy);
        const float offset_x = ox - left;
        const float offset_y = oy - top;
        if (offset_x != 0.f || offset_y != 0.f) {
            for (android_view_s* child : sorted_v) {
                if (child->visibility == ANDROID_GONE) continue;
                child->rl_left += offset_x;
                child->rl_right += offset_x;
                child->rl_top += offset_y;
                child->rl_bottom += offset_y;
            }
        }
    }

    view->measured_baseline = -1.f;
    return {width, height};
}

} // namespace (RelativeLayout helpers)


android_measured_size_t measure_scroll(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    if (view->children.empty()) {
        return {resolve_size(padding_h(view, ui), spec_w), resolve_size(padding_v(view, ui), spec_h)};
    }
    android_view_s* child = view->children.front();
    const float content_w = std::max(0.f, spec_w.size - padding_h(view, ui));
    const float content_h = std::max(0.f, spec_h.size - padding_v(view, ui));
    /* AOSP ScrollView: child width measured against the exact content width,
     * child height measured unbounded. */
    measure_child_with_margins(view, child,
        {content_w, ANDROID_MEASURE_EXACTLY},
        {0.f, ANDROID_MEASURE_UNSPECIFIED}, ui);
    const float resolved_h = resolve_size(
        child->measured.height + margin_v(child->lp, ui) + padding_v(view, ui), spec_h);
    const float overflow_y = std::max(0.f,
        child->measured.height + margin_v(child->lp, ui) - std::max(0.f, resolved_h - padding_v(view, ui)));
    view->scroll_metrics.scrollable_overflow_y = overflow_y;
    view->scroll_metrics.scrollable_overflow_x = 0.f;
    return {resolve_size(child->measured.width + margin_h(child->lp, ui) + padding_h(view, ui), spec_w),
            resolved_h};
}

/* ── Measure dispatch ──────────────────────────────────────────────── */

android_measured_size_t measure_view(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    if (view->visibility == ANDROID_GONE) {
        view->measured = {0.f, 0.f};
        view->measured_baseline = 0.f;
        return view->measured;
    }
    android_measured_size_t result{};
    switch (view->cls) {
        case ANDROID_VIEW_LINEAR_LAYOUT: result = measure_linear(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_FRAME_LAYOUT:  result = measure_frame(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_RELATIVE_LAYOUT: result = measure_relative(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_SCROLL_VIEW:   result = measure_scroll(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_GRID_LAYOUT:   result = measure_grid(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_LIST_VIEW:
        case ANDROID_VIEW_RECYCLER_VIEW:    result = measure_list(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_CONSTRAINT_LAYOUT:
            result = measure_constraint(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_TEXT_VIEW:
        case ANDROID_VIEW_BUTTON:
        case ANDROID_VIEW_EDIT_TEXT:     result = measure_text_view(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_CHECK_BOX:
        case ANDROID_VIEW_RADIO_BUTTON:  result = measure_checkable(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_IMAGE_VIEW:    result = measure_image(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_PROGRESS_BAR:  result = measure_progress(view, spec_w, spec_h, ui); break;
        default: result = measure_base(view, spec_w, spec_h, ui); break;
    }
    view->measured = result;
    return result;
}

/* ── Layout: view groups ───────────────────────────────────────────── */

/* Faithful port of AOSP LinearLayout.layoutVertical/layoutHorizontal. The
 * container's major gravity offsets the whole column/row; the cross axis
 * resolves per-child gravity. Baseline alignment is grouped by the child's
 * vertical-gravity bucket (TOP/BOTTOM adjust, CENTER_VERTICAL never does). */
void layout_linear(android_view_s* view, float x, float y, float w, float h,
                   const android_ui_s* ui) {
    view->bounds = {x, y, w, h};
    const bool vertical = view->orientation == ANDROID_VERTICAL;
    const float pad_left = dp(ui, view->padding_left_dp), pad_top = dp(ui, view->padding_top_dp);
    const float pad_right = dp(ui, view->padding_right_dp), pad_bottom = dp(ui, view->padding_bottom_dp);
    const float content_w = std::max(0.f, w - pad_left - pad_right);
    const float content_h = std::max(0.f, h - pad_top - pad_bottom);

    /* Total content extent on the main axis (recomputed from children and
     * dividers, matching AOSP mTotalLength). */
    const float default_main_margin = linear_default_main_margin(view);
    auto child_main_margins = [&](const android_view_s* child) {
        const float mh = margin_h(child->lp, ui);
        const float mv = margin_v(child->lp, ui);
        if (view->use_default_margins) {
            if (vertical && mv == 0.f) return std::pair<float, float>{mh, default_main_margin};
            if (!vertical && mh == 0.f) return std::pair<float, float>{default_main_margin, mv};
        }
        return std::pair<float, float>{mh, mv};
    };
    float total = 0.f;
    int visible_count = 0;
    for (int i = 0; i < static_cast<int>(view->children.size()); ++i) {
        android_view_s* child = view->children[static_cast<size_t>(i)];
        if (child->visibility == ANDROID_GONE) continue;
        if (linear_has_divider_before(view, i)) {
            total += view->divider_thickness_px;
        }
        visible_count++;
        const auto [mh, mv] = child_main_margins(child);
        total += (vertical ? child->measured.height : child->measured.width) + (vertical ? mv : mh);
    }
    if (visible_count > 0 &&
        linear_has_divider_before(view, static_cast<int>(view->children.size()))) {
        total += view->divider_thickness_px;
    }

    /* Major gravity offsets the whole run (mTotalLength includes padding).
     * AOSP switches on the gravity masked to the axis; use equality, not
     * subset, so FILL never falls through to an edge alignment. In RTL the
     * container's default START gravity resolves to RIGHT (AOSP
     * getAbsoluteGravity), so a run with no explicit horizontal gravity is
     * aligned to the right edge. */
    const bool rtl = !vertical && view->layout_direction == ANDROID_LAYOUT_DIRECTION_RTL;
    const int32_t container_gravity = view->gravity;
    float run_offset = 0.f;
    if (vertical) {
        const int32_t vmask = container_gravity & ANDROID_GRAVITY_FILL_VERTICAL;
        if (vmask == ANDROID_GRAVITY_BOTTOM) {
            run_offset = h - pad_bottom - total - pad_top;
        } else if (vmask == ANDROID_GRAVITY_CENTER_VERTICAL) {
            run_offset = (h - pad_top - pad_bottom - total) * 0.5f;
        }
    } else {
        const int32_t container_gravity_norm = gravity_normalize_ltr(container_gravity);
        const int32_t hmask = container_gravity_norm & ANDROID_GRAVITY_FILL_HORIZONTAL;
        /* RIGHT, or the default START/LEFT gravity resolved to RIGHT in RTL
         * (the runtime has no RELATIVE_HORIZONTAL bit, so LEFT is treated as
         * the LinearLayout default START). */
        if (hmask == ANDROID_GRAVITY_RIGHT || (rtl && hmask == ANDROID_GRAVITY_LEFT)) {
            run_offset = w - pad_right - total - pad_left;
        } else if (hmask == ANDROID_GRAVITY_CENTER_HORIZONTAL) {
            run_offset = (w - pad_left - pad_right - total) * 0.5f;
        }
    }
    if (run_offset < 0.f) run_offset = 0.f;
    view->divider_rects.clear();
    const int count = static_cast<int>(view->children.size());

    float cursor = 0.f;
    for (int order = 0; order < count; ++order) {
        const int idx = rtl ? count - 1 - order : order;
        android_view_s* child = view->children[static_cast<size_t>(idx)];
        if (child->visibility == ANDROID_GONE) continue;
        const float m_left = dp(ui, child->lp.margins_dp.left), m_top = dp(ui, child->lp.margins_dp.top);
        const float m_right = dp(ui, child->lp.margins_dp.right), m_bottom = dp(ui, child->lp.margins_dp.bottom);
        /* useDefaultMargins: the main-axis margins default to the divider size */
        const float m_main_begin = (vertical ? m_top : m_left) == 0.f && view->use_default_margins
            ? default_main_margin : (vertical ? m_top : m_left);
        const float m_main_end = (vertical ? m_bottom : m_right) == 0.f && view->use_default_margins
            ? default_main_margin : (vertical ? m_bottom : m_right);
        const float cw = child->measured.width, ch = child->measured.height;
        const int32_t gravity = gravity_normalize_ltr(
            child->lp.gravity != ANDROID_GRAVITY_NO_GRAVITY
                ? child->lp.gravity : container_gravity);
        float cx = x, cy = y;
        if (vertical) {
            /* divider before this child (AOSP layoutVertical) */
            if (linear_has_divider_before(view, idx)) {
                const float top = y + pad_top + run_offset + cursor - view->divider_thickness_px;
                view->divider_rects.push_back({x + pad_left + view->divider_padding_px, top,
                    w - pad_right - pad_left - 2.f * view->divider_padding_px,
                    view->divider_thickness_px});
                cursor += view->divider_thickness_px;
            }
            /* main axis */
            cy = y + pad_top + run_offset + cursor + m_main_begin;
            /* cross axis: horizontal gravity of the child */
            const int32_t hmask = gravity & ANDROID_GRAVITY_FILL_HORIZONTAL;
            if (hmask == ANDROID_GRAVITY_CENTER_HORIZONTAL) {
                cx = x + pad_left + (content_w - cw) * 0.5f + m_left - m_right;
            } else if (hmask == ANDROID_GRAVITY_RIGHT) {
                cx = x + w - pad_right - cw - m_right;
            } else {
                cx = x + pad_left + m_left;
            }
            cursor += ch + m_main_begin + m_main_end;
        } else {
            /* divider before this child (AOSP layoutHorizontal uses the
             * after-check in RTL because rendering happens in reverse) */
            const bool divider = rtl ? linear_has_divider_after(view, idx)
                                     : linear_has_divider_before(view, idx);
            if (divider) {
                const float left = x + pad_left + run_offset + cursor - view->divider_thickness_px;
                view->divider_rects.push_back({left,
                    y + pad_top + view->divider_padding_px, view->divider_thickness_px,
                    h - pad_bottom - pad_top - 2.f * view->divider_padding_px});
                cursor += view->divider_thickness_px;
            }
            /* main axis */
            cx = x + pad_left + run_offset + cursor + m_main_begin;
            /* cross axis: vertical gravity of the child (equality on mask) */
            const int32_t vmask = gravity & ANDROID_GRAVITY_FILL_VERTICAL;
            if (vmask == ANDROID_GRAVITY_BOTTOM) {
                cy = y + h - pad_bottom - ch - m_bottom;
                const float baseline = child->measured_baseline;
                if (view->baseline_aligned && baseline >= 0.f &&
                    child->lp.height.kind != ANDROID_SIZE_KIND_MATCH_PARENT) {
                    const float descent = ch - baseline;
                    cy -= std::max(0.f, view->baseline_descent[2] - descent);
                }
            } else if (vmask == ANDROID_GRAVITY_CENTER_VERTICAL) {
                /* baseline alignment is intentionally not applied here (AOSP
                 * bug #1038483 removed it for CENTER_VERTICAL) */
                cy = y + pad_top + std::max(0.f, (content_h - ch) * 0.5f) + m_top - m_bottom;
            } else if (vmask == ANDROID_GRAVITY_TOP) {
                cy = y + pad_top + m_top;
                const float baseline = child->measured_baseline;
                if (view->baseline_aligned && baseline >= 0.f &&
                    child->lp.height.kind != ANDROID_SIZE_KIND_MATCH_PARENT) {
                    cy += std::max(0.f, view->baseline_ascent[1] - baseline);
                }
            } else {
                cy = y + pad_top;
            }
            cursor += cw + m_main_begin + m_main_end;
        }
        layout_view(child, cx, cy, cw, ch, ui);
    }
}

void layout_frame(android_view_s* view, float x, float y, float w, float h,
                  const android_ui_s* ui) {
    view->bounds = {x, y, w, h};
    const float pad_left = dp(ui, view->padding_left_dp), pad_top = dp(ui, view->padding_top_dp);
    const float pad_right = dp(ui, view->padding_right_dp), pad_bottom = dp(ui, view->padding_bottom_dp);
    const float parent_left = pad_left, parent_right = w - pad_right;
    const float parent_top = pad_top, parent_bottom = h - pad_bottom;
    for (android_view_s* child : view->children) {
        if (child->visibility == ANDROID_GONE) continue;
        const float m_left = dp(ui, child->lp.margins_dp.left), m_top = dp(ui, child->lp.margins_dp.top);
        const float m_right = dp(ui, child->lp.margins_dp.right), m_bottom = dp(ui, child->lp.margins_dp.bottom);
        const float cw = child->measured.width, ch = child->measured.height;
        /* AOSP FrameLayout.layoutChildren: exact per-gravity formulas; the
         * horizontal mask is matched by equality so FILL never falls through
         * to an edge case. */
        const int32_t gravity = child->lp.gravity != ANDROID_GRAVITY_NO_GRAVITY
            ? child->lp.gravity
            : (view->gravity != ANDROID_GRAVITY_NO_GRAVITY ? view->gravity
                                                                  : (ANDROID_GRAVITY_TOP | ANDROID_GRAVITY_LEFT));
        const int32_t hmask = gravity & ANDROID_GRAVITY_FILL_HORIZONTAL;
        float cx = parent_left + m_left;
        if (hmask == ANDROID_GRAVITY_CENTER_HORIZONTAL) {
            cx = parent_left + (parent_right - parent_left - cw) * 0.5f + m_left - m_right;
        } else if (hmask == ANDROID_GRAVITY_RIGHT) {
            cx = parent_right - cw - m_right;
        }
        const int32_t vmask = gravity & ANDROID_GRAVITY_FILL_VERTICAL;
        float cy = parent_top + m_top;
        if (vmask == ANDROID_GRAVITY_CENTER_VERTICAL) {
            cy = parent_top + (parent_bottom - parent_top - ch) * 0.5f + m_top - m_bottom;
        } else if (vmask == ANDROID_GRAVITY_BOTTOM) {
            cy = parent_bottom - ch - m_bottom;
        }
        layout_view(child, x + cx, y + cy, cw, ch, ui);
    }
}

void layout_relative(android_view_s* view, float x, float y, float w, float h,
                     const android_ui_s* ui) {
    view->bounds = {x, y, w, h};
    /* AOSP onLayout: the positions were already computed during onMeasure and
     * cached in the layout params; apply them verbatim. */
    for (android_view_s* child : view->children) {
        if (child->visibility == ANDROID_GONE) {
            child->bounds = {x, y, 0.f, 0.f};
            continue;
        }
        layout_view(child, x + child->rl_left, y + child->rl_top,
                    child->rl_right - child->rl_left,
                    child->rl_bottom - child->rl_top, ui);
    }
}

void layout_scroll(android_view_s* view, float x, float y, float w, float h,
                   const android_ui_s* ui) {
    view->bounds = {x, y, w, h};
    if (view->children.empty()) return;
    android_view_s* child = view->children.front();
    const float m_left = dp(ui, child->lp.margins_dp.left), m_top = dp(ui, child->lp.margins_dp.top);
    const float mv = margin_v(child->lp, ui);
    const float content_h = std::max(0.f, h - padding_v(view, ui));
    const float overflow = std::max(0.f, child->measured.height + mv - content_h);
    if (view->scroll_y > overflow) view->scroll_y = overflow;
    if (view->scroll_y < 0.f) view->scroll_y = 0.f;
    if (view->scroll_x != 0.f) view->scroll_x = 0.f;
    view->scroll_metrics.scrollable_overflow_y = overflow;
    view->scroll_metrics.scrollable_overflow_x = 0.f;
    view->scroll_metrics.scroll_offset_x = view->scroll_x;
    view->scroll_metrics.scroll_offset_y = view->scroll_y;
    const float cy = y + dp(ui, view->padding_top_dp) + m_top - view->scroll_y;
    const float cx = x + dp(ui, view->padding_left_dp) + m_left - view->scroll_x;
    layout_view(child, cx, cy, child->measured.width, child->measured.height, ui);
}

/* ── Layout dispatch ───────────────────────────────────────────────── */

/* ── ConstraintLayout ──────────────────────────────────────────────── */
/* Drives the ported androidx.constraintlayout.core solver. Children are
 * measured first (their measured size is the intrinsic for WRAP/FIXED),
 * then the layout pass builds a ConstraintWidgetContainer, solves, and
 * writes the resolved frames back to the views. */

using constraint::Barrier;
using constraint::ConstraintAnchor;
using constraint::ConstraintWidget;
using constraint::ConstraintWidgetContainer;

static ConstraintWidget::DimensionBehaviour constraint_behavior(
    const android_size_t& size) {
    switch (size.kind) {
        case ANDROID_SIZE_KIND_MATCH_PARENT:
            return ConstraintWidget::DimensionBehaviour::MATCH_PARENT;
        case ANDROID_SIZE_KIND_WRAP_CONTENT:
            return ConstraintWidget::DimensionBehaviour::WRAP_CONTENT;
        default:
            /* EXACT: value 0 is the 0dp MATCH_CONSTRAINT convention */
            if (size.value_dp <= 0.f) {
                return ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT;
            }
            return ConstraintWidget::DimensionBehaviour::FIXED;
    }
}

static ConstraintAnchor::Type constraint_anchor_type(int32_t side) {
    switch (side) {
        case ANDROID_CONSTRAINT_RIGHT: return ConstraintAnchor::Type::RIGHT;
        case ANDROID_CONSTRAINT_TOP: return ConstraintAnchor::Type::TOP;
        case ANDROID_CONSTRAINT_BOTTOM: return ConstraintAnchor::Type::BOTTOM;
        case ANDROID_CONSTRAINT_END: return ConstraintAnchor::Type::RIGHT;  /* LTR */
        case ANDROID_CONSTRAINT_START: /* fall through */
        case ANDROID_CONSTRAINT_LEFT:
        default: return ConstraintAnchor::Type::LEFT;
    }
}

static void apply_constraint_params(
    ConstraintWidget* widget, const android_view_s* view,
    ConstraintWidgetContainer* container, const android_ui_s* ui) {
    const android_constraint_params_t& cp = view->lp.constraint;
    widget->set_horizontal_bias_percent(cp.bias_h);
    widget->set_vertical_bias_percent(cp.bias_v);
    widget->set_horizontal_chain_style(cp.chain_style_h);
    widget->set_vertical_chain_style(cp.chain_style_v);
    if (cp.dimension_ratio > 0.f) {
        widget->set_dimension_ratio(cp.dimension_ratio, ConstraintWidget::UNKNOWN);
    }
    widget->set_horizontal_match_style(
        cp.match_default_w, static_cast<int>(cp.match_min_w_dp),
        static_cast<int>(cp.match_max_w_dp), cp.match_percent_w);
    widget->set_vertical_match_style(
        cp.match_default_h, static_cast<int>(cp.match_min_h_dp),
        static_cast<int>(cp.match_max_h_dp), cp.match_percent_h);
    for (int i = 0; i < cp.constraint_count; ++i) {
        const android_constraint_t& c = cp.constraints[i];
        ConstraintWidget* target = container;
        if (c.target_id != -1) {
            if (android_view_s* tv = android_ui_find_view_by_id(
                    const_cast<android_ui_s*>(ui), c.target_id)) {
                target = tv->constraint_widget;
            } else {
                continue; /* unresolved target: skip the connection */
            }
        }
        if (target == nullptr) {
            continue; /* target widget not created yet (e.g. barrier pass): skip */
        }
        const ConstraintAnchor::Type side = constraint_anchor_type(c.side);
        widget->connect(side, target, constraint_anchor_type(c.target_side),
                        dp(ui, c.margin_dp));
        if (c.gone_margin_dp != c.margin_dp) {
            widget->get_anchor(side)->set_gone_margin(dp(ui, c.gone_margin_dp));
        }
    }
}

void layout_constraint(android_view_s* view, float x, float y, float w, float h,
                       const android_ui_s* ui) {
    if (view->visibility == ANDROID_GONE) {
        view->bounds = {x, y, 0.f, 0.f};
        return;
    }
    ConstraintWidgetContainer container(0.f, 0.f, w, h);

    /* Pass 1: create every child widget (regular + barriers) so that
     * references resolve in any order. */
    for (android_view_s* child : view->children) {
        if (child->visibility == ANDROID_GONE) continue;
        if (child->cls == ANDROID_VIEW_BARRIER) {
            Barrier* barrier = new Barrier();
            child->constraint_widget = barrier;
            barrier->set_debug_name("barrier");
            barrier->set_barrier_type(child->barrier_type);
            barrier->set_margin(dp(ui, child->barrier_margin_dp));
            barrier->set_allows_gone_widget(child->barrier_allows_gone);
            container.add(barrier);
            continue;
        }
        ConstraintWidget* widget =
            new ConstraintWidget(child->measured.width, child->measured.height);
        child->constraint_widget = widget;
        widget->set_debug_name("view");
        const ConstraintWidget::DimensionBehaviour bh =
            constraint_behavior(child->lp.width);
        const ConstraintWidget::DimensionBehaviour bv =
            constraint_behavior(child->lp.height);
        widget->set_horizontal_dimension_behaviour(bh);
        widget->set_vertical_dimension_behaviour(bv);
        if (bh == ConstraintWidget::DimensionBehaviour::MATCH_PARENT) {
            widget->width = w;
        }
        if (bv == ConstraintWidget::DimensionBehaviour::MATCH_PARENT) {
            widget->height = h;
        }
        container.add(widget);
    }

    /* Pass 2: wire barrier references (all widgets now exist). */
    for (android_view_s* child : view->children) {
        if (child->visibility == ANDROID_GONE) continue;
        if (child->cls != ANDROID_VIEW_BARRIER) continue;
        Barrier* barrier = static_cast<Barrier*>(child->constraint_widget);
        if (barrier == nullptr) continue;
        for (int32_t ref : child->barrier_references) {
            if (android_view_s* tv = android_ui_find_view_by_id(
                    const_cast<android_ui_s*>(ui), ref)) {
                if (tv->constraint_widget != nullptr) {
                    barrier->add_helper_widget(tv->constraint_widget);
                }
            }
        }
    }

    /* Pass 3: wire constraint connections on regular children. */
    for (android_view_s* child : view->children) {
        if (child->visibility == ANDROID_GONE) continue;
        if (child->cls == ANDROID_VIEW_BARRIER) continue;
        if (ConstraintWidget* widget = child->constraint_widget) {
            apply_constraint_params(widget, child, &container, ui);
        }
    }

    container.layout();

    for (android_view_s* child : view->children) {
        ConstraintWidget* widget = child->constraint_widget;
        if (widget == nullptr) {
            if (child->visibility == ANDROID_GONE) child->bounds = {x, y, 0.f, 0.f};
            continue;
        }
        const float cx = x + widget->get_left();
        const float cy = y + widget->get_top();
        const float cw = widget->get_width();
        const float ch = widget->get_height();
        child->bounds = {cx, cy, cw, ch};
        if (child->cls != ANDROID_VIEW_BARRIER) {
            child->measured = {cw, ch};
        }
        child->constraint_widget = nullptr;
        delete widget;
    }
    view->bounds = {x, y, w, h};
}

android_measured_size_t measure_constraint(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    const float padding_w = padding_h(view, ui);
    const float padding_vh = padding_v(view, ui);
    float max_w = 0.f, max_h = 0.f;
    for (android_view_s* child : view->children) {
        if (child->visibility == ANDROID_GONE) continue;
        if (child->cls == ANDROID_VIEW_BARRIER) continue; /* virtual helper */
        const android_measure_spec_t cw = get_child_measure_spec(
            {std::max(0.f, spec_w.size - padding_w), spec_w.mode}, 0.f,
            child->lp.width, ui);
        const android_measure_spec_t ch = get_child_measure_spec(
            {std::max(0.f, spec_h.size - padding_vh), spec_h.mode}, 0.f,
            child->lp.height, ui);
        child->measured = measure_view(child, cw, ch, ui);
        max_w = std::max(max_w, child->measured.width + margin_h(child->lp, ui));
        max_h = std::max(max_h, child->measured.height + margin_v(child->lp, ui));
    }
    const float desired_w = std::max(dp(ui, view->min_width_dp), max_w) + padding_w;
    const float desired_h = std::max(dp(ui, view->min_height_dp), max_h) + padding_vh;
    const android_measured_size_t result{resolve_size(desired_w, spec_w),
                                         resolve_size(desired_h, spec_h)};
    view->measured_baseline = -1.f;
    return result;
}

void layout_view(android_view_s* view, float x, float y, float w, float h,
                 const android_ui_s* ui) {
    if (view->visibility == ANDROID_GONE) {
        view->bounds = {x, y, 0.f, 0.f};
        return;
    }
    switch (view->cls) {
        case ANDROID_VIEW_LINEAR_LAYOUT: layout_linear(view, x, y, w, h, ui); break;
        case ANDROID_VIEW_FRAME_LAYOUT:  layout_frame(view, x, y, w, h, ui); break;
        case ANDROID_VIEW_RELATIVE_LAYOUT: layout_relative(view, x, y, w, h, ui); break;
        case ANDROID_VIEW_SCROLL_VIEW:   layout_scroll(view, x, y, w, h, ui); break;
        case ANDROID_VIEW_GRID_LAYOUT:   layout_grid(view, x, y, w, h, ui); break;
        case ANDROID_VIEW_LIST_VIEW:
        case ANDROID_VIEW_RECYCLER_VIEW:   layout_list(view, x, y, w, h, ui); break;
        case ANDROID_VIEW_CONSTRAINT_LAYOUT:
            layout_constraint(view, x, y, w, h, ui); break;
        default: view->bounds = {x, y, w, h}; break;
    }
}

/* ── Hit testing ───────────────────────────────────────────────────── */

android_view_s* hit_test(android_view_s* view, float px, float py) {
    if (view->visibility != ANDROID_VISIBLE || !view->enabled) return nullptr;
    if (px < view->bounds.x || py < view->bounds.y ||
        px >= view->bounds.x + view->bounds.width ||
        py >= view->bounds.y + view->bounds.height) {
        return nullptr;
    }
    for (auto it = view->children.rbegin(); it != view->children.rend(); ++it) {
        if (android_view_s* hit = hit_test(*it, px, py)) return hit;
    }
    return view;
}

} // namespace viewruntime::android

extern "C" {

API status_t android_ui_measure(
    android_ui_t ui, android_view_t root, float width_px, float height_px) {
    if (!ui || !root || root->ui != ui) return ERROR_NULL_ARG;
    if (width_px <= 0.f || height_px <= 0.f || !std::isfinite(width_px) || !std::isfinite(height_px)) {
        return ERROR_INVALID_STATE;
    }
    viewruntime::android::measure_view(root,
        {width_px, ANDROID_MEASURE_EXACTLY},
        {height_px, ANDROID_MEASURE_EXACTLY}, ui);
    return OK;
}

API status_t android_ui_layout(
    android_ui_t ui, android_view_t root,
    float x, float y, float width_px, float height_px) {
    if (!ui || !root || root->ui != ui) return ERROR_NULL_ARG;
    if (width_px < 0.f || height_px < 0.f || !std::isfinite(width_px) || !std::isfinite(height_px)) {
        return ERROR_INVALID_STATE;
    }
    viewruntime::android::layout_view(root, x, y, width_px, height_px, ui);
    return OK;
}

API status_t android_view_get_bounds(
    android_view_t view, rectf* out_bounds) {
    if (!view || !out_bounds) return ERROR_NULL_ARG;
    *out_bounds = view->bounds;
    return OK;
}

API status_t android_view_get_measured_size(
    android_view_t view, sizef* out_size) {
    if (!view || !out_size) return ERROR_NULL_ARG;
    *out_size = {view->measured.width, view->measured.height};
    return OK;
}

API android_view_t android_ui_hit_test(
    android_ui_t ui, android_view_t root, float x, float y) {
    if (!ui || !root || root->ui != ui) return nullptr;
    return viewruntime::android::hit_test(root, x, y);
}

API status_t android_view_set_scroll_offset(
    android_view_t view, float x, float y) {
    if (!view) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_SCROLL_VIEW &&
        view->cls != ANDROID_VIEW_LIST_VIEW &&
        view->cls != ANDROID_VIEW_RECYCLER_VIEW) return ERROR_INVALID_STATE;
    view->scroll_x = x < 0.f ? 0.f : x;
    view->scroll_y = y < 0.f ? 0.f : y;
    return OK;
}

API scroll_metrics_t android_view_get_scroll_metrics(
    android_view_t view) {
    if (!view) return {};
    return view->scroll_metrics;
}

} // extern "C"
