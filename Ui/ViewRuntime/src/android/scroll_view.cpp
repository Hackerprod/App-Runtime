#include "android_types.h"

#include <algorithm>

namespace viewruntime::android {

/* ── ScrollView ────────────────────────────────────────────────────── */
/* Faithful port of AOSP ScrollView.onMeasure/onLayout
 * (frameworks/base/core/java/android/widget/ScrollView.java). The runtime
 * models a single child.
 *
 * AOSP mFillViewport (default false) is NOT modeled: when the APK sets
 * fillViewport=true, onMeasure (ScrollView.java:477-509) re-measures the
 * child to the viewport height and the child's height diverges. The
 * `android:fillViewport` attribute is currently ignored. */

android_measured_size_t measure_scroll(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    if (view->children.empty()) {
        /* AOSP super.onMeasure (FrameLayout.onMeasure, FrameLayout.java:214-216):
         * the max is clamped by getSuggestedMinimumWidth/Height. */
        const float empty_w = std::max(padding_h(view, ui), dp(ui, view->min_width_dp));
        const float empty_h = std::max(padding_v(view, ui), dp(ui, view->min_height_dp));
        return {resolve_size(empty_w, spec_w), resolve_size(empty_h, spec_h)};
    }
    android_view_s* child = view->children.front();
    /* AOSP ScrollView.measureChildWithMargins (ScrollView.java:1539-1553): the
     * child width spec goes through getChildMeasureSpec PRESERVING the parent
     * spec mode (getChildMeasureSpec(parentWidthMeasureSpec, padding + margins,
     * lp.width)); the child height spec is UNSPECIFIED (the size carries the
     * available height, but the mode lets the child report its full content). */
    /* AOSP super.onMeasure (FrameLayout.java:193) skips a GONE child
     * entirely: it is not measured and contributes no margins to the resolved
     * size. measure_view already returns {0,0} for GONE, but the child's
     * margins must be zeroed here or they would leak into the resolved
     * size/overflow. */
    const float child_mh = child->visibility == ANDROID_GONE ? 0.f : margin_h(child->lp, ui);
    const float child_mv = child->visibility == ANDROID_GONE ? 0.f : margin_v(child->lp, ui);
    const float used_h = padding_h(view, ui) + child_mh;
    const float used_v = padding_v(view, ui) + child_mv;
    const android_measure_spec_t cw =
        get_child_measure_spec(spec_w, used_h, child->lp.width, ui);
    const android_measure_spec_t ch =
        {std::max(0.f, spec_h.size - used_v), ANDROID_MEASURE_UNSPECIFIED};
    child->measured = measure_view(child, cw, ch, ui);
    /* AOSP super.onMeasure clamps the resolved size by the suggested minimum
     * (FrameLayout.java:214-216). */
    const float resolved_h = resolve_size(
        std::max(child->measured.height + child_mv + padding_v(view, ui),
                 dp(ui, view->min_height_dp)), spec_h);
    /* AOSP onLayout scrollRange: max(0, childHeight - (height - padding)) */
    const float overflow_y = std::max(0.f,
        child->measured.height - std::max(0.f, resolved_h - padding_v(view, ui)));
    view->scroll_metrics.scrollable_overflow_y = overflow_y;
    view->scroll_metrics.scrollable_overflow_x = 0.f;
    return {resolve_size(
        std::max(child->measured.width + child_mh + padding_h(view, ui),
                 dp(ui, view->min_width_dp)), spec_w),
            resolved_h};
}

void layout_scroll(android_view_s* view, float x, float y, float w, float h,
                   const android_ui_s* ui) {
    view->bounds = {x, y, w, h};
    if (view->children.empty()) {
        /* AOSP onLayout with no child: childHeight = 0 -> scrollRange = 0 ->
         * mScrollY is clamped to 0 and scrollTo re-claims the zero offset
         * (ScrollView.java:1857-1870). */
        view->scroll_x = 0.f;
        view->scroll_y = 0.f;
        view->scroll_metrics.scrollable_overflow_x = 0.f;
        view->scroll_metrics.scrollable_overflow_y = 0.f;
        view->scroll_metrics.scroll_offset_x = 0.f;
        view->scroll_metrics.scroll_offset_y = 0.f;
        return;
    }
    android_view_s* child = view->children.front();
    const float m_left = dp(ui, child->lp.margins_dp.left), m_top = dp(ui, child->lp.margins_dp.top);
    const float m_right = dp(ui, child->lp.margins_dp.right), m_bottom = dp(ui, child->lp.margins_dp.bottom);
    const float pad_left = dp(ui, view->padding_left_dp), pad_top = dp(ui, view->padding_top_dp);
    const float pad_right = dp(ui, view->padding_right_dp), pad_bottom = dp(ui, view->padding_bottom_dp);
    const float content_h = std::max(0.f, h - padding_v(view, ui));
    /* AOSP onLayout (ScrollView.java:1842): scrollRange = Math.max(0,
     * childHeight - (b - t - mPaddingBottom - mPaddingTop)); clamp mScrollY,
     * then re-claim the offset (scrollTo). */
    const float overflow = std::max(0.f, child->measured.height - content_h);
    if (view->scroll_y > overflow) view->scroll_y = overflow;
    if (view->scroll_y < 0.f) view->scroll_y = 0.f;
    if (view->scroll_x != 0.f) view->scroll_x = 0.f;
    view->scroll_metrics.scrollable_overflow_y = overflow;
    view->scroll_metrics.scrollable_overflow_x = 0.f;
    view->scroll_metrics.scroll_offset_x = view->scroll_x;
    view->scroll_metrics.scroll_offset_y = view->scroll_y;
    const float cw = child->measured.width, ch = child->measured.height;
    /* AOSP super.onLayout -> FrameLayout.layoutChildren honors the child's
     * layout_gravity (FrameLayout.java:293-330). Unspecified (-1) children
     * use the default TOP|START placement below. */
    float cx = x + pad_left + m_left;
    float cy = y + pad_top + m_top;
    if (child->lp.gravity != ANDROID_GRAVITY_UNSPECIFIED) {
        const int32_t gravity = gravity_normalize_ltr(child->lp.gravity);
        const int32_t hmask = gravity & ANDROID_GRAVITY_FILL_HORIZONTAL;
        if (hmask == ANDROID_GRAVITY_CENTER_HORIZONTAL) {
            /* AOSP super.onLayout -> FrameLayout.layoutChildren:
             * (parentRight - parentLeft - width) / 2 (FrameLayout.java:304).
             * The space is NOT clamped to 0 (a negative delta is allowed and
             * truncates toward zero, exactly like the Java int division). */
            const float delta = w - pad_left - pad_right - cw;
            cx = x + pad_left + static_cast<float>(static_cast<int>(delta) / 2) +
                 m_left - m_right;
        } else if (hmask == ANDROID_GRAVITY_RIGHT) {
            cx = x + w - pad_right - cw - m_right;
        }
        const int32_t vmask = gravity & ANDROID_GRAVITY_FILL_VERTICAL;
        if (vmask == ANDROID_GRAVITY_CENTER_VERTICAL) {
            /* AOSP (parentBottom - parentTop - height) / 2 (FrameLayout.java:322),
             * raw space (no clamp), truncating toward zero. */
            const float delta = h - pad_top - pad_bottom - ch;
            cy = y + pad_top + static_cast<float>(static_cast<int>(delta) / 2) +
                 m_top - m_bottom;
        } else if (vmask == ANDROID_GRAVITY_BOTTOM) {
            cy = y + h - pad_bottom - ch - m_bottom;
        }
    }
    cx -= view->scroll_x;
    cy -= view->scroll_y;
    layout_view(child, cx, cy, cw, ch, ui);
}

} // namespace viewruntime::android
