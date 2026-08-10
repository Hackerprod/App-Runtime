/* Real text measurement via stb_truetype (MIT, single header). The measurer
 * follows the Android TextView model: advance widths scaled by the font's
 * pixel height, ascent/descent from the font's vertical metrics, and word
 * wrap against a max width (each wrapped line adds one line height).
 *
 * Registered through android_ui_set_font(), which loads a TrueType file and
 * installs this measurer with the ui as user_data. */

#define STB_TRUETYPE_IMPLEMENTATION
#include "../third_party/stb_truetype.h"

#include "../include/viewruntime/viewruntime_backend.h"
#include "../src/android/android_types.h"

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace viewruntime::android {

/* Decode one UTF-8 code point; returns the byte length consumed (0 at end).
 * The text is a C string, so the '\0' terminator is the only length bound:
 * a multi-byte sequence truncated at the end (e.g. "a\xC2") must NOT read
 * s[1]/s[2]/s[3] past the terminator — the wrap loop advances `text += n`
 * and would then read out of bounds. When a continuation byte is the
 * terminator (or missing), emit U+FFFD and consume just the lead byte. */
static int utf8_decode(const char* s, unsigned int* out) {
    const unsigned char c = static_cast<unsigned char>(*s);
    if (c < 0x80) {
        *out = c;
        return 1;
    }
    if ((c & 0xE0) == 0xC0 && s[1] != '\0') {
        *out = ((c & 0x1F) << 6) | (static_cast<unsigned char>(s[1]) & 0x3F);
        return 2;
    }
    if ((c & 0xF0) == 0xE0 && s[1] != '\0' && s[2] != '\0') {
        *out = ((c & 0x0F) << 12) | ((static_cast<unsigned char>(s[1]) & 0x3F) << 6) |
               (static_cast<unsigned char>(s[2]) & 0x3F);
        return 3;
    }
    if ((c & 0xF8) == 0xF0 && s[1] != '\0' && s[2] != '\0' && s[3] != '\0') {
        *out = ((c & 0x07) << 18) | ((static_cast<unsigned char>(s[1]) & 0x3F) << 12) |
               ((static_cast<unsigned char>(s[2]) & 0x3F) << 6) |
               (static_cast<unsigned char>(s[3]) & 0x3F);
        return 4;
    }
    *out = 0xFFFD; /* replacement character (malformed or truncated) */
    return 1;
}

/* Width of a single line of text (no wrap) in pixels. */
static float stb_line_width(stbtt_fontinfo* font, const char* text, float scale) {
    float width = 0.f;
    int previous = 0;
    while (*text) {
        unsigned int cp = 0;
        const int n = utf8_decode(text, &cp);
        if (n == 0) break;
        text += n;
        int advance = 0, lsb = 0;
        stbtt_GetCodepointHMetrics(font, static_cast<int>(cp), &advance, &lsb);
        if (previous != 0) {
            width += stbtt_GetCodepointKernAdvance(font, previous, static_cast<int>(cp)) * scale;
        }
        width += static_cast<float>(advance) * scale;
        previous = static_cast<int>(cp);
    }
    return width;
}

/* Width of the text range [start, end) — AOSP Layout.getDesiredWidth(source,
 * start, end) measures a BOUNDED range (Layout.java:250); the wrap pass must
 * not measure from word_start to the end of the whole string (which would
 * count every following word and inflate the width). */
static float stb_range_width(stbtt_fontinfo* font, const char* start,
                             const char* end, float scale) {
    float width = 0.f;
    int previous = 0;
    const char* p = start;
    while (p < end) {
        unsigned int cp = 0;
        const int n = utf8_decode(p, &cp);
        if (n == 0) break;
        p += n;
        int advance = 0, lsb = 0;
        stbtt_GetCodepointHMetrics(font, static_cast<int>(cp), &advance, &lsb);
        if (previous != 0) {
            width += stbtt_GetCodepointKernAdvance(font, previous, static_cast<int>(cp)) * scale;
        }
        width += static_cast<float>(advance) * scale;
        previous = static_cast<int>(cp);
    }
    return width;
}

android_text_metrics_t stb_text_measurer(const char* text, float size_px,
                                         float max_width, void* user_data) {
    android_ui_s* ui = static_cast<android_ui_s*>(user_data);
    stbtt_fontinfo* font = ui ? static_cast<stbtt_fontinfo*>(ui->font_face) : nullptr;
    if (font == nullptr || text == nullptr) {
        return {0.f, size_px * 1.2f, size_px * 0.8f};
    }

    const float scale = stbtt_ScaleForPixelHeight(font, size_px);
    int ascent = 0, descent = 0, line_gap = 0;
    stbtt_GetFontVMetrics(font, &ascent, &descent, &line_gap);
    /* AOSP line height = descent - ascent (StaticLayout.java:1255 out():
     * v += below - above, with fm.ascent/descent and NO leading). The hhea
     * lineGap is NOT added — do not include line_gap here. */
    const float line_height = static_cast<float>(ascent - descent) * scale;
    const float baseline = static_cast<float>(ascent) * scale;

    if (max_width <= 0.f) {
        /* AOSP getDesiredWidth (no limit) returns the MAX width over
         * paragraphs, omitting the '\n' itself (Layout.java:277-293
         * measurePara per paragraph, need = max). The plain single-string
         * width would sum paragraphs and measure '\n' as a glyph. Height:
         * one line per paragraph (Layout.java:230-231) — a multi-paragraph
         * string is multiple lines even without a width limit. */
        float widest = 0.f;
        int para_lines = 1;
        const char* p = text;
        while (*p) {
            const char* nl = std::strchr(p, '\n');
            const char* end = nl ? nl : p + std::strlen(p);
            widest = std::max(widest, stb_range_width(font, p, end, scale));
            if (!nl) break;
            ++para_lines;
            p = nl + 1;
        }
        return {widest, line_height * static_cast<float>(para_lines), baseline};
    }

    /* Word wrap: break at spaces so each line fits within max_width. AOSP
     * getLineVisibleEnd strips ALL trailing whitespace from every line EXCEPT
     * the last (Layout.java:2767-2793) — an internal space ("aa bb") is kept,
     * only whitespace that ends the line is dropped, and ALL of it (not one
     * space). Track `visible` = width WITHOUT trailing whitespace separately
     * from `line_width` (which includes it), so the captured width of a
     * wrapped/paragraph line is `visible` and the final line keeps its
     * trailing whitespace. */
    float total_width = 0.f;
    int lines = 1;
    const char* word_start = text;
    float line_width = 0.f; /* width incl. trailing whitespace of this line */
    float visible = 0.f;    /* width of this line WITHOUT trailing whitespace */
    while (*text) {
        unsigned int cp = 0;
        const int n = utf8_decode(text, &cp);
        if (n == 0) break;
        const char* next = text + n;
        if (cp == ' ' || cp == '\t' || cp == '\n') {
            /* measure the word [word_start, text) — bounded, AOSP
             * getDesiredWidth(start,end); never to end-of-string. */
            const float word_w = stb_range_width(font, word_start, text, scale);
            /* Only a REAL word (word_w > 0) can force a wrap: with trailing
             * whitespace before '\n' or the end (word_w == 0) the line must
             * NOT wrap — line_width + 0 > max would otherwise create a
             * phantom line ("ab \n" must be 2 lines, not 3). */
            if (word_w > 0.f && line_width + word_w > max_width && line_width > 0.f) {
                /* Wrap: the previous line ends here. Capture its visible
                 * width (no trailing whitespace — getLineVisibleEnd). */
                total_width = std::max(total_width, visible);
                ++lines;
                line_width = word_w;
                visible = word_w;
            } else {
                line_width += word_w;
                /* A real (non-empty) word after the trailing spaces makes
                 * them internal: only then does visible reach line_width.
                 * Consecutive separators ("aa   ") stay trailing. */
                if (word_w > 0.f) visible = line_width;
            }
            if (cp == '\n') {
                /* AOSP getDesiredWidthWithLimit measures each paragraph
                 * separately and takes the max (Layout.java:277-291) — the
                 * paragraph BEFORE the '\n' must contribute its width. Capture
                 * it BEFORE resetting the line (the old code maxed against 0,
                 * losing the paragraph width entirely). */
                total_width = std::max(total_width, visible);
                ++lines;
                line_width = 0.f;
                visible = 0.f;
            } else {
                /* Separator (or trailing) space: joins the line width but is
                 * still trailing whitespace until a non-space follows, so
                 * `visible` is unchanged. AOSP NEVER breaks the line AT a
                 * space — the break happens when the NEXT word does not fit
                 * (handled in the word branch above). Breaking here would
                 * create a phantom line for a trailing space ("ab " with
                 * max ∈ [w_ab, w_ab+w_s) must stay ONE line) and would leave
                 * a leading space on the next line. */
                line_width += stb_line_width(font, " ", scale);
            }
            word_start = next;
            text = next;
            continue;
        }
        text = next;
    }
    /* last word — the FINAL line keeps its trailing whitespace
     * (getLineVisibleEnd strips only non-last lines, Layout.java:2767).
     * Only a REAL word (last_w > 0) can force a wrap: if the text ends in
     * trailing whitespace (last_w == 0) the line must NOT wrap — "ab " with
     * max ∈ [w_ab, w_ab+w_s) is one line, and line_width + 0 > max would
     * otherwise create a phantom line. */
    const float last_w = stb_range_width(font, word_start, text, scale);
    if (last_w > 0.f && line_width + last_w > max_width && line_width > 0.f) {
        /* The previous line ends here (it wrapped): capture its visible
         * width WITHOUT trailing whitespace before starting the last line. */
        total_width = std::max(total_width, visible);
        ++lines;
        line_width = last_w;
    } else {
        line_width += last_w;
    }
    total_width = std::max(total_width, line_width);
    if (total_width > max_width) total_width = max_width;

    return {total_width, line_height * static_cast<float>(lines), baseline};
}

} // namespace viewruntime::android

