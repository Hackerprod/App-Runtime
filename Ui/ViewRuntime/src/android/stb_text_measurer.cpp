/* Real text measurement via stb_truetype (MIT, single header). The measurer
 * follows the Android TextView model: advance widths scaled by the font's
 * pixel height, ascent/descent from the font's vertical metrics, and word
 * wrap against a max width (each wrapped line adds one line height).
 *
 * Registered through android_ui_set_font(), which loads a TrueType file and
 * installs this measurer with the ui as user_data. */

#define STB_TRUETYPE_IMPLEMENTATION
#include "../third_party/stb_truetype.h"

#include "../src/android/android_types.h"

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace viewruntime::android {

/* Decode one UTF-8 code point; returns the byte length consumed (0 at end). */
static int utf8_decode(const char* s, unsigned int* out) {
    const unsigned char c = static_cast<unsigned char>(*s);
    if (c < 0x80) {
        *out = c;
        return 1;
    }
    if ((c & 0xE0) == 0xC0) {
        *out = ((c & 0x1F) << 6) | (static_cast<unsigned char>(s[1]) & 0x3F);
        return 2;
    }
    if ((c & 0xF0) == 0xE0) {
        *out = ((c & 0x0F) << 12) | ((static_cast<unsigned char>(s[1]) & 0x3F) << 6) |
               (static_cast<unsigned char>(s[2]) & 0x3F);
        return 3;
    }
    if ((c & 0xF8) == 0xF0) {
        *out = ((c & 0x07) << 18) | ((static_cast<unsigned char>(s[1]) & 0x3F) << 12) |
               ((static_cast<unsigned char>(s[2]) & 0x3F) << 6) |
               (static_cast<unsigned char>(s[3]) & 0x3F);
        return 4;
    }
    *out = 0xFFFD; /* replacement character */
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
    const float line_height = static_cast<float>(ascent - descent + line_gap) * scale;
    const float baseline = static_cast<float>(ascent) * scale;

    if (max_width <= 0.f) {
        return {stb_line_width(font, text, scale), line_height, baseline};
    }

    /* Word wrap: break at spaces so each line fits within max_width. */
    float total_width = 0.f;
    int lines = 1;
    const char* line_start = text;
    const char* word_start = text;
    float line_width = 0.f;
    while (*text) {
        unsigned int cp = 0;
        const int n = utf8_decode(text, &cp);
        if (n == 0) break;
        const char* next = text + n;
        if (cp == ' ' || cp == '\t' || cp == '\n') {
            /* measure the word [word_start, text) */
            const float word_w = stb_line_width(font, word_start, scale);
            if (line_width + word_w > max_width && line_width > 0.f) {
                ++lines;
                line_width = word_w;
            } else {
                line_width += word_w;
            }
            if (cp == '\n') {
                ++lines;
                line_width = 0.f;
            } else {
                const float space_w = stb_line_width(font, " ", scale);
                if (line_width + space_w > max_width && line_width > 0.f) {
                    ++lines;
                    line_width = 0.f;
                } else {
                    line_width += space_w;
                }
            }
            word_start = next;
            total_width = std::max(total_width, line_width);
            text = next;
            continue;
        }
        text = next;
    }
    /* last word */
    const float last_w = stb_line_width(font, word_start, scale);
    if (line_width + last_w > max_width && line_width > 0.f) {
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
    return OK;
}

} // extern "C"
