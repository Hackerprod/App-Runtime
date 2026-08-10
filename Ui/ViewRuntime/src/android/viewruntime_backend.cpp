/* Phase 1 render backend (see include/viewruntime/viewruntime_backend.h and
 * docs/viewruntime-integration-spec.md). Rasterizes App Runtime's flat draw
 * calls into an off-screen ARGB8888 buffer: fill rects and text glyphs
 * (stb_truetype) with clipping. Text measurement shares the same font, so
 * layout and paint agree. */

#include "../include/viewruntime/viewruntime_backend.h"
#include "../include/viewruntime/viewruntime.h" /* TEXT_ALIGN_* / FONT_WEIGHT_* */

#include "../third_party/stb_truetype.h" /* declarations only; the
                                            implementation lives in
                                            stb_text_measurer.cpp */

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <utility>
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

struct Image {
    int width = 0;
    int height = 0;
    std::vector<uint32_t> pixels; /* straight ARGB8888 */
};

struct Surface {
    int width = 0;
    int height = 0;
    float density = 1.f;
    std::vector<uint32_t> pixels; /* straight ARGB8888, row-major */

    stbtt_fontinfo* font = nullptr;
    uint8_t* font_data = nullptr;
    size_t font_data_size = 0;

    std::vector<std::pair<std::string, Image>> images; /* source -> bitmap */

    /* Clip stack: viewport rectangles applied to every draw. The top entry
     * is the intersection of all pushed rects (nested ScrollView/Frame
     * clipping); empty = no clipping (full surface). */
    std::vector<std::pair<int,int>> clip_min; /* (x0, y0) per level */
    std::vector<std::pair<int,int>> clip_max; /* (x1, y1) per level */
};

/* Clamp an integer rect to the current clip stack (or the surface bounds
 * when the stack is empty). Returns false when fully clipped out. */
static bool apply_clip(Surface* s, int x0, int y0, int x1, int y1,
                       int* ox0, int* oy0, int* ox1, int* oy1) {
    int cx0 = 0, cy0 = 0, cx1 = s->width, cy1 = s->height;
    if (!s->clip_min.empty()) {
        const auto& mn = s->clip_min.back();
        const auto& mx = s->clip_max.back();
        cx0 = mn.first; cy0 = mn.second;
        cx1 = mx.first; cy1 = mx.second;
    }
    x0 = std::max(x0, cx0); y0 = std::max(y0, cy0);
    x1 = std::min(x1, cx1); y1 = std::min(y1, cy1);
    if (x1 <= x0 || y1 <= y0) return false;
    *ox0 = x0; *oy0 = y0; *ox1 = x1; *oy1 = y1;
    return true;
}

static Image* find_image(Surface* s, const char* source) {
    for (auto& entry : s->images) {
        if (entry.first == source) return &entry.second;
    }
    return nullptr;
}

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
    int cx0, cy0, cx1, cy1;
    if (!apply_clip(s, x0, y0, x1, y1, &cx0, &cy0, &cx1, &cy1)) return;
    for (int py = cy0; py < cy1; ++py) {
        uint32_t* row = &s->pixels[static_cast<size_t>(py) * s->width];
        for (int px = cx0; px < cx1; ++px) {
            blend_pixel(&row[px], color);
        }
    }
}

/* Blit a glyph bitmap (8-bit alpha, one byte per pixel) into the rect,
 * clipped to BOTH the surface bounds and the caller's view box. AOSP clips
 * every child draw to the view's laid-out box (View.java:24905-24915
 * canvas.clipRect + FLAG_CLIP_CHILDREN, ViewGroup.java:726); without the view
 * box a long/descending run bleeds outside the view. */
