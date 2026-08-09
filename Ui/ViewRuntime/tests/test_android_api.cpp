/* C API surface: lifecycle, ownership, error contracts, ids. */

#include "android_test_util.h"

static void test_capabilities() {
    EXPECT((abi_capabilities() & CAPABILITY_ANDROID_UI) != 0);
    EXPECT(paint_command_size() >= sizeof(paint_command_t));
}

static void test_create_destroy() {
    android_ui_t ui = nullptr;
    EXPECT(android_ui_create(nullptr, nullptr) == ERROR_NULL_ARG);
    EXPECT(android_ui_create(nullptr, &ui) == OK);
    android_ui_destroy(ui);
    android_ui_destroy(nullptr); /* no-op */
}

static void test_view_create_errors() {
    android_ui_t ui = make_ui();
    android_view_t v = nullptr;
    EXPECT(android_view_create(ui, (android_view_class_t)999, 0, &v) ==
           ERROR_INVALID_STATE);
    EXPECT(android_view_create(ui, ANDROID_VIEW_VIEW, 0, nullptr) ==
           ERROR_NULL_ARG);
    EXPECT(android_view_create(nullptr, ANDROID_VIEW_VIEW, 0, &v) ==
           ERROR_NULL_ARG);
    android_ui_destroy(ui);
}

static void test_tree_ownership() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT, 10);
    android_view_t child = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 20);
    android_view_t other = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 30);

    EXPECT(android_view_add_child(ui, root, child) == OK);
    EXPECT(android_view_get_parent(child) == root);
    EXPECT(android_view_get_child_count(root) == 1);
    EXPECT(android_view_get_child(root, 0) == child);

    /* double parent rejected */
    EXPECT(android_view_add_child(ui, root, child) == ERROR_INVALID_STATE);
    /* self adoption rejected */
    EXPECT(android_view_add_child(ui, root, root) == ERROR_INVALID_STATE);
    /* null args */
    EXPECT(android_view_add_child(nullptr, root, child) == ERROR_NULL_ARG);
    EXPECT(android_view_add_child(ui, nullptr, child) == ERROR_NULL_ARG);

    /* find by id */
    EXPECT(android_ui_find_view_by_id(ui, 20) == child);
    EXPECT(android_ui_find_view_by_id(ui, 999) == nullptr);

    /* detach puts it back as a root, stays owned */
    EXPECT(android_view_detach(ui, child) == OK);
    EXPECT(android_view_get_parent(child) == nullptr);
    EXPECT(android_view_get_child_count(root) == 0);

    /* remove_child validation */
    EXPECT(android_view_remove_child(ui, root, other) == ERROR_INVALID_STATE);
    android_view_add_child(ui, root, child);
    EXPECT(android_view_remove_child(ui, root, child) == OK);
    EXPECT(android_view_get_parent(child) == nullptr);

    /* clear destroys all views */
    android_view_add_child(ui, root, other);
    EXPECT(android_ui_clear(ui) == OK);
    EXPECT(android_view_get_child_count(root) == 0); /* root is dead but handle must not crash ops */
    android_ui_destroy(ui);
}

static void test_setter_validation() {
    android_ui_t ui = make_ui();
    android_view_t tv = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_view_t bar = make_view(ui, ANDROID_VIEW_PROGRESS_BAR);
    android_view_t linear = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);

    android_layout_params_t bad{};
    bad.width.kind = (android_size_kind_t)7;
    EXPECT(android_view_set_layout_params(tv, &bad) == ERROR_INVALID_STATE);

    android_layout_params_t neg{};
    neg.width.kind = ANDROID_SIZE_KIND_EXACT;
    neg.width.value_dp = -1.f;
    EXPECT(android_view_set_layout_params(tv, &neg) == ERROR_INVALID_STATE);

    EXPECT(android_view_set_visibility(tv, 3) == ERROR_INVALID_STATE);
    EXPECT(android_view_set_padding_dp(tv, -1.f) == ERROR_NULL_ARG);
    EXPECT(android_view_set_orientation(tv, 5) == ERROR_INVALID_STATE);
    EXPECT(android_view_set_text_size_sp(tv, 0.f) == ERROR_NULL_ARG);
    EXPECT(android_view_set_scale_type(tv, 42) == ERROR_INVALID_STATE);
    EXPECT(android_view_set_checked(tv, TRUE) == ERROR_INVALID_STATE);
    EXPECT(android_view_set_checked(bar, TRUE) == ERROR_INVALID_STATE);
    EXPECT(android_view_set_progress(tv, 0, 100, 50) == ERROR_INVALID_STATE);
    EXPECT(android_view_set_progress(bar, 100, 0, 50) == ERROR_INVALID_STATE);
    EXPECT(android_view_set_progress(bar, 0, 100, 5000) == OK);
    android_view_set_progress(bar, 0, 100, 5000);
    EXPECT(android_view_set_baseline_aligned(tv, TRUE) == ERROR_INVALID_STATE);
    EXPECT(android_view_set_baseline_aligned(linear, TRUE) == OK);
    EXPECT(android_view_set_scroll_offset(tv, 0.f, 1.f) == ERROR_INVALID_STATE);

    /* queries on null */
    EXPECT(android_view_get_child_count(nullptr) == 0);
    EXPECT(android_view_get_child(nullptr, 0) == nullptr);
    EXPECT(android_ui_find_view_by_id(nullptr, 1) == nullptr);
    android_ui_destroy(ui);
}

static void test_measure_errors() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    EXPECT(android_ui_measure(ui, root, 0.f, 100.f) == ERROR_INVALID_STATE);
    EXPECT(android_ui_measure(nullptr, root, 100.f, 100.f) == ERROR_NULL_ARG);
    EXPECT(android_ui_measure(ui, nullptr, 100.f, 100.f) == ERROR_NULL_ARG);
    EXPECT(android_ui_layout(ui, root, 0.f, 0.f, -1.f, 100.f) == ERROR_INVALID_STATE);
    EXPECT(android_ui_record(ui, root, nullptr) == ERROR_NULL_ARG);
    display_list_t list = nullptr;
    EXPECT(android_ui_record(nullptr, root, &list) == ERROR_NULL_ARG);
    android_ui_destroy(ui);
}

int main() {
    test_capabilities();
    test_create_destroy();
    test_view_create_errors();
    test_tree_ownership();
    test_setter_validation();
    test_measure_errors();
    return test_result();
}
