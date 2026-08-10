#include "android_types.h"

#include <algorithm>
#include <limits>
#include <vector>

namespace viewruntime::android {

/* ── LinearLayout ──────────────────────────────────────────────────── */
/* Faithful port of AOSP LinearLayout.measureVertical/measureHorizontal and
 * layoutVertical/layoutHorizontal
 * (frameworks/base/core/java/android/widget/LinearLayout.java). */

/* AOSP LinearLayout.hasDividerBeforeChildAt (LinearLayout.java:734, master
 * semantics: BEGINNING before the first non-GONE view, MIDDLE between
 * non-GONE views, END after the last). */
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

/* AOSP LinearLayout.hasDividerAfterChildAt (LinearLayout.java:758, used by
 * the RTL layout path). */
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

/* AOSP Gravity.getAbsoluteGravity (android.view.Gravity): resolves the
 * relative START/END bits against the real layout direction — START -> LEFT,
 * END -> RIGHT for LTR; START -> RIGHT, END -> LEFT for RTL. Non-relative
 * bits pass through (LinearLayout.java:1707 / 1786). */
static int32_t linear_gravity_absolute(int32_t gravity, bool rtl) {
    int32_t result = gravity;
    if (gravity_has(result, ANDROID_GRAVITY_START)) {
        result &= ~ANDROID_GRAVITY_START; /* clears RELATIVE_LAYOUT_DIRECTION|LEFT */
        result |= rtl ? ANDROID_GRAVITY_RIGHT : ANDROID_GRAVITY_LEFT;
    } else if (gravity_has(result, ANDROID_GRAVITY_END)) {
        result &= ~ANDROID_GRAVITY_END; /* clears RELATIVE_LAYOUT_DIRECTION|RIGHT */
        result |= rtl ? ANDROID_GRAVITY_LEFT : ANDROID_GRAVITY_RIGHT;
    }
    return result;
}

/* ── Measure ───────────────────────────────────────────────────────── */

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
    /* AOSP consumedExcessSpace: the measured (WRAP) sizes of 0-dimension
     * weighted children measured during pass 1, re-added to remainingExcess
     * so they only ever receive their share (LinearLayout.java:992). */
    float consumed_excess = 0.f;

    float max_ascent[4] = {-1.f, -1.f, -1.f, -1.f};
    float max_descent[4] = {-1.f, -1.f, -1.f, -1.f};

    float largest_main = -(std::numeric_limits<float>::max)();
    int non_skipped_child_count = 0;
    const float default_main_margin = linear_default_main_margin(view);