static void blit_glyph(Surface* s, const unsigned char* bitmap, int gw, int gh,
                       int xoff, int yoff, int pen_x, int pen_y,
                       uint32_t color,
                       int clip_x0, int clip_y0, int clip_x1, int clip_y1) {
    const uint32_t alpha = (color >> 24) & 0xFF;
    const uint32_t rgb = color & 0x00FFFFFF;
    for (int gy = 0; gy < gh; ++gy) {
        const int py = pen_y + yoff + gy;
        if (py < clip_y0 || py >= clip_y1) continue;
        if (py < 0 || py >= s->height) continue;
        uint32_t* row = &s->pixels[static_cast<size_t>(py) * s->width];
        for (int gx = 0; gx < gw; ++gx) {
            const int px = pen_x + xoff + gx;
            if (px < clip_x0 || px >= clip_x1) continue;
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
    /* AOSP line height = descent - ascent (StaticLayout.java:1255), no
     * leading — consistent with stb_text_measurer.cpp. */
    const float line_height = static_cast<float>(ascent - descent) * scale;
    const float baseline = static_cast<float>(ascent) * scale;

    if (max_width_px <= 0.f) {
        /* AOSP getDesiredWidth (no limit): MAX width over paragraphs, '\n'
         * not measured (Layout.java:277-293), height = one line per paragraph
         * (Layout.java:230-231) — consistent with stb_text_measurer.cpp. */
        float widest = 0.f;
        int para_lines = 1;
        int seg_start = 0;
        for (int i = 0; i <= len; ++i) {
            const bool at_end = i == len;
            unsigned int cp = 0;
            if (!at_end) {
                int n = 1;
                n = utf16_decode(text, len, i, &cp);
            }
            if (at_end || cp == '\n') {
                widest = std::max(widest,
                    line_width(s->font, text + seg_start, i - seg_start, scale));
                if (cp == '\n') ++para_lines;
                seg_start = i + 1;
            }
            if (at_end) break;
        }
        if (out_w) *out_w = widest;
        if (out_h) *out_h = line_height * static_cast<float>(para_lines);
        if (out_baseline) *out_baseline = baseline;
        return;
    }

    /* word wrap — same semantics as stb_text_measurer.cpp: AOSP
     * getLineVisibleEnd strips ALL trailing whitespace from every line except
     * the last (Layout.java:2767-2793). `visible` is the line width WITHOUT
     * trailing whitespace; the last line keeps its trailing whitespace. */
    float total_width = 0.f;
    int lines = 1;
    float line_w = 0.f;
    float visible = 0.f;
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
            /* Only a REAL word (word_w > 0) can force a wrap: when the text
             * ends in trailing whitespace (word_w == 0 at cp == 0) the line
             * must NOT wrap — "ab " with max ∈ [w_ab, w_ab+w_s) is one line,
             * and line_w + 0 > max would otherwise create a phantom line.
             * Consistent with stb_text_measurer.cpp. */
            if (word_w > 0.f && line_w + word_w > max_width_px && line_w > 0.f) {
                /* Wrap: capture the previous line's visible width (no
                 * trailing whitespace) BEFORE starting the new line. */
                total_width = std::max(total_width, visible);
                ++lines;
                line_w = word_w;
                visible = word_w;
            } else {
                line_w += word_w;
                if (word_w > 0.f) visible = line_w;
            }
            if (cp == '\n') {
                /* paragraph end: capture visible width before reset */
                total_width = std::max(total_width, visible);
                ++lines;
                line_w = 0.f;
                visible = 0.f;
            } else if (cp == ' ' || cp == '\t') {
                /* Separator (or trailing) space: joins the line width but is
                 * still trailing whitespace until a non-space follows, so
                 * `visible` is unchanged. AOSP NEVER breaks the line AT a
                 * space — the break happens when the NEXT word does not fit
                 * (word branch above). Breaking here would create a phantom
                 * line for a trailing space ("ab " with max
                 * ∈ [w_ab, w_ab+w_s) must stay ONE line) and would leave a
                 * leading space on the next line. Consistent with
                 * stb_text_measurer.cpp. */
                line_w += line_width(s->font, text + i, 1, scale);
            } else if (cp == 0) {
                /* end of text — the FINAL line keeps its trailing whitespace
                 * (getLineVisibleEnd strips only non-last lines). */
                total_width = std::max(total_width, line_w);
            }
            word_start = i + n;
        }
        if (i >= len) break;
        i += n;
    }
    total_width = std::min(total_width, max_width_px);
    if (out_w) *out_w = total_width;
    if (out_h) *out_h = line_height * static_cast<float>(lines);
    if (out_baseline) *out_baseline = baseline;
}

/* Blit an uploaded image: map src rect (image pixel coords) into dst rect
 * (surface coords) with nearest-neighbor scaling, clipped to the surface. */
static void draw_image(Surface* s, const Image& img,
                       float src_x, float src_y, float src_w, float src_h,
                       float dst_x, float dst_y, float dst_w, float dst_h) {
    if (src_w <= 0.f || src_h <= 0.f || dst_w <= 0.f || dst_h <= 0.f) return;
    const int x0 = static_cast<int>(std::floor(dst_x));
    const int y0 = static_cast<int>(std::floor(dst_y));
    const int x1 = static_cast<int>(std::ceil(dst_x + dst_w));
    const int y1 = static_cast<int>(std::ceil(dst_y + dst_h));
    /* Clip to the ACTIVE clip stack (ScrollView/ListView viewports), not just
     * the surface bounds — the old code clamped only to the surface, so a
     * CENTER_CROP image inside a scrolled view bled outside the viewport.
     * AOSP clips child draws to the view box (View.java:24905-24915). */
    int cx0, cy0, cx1, cy1;
    if (!apply_clip(s, x0, y0, x1, y1, &cx0, &cy0, &cx1, &cy1)) return;

    for (int py = cy0; py < cy1; ++py) {
        const int iy = static_cast<int>((static_cast<float>(py) - dst_y) * src_h / dst_h);
        if (iy < 0 || iy >= img.height) continue;
        const uint32_t* src_row = &img.pixels[static_cast<size_t>(iy) * img.width];
        uint32_t* dst_row = &s->pixels[static_cast<size_t>(py) * s->width];
        for (int px = cx0; px < cx1; ++px) {
            const int ix = static_cast<int>((static_cast<float>(px) - dst_x) * src_w / dst_w);
            if (ix < 0 || ix >= img.width) continue;
            blend_pixel(&dst_row[px], src_row[ix]);
        }
    }
}

/* Load stb_truetype from owned bytes; returns false on failure (caller keeps
 * ownership of `data` and `face` on failure). */
static bool surface_load_font(Surface* s, uint8_t* data, size_t size) {
    if (!data || size == 0) return false;
    stbtt_fontinfo* face = new (std::nothrow) stbtt_fontinfo();
    if (!face || !stbtt_InitFont(face, data, 0)) {
        delete face;
        return false;
    }
    /* Replace any previous font. */
    delete s->font;
    std::free(s->font_data);
    s->font = face;
    s->font_data = data;
    s->font_data_size = size;
    return true;
}

} // namespace viewruntime_backend

/* ── C ABI ─────────────────────────────────────────────────────────── */

