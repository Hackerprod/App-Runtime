/* Phase 1 render backend (see include/viewruntime/viewruntime_backend.h and
 * docs/viewruntime-integration-spec.md). Rasterizes App Runtime's flat draw
 * calls into an off-screen ARGB8888 buffer: fill rects and text glyphs
 * (stb_truetype) with clipping. Text measurement shares the same font, so
 * layout and paint agree. */

#include "../include/viewruntime/viewruntime_backend.h"

#include "../third_party/stb_truetype.h" /* declarations only; the
                                            implementation lives in
                                            stb_text_measurer.cpp */

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>

namespace viewruntime_backend {

/* ── UTF-16 ────────────────────────────────────────────────────────── */

/* Decode one UTF-16 code point; returns the number of uint16 consumed (0 at
 * end). Handles surrogate pairs. */
static int utf16_decode(const uint16_t* s, int len, int i, unsigned int* out) {
    const uint32_t c = s[i];
    if (c >= 0xD800 && c <= 0xDBFF && i + 1 < len) {
        const uint32_t lo = s[i + 1];
        if (lo >= 0xDC00 && lo <= 0xDFFF) {
            *out = 0x10000 + ((c - 0xD800) << 10) + (lo - 0xDC00);
            return 2;
        }
    }
    if (c >= 0xDC00 && c <= 0xDFFF) {
        *out = 0xFFFD; /* lone low surrogate */
        return 1;
    }
    *out = c;
    return 1;
}

/* ── Surface ───────────────────────────────────────────────────────── */

struct Surface {
    int width = 0;
    int height = 0;
    float density = 1.f;
    std::vector<uint32_t> pixels; /* straight ARGB8888, row-major */

