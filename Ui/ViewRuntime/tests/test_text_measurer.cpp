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

    /* Regression: a single word in the MIDDLE of a longer text must measure
     * only that word, not to the end of the whole string. Before the fix,
     * stb_line_width(word_start) ran to end-of-string: with a wide max the
     * first word "absorbed" every following word, inflating the width many
     * times over (RuntimeApiLab description: "Ejecuta" measured 295px at 13sp
     * instead of ~35px; total 1707px for 53 chars). */
    android_text_metrics_t wide{};
    android_ui_measure_text(ui,
        "Ejecuta 4 pings a google.com y calcula la latencia media.",
        13.f, 99999.f, &wide);
    /* 53 chars at ~7px/char ≈ 370px; any value > 3x that is the old bug. */
    EXPECT(wide.width > 100.f && wide.width < 1100.f);
    /* ~53 chars / 7px: the first word alone must be far below the total. */
    android_text_metrics_t first{};
    android_ui_measure_text(ui, "Ejecuta", 13.f, 99999.f, &first);
    EXPECT(first.width < wide.width * 0.5f);

    /* Regression: paragraphs separated by '\n' — AOSP getDesiredWidthWithLimit
     * measures EACH paragraph and takes the max (Layout.java:277-291). Before
     * the fix, the width of a paragraph ending in '\n' was discarded (maxed
     * against 0 after the reset), so "ab\ncd" reported only the width of "cd".
     * Here "ab" is wider than "cd", so the total must be ~w("ab"). */
    android_text_metrics_t pa{}, pb{}, multi{};
    android_ui_measure_text(ui, "ab", 13.f, 99999.f, &pa);
    android_ui_measure_text(ui, "cd", 13.f, 99999.f, &pb);
    android_ui_measure_text(ui, "ab\ncd", 13.f, 99999.f, &multi);
    EXPECT(multi.width >= pa.width); /* paragraph 1 width is kept */
    EXPECT(multi.width >= pb.width); /* paragraph 2 width is kept */
    EXPECT(multi.height > pa.height); /* two lines */
    /* A trailing newline must still report the paragraph width, not 0. */
    android_text_metrics_t trail{};
    android_ui_measure_text(ui, "ab\n", 13.f, 99999.f, &trail);
    EXPECT(trail.width >= pa.width);

    /* Regression (audit round 4, BUG 1+2): AOSP getLineVisibleEnd strips
     * ALL trailing whitespace from non-last lines (Layout.java:2767-2793),
     * but only what actually ENDS the line. An INTERNAL space ("aa bb")
     * is kept; the old code subtracted exactly one space unconditionally
     * at '\n' (measure "aa bb\n" as 80 instead of 85) and also on wrapped
     * lines. */
    android_text_metrics_t internal{}, no_trail{}, multi_sp{};
    android_ui_measure_text(ui, "aa bb", 13.f, 99999.f, &internal); /* internal space kept */
    android_ui_measure_text(ui, "aa bb\n", 13.f, 99999.f, &no_trail); /* trailing \n: same width */
    EXPECT_NEAR(no_trail.width, internal.width, 0.01f);
    /* "aa   \n" — three trailing spaces: ALL must be stripped (old code
     * stripped exactly one). "aa" alone is the visible width. */
    android_ui_measure_text(ui, "aa", 13.f, 99999.f, &multi_sp);
    android_ui_measure_text(ui, "aa   \n", 13.f, 99999.f, &trail);
    EXPECT_NEAR(trail.width, multi_sp.width, 0.01f);

    /* Regression (audit round 4, BUG 3): max_width<=0 on a multi-paragraph
     * string reports one line PER PARAGRAPH (Layout.java:230-231) — the old
     * code returned a single line height. */
    android_text_metrics_t one_line{};
    android_ui_measure_text(ui, "a", 13.f, 99999.f, &one_line);
    android_ui_measure_text(ui, "a\nb", 13.f, 99999.f, &multi);
    EXPECT_NEAR(multi.height, one_line.height * 2.f, one_line.height * 0.1f);

    /* Regression (audit round 5, LOW): AOSP NEVER breaks a line AT a space —
     * the break happens when the NEXT word does not fit (StaticLayout breaks
     * only at a break opportunity with a following word). A trailing space
     * must NOT create a phantom extra line: "ab " with max ∈ [w_ab,
     * w_ab+w_s) is ONE line, not two. */
    android_text_metrics_t ab{}, ab_trail{}, ab_nl{};
    android_ui_measure_text(ui, "ab", 13.f, 99999.f, &ab);
    /* max just wide enough for "ab" but not "ab " — the trailing space must
     * not push the line count to 2. */
    android_ui_measure_text(ui, "ab ", 13.f, ab.width, &ab_trail);
    EXPECT_NEAR(ab_trail.height, ab.height, ab.height * 0.1f);
    /* "ab \n" → exactly 2 lines ("ab " + empty), not 3. */
    android_ui_measure_text(ui, "ab \n", 13.f, ab.width, &ab_nl);
    EXPECT_NEAR(ab_nl.height, ab.height * 2.f, ab.height * 0.1f);

    /* Regression (audit round 7, MEDIUM): a UTF-8 string truncated mid
     * multi-byte sequence must not read out of bounds. "a\xC2" (lead byte
     * with NO continuation) and "a\xF0\x9F\x98" (3 of 4 bytes) must decode
     * safely — the old utf8_decode read s[1]/s[2]/s[3] past the '\0'. */
    android_ui_measure_text(ui, "a\xC2", 13.f, 99999.f, &ab_trail);
    EXPECT(ab_trail.width >= 0.f);
    android_ui_measure_text(ui, "a\xF0\x9F\x98", 13.f, 99999.f, &ab_nl);
    EXPECT(ab_nl.width >= 0.f);

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