using viewruntime_backend::Image;
using viewruntime_backend::Surface;
using viewruntime_backend::pack_color;

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
                    if (!viewruntime_backend::surface_load_font(
                            s, data, static_cast<size_t>(len))) {
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

/* Install the exact bytes the UI session measures with (android_ui_set_font
 * propagates them here), so paint and measure use the same font. */
VIEWRUNTIME_BACKEND_API void viewruntime_surface_set_font(
    void* surface, const uint8_t* font_data, int32_t font_size) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s || !font_data || font_size <= 0) return;
    uint8_t* copy = static_cast<uint8_t*>(std::malloc(static_cast<size_t>(font_size)));
    if (!copy) return;
    std::memcpy(copy, font_data, static_cast<size_t>(font_size));
    if (!viewruntime_backend::surface_load_font(s, copy, static_cast<size_t>(font_size))) {
        std::free(copy);
    }
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
    s->clip_min.clear();
    s->clip_max.clear();
}

/* Push a clip rect (intersected with the current top). Rects are in surface
 * coordinates; the backend clamps to the surface bounds so an oversized or
 * negative clip can never produce out-of-bounds writes/reads (the old code
 * stored the raw rect — a clip wider than the surface escaped apply_clip's
 * clamping because apply_clip trusts the stack top as a bound). */
VIEWRUNTIME_BACKEND_API void viewruntime_clip_push(void* surface, float x, float y,
                                                   float w, float h) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s) return;
    int x0 = static_cast<int>(std::floor(x));
    int y0 = static_cast<int>(std::floor(y));
    int x1 = static_cast<int>(std::ceil(x + w));
    int y1 = static_cast<int>(std::ceil(y + h));
    if (!s->clip_min.empty()) {
        const auto& mn = s->clip_min.back();
        const auto& mx = s->clip_max.back();
        x0 = std::max(x0, mn.first); y0 = std::max(y0, mn.second);
        x1 = std::min(x1, mx.first); y1 = std::min(y1, mx.second);
    }
    /* Clamp to the surface bounds FIRST (then the intersection above is
     * always within bounds). */
    x0 = std::max(x0, 0); y0 = std::max(y0, 0);
    x1 = std::min(x1, s->width); y1 = std::min(y1, s->height);
    s->clip_min.emplace_back(x0, y0);
    s->clip_max.emplace_back(x1, y1);
}

VIEWRUNTIME_BACKEND_API void viewruntime_clip_pop(void* surface) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s || s->clip_min.empty()) return;
    s->clip_min.pop_back();
    s->clip_max.pop_back();
}

VIEWRUNTIME_BACKEND_API void viewruntime_draw_fill_rect(
    void* surface, float x, float y, float w, float h,
    uint8_t a, uint8_t r, uint8_t g, uint8_t b, int32_t /*view_id*/) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s || s->pixels.empty()) return;
    viewruntime_backend::fill_rect(s, x, y, w, h,
                                   viewruntime_backend::pack_color(a, r, g, b));
}

/* Rounded-rect fill: AOSP GradientDrawable clamps the radius to
 * min(radius, min(w,h)*0.5) so a very wide/short rect never renders a thin
 * ellipse (GradientDrawable.java:823-825, Skia clamps independently per axis
 * otherwise). Rasterization: fill the interior (the axis-aligned box inset by
 * radius) plus, per corner, only pixels whose distance from the corner center
 * is within radius (a quarter-disc). Radius 0 is a plain rect. */
VIEWRUNTIME_BACKEND_API void viewruntime_draw_fill_rounded_rect(
    void* surface, float x, float y, float w, float h,
    float radius_px, uint8_t a, uint8_t r, uint8_t g, uint8_t b,
    int32_t /*view_id*/) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s || s->pixels.empty()) return;
    if (radius_px <= 0.f) {
        viewruntime_backend::fill_rect(s, x, y, w, h,
                                       viewruntime_backend::pack_color(a, r, g, b));
        return;
    }
    const float rad = std::min(radius_px, std::min(w, h) * 0.5f);
    const uint32_t color = viewruntime_backend::pack_color(a, r, g, b);
    int x0 = static_cast<int>(std::floor(x));
    int y0 = static_cast<int>(std::floor(y));
    int x1 = static_cast<int>(std::ceil(x + w));
    int y1 = static_cast<int>(std::ceil(y + h));
    int cx0, cy0, cx1, cy1;
    if (!apply_clip(s, x0, y0, x1, y1, &cx0, &cy0, &cx1, &cy1)) return;
    const float r2 = rad * rad;
    /* Corner centers (inset by rad from the rect edges). */
    const float cxl = x + rad, cxr = x + w - rad;
    const float cyt = y + rad, cyb = y + h - rad;
    for (int py = cy0; py < cy1; ++py) {
        uint32_t* row = &s->pixels[static_cast<size_t>(py) * s->width];
        for (int px = cx0; px < cx1; ++px) {
            const float fx = static_cast<float>(px) + 0.5f;
            const float fy = static_cast<float>(py) + 0.5f;
            /* Interior: strictly inside the inset box (all four corners
             * inside their quarter-discs are covered by the box). */
            if (fx >= cxl && fx <= cxr && fy >= cyt && fy <= cyb) {
                viewruntime_backend::blend_pixel(&row[px], color);
                continue;
            }
            /* Corner regions: inside the box but outside the inset box —
             * keep pixels within radius of the corner center; the pure
             * left/right/top/bottom edge strips (between the two corner
             * spans) are always inside the rounded rect. */
            if (fx < cxl) {
                if (fy < cyt) {
                    const float dx = fx - cxl, dy = fy - cyt;
                    if (dx * dx + dy * dy <= r2) viewruntime_backend::blend_pixel(&row[px], color);
                } else if (fy > cyb) {
                    const float dx = fx - cxl, dy = fy - cyb;
                    if (dx * dx + dy * dy <= r2) viewruntime_backend::blend_pixel(&row[px], color);
                } else {
                    viewruntime_backend::blend_pixel(&row[px], color);
                }
            } else if (fx > cxr) {
                if (fy < cyt) {
                    const float dx = fx - cxr, dy = fy - cyt;
                    if (dx * dx + dy * dy <= r2) viewruntime_backend::blend_pixel(&row[px], color);
                } else if (fy > cyb) {
                    const float dx = fx - cxr, dy = fy - cyb;
                    if (dx * dx + dy * dy <= r2) viewruntime_backend::blend_pixel(&row[px], color);
                } else {
                    viewruntime_backend::blend_pixel(&row[px], color);
                }
            } else {
                /* Between the vertical corner spans: top/bottom edge strips. */
                if (fy < cyt || fy > cyb) {
                    viewruntime_backend::blend_pixel(&row[px], color);
                }
            }
        }
    }
}

