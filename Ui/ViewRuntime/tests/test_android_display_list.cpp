/* Display-list recording: command sequence, order, ownership. */

#include "android_test_util.h"

static bool_t test_image_dimensions(const char*, sizef* out, void*) {
    *out = {100.f, 50.f};
    return TRUE;
}

static void expect_tag(const display_list_t list, int32_t index,
                       paint_command_tag_t tag) {
    paint_command_t cmd{};
    display_list_get_command(list, index, &cmd);
    EXPECT(cmd.tag == tag);
    paint_command_free(&cmd);
}

static void test_text_view_record() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_set_background_color(root, {1.f, 0.f, 0.f, 1.f});
    android_view_t tv = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    set_wrap(tv);
    android_view_set_text(tv, "Hi");
    android_view_add_child(ui, root, tv);
    frame_and_layout(ui, root, 200.f, 300.f);
    display_list_t list = nullptr;
    EXPECT(android_ui_record(ui, root, &list) == OK);
    EXPECT(display_list_get_count(list) == 2);
    paint_command_t fill{};
    display_list_get_command(list, 0, &fill);
    EXPECT(fill.tag == PAINT_FILL_ROUNDED_RECT);
    EXPECT_NEAR(fill.data.fill_rounded_rect.color.r, 1.0, 0.001);
    EXPECT_NEAR(fill.data.fill_rounded_rect.color.g, 0.0, 0.001);
    paint_command_free(&fill);
    paint_command_t text{};
    display_list_get_command(list, 1, &text);
    EXPECT(text.tag == PAINT_DRAW_TEXT);
    EXPECT(text.data.draw_text.text != nullptr);
    EXPECT_NEAR(text.data.draw_text.font_size, 32.0, 0.01);
    paint_command_free(&text);
    display_list_destroy(list);
    android_ui_destroy(ui);
}

static void test_progress_record() {
    android_ui_t ui = make_ui();
    android_view_t bar = make_view(ui, ANDROID_VIEW_PROGRESS_BAR);
    set_match(bar);
    android_view_set_progress(bar, 0, 100, 30);
    frame_and_layout(ui, bar, 200.f, 50.f);
    display_list_t list = nullptr;
    EXPECT(android_ui_record(ui, bar, &list) == OK);
    EXPECT(display_list_get_count(list) == 2);
    paint_command_t track{};
    display_list_get_command(list, 0, &track);
    EXPECT(track.tag == PAINT_FILL_ROUNDED_RECT);
    EXPECT_NEAR(track.data.fill_rounded_rect.rect.width, 200.0, 0.01);
    paint_command_t fill{};
    display_list_get_command(list, 1, &fill);
    EXPECT(fill.tag == PAINT_FILL_ROUNDED_RECT);
    EXPECT_NEAR(fill.data.fill_rounded_rect.rect.width, 60.0, 0.01);
    paint_command_free(&track);
    paint_command_free(&fill);
    display_list_destroy(list);
    android_ui_destroy(ui);
}

static void test_scroll_record() {
    android_ui_t ui = make_ui();
    android_view_t scroll = make_view(ui, ANDROID_VIEW_SCROLL_VIEW);
    set_match(scroll);
    android_view_t content = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_wrap(content);
    for (int i = 0; i < 20; ++i) {
        android_view_t item = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
        set_wrap(item);
        android_view_set_text(item, "X");
        android_view_add_child(ui, content, item);
    }
    android_view_add_child(ui, scroll, content);
    frame_and_layout(ui, scroll, 200.f, 300.f);
    android_view_set_scroll_offset(scroll, 0.f, 100.f);
    android_ui_layout(ui, scroll, 0.f, 0.f, 200.f, 300.f);
    display_list_t list = nullptr;
    EXPECT(android_ui_record(ui, scroll, &list) == OK);
    /* Children are laid out in screen space; the recorder only clips. */
    EXPECT(display_list_get_count(list) == 22);
    expect_tag(list, 0, PAINT_PUSH_CLIP);
    expect_tag(list, 21, PAINT_POP_CLIP);
    display_list_destroy(list);
    android_ui_destroy(ui);
}

