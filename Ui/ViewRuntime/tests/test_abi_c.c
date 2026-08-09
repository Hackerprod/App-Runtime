#include <viewruntime/viewruntime.h>
#include <viewruntime/android.h>

#include <stdio.h>

static int fail(const char* message) {
    fprintf(stderr, "FAIL: %s\n", message);
    return 1;
}

static android_text_metrics_t c_measurer(const char* text, float size, float max_width, void* user_data) {
    float width = 0.f;
    for (const char* p = text; p && *p; ++p) {
        width += (*p == ' ' ? 0.33f : 0.56f) * size;
    }
    if (max_width > 0.f && width > max_width) width = max_width;
    android_text_metrics_t metrics = {width, size * 1.2f, size * 0.8f};
    return metrics;
}

int main(void) {
    const uint32_t version = abi_version();
    const capabilities_t required_capabilities =
        CAPABILITY_ANDROID_UI |
        CAPABILITY_DISPLAY_LIST |
        CAPABILITY_RENDER_PLAN;

    if (version != ABI_VERSION_CURRENT) {
        return fail("runtime ABI version differs from the C header");
    }
    if (ABI_VERSION_GET_MAJOR(version) != ABI_VERSION_MAJOR ||
        ABI_VERSION_GET_MINOR(version) != ABI_VERSION_MINOR ||
        ABI_VERSION_GET_PATCH(version) != ABI_VERSION_PATCH) {
        return fail("ABI version packing is incorrect in C");
    }
    if ((abi_capabilities() & required_capabilities) != required_capabilities) {
        return fail("runtime capability mask is incomplete");
    }
    if (paint_command_size() != sizeof(paint_command_t)) {
        return fail("runtime paint-command size differs from the C header");
    }

    /* The C11 consumer drives the Android pipeline end to end. */
    const android_ui_options_t options = {2.f, 2.f};
    android_ui_t ui = NULL;
    if (android_ui_create(&options, &ui) != OK || ui == NULL) {
        return fail("ui creation failed");
    }
    android_ui_set_text_measurer(ui, c_measurer, NULL);

    android_view_t root = NULL;
    if (android_view_create(ui, ANDROID_VIEW_LINEAR_LAYOUT, 1, &root) != OK) {
        return fail("root creation failed");
    }
    android_view_t label = NULL;
    if (android_view_create(ui, ANDROID_VIEW_TEXT_VIEW, 2, &label) != OK) {
        return fail("label creation failed");
    }
    android_layout_params_t lp;
    lp.width.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lp.height.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lp.margins_dp.left = lp.margins_dp.top = lp.margins_dp.right = lp.margins_dp.bottom = 0.f;
    lp.gravity = ANDROID_GRAVITY_NO_GRAVITY;
    lp.weight = 0.f;
    android_view_set_layout_params(root, &lp);
    android_view_set_layout_params(label, &lp);
    android_view_set_text(label, "C11");
    if (android_view_add_child(ui, root, label) != OK) {
        return fail("add child failed");
    }
    if (android_ui_measure(ui, root, 320.f, 640.f) != OK) {
        return fail("measure failed");
    }
    if (android_ui_layout(ui, root, 0.f, 0.f, 320.f, 640.f) != OK) {
        return fail("layout failed");
    }
    display_list_t list = NULL;
    if (android_ui_record(ui, root, &list) != OK || list == NULL) {
        return fail("record failed");
    }
    if (display_list_get_count(list) != 1) {
        display_list_destroy(list);
        android_ui_destroy(ui);
        return fail("unexpected display-list command count");
    }
    paint_command_t cmd;
    if (display_list_get_command(list, 0, &cmd) != OK ||
        cmd.tag != PAINT_DRAW_TEXT) {
        display_list_destroy(list);
        android_ui_destroy(ui);
        return fail("expected a draw-text command");
    }
    paint_command_free(&cmd);
    display_list_destroy(list);
    android_ui_destroy(ui);

    puts("OK: C ABI smoke test passed");
    return 0;
}
