#include "android_types.h"

#include <algorithm>
#include <vector>

namespace viewruntime::android {

/* ── FrameLayout ───────────────────────────────────────────────────── */
/* Faithful port of AOSP FrameLayout.onMeasure/layoutChildren
 * (frameworks/base/core/java/android/widget/FrameLayout.java). The runtime
 * has no foreground drawable, so the foreground padding terms are the plain
 * view padding. */

/* AOSP FrameLayout.DEFAULT_CHILD_GRAVITY = Gravity.TOP | Gravity.START
 * (FrameLayout.java:60). Children whose layout_gravity is unspecified
 * (LayoutParams.gravity == -1, FrameLayout.java:449) get this default; the
 * container's own gravity is NOT inherited (FrameLayout has none). */
static constexpr int32_t FRAME_DEFAULT_CHILD_GRAVITY =
    ANDROID_GRAVITY_TOP | ANDROID_GRAVITY_START;

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
    /* AOSP FrameLayout.onMeasure pass 1 (FrameLayout.java:179): measure every
     * non-GONE child (mMeasureAllChildren is not modeled), tracking the max
     * width/height including margins. */
    for (android_view_s* child : view->children) {
        if (child->visibility == ANDROID_GONE) continue;
        measure_child_with_margins(child,
            {content_w, spec_w.mode}, {content_h, spec_h.mode}, ui);
        desired_w = std::max(desired_w, child->measured.width + margin_h(child->lp, ui));
        desired_h = std::max(desired_h, child->measured.height + margin_v(child->lp, ui));
        if (measure_match_parent &&
            (child->lp.width.kind == ANDROID_SIZE_KIND_MATCH_PARENT ||
             child->lp.height.kind == ANDROID_SIZE_KIND_MATCH_PARENT)) {
            match_parent_children.push_back(child);
        }
    }
    /* AOSP FrameLayout.onMeasure applies getSuggestedMinimumWidth/Height
     * after adding the padding and before resolveSizeAndState
     * (FrameLayout.java:210-216). */
    desired_w = std::max(desired_w + padding_h(view, ui), dp(ui, view->min_width_dp));
    desired_h = std::max(desired_h + padding_v(view, ui), dp(ui, view->min_height_dp));
    const float resolved_w = resolve_size(desired_w, spec_w);
    const float resolved_h = resolve_size(desired_h, spec_h);

    /* AOSP FrameLayout.onMeasure second pass (FrameLayout.java:229): when the
     * parent was not EXACTLY and more than one child declared MATCH_PARENT,
     * re-measure those children against the final resolved size. */
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

/* AOSP FrameLayout.layoutChildren (FrameLayout.java:273): per-child gravity
 * with exact placement formulas. The horizontal mask is matched by equality
 * so FILL never falls through to an edge case. */
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
        /* AOSP: int gravity = lp.gravity; if (gravity == -1)
         * gravity = DEFAULT_CHILD_GRAVITY; (FrameLayout.java:293-296) */
        const int32_t gravity = child->lp.gravity != ANDROID_GRAVITY_UNSPECIFIED
            ? child->lp.gravity
            : FRAME_DEFAULT_CHILD_GRAVITY;
        /* AOSP: absoluteGravity = getAbsoluteGravity(gravity, layoutDirection);
         * verticalGravity = gravity & VERTICAL_GRAVITY_MASK (LTR runtime). */
        const int32_t gravity_ltr = gravity_normalize_ltr(gravity);
        const int32_t hmask = gravity_ltr & ANDROID_GRAVITY_FILL_HORIZONTAL;
        float cx = parent_left + m_left;
        if (hmask == ANDROID_GRAVITY_CENTER_HORIZONTAL) {
            /* AOSP integer division (parentRight - parentLeft - width) / 2
             * truncates toward ZERO (FrameLayout.java:304); * 0.5f would keep
             * the fraction and diverge on .5 offsets. */
            const float delta = parent_right - parent_left - cw;
            cx = parent_left + static_cast<float>(static_cast<int>(delta) / 2) +
                 m_left - m_right;
        } else if (hmask == ANDROID_GRAVITY_RIGHT) {
            cx = parent_right - cw - m_right;
        }
        const int32_t vmask = gravity & ANDROID_GRAVITY_FILL_VERTICAL;
        float cy = parent_top + m_top;
        if (vmask == ANDROID_GRAVITY_CENTER_VERTICAL) {
            /* AOSP integer division (parentBottom - parentTop - height) / 2
             * truncates toward ZERO (FrameLayout.java:322). */
            const float delta = parent_bottom - parent_top - ch;
            cy = parent_top + static_cast<float>(static_cast<int>(delta) / 2) +
                 m_top - m_bottom;
        } else if (vmask == ANDROID_GRAVITY_BOTTOM) {
            cy = parent_bottom - ch - m_bottom;
        }
        layout_view(child, x + cx, y + cy, cw, ch, ui);
    }
}

} // namespace viewruntime::android