namespace viewruntime::android {

void android_ui_release_font(android_ui_s* ui) {
    if (ui->font_face) {
        delete static_cast<stbtt_fontinfo*>(ui->font_face);
        ui->font_face = nullptr;
    }
    std::free(ui->font_data);
    ui->font_data = nullptr;
    ui->font_data_size = 0;
}

} // namespace viewruntime::android

extern "C" {

API status_t android_ui_measure_text(
    android_ui_t ui, const char* text, float size_px, float max_width,
    android_text_metrics_t* out_metrics) {
    if (!ui || !text || !out_metrics) return ERROR_NULL_ARG;
    if (!ui->text_measurer) return ERROR_INVALID_STATE;
    *out_metrics = ui->text_measurer(text, size_px, max_width, ui->text_measurer_data);
    return OK;
}

API status_t android_ui_set_font(android_ui_t ui, const char* path) {
    if (!ui || !path) return ERROR_NULL_ARG;
    ::android_ui_s* u = ui;

    FILE* f = std::fopen(path, "rb");
    if (!f) return ERROR_INVALID_STATE;
    std::fseek(f, 0, SEEK_END);
    const long len = std::ftell(f);
    std::fseek(f, 0, SEEK_SET);
    if (len <= 0) {
        std::fclose(f);
        return ERROR_INVALID_STATE;
    }
    uint8_t* data = static_cast<uint8_t*>(std::malloc(static_cast<size_t>(len)));
    if (!data) {
        std::fclose(f);
        return ERROR_OUT_OF_MEMORY;
    }
    const size_t read = std::fread(data, 1, static_cast<size_t>(len), f);
    std::fclose(f);
    if (read != static_cast<size_t>(len)) {
        std::free(data);
        return ERROR_INVALID_STATE;
    }

    /* Replace any previous font. */
    if (u->font_face) {
        delete static_cast<stbtt_fontinfo*>(u->font_face);
        u->font_face = nullptr;
    }
    std::free(u->font_data);
    u->font_data = data;
    u->font_data_size = static_cast<size_t>(len);

    stbtt_fontinfo* face = new stbtt_fontinfo();
    if (!stbtt_InitFont(face, data, 0)) {
        delete face;
        std::free(u->font_data);
        u->font_data = nullptr;
        u->font_data_size = 0;
        return ERROR_INVALID_STATE;
    }
    u->font_face = face;
    ui->text_measurer = viewruntime::android::stb_text_measurer;
    ui->text_measurer_data = ui;

    /* Keep paint and measure on the SAME font: propagate the exact bytes to
     * the registered render surface so draw_text renders real glyphs instead
     * of the no-font solid-block fallback. */
    if (ui->surface) {
        viewruntime_surface_set_font(ui->surface, data, static_cast<int32_t>(len));
    }
    return OK;
}

} // extern "C"