/* Resolve the gradient axis endpoints for an angle, exactly like AOSP
 * GradientDrawable angle→Orientation (java:1822-1851) + the LinearGradient
 * endpoint switch (java:1304-1336). Returns false when the angle is not one
 * of the 8 canonical orientations (AOSP keeps the previous/default in that
 * case). */
static bool gradient_axis(float x, float y, float w, float h, int32_t angle,
                          float* ox0, float* oy0, float* ox1, float* oy1) {
    /* AOSP wraps the angle %360, offsetting negatives (GradientDrawable.java:
     * 1816-1817 sWrapNegativeAngleMeasurements: ((angle % 360) + 360) % 360). */
    angle = ((angle % 360) + 360) % 360;
    switch (angle) {
        case 0:   *ox0 = x;         *oy0 = y;         *ox1 = x + w;     *oy1 = y;         return true; /* LEFT_RIGHT */
        case 45:  *ox0 = x;         *oy0 = y + h;     *ox1 = x + w;     *oy1 = y;         return true; /* BL_TR */
        case 90:  *ox0 = x;         *oy0 = y + h;     *ox1 = x;         *oy1 = y;         return true; /* BOTTOM_TOP */
        case 135: *ox0 = x + w;     *oy0 = y + h;     *ox1 = x;         *oy1 = y;         return true; /* BR_TL */
        case 180: *ox0 = x + w;     *oy0 = y;         *ox1 = x;         *oy1 = y;         return true; /* RIGHT_LEFT */
        case 225: *ox0 = x + w;     *oy0 = y;         *ox1 = x;         *oy1 = y + h;     return true; /* TR_BL */
        case 270: *ox0 = x;         *oy0 = y;         *ox1 = x;         *oy1 = y + h;     return true; /* TOP_BOTTOM */
        case 315: *ox0 = x;         *oy0 = y;         *ox1 = x + w;     *oy1 = y + h;     return true; /* TL_BR */
        default:  return false;
    }
}

/* Rounded-rect fill with a linear gradient (start→end). Skia LinearGradient
 * CLAMP semantics: t = clamp(((p - p0) . d) / (d . d), 0, 1), color =
 * lerp(start, end, t). The corner radius clamps like GradientDrawable:823. */
VIEWRUNTIME_BACKEND_API void viewruntime_draw_fill_rounded_rect_gradient(
    void* surface, float x, float y, float w, float h,
    float radius_px, int32_t angle_deg,
    uint8_t a0, uint8_t r0, uint8_t g0, uint8_t b0,
    uint8_t a1, uint8_t r1, uint8_t g1, uint8_t b1, int32_t /*view_id*/) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s || s->pixels.empty()) return;
    float x0, y0, x1, y1;
    if (!gradient_axis(x, y, w, h, angle_deg, &x0, &y0, &x1, &y1)) {
        /* Unknown angle: AOSP keeps the previous/default orientation
         * (TOP_BOTTOM, java:1850). */
        gradient_axis(x, y, w, h, 270, &x0, &y0, &x1, &y1);
    }
    const float dx = x1 - x0, dy = y1 - y0;
    const float dlen2 = dx * dx + dy * dy;
    /* Pre-scale the color deltas; per-pixel t in [0,1] lerps them. */
    const float da = (static_cast<float>(a1) - a0) / 255.f;
    const float dr = (static_cast<float>(r1) - r0) / 255.f;
    const float dg = (static_cast<float>(g1) - g0) / 255.f;
    const float db = (static_cast<float>(b1) - b0) / 255.f;

    const float rad = std::min(radius_px, std::min(w, h) * 0.5f);
    int ix0 = static_cast<int>(std::floor(x));
    int iy0 = static_cast<int>(std::floor(y));
    int ix1 = static_cast<int>(std::ceil(x + w));
    int iy1 = static_cast<int>(std::ceil(y + h));
    int cx0, cy0, cx1, cy1;
    if (!apply_clip(s, ix0, iy0, ix1, iy1, &cx0, &cy0, &cx1, &cy1)) return;
    const float r2 = rad * rad;
    const float cxl = x + rad, cxr = x + w - rad;
    const float cyt = y + rad, cyb = y + h - rad;
    for (int py = cy0; py < cy1; ++py) {
        uint32_t* row = &s->pixels[static_cast<size_t>(py) * s->width];
        for (int px = cx0; px < cx1; ++px) {
            const float fx = static_cast<float>(px) + 0.5f;
            const float fy = static_cast<float>(py) + 0.5f;
            bool inside;
            if (fx >= cxl && fx <= cxr && fy >= cyt && fy <= cyb) {
                inside = true;
            } else if (fx < cxl) {
                if (fy < cyt) { const float ddx = fx - cxl, ddy = fy - cyt; inside = ddx * ddx + ddy * ddy <= r2; }
                else if (fy > cyb) { const float ddx = fx - cxl, ddy = fy - cyb; inside = ddx * ddx + ddy * ddy <= r2; }
                else inside = true;
            } else if (fx > cxr) {
                if (fy < cyt) { const float ddx = fx - cxr, ddy = fy - cyt; inside = ddx * ddx + ddy * ddy <= r2; }
                else if (fy > cyb) { const float ddx = fx - cxr, ddy = fy - cyb; inside = ddx * ddx + ddy * ddy <= r2; }
                else inside = true;
            } else {
                inside = (fy < cyt || fy > cyb);
            }
            if (!inside) continue;
            /* Skia LinearGradient CLAMP: t = clamp(projection, 0, 1). A
             * degenerate (zero-length) gradient yields the END color under
             * CLAMP (SkLinearGradient.cpp:96-103 MakeDegenerateGradient +
             * SkGradientBaseShader.cpp:1117-1120) — not the start color. */
            float t;
            if (dlen2 <= 0.f) {
                t = 1.f;
            } else {
                t = ((fx - x0) * dx + (fy - y0) * dy) / dlen2;
                if (t < 0.f) t = 0.f;
                else if (t > 1.f) t = 1.f;
            }
            const uint32_t src =
                (static_cast<uint32_t>((a0 + da * t * 255.f) + 0.5f) << 24) |
                (static_cast<uint32_t>((r0 + dr * t * 255.f) + 0.5f) << 16) |
                (static_cast<uint32_t>((g0 + dg * t * 255.f) + 0.5f) << 8) |
                static_cast<uint32_t>((b0 + db * t * 255.f) + 0.5f);
            viewruntime_backend::blend_pixel(&row[px], src);
        }
    }
}