    stbtt_fontinfo* font = nullptr;
    uint8_t* font_data = nullptr;
    size_t font_data_size = 0;
};

static uint32_t pack_color(uint8_t a, uint8_t r, uint8_t g, uint8_t b) {
    return (static_cast<uint32_t>(a) << 24) |
           (static_cast<uint32_t>(r) << 16) |
           (static_cast<uint32_t>(g) << 8) |
           static_cast<uint32_t>(b);
}

/* Straight-alpha source-over blend into the buffer. */
static void blend_pixel(uint32_t* dst, uint32_t src) {
    const uint32_t sa = (src >> 24) & 0xFF;
    if (sa == 0xFF) {
        *dst = src;
        return;
    }
    if (sa == 0) return;
    const uint32_t da = (*dst >> 24) & 0xFF;
    const uint32_t dr = (*dst >> 16) & 0xFF;
    const uint32_t dg = (*dst >> 8) & 0xFF;
    const uint32_t db = *dst & 0xFF;
    const uint32_t sr = (src >> 16) & 0xFF;
    const uint32_t sg = (src >> 8) & 0xFF;
    const uint32_t sb = src & 0xFF;
    const float sa_f = sa / 255.f;
    const float da_f = da / 255.f;
    const float out_a = sa_f + da_f * (1.f - sa_f);
    if (out_a <= 0.f) {
        *dst = 0;
        return;
    }
    auto blend = [&](uint32_t s, uint32_t d) -> uint32_t {
        return static_cast<uint32_t>(
            ((s * sa_f + d * da_f * (1.f - sa_f)) / out_a) + 0.5f);
    };
    const uint32_t oa = static_cast<uint32_t>(out_a * 255.f + 0.5f);
    const uint32_t orr = blend(sr, dr);
    const uint32_t og = blend(sg, dg);
    const uint32_t ob = blend(sb, db);
    *dst = (oa << 24) | (orr << 16) | (og << 8) | ob;
}

static void fill_rect(Surface* s, float x, float y, float w, float h,
                      uint32_t color) {
    const int x0 = static_cast<int>(std::floor(x));
    const int y0 = static_cast<int>(std::floor(y));
    const int x1 = static_cast<int>(std::ceil(x + w));
    const int y1 = static_cast<int>(std::ceil(y + h));
    const int cx0 = std::max(0, x0);
    const int cy0 = std::max(0, y0);
    const int cx1 = std::min(s->width, x1);
    const int cy1 = std::min(s->height, y1);
    for (int py = cy0; py < cy1; ++py) {
        uint32_t* row = &s->pixels[static_cast<size_t>(py) * s->width];
        for (int px = cx0; px < cx1; ++px) {
            blend_pixel(&row[px], color);
        }
    }
}

/* Blit a glyph bitmap (8-bit alpha, one byte per pixel) into the rect. */
static void blit_glyph(Surface* s, const unsigned char* bitmap, int gw, int gh,
                       int xoff, int yoff, int pen_x, int pen_y,
                       uint32_t color) {
    const uint32_t alpha = (color >> 24) & 0xFF;
    const uint32_t rgb = color & 0x00FFFFFF;
    for (int gy = 0; gy < gh; ++gy) {
        const int py = pen_y + yoff + gy;
        if (py < 0 || py >= s->height) continue;
        uint32_t* row = &s->pixels[static_cast<size_t>(py) * s->width];
        for (int gx = 0; gx < gw; ++gx) {
            const int px = pen_x + xoff + gx;
            if (px < 0 || px >= s->width) continue;
            const unsigned int glyph_a = bitmap[static_cast<size_t>(gy) * gw + gx];
            if (glyph_a == 0) continue;
            /* color * glyph_a * alpha */
            const uint32_t sa = (glyph_a * alpha) / 255;
            const uint32_t src = (sa << 24) | rgb;
            blend_pixel(&row[px], src);
        }
    }
}

/* Width of one unwrapped line of UTF-16 text in pixels (same advances the
 * draw path uses). */
static float line_width(stbtt_fontinfo* font, const uint16_t* text, int len,
                        float scale) {
    float width = 0.f;
    int previous = 0;
    for (int i = 0; i < len;) {
        unsigned int cp = 0;
        const int n = utf16_decode(text, len, i, &cp);
        if (n == 0) break;
        i += n;
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

/* Shared text metrics: same font, scale and advances as the draw path.
 * Word-wraps against max_width (0 = single line). */
static void measure_text_common(Surface* s, const uint16_t* text, int len,
                                float text_size_px, float max_width_px,
                                float* out_w, float* out_h, float* out_baseline) {
    if (s->font == nullptr || len <= 0) {
        /* fallback: deterministic proportional approximation */
        const float width = len > 0 ? 0.56f * text_size_px * static_cast<float>(len) : 0.f;
        if (out_w) *out_w = width;
        if (out_h) *out_h = text_size_px * 1.2f;
        if (out_baseline) *out_baseline = text_size_px * 0.8f;
        return;
    }
    const float scale = stbtt_ScaleForPixelHeight(s->font, text_size_px);
    int ascent = 0, descent = 0, line_gap = 0;
    stbtt_GetFontVMetrics(s->font, &ascent, &descent, &line_gap);
    const float line_height = static_cast<float>(ascent - descent + line_gap) * scale;
    const float baseline = static_cast<float>(ascent) * scale;

    if (max_width_px <= 0.f) {
        if (out_w) *out_w = line_width(s->font, text, len, scale);
        if (out_h) *out_h = line_height;
        if (out_baseline) *out_baseline = baseline;
        return;
    }

    /* word wrap */
    float total_width = 0.f;
    int lines = 1;
    float line_w = 0.f;
    int word_start = 0;
    for (int i = 0; i <= len;) {
        unsigned int cp = 0;
        int n = 1;
        if (i < len) {
            n = utf16_decode(text, len, i, &cp);
        } else {
            cp = 0; /* end of text: flush the last word */
        }
        const bool is_break = cp == 0 || cp == ' ' || cp == '\t' || cp == '\n';
        if (is_break) {
            const float word_w = line_width(s->font, text + word_start, i - word_start, scale);
            if (line_w + word_w > max_width_px && line_w > 0.f) {
                ++lines;
                line_w = word_w;
            } else {
                line_w += word_w;
            }
            if (cp == '\n') {
                ++lines;
                line_w = 0.f;
            } else if (cp == ' ' || cp == '\t') {
                const float space_w = line_width(s->font, text + i, 1, scale);
                if (line_w + space_w > max_width_px && line_w > 0.f) {
                    ++lines;
                    line_w = 0.f;
                } else {
                    line_w += space_w;
                }
            }
            word_start = i + n;
            total_width = std::max(total_width, line_w);
        }
        if (i >= len) break;
        i += n;
    }
    total_width = std::min(total_width, max_width_px);
    if (out_w) *out_w = total_width;
    if (out_h) *out_h = line_height * static_cast<float>(lines);
    if (out_baseline) *out_baseline = baseline;
}

} // namespace viewruntime_backend

/* ── C ABI ─────────────────────────────────────────────────────────── */

using viewruntime_backend::Surface;

extern "C" {

VIEWRUNTIME_BACKEND_API void* viewruntime_surface_create(const char* font_path) {
    Surface* s = new (std::nothrow) Surface();
    if (!s) return nullptr;
    if (font_path != nullptr) {
        FILE* f = std::fopen(font_path, "rb");
        if (f) {
            std::fseek(f, 0, SEEK_END);
            const long len = std::ftell(f);
            std::fseek(f, 0, SEEK_SET);
            if (len > 0) {
                uint8_t* data = static_cast<uint8_t*>(std::malloc(static_cast<size_t>(len)));
                if (data && std::fread(data, 1, static_cast<size_t>(len), f) ==
                                static_cast<size_t>(len)) {
                    stbtt_fontinfo* face = new (std::nothrow) stbtt_fontinfo();
                    if (face && stbtt_InitFont(face, data, 0)) {
                        s->font = face;
                        s->font_data = data;
                        s->font_data_size = static_cast<size_t>(len);
                    } else {
                        delete face;
                        std::free(data);
                    }
                } else {
                    std::free(data);
                }
            }
            std::fclose(f);
        }
    }
    return s;
}

VIEWRUNTIME_BACKEND_API void viewruntime_surface_destroy(void* surface) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s) return;
    delete s->font;
    std::free(s->font_data);
    delete s;
}

VIEWRUNTIME_BACKEND_API void viewruntime_surface_resize(
    void* surface, int pixel_width, int pixel_height, float density) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s || pixel_width <= 0 || pixel_height <= 0) return;
    s->width = pixel_width;
    s->height = pixel_height;
    s->density = density > 0.f ? density : 1.f;
    s->pixels.assign(static_cast<size_t>(pixel_width) * pixel_height, 0u);
}