    /* AOSP measureVertical/measureHorizontal pass 1. */
    for (size_t idx = 0; idx < view->children.size(); ++idx) {
        android_view_s* child = view->children[idx];
        if (child->visibility == ANDROID_GONE) continue;
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
        /* AOSP useExcessSpace: lp.height == 0 && lp.weight > 0 (vertical). */
        const bool use_excess = child->lp.weight > 0.f &&
            ((vertical && child->lp.height.kind == ANDROID_SIZE_KIND_EXACT &&
              child->lp.height.value_dp == 0.f) ||
             (!vertical && child->lp.width.kind == ANDROID_SIZE_KIND_EXACT &&
              child->lp.width.value_dp == 0.f));

        if (use_excess && main_mode == ANDROID_MEASURE_EXACTLY) {
            /* AOSP optimization: skip measuring 0-dimension weighted children
             * under an EXACTLY parent; only their margins take space
             * (LinearLayout.java:856). Vertical accumulates with Math.max for
             * the negative-margin case (LinearLayout.java:861); horizontal
             * only when not EXACTLY (LinearLayout.java:1204-1208). */
            if (vertical) {
                total_length = std::max(total_length, total_length + (vertical ? mv : mh));
            } else {
                total_length += vertical ? mv : mh;
            }
            /* AOSP measureHorizontal baseline-aligned exception
             * (LinearLayout.java:1216-1224): baseline alignment needs the
             * measured child to derive its baseline offset, so the 0-dim
             * weighted child is measured with free (UNSPECIFIED) specs and
             * skippedMeasure stays false; the child is re-measured in the
             * weight pass. measureVertical has NO such exception
             * (LinearLayout.java:856-862). */
            if (!vertical && view->baseline_aligned) {
                child->measured = measure_view(child,
                    {spec_w.size, ANDROID_MEASURE_UNSPECIFIED},
                    {spec_h.size, ANDROID_MEASURE_UNSPECIFIED}, ui);
            } else {
                skipped_measure = true;
            }
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
            /* AOSP accumulates with Math.max so negative margins can collapse
             * the run: vertical always (LinearLayout.java:891-892), horizontal
             * when not EXACTLY (LinearLayout.java:1256-1258). */
            if (vertical || main_mode != ANDROID_MEASURE_EXACTLY) {
                total_length = std::max(total_length,
                    total_length + main_size + (vertical ? mv : mh));
            } else {
                total_length += main_size + (vertical ? mv : mh);
            }
            if (use_excess) {
                /* AOSP consumedExcessSpace += childHeight */
                consumed_excess += main_size;
            }
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

        /* AOSP baseline buckets (measureHorizontal only, LinearLayout.java:1279):
         * gravity = (lp.gravity < 0 ? mGravity : lp.gravity)
         * index = ((gravity >> AXIS_Y_SHIFT) & ~AXIS_SPECIFIED) >> 1 */
        if (!vertical && view->baseline_aligned && child->measured_baseline >= 0.f) {
            /* AOSP masks with Gravity.VERTICAL_GRAVITY_MASK before deriving the
             * bucket (LinearLayout.java:1284-1287), so relative bits (START/END)
             * never leak into the index. */
            const int32_t gravity = (child->lp.gravity < 0 ? view->gravity : child->lp.gravity)
                & ANDROID_GRAVITY_FILL_VERTICAL;
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

    /* AOSP measureHorizontal: when any baseline bucket is populated, the cross
     * size is raised to max(ascent) + max(descent) across the buckets
     * (LinearLayout.java:1316-1329). */
    if (!vertical) {
        const bool any_baseline = max_ascent[0] != -1.f || max_ascent[1] != -1.f ||
                                  max_ascent[2] != -1.f || max_ascent[3] != -1.f;
        if (any_baseline) {
            const float ascent = std::max(std::max(max_ascent[0], max_ascent[1]),
                                          std::max(max_ascent[2], max_ascent[3]));
            const float descent = std::max(std::max(max_descent[0], max_descent[1]),
                                           std::max(max_descent[2], max_descent[3]));
            max_cross = std::max(max_cross, ascent + descent);
        }
    }
    /* AOSP mTotalLength (content extent, before padding) is recorded here for
     * the layout pass; the weight pass / useLargestChild re-sum overwrite it
     * below exactly like AOSP re-sums mTotalLength. */
    view->linear_measured_main = total_length;

    /* measureWithLargestChild: every visible child contributes the size of
     * the largest one (AOSP re-sums mTotalLength with largestChildHeight,
     * LinearLayout.java:952). */
    if (view->use_largest_child && main_mode != ANDROID_MEASURE_EXACTLY &&
        non_skipped_child_count > 0) {
        total_length = 0.f;
        for (size_t idx = 0; idx < view->children.size(); ++idx) {
            android_view_s* child = view->children[idx];
            if (child->visibility == ANDROID_GONE) continue;
            /* AOSP measureHorizontal re-adds the per-child dividers in the
             * re-sum (LinearLayout.java:1349-1351); measureVertical does NOT
             * (LinearLayout.java:952-975). */
            if (!vertical && linear_has_divider_before(view, static_cast<int>(idx))) {
                total_length += view->divider_thickness_px;
            }
            float mh = margin_h(child->lp, ui);
            float mv = margin_v(child->lp, ui);
            if (view->use_default_margins) {
                if (vertical && mv == 0.f) mv = default_main_margin;
                if (!vertical && mh == 0.f) mh = default_main_margin;
            }
            /* AOSP Math.max accumulation (LinearLayout.java:971-973 /
             * 1359-1361; the re-sum only runs when not EXACTLY). */
            total_length = std::max(total_length,
                total_length + largest_main + (vertical ? mv : mh));
        }
        /* AOSP end divider in the horizontal re-sum (LinearLayout.java:1365-1367). */
        if (!vertical && non_skipped_child_count > 0 &&
            linear_has_divider_before(view, static_cast<int>(view->children.size()))) {
            total_length += view->divider_thickness_px;
        }
        view->linear_measured_main = total_length;
    }

    /* Add padding, then resolve the main size BEFORE the weight pass; the
     * resolved value is what the layout reports. */
    total_length += vertical ? padding_v(view, ui) : padding_h(view, ui);
    const float main_resolved = resolve_size(
        std::max(total_length, vertical ? dp(ui, view->min_height_dp) : dp(ui, view->min_width_dp)),
        vertical ? spec_h : spec_w);

    /* AOSP P+ weight pass (LinearLayout.java:991): weighted children are
     * always re-measured when any weight exists (sRemeasureWeightedChildren),
     * distributing positive AND negative excess. consumedExcessSpace cancels
     * the pass-1 WRAP sizes of 0-dimension weighted children. */
    if (skipped_measure || total_weight > 0.f) {
        float remaining_excess = main_resolved - total_length + consumed_excess;
        float remaining_weight_sum = view->weight_sum > 0.f ? view->weight_sum : total_weight;
        float new_total = 0.f;
        int weight_visible_count = 0;

        /* AOSP resets the baseline buckets and the cross-axis max (maxHeight)
         * before the weight pass re-accumulates them from the re-measured
         * children only (LinearLayout.java:1391-1393). */
        for (int i = 0; i < 4; ++i) {
            max_ascent[i] = -1.f;
            max_descent[i] = -1.f;
        }
        max_cross = -(std::numeric_limits<float>::max)();

        for (size_t idx = 0; idx < view->children.size(); ++idx) {
            android_view_s* child = view->children[idx];
            if (child->visibility == ANDROID_GONE) continue;
            weight_visible_count++;
            /* AOSP measureHorizontal re-adds the per-child dividers in the
             * weight pass (LinearLayout.java:1405-1407); measureVertical does
             * NOT (LinearLayout.java:999-1053). */
            if (!vertical && linear_has_divider_before(view, static_cast<int>(idx))) {
                new_total += view->divider_thickness_px;
            }
            const float mh = margin_h(child->lp, ui);
            const float mv = margin_v(child->lp, ui);
            const float child_weight = child->lp.weight;
            if (child_weight > 0.f) {
                /* AOSP guards only on childWeight > 0, NOT on remainingWeightSum
                 * (LinearLayout.java:1007/1411): once it reaches 0 the division
                 * runs against the real value and the share goes negative
                 * (children shrink). */
                /* AOSP truncates each share to int — (int)(childWeight *
                 * remainingExcess / remainingWeightSum) — so the fractional
                 * remainder accumulates and is absorbed by the last weighted
                 * child via the sequential remainingExcess subtraction
                 * (LinearLayout.java:1008/1412). The runtime stays float, so
                 * the truncation is replicated explicitly for fidelity. When
                 * remainingWeightSum is exactly 0 the Java float division
                 * yields ±Inf and the (int) cast saturates to Integer.MAX/MIN_VALUE
                 * (0 for NaN when excess == 0); C++ cannot cast Inf to int, so
                 * the AOSP limits are replicated by hand. */
                float share;
                if (remaining_weight_sum == 0.f) {
                    if (remaining_excess == 0.f) {
                        share = 0.f; /* (int)NaN == 0 */
                    } else {
                        share = remaining_excess > 0.f
                            ? static_cast<float>((std::numeric_limits<int>::max)())
                            : static_cast<float>((std::numeric_limits<int>::min)());
                    }
                } else {
                    share = static_cast<float>(static_cast<int>(
                        child_weight * remaining_excess / remaining_weight_sum));
                }
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
            }
            /* AOSP re-accumulates the baseline buckets for EVERY non-GONE
             * child in the weight pass, weighted or not
             * (LinearLayout.java:1462-1475). */
            if (!vertical && view->baseline_aligned && child->measured_baseline >= 0.f) {
                /* AOSP masks with Gravity.VERTICAL_GRAVITY_MASK
                 * (LinearLayout.java:1466-1469). */
                const int32_t gravity = (child->lp.gravity < 0 ? view->gravity : child->lp.gravity)
                    & ANDROID_GRAVITY_FILL_VERTICAL;
                const int bucket = ((gravity >> 4) & ~1) >> 1;
                const float child_height_with_margins = child->measured.height + mv;
                max_ascent[bucket] = std::max(max_ascent[bucket], child->measured_baseline);
                max_descent[bucket] = std::max(max_descent[bucket], child_height_with_margins - child->measured_baseline);
            }
            /* AOSP re-accumulates the cross metrics for EVERY child in the
             * weight pass (measureVertical.java:1038, measureHorizontal:1451):
             * maxWidth/maxHeight, allFillParent and alternativeMaxWidth/
             * alternativeMaxHeight. */
            const bool match_locally = cross_mode != ANDROID_MEASURE_EXACTLY &&
                ((vertical && child->lp.width.kind == ANDROID_SIZE_KIND_MATCH_PARENT) ||
                 (!vertical && child->lp.height.kind == ANDROID_SIZE_KIND_MATCH_PARENT));
            const float margin_cross = vertical ? mh : mv;
            const float cross_size = (vertical ? child->measured.width : child->measured.height) + margin_cross;
            max_cross = std::max(max_cross, cross_size);
            all_fill_parent = all_fill_parent &&
                ((vertical && child->lp.width.kind == ANDROID_SIZE_KIND_MATCH_PARENT) ||
                 (!vertical && child->lp.height.kind == ANDROID_SIZE_KIND_MATCH_PARENT));
            alternative_max = std::max(alternative_max, match_locally ? margin_cross : cross_size);
            /* AOSP Math.max accumulation (LinearLayout.java:1051-1052 /
             * 1446-1448). */
            if (vertical || main_mode != ANDROID_MEASURE_EXACTLY) {
                new_total = std::max(new_total,
                    new_total + (vertical ? child->measured.height : child->measured.width) +
                    (vertical ? mv : mh));
            } else {
                new_total += (vertical ? child->measured.height : child->measured.width) +
                             (vertical ? mv : mh);
            }
        }
        /* AOSP end divider in the horizontal weight pass (LinearLayout.java:1478-1480). */
        if (!vertical && weight_visible_count > 0 &&
            linear_has_divider_before(view, static_cast<int>(view->children.size()))) {
            new_total += view->divider_thickness_px;
        }
        total_length = new_total + (vertical ? padding_v(view, ui) : padding_h(view, ui));
        view->linear_measured_main = new_total;

        /* AOSP re-elevates maxHeight from the baseline buckets after the
         * weight pass (LinearLayout.java:1486-1499). */
        if (!vertical) {
            const bool any_baseline = max_ascent[0] != -1.f || max_ascent[1] != -1.f ||
                                      max_ascent[2] != -1.f || max_ascent[3] != -1.f;
            if (any_baseline) {
                const float ascent = std::max(std::max(max_ascent[0], max_ascent[1]),
                                              std::max(max_ascent[2], max_ascent[3]));
                const float descent = std::max(std::max(max_descent[0], max_descent[1]),
                                               std::max(max_descent[2], max_descent[3]));
                max_cross = std::max(max_cross, ascent + descent);
            }
        }
    } else if (view->use_largest_child && main_mode != ANDROID_MEASURE_EXACTLY) {
        /* AOSP merges weightedMax into alternativeMax before the
         * measureWithLargestChild re-measure (LinearLayout.java:1059-1060 /
         * 1501: alternativeMax = Math.max(alternativeMax, weightedMax)). In
         * this branch no weight was distributed (weighted_max stayed 0), so it
         * is a no-op today — kept for exact AOSP structure. */
        alternative_max = std::max(alternative_max, weighted_max);
        /* AOSP: no excess to distribute, so make all weighted views as large
         * as the largest child (they were measured once already,
         * LinearLayout.java:1065). */
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
     * not EXACTLY (AOSP ViewGroup.forceUniformWidth, LinearLayout.java:1104). */
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
                /* AOSP forceUniformWidth passes the container's horizontal
                 * padding plus the child's margins as the used size
                 * (LinearLayout.java:1120): getChildMeasureSpec(uniform,
                 * mPaddingLeft + mPaddingRight + lp.leftMargin +
                 * lp.rightMargin, lp.width). */
                cw = get_child_measure_spec({uniform, ANDROID_MEASURE_EXACTLY},
                                            padding_h(view, ui) + mh,
                                            child->lp.width, ui);
                /* AOSP temporarily forces lp.height = child.getMeasuredHeight()
                 * so the main axis re-measures EXACTLY at the already-measured
                 * height instead of the original WRAP layout param
                 * (LinearLayout.java:1116-1117). For a 0-dim weighted child
                 * that height is the one from the weight pass. */
                ch = {child->measured.height, ANDROID_MEASURE_EXACTLY};
            } else {
                /* AOSP forceUniformHeight reuses the old measured width
                 * (LinearLayout.java:1558-1559). */
                cw = {child->measured.width, ANDROID_MEASURE_EXACTLY};
                /* AOSP forceUniformHeight passes the container's vertical
                 * padding plus the child's margins as the used size
                 * (LinearLayout.java:1562): getChildMeasureSpec(uniform,
                 * mPaddingTop + mPaddingBottom + lp.topMargin +
                 * lp.bottomMargin, lp.height). */
                ch = get_child_measure_spec({uniform, ANDROID_MEASURE_EXACTLY},
                                            padding_v(view, ui) + mv,
                                            child->lp.height, ui);
            }
            child->measured = measure_view(child, cw, ch, ui);
        }
    }
    return result;
}

/* ── Layout ────────────────────────────────────────────────────────── */

/* Faithful port of AOSP LinearLayout.layoutVertical/layoutHorizontal
 * (LinearLayout.java:1656/1761). The container's major gravity offsets the
 * whole column/row; the cross axis resolves per-child gravity. The per-child
 * gravity inheritance is EXACTLY AOSP: lp.gravity defaults to -1
 * (LayoutParams.gravity = -1, LinearLayout.java:2079) and, when < 0, the
 * child inherits the container's cross-axis gravity (minorGravity)
 * (LinearLayout.java:1702-1705 / 1828-1831). */
void layout_linear(android_view_s* view, float x, float y, float w, float h,
                   const android_ui_s* ui) {
    view->bounds = {x, y, w, h};
    const bool vertical = view->orientation == ANDROID_VERTICAL;
    const float pad_left = dp(ui, view->padding_left_dp), pad_top = dp(ui, view->padding_top_dp);
    const float pad_right = dp(ui, view->padding_right_dp), pad_bottom = dp(ui, view->padding_bottom_dp);
    /* AOSP childSpace = width - paddingLeft - paddingRight (LinearLayout.java:
     * 1667) / height - paddingTop - paddingBottom (:1773) with NO clamp: a
     * cross-axis CENTER with padding exceeding the box resolves to a negative
     * space and the child is offset (clipped) accordingly. */
    const float content_w = w - pad_left - pad_right;
    const float content_h = h - pad_top - pad_bottom;

    /* AOSP mTotalLength (content extent, no padding) is read directly from
     * measure: layoutVertical/layoutHorizontal use it for the major-gravity
     * offset (LinearLayout.java:1674-1689 / 1786-1801). Re-summing here would
     * diverge because after the weight pass or the useLargestChild re-sum the
     * vertical mTotalLength excludes the dividers (LinearLayout.java:952-975 /
     * 999-1053), while the horizontal one includes them (1331-1368 / 1398-1483). */
    const float total = view->linear_measured_main;
    const float default_main_margin = linear_default_main_margin(view);

    /* Major gravity offsets the whole run. AOSP resolves the container's
     * relative gravity against the real layout direction (getAbsoluteGravity,
     * LinearLayout.java:1786): START resolves to RIGHT in RTL, an explicit
     * LEFT stays LEFT and an explicit END resolves to LEFT. */
    const bool is_rtl = view->layout_direction == ANDROID_LAYOUT_DIRECTION_RTL;
    const bool rtl = !vertical && is_rtl;
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
        const int32_t container_abs = linear_gravity_absolute(container_gravity, is_rtl);
        const int32_t hmask = container_abs & ANDROID_GRAVITY_FILL_HORIZONTAL;
        if (hmask == ANDROID_GRAVITY_RIGHT) {
            run_offset = w - pad_right - total - pad_left;
        } else if (hmask == ANDROID_GRAVITY_CENTER_HORIZONTAL) {
            run_offset = (w - pad_left - pad_right - total) * 0.5f;
        }
    }
    /* AOSP does NOT clamp childTop/childLeft to 0: when mTotalLength exceeds
     * the container, the BOTTOM/CENTER offsets go negative and the overflow is
     * clipped at the top/left edge (LinearLayout.java:1677/1682,
     * 1789/1794). */
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
        /* AOSP: int gravity = lp.gravity; if (gravity < 0) gravity = minorGravity;
         * then getAbsoluteGravity(gravity, layoutDirection)
         * (LinearLayout.java:1702-1707 / 1828-1831). RTL applies to the cross
         * axis of the vertical layout too. */
        const int32_t gravity = linear_gravity_absolute(
            child->lp.gravity < 0 ? container_gravity : child->lp.gravity, is_rtl);
        float cx = x, cy = y;
        if (vertical) {
            /* divider before this child (AOSP layoutVertical); the rect starts
             * at the end of the previous child's box (child.getTop() - lp.topMargin
             * - mDividerHeight, LinearLayout.java:442) — no thickness subtracted. */
            if (linear_has_divider_before(view, idx)) {
                const float top = y + pad_top + run_offset + cursor;
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
                /* AOSP draws dividers at child.getLeft() - lp.leftMargin -
                 * mDividerWidth (LTR, LinearLayout.java:487) and at
                 * child.getRight() + lp.rightMargin (RTL, LinearLayout.java:485).
                 * The RTL cursor loop only ever places a divider to the LEFT of
                 * the child it just processed, which covers MIDDLE and (for the
                 * rightmost child, the last in normal order) END. The BEGINNING
                 * divider sits to the RIGHT of the FIRST non-GONE child (drawn
                 * by drawDividersHorizontal at that child's right edge,
                 * LinearLayout.java:481-489), so it is appended as a rect after
                 * the child's box — rect only, no spacing. */
                const bool divider = rtl ? linear_has_divider_after(view, idx)
                                         : linear_has_divider_before(view, idx);
                if (divider) {
                    /* AOSP LTR draws at child.getLeft() - lp.leftMargin - mDividerWidth
                     * (LinearLayout.java:487); RTL uses the after-child formula
                     * child.getRight() + lp.rightMargin (LinearLayout.java:485/506).
                     * In this cursor model the already-placed box ends exactly at
                     * the run start + cursor, so no thickness is subtracted. */
                    const float left = x + pad_left + run_offset + cursor;
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
                     * bug #1038483 removed it for CENTER_VERTICAL). No Math.max:
                     * (childSpace - childHeight) / 2 may be negative
                     * (LinearLayout.java:1853-1854). */
                    cy = y + pad_top + (content_h - ch) * 0.5f + m_top - m_bottom;
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
                if (rtl && (view->show_dividers & ANDROID_SHOW_DIVIDER_BEGINNING) != 0) {
                    /* First non-GONE child in normal order? The cursor just
                     * advanced past this child's box, so
                     * cursor - m_main_end is the child's right edge; AOSP puts
                     * the BEGINNING rect at child.getRight() + lp.rightMargin
                     * (LinearLayout.java:485). */
                    bool first_visible = true;
                    for (int i = 0; i < idx; ++i) {
                        if (view->children[static_cast<size_t>(i)]->visibility != ANDROID_GONE) {
                            first_visible = false;
                            break;
                        }
                    }
                    if (first_visible) {
                        const float begin_left = x + pad_left + run_offset + cursor - m_main_end + m_right;
                        view->divider_rects.push_back({begin_left,
                            y + pad_top + view->divider_padding_px, view->divider_thickness_px,
                            h - pad_bottom - pad_top - 2.f * view->divider_padding_px});
                    }
                }
            }
        layout_view(child, cx, cy, cw, ch, ui);
    }

    /* AOSP end divider: vertical at child.getBottom() + lp.bottomMargin
     * (LinearLayout.java:454-457), LTR horizontal at child.getRight() +
     * lp.rightMargin (LinearLayout.java:508). In RTL the end divider is
     * already emitted inside the loop (the last child's hasDividerAfter check,
     * LinearLayout.java:1872-1874), so it is not repeated here. */
    if (linear_has_divider_before(view, count)) {
        /* AOSP falls back to the container edge when every child is GONE
         * (getLastNonGoneChild() == null, LinearLayout.java:449-453 /
         * 497-502): the END divider sits at height - paddingBottom -
         * dividerHeight (vertical) or width - paddingRight - dividerWidth
         * (LTR horizontal). */
        bool any_visible = false;
        for (android_view_s* child : view->children) {
            if (child->visibility != ANDROID_GONE) { any_visible = true; break; }
        }
        if (vertical) {
            const float top = any_visible ? y + pad_top + run_offset + cursor
                                          : y + h - pad_bottom - view->divider_thickness_px;
            view->divider_rects.push_back({x + pad_left + view->divider_padding_px, top,
                w - pad_right - pad_left - 2.f * view->divider_padding_px,
                view->divider_thickness_px});
        } else if (!rtl) {
            const float left = any_visible ? x + pad_left + run_offset + cursor
                                           : x + w - pad_right - view->divider_thickness_px;
            view->divider_rects.push_back({left,
                y + pad_top + view->divider_padding_px, view->divider_thickness_px,
                h - pad_bottom - pad_top - 2.f * view->divider_padding_px});
        } else if (!any_visible) {
            /* AOSP drawDividersHorizontal all-GONE RTL fallback: with
             * getLastNonGoneChild() == null the END divider sits at
             * getPaddingLeft() (LinearLayout.java:497-502). In RTL with a
             * visible child the divider is emitted inside the loop (the last
             * child's hasDividerAfter check); this closes the all-GONE gap. */
            const float left = x + pad_left;
            view->divider_rects.push_back({left,
                y + pad_top + view->divider_padding_px, view->divider_thickness_px,
                h - pad_bottom - pad_top - 2.f * view->divider_padding_px});
        }
    }
}

} // namespace viewruntime::android