/* Rounded-rect stroke: AOSP GradientDrawable strokes the SAME rect as the
 * fill with the same corner radius (drawRoundRect(mRect, rad, rad,
 * mStrokePaint), GradientDrawable.java:825-827). The border is the band
 * within width_px of the rounded outline. dash_width_px/gap: when
 * dash_width_px > 0 the border is drawn in dashes of dash_width_px separated
 * by dash_gap_px along the outline (mStrokePaint.setPathEffect, java:417);
 * solid otherwise. */
VIEWRUNTIME_BACKEND_API void viewruntime_draw_stroke_rounded_rect(
    void* surface, float x, float y, float w, float h,
    float radius_px, float width_px, float dash_width_px, float dash_gap_px,
    uint8_t a, uint8_t r, uint8_t g, uint8_t b, int32_t /*view_id*/) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s || s->pixels.empty() || width_px <= 0.f) return;
    const uint32_t color = viewruntime_backend::pack_color(a, r, g, b);
    /* AOSP insets mRect by strokeWidth*0.5 for the CENTERLINE (java:1281)
     * and strokes it with strokeWidth — the band is [centerline - w/2,
     * centerline + w/2]. The corner radius clamps against the CENTERLINE
     * dims (java:823-824 over mRect), then the band arcs are radius
     * rad_c ± w/2 around the centerline corner centers (Skia offset). */
    const float half = width_px * 0.5f;
    const float cxl = x + half, cyt = y + half;
    const float cw = w - width_px, ch = h - width_px;
    if (cw <= 0.f || ch <= 0.f) return;
    const float rad_c = std::min(radius_px, std::min(cw, ch) * 0.5f);
    const float r_out = rad_c + half;                 /* exterior arc radius */
    const float r_in = std::max(0.f, rad_c - half);   /* interior arc radius */
    const float r2o = r_out * r_out;
    const float r2i = r_in * r_in;
    /* Corner centers of the centerline rounded rect. */
    const float ccl = cxl + rad_c, ccr = cxl + cw - rad_c;
    const float cct = cyt + rad_c, ccb = cyt + ch - rad_c;

    int x0 = static_cast<int>(std::floor(x));
    int y0 = static_cast<int>(std::floor(y));
    int x1 = static_cast<int>(std::ceil(x + w));
    int y1 = static_cast<int>(std::ceil(y + h));
    int cx0, cy0, cx1, cy1;
    if (!apply_clip(s, x0, y0, x1, y1, &cx0, &cy0, &cx1, &cy1)) return;

    for (int py = cy0; py < cy1; ++py) {
        const float fy = static_cast<float>(py) + 0.5f;
        uint32_t* row = &s->pixels[static_cast<size_t>(py) * s->width];
        for (int px = cx0; px < cx1; ++px) {
            const float fx = static_cast<float>(px) + 0.5f;
            /* Inside the EXTERIOR arc (centerline corners, radius r_out)? */
            bool inside_ext;
            if (fx >= ccl && fx <= ccr && fy >= cct && fy <= ccb) {
                inside_ext = true;
            } else if (fx < ccl) {
                if (fy < cct) { const float ddx = fx - ccl, ddy = fy - cct; inside_ext = ddx*ddx + ddy*ddy <= r2o; }
                else if (fy > ccb) { const float ddx = fx - ccl, ddy = fy - ccb; inside_ext = ddx*ddx + ddy*ddy <= r2o; }
                else inside_ext = true;
            } else if (fx > ccr) {
                if (fy < cct) { const float ddx = fx - ccr, ddy = fy - cct; inside_ext = ddx*ddx + ddy*ddy <= r2o; }
                else if (fy > ccb) { const float ddx = fx - ccr, ddy = fy - ccb; inside_ext = ddx*ddx + ddy*ddy <= r2o; }
                else inside_ext = true;
            } else {
                inside_ext = (fy < cct || fy > ccb);
            }
            if (!inside_ext) continue;
            /* Inside the INTERIOR of the stroke band? The band interior is
             * the rounded rect at the centerline reduced by half (the stroke
             * offset), i.e. the rect inset by the FULL stroke width with
             * corner radius r_in, sharing the centerline corner centers
             * (ccl,cct) — the Skia stroke of a rounded path. A point
             * left/right/above/below the corner centers is OUTSIDE (except
             * within the corner arcs). */
            const float bx0 = cxl + half, by0 = cyt + half;
            const float bx1 = cxl + cw - half, by1 = cyt + ch - half;
            bool inside_int;
            if (fx >= bx0 && fx <= bx1 && fy >= by0 && fy <= by1) {
                inside_int = true;
            } else if (fx < ccl) {
                if (fy < cct) { const float ddx = fx - ccl, ddy = fy - cct; inside_int = ddx*ddx + ddy*ddy <= r2i; }
                else if (fy > ccb) { const float ddx = fx - ccl, ddy = fy - ccb; inside_int = ddx*ddx + ddy*ddy <= r2i; }
                else inside_int = false;
            } else if (fx > ccr) {
                if (fy < cct) { const float ddx = fx - ccr, ddy = fy - cct; inside_int = ddx*ddx + ddy*ddy <= r2i; }
                else if (fy > ccb) { const float ddx = fx - ccr, ddy = fy - ccb; inside_int = ddx*ddx + ddy*ddy <= r2i; }
                else inside_int = false;
            } else {
                /* Between the vertical corner spans: inside the band interior
                 * only when also between the horizontal interior edges. */
                inside_int = (fy >= by0 && fy <= by1);
            }
            if (inside_int) continue;
            viewruntime_backend::blend_pixel(&row[px], color);
        }
    }
}