static void test_checkable_record() {
    android_ui_t ui = make_ui();
    android_view_t box = make_view(ui, ANDROID_VIEW_CHECK_BOX);
    set_wrap(box);
    android_view_set_text(box, "Option");
    android_view_set_checked(box, TRUE);
    frame_and_layout(ui, box, 200.f, 50.f);
    display_list_t list = nullptr;
    EXPECT(android_ui_record(ui, box, &list) == OK);
    EXPECT(display_list_get_count(list) == 2);
    paint_command_t indicator{};
    display_list_get_command(list, 0, &indicator);
    EXPECT(indicator.tag == PAINT_FILL_ROUNDED_RECT);
    EXPECT_NEAR(indicator.data.fill_rounded_rect.rect.width, 32.0, 0.01);
    paint_command_free(&indicator);
    display_list_destroy(list);

    android_view_t radio = make_view(ui, ANDROID_VIEW_RADIO_BUTTON);
    set_wrap(radio);
    android_view_set_text(radio, "R");
    android_view_set_checked(radio, FALSE);
    frame_and_layout(ui, radio, 200.f, 50.f);
    display_list_t rlist = nullptr;
    EXPECT(android_ui_record(ui, radio, &rlist) == OK);
    expect_tag(rlist, 0, PAINT_STROKE_ROUNDED_RECT);
    display_list_destroy(rlist);
    android_ui_destroy(ui);
}

static void test_image_record() {
    android_ui_t ui = make_ui();
    android_ui_set_image_dimensions(ui, test_image_dimensions, nullptr);
    android_view_t img = make_view(ui, ANDROID_VIEW_IMAGE_VIEW);
    set_match(img);
    android_view_set_image_source(img, "ic_avatar");
    android_view_set_scale_type(img, ANDROID_SCALE_FIT_CENTER);
    frame_and_layout(ui, img, 200.f, 100.f);
    display_list_t list = nullptr;
    EXPECT(android_ui_record(ui, img, &list) == OK);
    EXPECT(display_list_get_count(list) == 1);
    paint_command_t cmd{};
    display_list_get_command(list, 0, &cmd);
    EXPECT(cmd.tag == PAINT_DRAW_IMAGE);
    /* intrinsic 100x50 mapped into the 200x100 view: source is the full
     * image, FIT_CENTER scales by 2 -> 200x100 destination */
    EXPECT_NEAR(cmd.data.draw_image.source_rect.width, 100.0, 0.01);
    EXPECT_NEAR(cmd.data.draw_image.source_rect.height, 50.0, 0.01);
    EXPECT_NEAR(cmd.data.draw_image.destination_rect.width, 200.0, 0.01);
    EXPECT_NEAR(cmd.data.draw_image.destination_rect.height, 100.0, 0.01);
    paint_command_free(&cmd);
    display_list_destroy(list);
    android_ui_destroy(ui);
}

static void test_button_record() {
    android_ui_t ui = make_ui();
    android_view_t btn = make_view(ui, ANDROID_VIEW_BUTTON);
    set_wrap(btn);
    android_view_set_text(btn, "Go");
    frame_and_layout(ui, btn, 200.f, 200.f);
    display_list_t list = nullptr;
    EXPECT(android_ui_record(ui, btn, &list) == OK);
    /* default background + text */
    EXPECT(display_list_get_count(list) == 2);
    expect_tag(list, 0, PAINT_FILL_ROUNDED_RECT);
    expect_tag(list, 1, PAINT_DRAW_TEXT);
    display_list_destroy(list);
    android_ui_destroy(ui);
}

int main() {
    test_text_view_record();
    test_progress_record();
    test_scroll_record();
    test_checkable_record();
    test_image_record();
    test_button_record();
    return test_result();
}
