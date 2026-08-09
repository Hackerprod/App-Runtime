/* Measure-spec fidelity: dp scaling, EXACTLY/AT_MOST/UNSPECIFIED, weights,
 * gone children, margins, padding. */

#include "android_test_util.h"

static void test_text_view_wrap() {
    android_ui_t ui = make_ui();
    android_view_t host = make_view(ui, ANDROID_VIEW_FRAME_LAYOUT);
    set_match(host);
    android_view_t tv = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    set_wrap(tv);
    android_view_set_text(tv, "Hello");
    android_view_add_child(ui, host, tv);
    frame_and_layout(ui, host, 320.f, 640.f);
    sizef size{};
    android_view_get_measured_size(tv, &size);
    EXPECT_NEAR(size.width, 89.6, 0.01);
    EXPECT_NEAR(size.height, 38.4, 0.01);
    android_ui_destroy(ui);
}

static void test_text_view_exact() {
    android_ui_t ui = make_ui();
    android_view_t host = make_view(ui, ANDROID_VIEW_FRAME_LAYOUT);
    set_match(host);
    android_view_t tv = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_layout_params_t lp{};
    lp.width.kind = ANDROID_SIZE_KIND_EXACT; lp.width.value_dp = 50.f;
    lp.height.kind = ANDROID_SIZE_KIND_EXACT; lp.height.value_dp = 10.f;
    android_view_set_layout_params(tv, &lp);
    android_view_set_text(tv, "Hello");
    android_view_add_child(ui, host, tv);
    frame_and_layout(ui, host, 320.f, 640.f);
    sizef size{};
    android_view_get_measured_size(tv, &size);
    EXPECT_NEAR(size.width, 100.0, 0.01);
    EXPECT_NEAR(size.height, 20.0, 0.01);
    android_ui_destroy(ui);
}

static void test_linear_vertical_sum() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_wrap(root);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    set_wrap(a); set_wrap(b);
    android_view_set_text(a, "A");
    android_view_set_text(b, "B");
    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, b);
    /* Frame parent gives AT_MOST specs so wrap resolves to desired. */
    android_view_t host = make_view(ui, ANDROID_VIEW_FRAME_LAYOUT);
    set_match(host);
    android_view_add_child(ui, host, root);
    frame_and_layout(ui, host, 320.f, 640.f);
    sizef size{};
    android_view_get_measured_size(root, &size);
    EXPECT_NEAR(size.height, 76.8, 0.01);
    EXPECT_NEAR(size.width, 17.92, 0.01);
    android_ui_destroy(ui);
}

static void test_linear_match_parent_child() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_t child = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    set_match(child);
    android_view_set_text(child, "A");
    android_view_add_child(ui, root, child);
    frame_and_layout(ui, root, 200.f, 300.f);
    sizef size{};
    android_view_get_measured_size(child, &size);
    EXPECT_NEAR(size.width, 200.0, 0.01);
    EXPECT_NEAR(size.height, 300.0, 0.01);
    android_ui_destroy(ui);
}

static void test_linear_weights() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    set_wrap(a); set_wrap(b);
    android_view_set_text(a, "A");
    android_view_set_text(b, "B");
    android_layout_params_t lpa{};
    lpa.width.kind = ANDROID_SIZE_KIND_MATCH_PARENT;
    lpa.height.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lpa.weight = 1.f;
    android_view_set_layout_params(a, &lpa);
    android_layout_params_t lpb = lpa;
    android_view_set_layout_params(b, &lpb);
    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, b);
    frame_and_layout(ui, root, 200.f, 300.f);
    sizef sa{}, sb{};
    android_view_get_measured_size(a, &sa);
    android_view_get_measured_size(b, &sb);
    /* AOSP: wrap-height + weight -> measuredHeight + share. remaining = 300 - 76.8
     * = 223.2; share 111.6 each -> 38.4 + 111.6 = 150. */
    EXPECT_NEAR(sa.height, 150.0, 0.01);
    EXPECT_NEAR(sb.height, 150.0, 0.01);
    android_ui_destroy(ui);
}

static void test_linear_zero_dim_weights() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_view_set_text(a, "A");
    android_view_set_text(b, "B");
    android_layout_params_t lp{};
    lp.width.kind = ANDROID_SIZE_KIND_MATCH_PARENT;
    lp.height.kind = ANDROID_SIZE_KIND_EXACT; lp.height.value_dp = 0.f;
    lp.weight = 1.f;
    android_view_set_layout_params(a, &lp);
    android_view_set_layout_params(b, &lp);
    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, b);
    frame_and_layout(ui, root, 200.f, 300.f);
    sizef sa{}, sb{};
    android_view_get_measured_size(a, &sa);
    android_view_get_measured_size(b, &sb);
    /* 0dp + weight: the child receives its share alone (300 / 2 = 150). */
    EXPECT_NEAR(sa.height, 150.0, 0.01);
    EXPECT_NEAR(sb.height, 150.0, 0.01);
    android_ui_destroy(ui);
}

