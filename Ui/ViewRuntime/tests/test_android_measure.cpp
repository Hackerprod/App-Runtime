/* Measure-spec fidelity: dp scaling, EXACTLY/AT_MOST/UNSPECIFIED, weights,
 * gone children, margins, padding. */

#include "android_test_util.h"

#include <algorithm>
#include <string>

/* Word-wrapping measurer (uniform glyphs: char = size, space = size/2) to
 * exercise the TV3 two-step height path — the final layout wraps at
 * width - padding, NOT at the raw widthLimit. */
inline android_text_metrics_t wrapping_text_measurer(
    const char* text, float size, float max_width, void*) {
    if (text == nullptr) return {0.f, size * 1.2f, size * 0.8f};
    float line_w = 0.f, total_w = 0.f;
    int lines = 1;
    std::string word;
    const auto flush_word = [&]() {
        if (word.empty()) return;
        const float word_w = static_cast<float>(word.size()) * size;
        if (line_w > 0.f && line_w + word_w > max_width) {
            ++lines;
            line_w = word_w;
        } else {
            line_w += word_w;
        }
        total_w = std::max(total_w, line_w);
        word.clear();
    };
    for (const char* p = text; *p; ++p) {
        if (*p == ' ') {
            flush_word();
            if (line_w > 0.f && line_w + 0.5f * size > max_width) {
                ++lines;
                line_w = 0.f;
            } else {
                line_w += 0.5f * size;
            }
        } else {
            word += *p;
        }
    }
    flush_word();
    if (max_width > 0.f && total_w > max_width) total_w = max_width;
    return {total_w, size * 1.2f * static_cast<float>(lines), size * 0.8f};
}

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
    /* AOSP TextView.onMeasure ceils the raw text width ((int)Math.ceil(...)):
     * 5 * 0.56 * 32 = 89.6 -> 90. */
    EXPECT_NEAR(size.width, 90.0, 0.01);
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
    /* AOSP width ceil: ceil(0.56*32) = 18 per glyph column. */
    EXPECT_NEAR(size.width, 18.0, 0.01);
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
     * = 223.2; each share is TRUNCATED to int (LinearLayout.java:1008
     * (int)(childWeight * remainingExcess / remainingWeightSum)): A gets
     * (int)111.6 = 111 -> 38.4 + 111 = 149.4, B then absorbs the fractional
     * remainder: (int)((223.2-111)/1) = 112 -> 38.4 + 112 = 150.4. */
    EXPECT_NEAR(sa.height, 149.4, 0.01);
    EXPECT_NEAR(sb.height, 150.4, 0.01);
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
    /* weightSum 4 overrides the sum: A share = (int)(223.2 / 4) = (int)55.8 =
     * 55 -> 38.4 + 55 = 93.4; B absorbs the remainder with the truncated
     * running excess: (int)(168.2 / 3) = 56 -> 38.4 + 56 = 94.4
     * (LinearLayout.java:1008/1412 truncation). */
    EXPECT_NEAR(sa.height, 93.4, 0.01);
    EXPECT_NEAR(sb.height, 94.4, 0.01);
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
    /* desired = padding(20px) + child(ceil 0.56*32 = 18 + margins 12px
     * horizontal, 38.4 + 8px vertical) */
    EXPECT_NEAR(size.width, 20.0 + 18.0 + 12.0, 0.01);
    EXPECT_NEAR(size.height, 20.0 + 38.4 + 8.0, 0.01);
    android_ui_destroy(ui);
}

/* TV3: the final layout wraps to want = width - padding, not the raw
 * widthSize. With 20px horizontal padding on an AT_MOST 320px text, the text
 * wraps at 320px (3 lines) for the WIDTH but at 280px (4 lines) for the
 * HEIGHT — AOSP TextView.java:11394 -> makeNewLayout 11403/11422. */
static void test_text_view_multiline_height_wraps_padded_width() {
    android_ui_t ui = make_ui(2.f);
    android_ui_set_text_measurer(ui, wrapping_text_measurer, nullptr);
    android_view_t host = make_view(ui, ANDROID_VIEW_FRAME_LAYOUT);
    set_match(host);
    android_view_t tv = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    set_wrap(tv);
    android_view_set_text(tv, "AAA BBB CCC DDD EEE FFF GGG");
    android_view_set_padding_edges_dp(tv, {10.f, 0.f, 10.f, 0.f});
    android_view_add_child(ui, host, tv);
    frame_and_layout(ui, host, 320.f, 640.f);
    sizef size{};
    android_view_get_measured_size(tv, &size);
    /* width = min(desired 320+40, 320) = 320; height from wrap at 280 ->
     * 4 lines * 38.4 (not 3 lines at the raw 320 limit). */
    EXPECT_NEAR(size.width, 320.0, 0.01);
    EXPECT_NEAR(size.height, 4.f * 38.4f, 0.01);
    android_ui_destroy(ui);
}

/* TV5: a singleLine TextView wraps to VERY_WIDE (TextView.java:11397), so the
 * height is one line even when the EXACTLY width would wrap the text across
 * many lines; without the flag the re-measure would report 7 lines. */
static void test_text_view_single_line_one_line_height() {
    android_ui_t ui = make_ui(2.f);
    android_ui_set_text_measurer(ui, wrapping_text_measurer, nullptr);
    android_view_t host = make_view(ui, ANDROID_VIEW_FRAME_LAYOUT);
    set_match(host);
    android_view_t tv = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_layout_params_t lp{};
    lp.width.kind = ANDROID_SIZE_KIND_EXACT; lp.width.value_dp = 20.f;
    lp.height.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    android_view_set_layout_params(tv, &lp);
    android_view_set_single_line(tv, TRUE);
    android_view_set_text(tv, "AAA BBB CCC DDD EEE FFF GGG");
    android_view_add_child(ui, host, tv);
    frame_and_layout(ui, host, 320.f, 640.f);
    sizef size{};
    android_view_get_measured_size(tv, &size);
    /* EXACTLY width = 40px wraps each 3-char word (96px) onto its own line;
     * singleLine keeps the height at one line (32 * 1.2 = 38.4). */
    EXPECT_NEAR(size.width, 40.0, 0.01);
    EXPECT_NEAR(size.height, 38.4, 0.01);
    android_ui_destroy(ui);
}

int main() {
    test_text_view_wrap();
    test_text_view_exact();
    test_text_view_multiline_height_wraps_padded_width();
    test_text_view_single_line_one_line_height();
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