/* OVAL fill: the ellipse inscribed in the box (GradientDrawable OVAL,
 * java:839-840 drawOval). A pixel is inside iff the normalized coordinates
 * satisfy ((fx-cx)/rx)² + ((fy-cy)/ry)² <= 1. */
VIEWRUNTIME_BACKEND_API void viewruntime_draw_fill_oval(
    void* surface, float x, float y, float w, float h,
    uint8_t a, uint8_t r, uint8_t g, uint8_t b, int32_t /*view_id*/) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s || s->pixels.empty() || w <= 0.f || h <= 0.f) return;
    const uint32_t color = viewruntime_backend::pack_color(a, r, g, b);
    const float rx = w * 0.5f, ry = h * 0.5f;
    const float cx = x + rx, cy = y + ry;
    const float rxx = rx * rx, ryy = ry * ry;
    int x0 = static_cast<int>(std::floor(x));
    int y0 = static_cast<int>(std::floor(y));
    int x1 = static_cast<int>(std::ceil(x + w));
    int y1 = static_cast<int>(std::ceil(y + h));
    int cx0, cy0, cx1, cy1;
    if (!apply_clip(s, x0, y0, x1, y1, &cx0, &cy0, &cx1, &cy1)) return;
    for (int py = cy0; py < cy1; ++py) {
        const float fy = static_cast<float>(py) + 0.5f - cy;
        uint32_t* row = &s->pixels[static_cast<size_t>(py) * s->width];
        for (int px = cx0; px < cx1; ++px) {
            const float fx = static_cast<float>(px) + 0.5f - cx;
            if ((fx * fx) / rxx + (fy * fy) / ryy <= 1.f)
                viewruntime_backend::blend_pixel(&row[px], color);
        }
    }
}

/* OVAL fill with a LINEAR gradient — GradientDrawable OVAL shape drawn with
 * the LinearGradient shader (java:840 drawOval(mRect, mFillPaint)). The
 * ellipse membership test is ((fx-cx)/rx)² + ((fy-cy)/ry)² <= 1 — a REAL
 * ellipse (a rounded rect with radius min(w,h)/2 would be a stadium for
 * w != h). The gradient axis/lerp follow the same Skia CLAMP semantics as
 * viewruntime_draw_fill_rounded_rect_gradient. */
