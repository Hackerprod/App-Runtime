/* ListView / RecyclerView list semantics, verified against AOSP:
 *
 * ListView (android.widget.ListView):
 *   - items measured with EXACTLY(lpHeight) when explicit, otherwise
 *     UNSPECIFIED (content height, uncapped) — measureScrapChild
 *   - content height = sum(items) + divider x (n-1) — measureHeightOfChildren
 *   - items stacked with `nextTop = child.bottom + dividerHeight` — fillDown
 *   - item margins are ignored (AbsListView.LayoutParams has none)
 *
 * RecyclerView + LinearLayoutManager (androidx.recyclerview):
 *   - items measured through getChildMeasureSpec on both axes
 *     (wrap degrades to AT_MOST under a bounded parent) — measureChildWithMargins
 *   - each item occupies a decorated box (margins) and the child sits inside
 *     it — layoutChunk + layoutDecoratedWithMargins
 *   - no divider; the scroll range is the total decorated content
 *
 * Both are scrollable: children are laid out in screen space (offset by the
 * clamped scroll), and the recorder clips to the viewport.
 */

#include "android_types.h"

#include <algorithm>

namespace viewruntime::android {

bool is_list_view(const android_view_s* view) {
    return view->cls == ANDROID_VIEW_LIST_VIEW || view->cls == ANDROID_VIEW_RECYCLER_VIEW;
}

bool list_is_vertical(const android_view_s* view) {
    /* ListView is vertical-only; RecyclerView follows its orientation. */
    return view->cls == ANDROID_VIEW_LIST_VIEW || view->orientation == ANDROID_VERTICAL;
}

android_measured_size_t measure_list(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    const bool vertical = list_is_vertical(view);
    const bool is_list = view->cls == ANDROID_VIEW_LIST_VIEW;
    const int32_t main_mode = vertical ? spec_h.mode : spec_w.mode;
    const int32_t cross_mode = vertical ? spec_w.mode : spec_h.mode;
    const float content_main = std::max(0.f, (vertical ? spec_h.size : spec_w.size) -
        (vertical ? padding_v(view, ui) : padding_h(view, ui)));
    const float content_cross = std::max(0.f, (vertical ? spec_w.size : spec_h.size) -
        (vertical ? padding_h(view, ui) : padding_v(view, ui)));

    float total = 0.f;
    float max_cross = 0.f;
    int visible = 0;
    for (android_view_s* child : view->children) {
        if (child->visibility == ANDROID_GONE) continue;
        ++visible;
        const float mh = margin_h(child->lp, ui);
        const float mv = margin_v(child->lp, ui);
        android_measure_spec_t cw, ch;
        if (vertical) {
            cw = get_child_measure_spec({content_cross, cross_mode}, mh, child->lp.width, ui);
            if (is_list) {
                ch = child->lp.height.kind == ANDROID_SIZE_KIND_EXACT
                    ? android_measure_spec_t{dp(ui, child->lp.height.value_dp), ANDROID_MEASURE_EXACTLY}
                    : android_measure_spec_t{0.f, ANDROID_MEASURE_UNSPECIFIED};
            } else {
                ch = get_child_measure_spec({content_main, main_mode}, mv, child->lp.height, ui);
            }
        } else {
            ch = get_child_measure_spec({content_cross, cross_mode}, mv, child->lp.height, ui);
            if (is_list) {
                cw = child->lp.width.kind == ANDROID_SIZE_KIND_EXACT
                    ? android_measure_spec_t{dp(ui, child->lp.width.value_dp), ANDROID_MEASURE_EXACTLY}
                    : android_measure_spec_t{0.f, ANDROID_MEASURE_UNSPECIFIED};
            } else {
                cw = get_child_measure_spec({content_main, main_mode}, mh, child->lp.width, ui);
            }
        }
        child->measured = measure_view(child, cw, ch, ui);

        const float main_size = vertical ? child->measured.height : child->measured.width;
        const float cross_size = vertical ? child->measured.width : child->measured.height;
        total += main_size + (vertical ? mv : mh);
        max_cross = std::max(max_cross, cross_size + (vertical ? mh : mv));
    }
    /* AOSP measureHeightOfChildren: the divider counts between all but one child. */
    if (is_list && view->divider_enabled && visible > 1) {
        total += dp(ui, view->divider_height_dp) * static_cast<float>(visible - 1);
    }

    const float desired_main = std::max(total + (vertical ? padding_v(view, ui) : padding_h(view, ui)),
        vertical ? dp(ui, view->min_height_dp) : dp(ui, view->min_width_dp));
    const float desired_cross = std::max(max_cross + (vertical ? padding_h(view, ui) : padding_v(view, ui)),
        vertical ? dp(ui, view->min_width_dp) : dp(ui, view->min_height_dp));
    const float resolved_main = resolve_size(desired_main, vertical ? spec_h : spec_w);
    const float resolved_cross = resolve_size(desired_cross, vertical ? spec_w : spec_h);

    const float overflow = std::max(0.f, total - std::max(0.f, resolved_main -
        (vertical ? padding_v(view, ui) : padding_h(view, ui))));
    view->scroll_metrics.scrollable_overflow_y = vertical ? overflow : 0.f;
    view->scroll_metrics.scrollable_overflow_x = vertical ? 0.f : overflow;
    view->measured_baseline = -1.f;
    if (vertical) return {resolved_cross, resolved_main};
    return {resolved_main, resolved_cross};
}

void layout_list(android_view_s* view, float x, float y, float w, float h,
                 const android_ui_s* ui) {
    view->bounds = {x, y, w, h};
    const bool vertical = list_is_vertical(view);
    const bool is_list = view->cls == ANDROID_VIEW_LIST_VIEW;
    const float pad_left = dp(ui, view->padding_left_dp), pad_top = dp(ui, view->padding_top_dp);
    const float pad_right = dp(ui, view->padding_right_dp), pad_bottom = dp(ui, view->padding_bottom_dp);
    const float content_w = std::max(0.f, w - pad_left - pad_right);
    const float content_h = std::max(0.f, h - pad_top - pad_bottom);
    const float divider = is_list && view->divider_enabled ? dp(ui, view->divider_height_dp) : 0.f;

    const float overflow = std::max(0.f, vertical
        ? view->scroll_metrics.scrollable_overflow_y
        : view->scroll_metrics.scrollable_overflow_x);
    if (vertical) {
        view->scroll_y = std::min(std::max(0.f, view->scroll_y), overflow);
        view->scroll_x = 0.f;
    } else {
        view->scroll_x = std::min(std::max(0.f, view->scroll_x), overflow);
        view->scroll_y = 0.f;
    }
    view->scroll_metrics.scroll_offset_x = view->scroll_x;
    view->scroll_metrics.scroll_offset_y = view->scroll_y;

    float cursor = 0.f;
    for (android_view_s* child : view->children) {
        if (child->visibility == ANDROID_GONE) continue;
        const float m_left = dp(ui, child->lp.margins_dp.left), m_top = dp(ui, child->lp.margins_dp.top);
        const float cw = child->measured.width, ch = child->measured.height;
        if (vertical) {
            const float cx = is_list ? (x + pad_left) : (x + pad_left + m_left);
            const float cy = y + pad_top + cursor + (is_list ? 0.f : m_top) - view->scroll_y;
            const float bw = is_list ? content_w : cw;
            layout_view(child, cx, cy, bw, ch, ui);
            cursor += ch + (is_list ? 0.f : margin_v(child->lp, ui)) + divider;
        } else {
            const float cx = x + pad_left + cursor + (is_list ? 0.f : m_left) - view->scroll_x;
            const float cy = is_list ? (y + pad_top) : (y + pad_top + m_top);
            const float bh = is_list ? content_h : ch;
            layout_view(child, cx, cy, cw, bh, ui);
            cursor += cw + (is_list ? 0.f : margin_h(child->lp, ui)) + divider;
        }
    }
}

} // namespace viewruntime::android
