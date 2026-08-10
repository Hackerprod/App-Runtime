/* ListView / RecyclerView fidelity, verified against AOSP:
 *   - ListView: items UNSPECIFIED height, divider x (n-1) in content height,
 *     divider rects between items (and after the last when it does not reach
 *     the bottom), Material default 1dp black 12%.
 *   - RecyclerView: getChildMeasureSpec on both axes, margins as decoration,
 *     vertical and horizontal, no divider. */

#include "android_test_util.h"

static void add_items(android_ui_t ui, android_view_t list, int count) {
    for (int i = 0; i < count; ++i) {
        android_view_t item = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 1000 + i);
        set_wrap(item);
        android_view_set_text(item, "X");
        android_view_add_child(ui, list, item);
    }
}

static void test_list_view_measure() {
    android_ui_t ui = make_ui();
    android_view_t list = make_view(ui, ANDROID_VIEW_LIST_VIEW, 1);
    set_match(list);
    add_items(ui, list, 20);
    frame_and_layout(ui, list, 200.f, 300.f);
    sizef size{};
    android_view_get_measured_size(list, &size);
    /* 20 * 38.4 + 19 * 2 (1dp divider) = 806 -> overflow 506 over 300 viewport */
    scroll_metrics_t m = android_view_get_scroll_metrics(list);
    EXPECT_NEAR(m.scrollable_overflow_y, 506.0, 0.01);
    android_ui_destroy(ui);
}

static void test_list_view_scroll_layout() {
    android_ui_t ui = make_ui();
    android_view_t list = make_view(ui, ANDROID_VIEW_LIST_VIEW, 1);
    set_match(list);
    add_items(ui, list, 20);
    frame_and_layout(ui, list, 200.f, 300.f);
    android_view_set_scroll_offset(list, 0.f, 100.f);
    android_ui_layout(ui, list, 0.f, 0.f, 200.f, 300.f);
    rectf first{};
    android_view_get_bounds(android_view_get_child(list, 0), &first);
    EXPECT_NEAR(first.y, -100.0, 0.01);
    rectf second{};
    android_view_get_bounds(android_view_get_child(list, 1), &second);
    /* item height 38.4 + divider 2 between items */
    EXPECT_NEAR(second.y, -100.0 + 38.4 + 2.0, 0.01);
    /* full width fill */
    EXPECT_NEAR(first.width, 200.0, 0.01);
    android_ui_destroy(ui);
}

static void test_list_view_divider_record() {
    android_ui_t ui = make_ui();
    android_view_t list = make_view(ui, ANDROID_VIEW_LIST_VIEW, 1);
    set_match(list);
    add_items(ui, list, 2);
    frame_and_layout(ui, list, 200.f, 300.f);
    display_list_t dl = nullptr;
    EXPECT(android_ui_record(ui, list, &dl) == OK);
    /* clip, text, divider, text, divider (trailing, floating), clip */
    EXPECT(display_list_get_count(dl) == 6);
    paint_command_t cmd{};
    display_list_get_command(dl, 2, &cmd);
    EXPECT(cmd.tag == PAINT_FILL_ROUNDED_RECT);
    EXPECT_NEAR(cmd.data.fill_rounded_rect.rect.y, 38.4, 0.01);
    EXPECT_NEAR(cmd.data.fill_rounded_rect.rect.height, 2.0, 0.01);
    EXPECT_NEAR(cmd.data.fill_rounded_rect.rect.width, 200.0, 0.01);
    paint_command_free(&cmd);
    display_list_destroy(dl);
    android_ui_destroy(ui);
}

static void test_list_view_divider_disabled() {
    android_ui_t ui = make_ui();
    android_view_t list = make_view(ui, ANDROID_VIEW_LIST_VIEW, 1);
    set_match(list);
    add_items(ui, list, 2);
    android_view_set_divider_enabled(list, FALSE);
    frame_and_layout(ui, list, 200.f, 300.f);
    display_list_t dl = nullptr;
    EXPECT(android_ui_record(ui, list, &dl) == OK);
    /* clip, text, text, clip */
    EXPECT(display_list_get_count(dl) == 4);
    display_list_destroy(dl);
    android_ui_destroy(ui);
}

static void test_recycler_view_no_divider() {
    android_ui_t ui = make_ui();
    android_view_t recycler = make_view(ui, ANDROID_VIEW_RECYCLER_VIEW, 1);
    set_match(recycler);
    add_items(ui, recycler, 20);
    frame_and_layout(ui, recycler, 200.f, 300.f);
    scroll_metrics_t m = android_view_get_scroll_metrics(recycler);
    /* 20 * 38.4, no divider -> 768 - 300 = 468 */
    EXPECT_NEAR(m.scrollable_overflow_y, 468.0, 0.01);
    android_ui_destroy(ui);
}

static void test_recycler_horizontal() {
    android_ui_t ui = make_ui();
    android_view_t recycler = make_view(ui, ANDROID_VIEW_RECYCLER_VIEW, 1);
    set_match(recycler);
    android_view_set_orientation(recycler, ANDROID_HORIZONTAL);
    for (int i = 0; i < 10; ++i) {
        android_view_t item = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 100 + i);
        set_wrap(item);
        android_view_set_text(item, "XX");
        android_view_add_child(ui, recycler, item);
    }
    frame_and_layout(ui, recycler, 200.f, 300.f);
    scroll_metrics_t m = android_view_get_scroll_metrics(recycler);
    /* "XX" = ceil(2 * 0.56 * 32) = 36 (AOSP TextView width ceil);
     * 10 * 36 = 360 -> x overflow 160 */
    EXPECT_NEAR(m.scrollable_overflow_x, 160.0, 0.01);
    android_view_set_scroll_offset(recycler, 50.f, 0.f);
    android_ui_layout(ui, recycler, 0.f, 0.f, 200.f, 300.f);
    rectf first{};
    android_view_get_bounds(android_view_get_child(recycler, 0), &first);
    EXPECT_NEAR(first.x, -50.0, 0.01);
    android_ui_destroy(ui);
}

static void test_recycler_item_margins() {
    android_ui_t ui = make_ui();
    android_view_t recycler = make_view(ui, ANDROID_VIEW_RECYCLER_VIEW, 1);
    set_match(recycler);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 1);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 2);
    set_wrap(a); set_wrap(b);
    android_view_set_text(a, "X");
    android_view_set_text(b, "Y");
    android_layout_params_t lp{};
    lp.width.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lp.height.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lp.margins_dp.top = 5.f;
    android_view_set_layout_params(a, &lp);
    android_view_set_layout_params(b, &lp);
    android_view_add_child(ui, recycler, a);
    android_view_add_child(ui, recycler, b);
    frame_and_layout(ui, recycler, 200.f, 300.f);
    rectf ba{}, bb{};
    android_view_get_bounds(a, &ba);
    android_view_get_bounds(b, &bb);
    /* RecyclerView margins act as decoration: item b starts after a's box
     * (38.4 + 10px margins) plus its own top margin. */
    EXPECT_NEAR(ba.y, 10.0, 0.01);
    EXPECT_NEAR(bb.y, 38.4 + 10.0 + 10.0, 0.01);
    android_ui_destroy(ui);
}

int main() {
    test_list_view_measure();
    test_list_view_scroll_layout();
    test_list_view_divider_record();
    test_list_view_divider_disabled();
    test_recycler_view_no_divider();
    test_recycler_horizontal();
    test_recycler_item_margins();
    return test_result();
}