VIEWRUNTIME_BACKEND_API void viewruntime_draw_fill_oval_gradient(
    void* surface, float x, float y, float w, float h,
    int32_t angle_deg,
    uint8_t a0, uint8_t r0, uint8_t g0, uint8_t b0,
    uint8_t a1, uint8_t r1, uint8_t g1, uint8_t b1, int32_t /*view_id*/) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s || s->pixels.empty() || w <= 0.f || h <= 0.f) return;
    float x0, y0, x1, y1;
    if (!gradient_axis(x, y, w, h, angle_deg, &x0, &y0, &x1, &y1)) {
        /* Unknown angle: AOSP keeps the previous/default orientation
         * (TOP_BOTTOM, java:1850). */
        gradient_axis(x, y, w, h, 270, &x0, &y0, &x1, &y1);
    }
    const float dx = x1 - x0, dy = y1 - y0;
    const float dlen2 = dx * dx + dy * dy;
    const float da = (static_cast<float>(a1) - a0) / 255.f;
    const float dr = (static_cast<float>(r1) - r0) / 255.f;
    const float dg = (static_cast<float>(g1) - g0) / 255.f;
    const float db = (static_cast<float>(b1) - b0) / 255.f;
    const float rx = w * 0.5f, ry = h * 0.5f;
    const float cx = x + rx, cy = y + ry;
    const float rxx = rx * rx, ryy = ry * ry;
    int x0i = static_cast<int>(std::floor(x));
    int y0i = static_cast<int>(std::floor(y));
    int x1i = static_cast<int>(std::ceil(x + w));
    int y1i = static_cast<int>(std::ceil(y + h));
    int cx0, cy0, cx1, cy1;
    if (!apply_clip(s, x0i, y0i, x1i, y1i, &cx0, &cy0, &cx1, &cy1)) return;
    for (int py = cy0; py < cy1; ++py) {
        const float fy = static_cast<float>(py) + 0.5f;
        const float ely = fy - cy;
        uint32_t* row = &s->pixels[static_cast<size_t>(py) * s->width];
        for (int px = cx0; px < cx1; ++px) {
            const float fx = static_cast<float>(px) + 0.5f;
            const float elx = fx - cx;
            if ((elx * elx) / rxx + (ely * ely) / ryy > 1.f) continue;
            /* Skia LinearGradient CLAMP: t = clamp(projection, 0, 1); a
             * degenerate (zero-length) gradient yields the END color. */
            float t;
            if (dlen2 <= 0.f) {
                t = 1.f;
            } else {
                t = ((fx - x0) * dx + (fy - y0) * dy) / dlen2;
                if (t < 0.f) t = 0.f;
                else if (t > 1.f) t = 1.f;
            }
            const uint32_t src =
                (static_cast<uint32_t>((a0 + da * t * 255.f) + 0.5f) << 24) |
                (static_cast<uint32_t>((r0 + dr * t * 255.f) + 0.5f) << 16) |
                (static_cast<uint32_t>((g0 + dg * t * 255.f) + 0.5f) << 8) |
                static_cast<uint32_t>((b0 + db * t * 255.f) + 0.5f);
            viewruntime_backend::blend_pixel(&row[px], src);
        }
    }
}

/* LINE shape: a horizontal line at the vertical center, stroke width thick.
 * AOSP insets mRect by strokeWidth/2 (java:1281) so the line spans
 * [left + w/2, right - w/2] at centerY (java:845-851 drawLine). */
VIEWRUNTIME_BACKEND_API void viewruntime_draw_fill_line(
    void* surface, float x, float y, float w, float h,
    float width_px, uint8_t a, uint8_t r, uint8_t g, uint8_t b,
    int32_t /*view_id*/) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s || s->pixels.empty() || w <= 0.f || width_px <= 0.f) return;
    const float cy = y + h * 0.5f;
    const float half = width_px * 0.5f;
    const float lx = x + half, rw = w - width_px;
    if (rw <= 0.f) return;
    viewruntime_backend::fill_rect(s, lx, cy - half, rw, width_px,
                                   viewruntime_backend::pack_color(a, r, g, b));
}

