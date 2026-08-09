/* Real text measurement via stb_truetype: font metrics, scaling, word wrap.
 * Uses a system TrueType font; skipped when none is available. */

#include "android_test_util.h"

static const char* find_system_font() {
    static const char* candidates[] = {
        "C:\\Windows\\Fonts\\segoeui.ttf",
        "C:\\Windows\\Fonts\\arial.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
    };
    for (const char* c : candidates) {
        FILE* f = std::fopen(c, "rb");
        if (f) {
            std::fclose(f);
            return c;
        }
    }
    return nullptr;
}

static void test_font_measures() {
    const char* font_path = find_system_font();
    if (font_path == nullptr) {
        std::printf("SKIP test_font_measures: no system font found\n");
        return;
    }
    android_ui_options_t options = {2.f, 2.f};
    android_ui_t ui = nullptr;
    EXPECT(android_ui_create(&options, &ui) == OK);
    EXPECT(android_ui_set_font(ui, font_path) == OK);

    android_text_metrics_t m10{}; android_ui_measure_text(ui, "Hello World", 10.f, 0.f, &m10);
    android_text_metrics_t m20{}; android_ui_measure_text(ui, "Hello World", 20.f, 0.f, &m20);

    EXPECT(m10.width > 0.f);
    EXPECT(m10.height > 0.f);
    EXPECT(m10.baseline > 0.f && m10.baseline < m10.height);
    /* doubling the font size doubles the width (linear scale) */
    EXPECT_NEAR(m20.width, m10.width * 2.f, 0.5f);
    /* line height scales linearly too */
    EXPECT_NEAR(m20.height, m10.height * 2.f, 0.5f);

    /* space contributes a real advance (not zero) */
    android_text_metrics_t sp{}; android_ui_measure_text(ui, " ", 10.f, 0.f, &sp);
    EXPECT(sp.width > 0.f);
    EXPECT(sp.width < m10.width);

    /* word wrap against a narrow max width produces multiple lines */
    android_text_metrics_t wrapped{};
    android_ui_measure_text(ui, "Hello World", 10.f, m10.width * 0.5f, &wrapped);
    EXPECT(wrapped.height > m10.height); /* at least two lines */
    EXPECT(wrapped.width <= m10.width * 0.5f + 0.5f);

    android_ui_destroy(ui);
}

static void test_font_textview_layout() {
    const char* font_path = find_system_font();
    if (font_path == nullptr) {
        std::printf("SKIP test_font_textview_layout: no system font found\n");
        return;
    }
    android_ui_options_t options = {2.f, 2.f};
    android_ui_t ui = nullptr;
    EXPECT(android_ui_create(&options, &ui) == OK);
    EXPECT(android_ui_set_font(ui, font_path) == OK);

    android_view_t text = nullptr;
    android_view_create(ui, ANDROID_VIEW_TEXT_VIEW, 0, &text);
    android_view_set_text(text, "Hello World");
    android_view_set_text_size_sp(text, 14.f);
    set_wrap(text); /* wrap-content: the view sizes to the real text run */
    /* wrap-content TextView in a frame: the frame sizes to the real text */
    android_view_t frame = nullptr;
    android_view_create(ui, ANDROID_VIEW_FRAME_LAYOUT, 0, &frame);
    android_layout_params_t fw{};
    fw.width.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    fw.height.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    android_view_set_layout_params(frame, &fw);
    android_view_add_child(ui, frame, text);
    android_ui_measure(ui, frame, 400.f, 400.f);
    sizef ts{};
    android_view_get_measured_size(text, &ts);
    EXPECT(ts.width > 0.f);
    EXPECT(ts.height > 0.f);
    EXPECT_NEAR(ts.height, 28.f * 1.2f, 14.f); /* roughly one line at 14sp */

    android_ui_destroy(ui);
}

int main() {
    test_font_measures();
    test_font_textview_layout();
    return test_result();
}
