#include <viewruntime/viewruntime.h>
#include <viewruntime/android.h>

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>

static void require(bool condition, const char* message) {
    if (!condition) {
        fprintf(stderr, "FAIL: %s\n", message);
        std::exit(1);
    }
}

static android_text_metrics_t measurer(const char* text, float size, float max_width, void*) {
    float width = 0.f;
    for (const char* p = text; p && *p; ++p) {
        width += (*p == ' ' ? 0.33f : 0.56f) * size;
    }
    if (max_width > 0.f && width > max_width) width = max_width;
    return {width, size * 1.2f, size * 0.8f};
}

int main() {
    const auto runtime_version = abi_version();
    require(runtime_version == ABI_VERSION_CURRENT,
        "runtime ABI version differs from the public header");
    require(ABI_VERSION_GET_MAJOR(runtime_version) == ABI_VERSION_MAJOR &&
            ABI_VERSION_GET_MINOR(runtime_version) == ABI_VERSION_MINOR &&
            ABI_VERSION_GET_PATCH(runtime_version) == ABI_VERSION_PATCH,
        "ABI version packing is incorrect");

    constexpr capabilities_t required_capabilities =
        CAPABILITY_ANDROID_UI | CAPABILITY_DISPLAY_LIST | CAPABILITY_RENDER_PLAN;
    require((abi_capabilities() & required_capabilities) == required_capabilities,
        "runtime does not advertise all compiled capabilities");
    require(paint_command_size() == sizeof(paint_command_t),
        "runtime paint-command size differs from the public header");
    require(status_message(OK) != nullptr, "status message lookup failed");

    /* End-to-end Android pipeline: tree -> measure -> layout -> record. */
    const android_ui_options_t options = {2.f, 2.f};
    android_ui_t ui = nullptr;
    require(android_ui_create(&options, &ui) == OK, "ui creation failed");
    android_ui_set_text_measurer(ui, measurer, nullptr);

    android_view_t root = nullptr;
    require(android_view_create(ui, ANDROID_VIEW_LINEAR_LAYOUT, 100, &root) == OK,
        "root creation failed");
    android_layout_params_t root_lp{};
    root_lp.width.kind = ANDROID_SIZE_KIND_MATCH_PARENT;
    root_lp.height.kind = ANDROID_SIZE_KIND_MATCH_PARENT;
    android_view_set_layout_params(root, &root_lp);
    android_view_set_orientation(root, ANDROID_VERTICAL);

    android_view_t title = nullptr;
    require(android_view_create(ui, ANDROID_VIEW_TEXT_VIEW, 101, &title) == OK,
        "title creation failed");
    android_layout_params_t title_lp{};
    title_lp.width.kind = ANDROID_SIZE_KIND_MATCH_PARENT;
    title_lp.height.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    android_view_set_layout_params(title, &title_lp);
    android_view_set_text(title, "Welcome");
    require(android_view_add_child(ui, root, title) == OK, "add title failed");

    android_view_t button = nullptr;
    require(android_view_create(ui, ANDROID_VIEW_BUTTON, 102, &button) == OK,
        "button creation failed");
    android_layout_params_t button_lp{};
    button_lp.width.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    button_lp.height.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    android_view_set_layout_params(button, &button_lp);
    android_view_set_text(button, "Go");
    require(android_view_add_child(ui, root, button) == OK, "add button failed");

    require(android_ui_measure(ui, root, 360.f, 720.f) == OK, "measure failed");
    require(android_ui_layout(ui, root, 0.f, 0.f, 360.f, 720.f) == OK, "layout failed");

    sizef title_size{};
    require(android_view_get_measured_size(title, &title_size) == OK, "title size failed");
    /* match_parent width fills the 360px viewport; height wraps text at 38.4 */
    require(std::fabs(title_size.width - 360.f) < 0.01f && std::fabs(title_size.height - 38.4f) < 0.01f,
        "title measured size is incorrect");

    rectf title_bounds{};
    android_view_get_bounds(title, &title_bounds);
    require(std::fabs(title_bounds.y - 0.f) < 0.01f, "title should sit at the top");

    rectf button_bounds{};
    android_view_get_bounds(button, &button_bounds);
    require(std::fabs(button_bounds.y - title_size.height) < 0.01f, "button should follow the title");

    require(android_ui_hit_test(ui, root, button_bounds.x + 1.f, button_bounds.y + 1.f) == button,
        "hit test did not resolve the button");
    require(android_ui_find_view_by_id(ui, 101) == title, "id lookup failed");

    display_list_t list = nullptr;
    require(android_ui_record(ui, root, &list) == OK, "record failed");
    /* button background fill + button text + title text = 3 commands */
    require(display_list_get_count(list) == 3, "unexpected display-list command count");
    paint_command_t cmd{};
    require(display_list_get_command(list, 2, &cmd) == OK, "command read failed");
    require(cmd.tag == PAINT_DRAW_TEXT && cmd.data.draw_text.text != nullptr &&
            std::strcmp(cmd.data.draw_text.text, "Go") == 0, "button text command is incorrect");
    paint_command_free(&cmd);
    require(display_list_get_command(list, 5, &cmd) == ERROR_INVALID_STATE,
        "out-of-range command read must fail");
    display_list_destroy(list);

    android_ui_destroy(ui);

    printf("OK: smoke test passed\n");
    return 0;
}