VIEWRUNTIME_BACKEND_API void viewruntime_draw_text(
    void* surface, float x, float y, float w, float h,
    const uint16_t* utf16_text, int32_t text_len,
    float text_size_px, uint8_t a, uint8_t r, uint8_t g, uint8_t b,
    int32_t /*view_id*/, int32_t text_align, int32_t bold, int32_t wrap) {
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
    const float line_height = static_cast<float>(ascent - descent) * scale;
    const uint32_t color = viewruntime_backend::pack_color(a, r, g, b);

    /* Clip rect = the view's full laid-out box. AOSP clips every child draw
     * to it (View.java:24905-24915 canvas.clipRect + FLAG_CLIP_CHILDREN,
     * ViewGroup.java:726) — without it long/descending text bleeds out of the
     * view. blit_glyph enforces these bounds per glyph. */
    const int clip_x0 = static_cast<int>(std::floor(x));
    const int clip_y0 = static_cast<int>(std::floor(y));
    const int clip_x1 = static_cast<int>(std::ceil(x + w));
    const int clip_y1 = static_cast<int>(std::ceil(y + h));

    /* Width of a run with kerning — shared by alignment and the wrap pass
     * (same advances the draw loop uses). */
    auto run_w = [&](int start, int end) -> float {
        float p = 0.f;
        int prv = 0;
        for (int i = start; i < end;) {
            unsigned int cp = 0;
            const int n = viewruntime_backend::utf16_decode(utf16_text, end, i, &cp);
            if (n == 0) break;
            i += n;
            if (prv != 0)
                p += stbtt_GetCodepointKernAdvance(s->font, prv, static_cast<int>(cp)) * scale;
            int adv = 0, lsb = 0;
            stbtt_GetCodepointHMetrics(s->font, static_cast<int>(cp), &adv, &lsb);
            p += static_cast<float>(adv) * scale;
            prv = static_cast<int>(cp);
        }
        return p;
    };

    /* Draw [start, end) at pen_x/pen_y: kerning between glyphs, fake bold
     * +1px (TextView.java:2551), clipped to the view box. */
    auto draw_run = [&](int start, int end, float pen_x, float pen_y) {
        int previous = 0;
        for (int i = start; i < end;) {
            unsigned int cp = 0;
            const int n = viewruntime_backend::utf16_decode(utf16_text, end, i, &cp);
            if (n == 0) break;
            i += n;
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
                                                    static_cast<int>(pen_y), color,
                                                    clip_x0, clip_y0, clip_x1, clip_y1);
                    if (bold) {
                        viewruntime_backend::blit_glyph(s, bitmap, gw, gh, xoff, yoff,
                                                        static_cast<int>(std::floor(pen_x)) + 1,
                                                        static_cast<int>(pen_y), color,
                                                        clip_x0, clip_y0, clip_x1, clip_y1);
                    }
                    stbtt_FreeBitmap(bitmap, nullptr);
                }
            }
            pen_x += static_cast<float>(advance) * scale;
            previous = static_cast<int>(cp);
        }
    };

    /* Draw one logical line [start, end) with the per-line alignment AOSP
     * Layout.draw applies (Layout.java:1209-1211, getLineLeft). */
    auto draw_line = [&](int start, int end, int line_index) {
        if (start >= end) return;
        const float pen_y = y + baseline_offset + static_cast<float>(line_index) * line_height;
        float pen_x = x;
        if (text_align == TEXT_ALIGN_CENTER) {
            pen_x = x + std::max(0.f, (w - run_w(start, end)) * 0.5f);
        } else if (text_align == TEXT_ALIGN_END || text_align == TEXT_ALIGN_RIGHT) {
            pen_x = x + std::max(0.f, w - run_w(start, end));
        }
        draw_run(start, end, pen_x, pen_y);
    };

    if (wrap == 0) {
        /* single line: the whole run on line 0 */
        draw_line(0, text_len, 0);
        return;
    }

    /* Multi-line draw — SAME word-wrap partition as measure_text_common:
     * break at spaces so each line fits within w; '\n' forces a paragraph
     * break. AOSP draws line by line with per-line alignment (Layout.java:
     * 926-936, 1209-1211); the previous code reset pen_x on '\n' without
     * advancing the baseline, collapsing multi-line text into one overflowing
     * line. */
    float line_w = 0.f;
    int line_start = 0;
    int word_start = 0;
    int line_visible_end = 0; /* just past the last NON-whitespace glyph of
                                 the current line (getLineVisibleEnd,
                                 Layout.java:2767-2793) — non-last lines must
                                 draw/align WITHOUT trailing whitespace, while
                                 the LAST line keeps it. */
    int line_index = 0;
    for (int i = 0; i <= text_len;) {
        unsigned int cp = 0;
        int n = 1;
        if (i < text_len) {
            n = viewruntime_backend::utf16_decode(utf16_text, text_len, i, &cp);
        } else {
            cp = 0; /* end of text: flush the last word */
        }
        const bool is_break = cp == 0 || cp == ' ' || cp == '\t' || cp == '\n';
        if (is_break) {
            const float word_w = viewruntime_backend::line_width(
                s->font, utf16_text + word_start, i - word_start, scale);
            if (word_w > 0.f && line_w + word_w > w && line_w > 0.f) {
                /* wrap before this word: draw the finished line WITHOUT its
                 * trailing whitespace (getLineVisibleEnd) */
                draw_line(line_start, line_visible_end, line_index);
                ++line_index;
                line_start = word_start;
                line_w = word_w;
                line_visible_end = i; /* this word becomes the new line's content */
            } else {
                line_w += word_w;
                if (word_w > 0.f) line_visible_end = i;
            }
            if (cp == '\n') {
                /* paragraph end: draw the line (visible end), start the next */
                draw_line(line_start, line_visible_end, line_index);
                ++line_index;
                line_start = i + n;
                line_w = 0.f;
                line_visible_end = line_start;
            } else if (cp == ' ' || cp == '\t') {
                line_w += viewruntime_backend::line_width(s->font, utf16_text + i, 1, scale);
                /* trailing whitespace: line_visible_end stays unchanged */
            }
            word_start = i + n;
        }
        if (i >= text_len) break;
        i += n;
    }
    /* last line (keeps trailing whitespace) */
    draw_line(line_start, text_len, line_index);
}

VIEWRUNTIME_BACKEND_API void viewruntime_surface_set_image(
    void* surface, const char* source, int width, int height,
    const uint8_t* argb_pixels) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s || !source || !argb_pixels || width <= 0 || height <= 0) return;
    Image img;
    img.width = width;
    img.height = height;
    img.pixels.resize(static_cast<size_t>(width) * height);
    /* Incoming bytes are ARGB8888 (A,R,G,B in memory); the surface stores
     * packed (a<<24)|(r<<16)|(g<<8)|b, so convert per pixel instead of
     * memcpy (which would byte-swap on little-endian hosts). */
    for (size_t i = 0; i < img.pixels.size(); ++i) {
        const uint8_t* p = argb_pixels + i * 4;
        img.pixels[i] = pack_color(p[0], p[1], p[2], p[3]);
    }
    for (auto& entry : s->images) {
        if (entry.first == source) {
            entry.second = std::move(img);
            return;
        }
    }
    s->images.emplace_back(std::string(source), std::move(img));
}

VIEWRUNTIME_BACKEND_API void viewruntime_draw_image(
    void* surface, const char* source,
    float src_x, float src_y, float src_w, float src_h,
    float dst_x, float dst_y, float dst_w, float dst_h,
    int32_t /*view_id*/) {
    Surface* s = static_cast<Surface*>(surface);
    if (!s || !source || s->pixels.empty()) return;
    const viewruntime_backend::Image* img = viewruntime_backend::find_image(s, source);
    if (img == nullptr) return;
    viewruntime_backend::draw_image(s, *img, src_x, src_y, src_w, src_h,
                                    dst_x, dst_y, dst_w, dst_h);
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