static void test_linear_weight_shrink_on_overflow() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_view_set_text(a, "A");
    android_view_set_text(b, "B");
    android_layout_params_t lp{};
    lp.width.kind = ANDROID_SIZE_KIND_MATCH_PARENT;
    lp.height.kind = ANDROID_SIZE_KIND_EXACT; lp.height.value_dp = 60.f;
    lp.weight = 1.f;
    android_view_set_layout_params(a, &lp);
    android_view_set_layout_params(b, &lp);
    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, b);
    frame_and_layout(ui, root, 200.f, 100.f);
    sizef sa{}, sb{};
    android_view_get_measured_size(a, &sa);
    android_view_get_measured_size(b, &sb);
    /* Negative excess shrinks weighted children: (100 - 240) / 2 = -70 -> 120 - 70 = 50. */
    EXPECT_NEAR(sa.height, 50.0, 0.01);
    EXPECT_NEAR(sb.height, 50.0, 0.01);
    android_ui_destroy(ui);
}

static void test_linear_weight_sum() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_set_weight_sum(root, 4.f);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    set_wrap(a); set_wrap(b);
    android_view_set_text(a, "A");
    android_view_set_text(b, "B");
    android_layout_params_t lp{};
    lp.width.kind = ANDROID_SIZE_KIND_MATCH_PARENT;
    lp.height.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lp.weight = 1.f;
    android_view_set_layout_params(a, &lp);
    android_view_set_layout_params(b, &lp);
    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, b);
    frame_and_layout(ui, root, 200.f, 300.f);
    sizef sa{}, sb{};
    android_view_get_measured_size(a, &sa);
    android_view_get_measured_size(b, &sb);
    /* weightSum 4 overrides the sum: share = 223.2 / 4 = 55.8 -> 38.4 + 55.8 = 94.2. */
    EXPECT_NEAR(sa.height, 94.2, 0.01);
    EXPECT_NEAR(sb.height, 94.2, 0.01);
    android_ui_destroy(ui);
}

static void test_linear_major_gravity() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    set_wrap(a);
    android_view_set_text(a, "A");
    android_view_add_child(ui, root, a);
    android_view_set_gravity(root, ANDROID_GRAVITY_CENTER_VERTICAL);
    frame_and_layout(ui, root, 200.f, 300.f);
    rectf b{};
    android_view_get_bounds(a, &b);
    EXPECT_NEAR(b.y, (300.f - 38.4f) / 2.f, 0.01);
    android_ui_destroy(ui);
}

static void test_gone_excluded() {
    android_ui_t ui = make_ui();
    android_view_t host = make_view(ui, ANDROID_VIEW_FRAME_LAYOUT);
    set_match(host);
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_wrap(root);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_view_t g = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    set_wrap(a); set_wrap(g); set_wrap(b);
    android_view_set_text(a, "A");
    android_view_set_text(g, "G");
    android_view_set_text(b, "B");
    android_view_set_visibility(g, ANDROID_GONE);
    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, g);
    android_view_add_child(ui, root, b);
    android_view_add_child(ui, host, root);
    frame_and_layout(ui, host, 320.f, 640.f);
    sizef size{};
    android_view_get_measured_size(root, &size);
    EXPECT_NEAR(size.height, 76.8, 0.01);
    rectf gb{};
    android_view_get_bounds(g, &gb);
    EXPECT_NEAR(gb.width, 0.0, 0.01);
    EXPECT_NEAR(gb.height, 0.0, 0.01);
    android_ui_destroy(ui);
}

static void test_margins_and_padding() {
    android_ui_t ui = make_ui();
    android_view_t host = make_view(ui, ANDROID_VIEW_FRAME_LAYOUT);
    set_match(host);
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_wrap(root);
    android_view_set_padding_edges_dp(root, {5.f, 5.f, 5.f, 5.f});
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    set_wrap(a);
    android_view_set_text(a, "A");
    android_layout_params_t lp{};
    lp.width.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lp.height.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lp.margins_dp = {3.f, 2.f, 3.f, 2.f};
    android_view_set_layout_params(a, &lp);
    android_view_add_child(ui, root, a);
    android_view_add_child(ui, host, root);
    frame_and_layout(ui, host, 320.f, 640.f);
    sizef size{};
    android_view_get_measured_size(root, &size);
    /* desired = padding(20px) + child(17.92 + margins 12px horizontal, 38.4 + 8px vertical) */
    EXPECT_NEAR(size.width, 20.0 + 17.92 + 12.0, 0.01);
    EXPECT_NEAR(size.height, 20.0 + 38.4 + 8.0, 0.01);
    android_ui_destroy(ui);
}

int main() {
    test_text_view_wrap();
    test_text_view_exact();
    test_linear_vertical_sum();
    test_linear_match_parent_child();
    test_linear_weights();
    test_linear_zero_dim_weights();
    test_linear_weight_shrink_on_overflow();
    test_linear_weight_sum();
    test_linear_major_gravity();
    test_gone_excluded();
    test_margins_and_padding();
    return test_result();
}