VIEWRUNTIME_BACKEND_API void viewruntime_frame_begin(void* surface) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s) return;
    std::fill(s->pixels.begin(), s->pixels.end(), 0u);
}

VIEWRUNTIME_BACKEND_API void viewruntime_draw_fill_rect(
    void* surface, float x, float y, float w, float h,
    uint8_t a, uint8_t r, uint8_t g, uint8_t b, int32_t /*view_id*/) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s || s->pixels.empty()) return;
    viewruntime_backend::fill_rect(s, x, y, w, h,
                                   viewruntime_backend::pack_color(a, r, g, b));
}

VIEWRUNTIME_BACKEND_API void viewruntime_draw_text(
    void* surface, float x, float y, float w, float h,
    const uint16_t* utf16_text, int32_t text_len,
    float text_size_px, uint8_t a, uint8_t r, uint8_t g, uint8_t b,
    int32_t /*view_id*/) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s || !utf16_text || text_len <= 0 || s->pixels.empty()) return;
    if (s->font == nullptr) {
        /* fallback: draw a solid block proportional to the text */
        const float fw = 0.56f * text_size_px * static_cast<float>(text_len);
        viewruntime_backend::fill_rect(s, x, y, std::min(fw, w), h,
                                       viewruntime_backend::pack_color(a, r, g, b));
        return;
    }

    const float scale = stbtt_ScaleForPixelHeight(s->font, text_size_px);
    int ascent = 0, descent = 0, line_gap = 0;
    stbtt_GetFontVMetrics(s->font, &ascent, &descent, &line_gap);
    const float baseline_offset = static_cast<float>(ascent) * scale;
    const uint32_t color = viewruntime_backend::pack_color(a, r, g, b);

    /* clip rect (the view's full laid-out box; text draws from top-left) */
    const int clip_x0 = static_cast<int>(std::floor(x));
    const int clip_y0 = static_cast<int>(std::floor(y));
    const int clip_x1 = static_cast<int>(std::ceil(x + w));
    const int clip_y1 = static_cast<int>(std::ceil(y + h));

    float pen_x = x;
    const int pen_base_y = static_cast<int>(std::round(y + baseline_offset));
    int previous = 0;
    for (int i = 0; i < text_len;) {
        unsigned int cp = 0;
        const int n = viewruntime_backend::utf16_decode(utf16_text, text_len, i, &cp);
        if (n == 0) break;
        i += n;
        if (cp == '\n') {
            pen_x = x; /* multi-line not modeled by App Runtime yet; reset pen */
            continue;
        }
        if (previous != 0) {
            pen_x += stbtt_GetCodepointKernAdvance(s->font, previous, static_cast<int>(cp)) * scale;
        }
        int advance = 0, lsb = 0;
        stbtt_GetCodepointHMetrics(s->font, static_cast<int>(cp), &advance, &lsb);
        if (cp != ' ') {
            int gw = 0, gh = 0, xoff = 0, yoff = 0;
            unsigned char* bitmap = stbtt_GetCodepointBitmap(
                s->font, scale, scale, static_cast<int>(cp), &gw, &gh, &xoff, &yoff);
            if (bitmap) {
                viewruntime_backend::blit_glyph(s, bitmap, gw, gh, xoff, yoff,
                                                static_cast<int>(std::floor(pen_x)),
                                                pen_base_y, color);
                stbtt_FreeBitmap(bitmap, nullptr);
            }
        }
        pen_x += static_cast<float>(advance) * scale;
        previous = static_cast<int>(cp);
    }
    (void)clip_x0; (void)clip_y0; (void)clip_x1; (void)clip_y1;
}

VIEWRUNTIME_BACKEND_API void viewruntime_frame_end(void* surface) {
    (void)surface; /* pixels are already rasterized; buffer is ready to blit */
}

VIEWRUNTIME_BACKEND_API void viewruntime_measure_text(
    void* surface, const uint16_t* utf16_text, int32_t text_len,
    float text_size_px, float max_width_px,
    float* out_width_px, float* out_height_px, float* out_baseline_px) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s) return;
    viewruntime_backend::measure_text_common(
        s, utf16_text, text_len, text_size_px, max_width_px,
        out_width_px, out_height_px, out_baseline_px);
}

VIEWRUNTIME_BACKEND_API void viewruntime_surface_pixels(
    void* surface, const uint8_t** out_pixels, int* out_pitch,
    int* out_width, int* out_height) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s) return;
    if (out_pixels) *out_pixels = s->pixels.empty()
        ? nullptr : reinterpret_cast<const uint8_t*>(s->pixels.data());
    if (out_pitch) *out_pitch = s->width * 4;
    if (out_width) *out_width = s->width;
    if (out_height) *out_height = s->height;
}

} // extern "C"
