/* Phase 1 render backend tests: rasterization of fill rects and text into
 * the ARGB8888 buffer, clip behavior, and measure/draw font consistency. */

#include "android_test_util.h"

#include <viewruntime/viewruntime_backend.h>

#include <algorithm>
#include <cstdint>
#include <vector>

static const char* find_system_font() {
    static const char* candidates[] = {
        "C:\\Windows\\Fonts\\segoeui.ttf",
        "C:\\Windows\\Fonts\\arial.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
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

static void test_fill_rect_pixels() {
    void* surface = viewruntime_surface_create(nullptr);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 10, 10, 2.f);
    viewruntime_frame_begin(surface);

    /* solid red rect covering the whole buffer */
    viewruntime_draw_fill_rect(surface, 0.f, 0.f, 10.f, 10.f, 255, 255, 0, 0, 1);

    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    EXPECT(w == 10 && h == 10 && pitch == 40);
    /* Memory layout is little-endian BGRA (b,g,r,a bytes) — the WPF Bgra32
     * format App Runtime blits. Red (A=255,R=255,G=0,B=0): b=0,g=0,r=255,a=255. */
    EXPECT(px[0] == 0 && px[1] == 0 && px[2] == 255 && px[3] == 255);
    /* pixel at (9,9) also red */
    const uint8_t* last = px + 9 * pitch + 9 * 4;
    EXPECT(last[0] == 0 && last[1] == 0 && last[2] == 255 && last[3] == 255);

    /* a rect drawn later over it wins (paint order): blue (A=255,B=255) */
    viewruntime_draw_fill_rect(surface, 2.f, 2.f, 3.f, 3.f, 255, 0, 0, 255, 2);
    const uint8_t* over = px + 3 * pitch + 3 * 4;
    EXPECT(over[0] == 255 && over[1] == 0 && over[2] == 0 && over[3] == 255);

    /* outside the surface is clipped */
    viewruntime_draw_fill_rect(surface, -5.f, -5.f, 3.f, 3.f, 255, 255, 255, 255, 3);
    EXPECT(px[0] == 0 && px[1] == 0 && px[2] == 255 && px[3] == 255); /* unchanged */

    viewruntime_frame_end(surface);
    viewruntime_surface_destroy(surface);
}

static void test_draw_text_paints_pixels() {
    const char* font_path = find_system_font();
    void* surface = viewruntime_surface_create(font_path);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 200, 100, 2.f);

    /* transparent background, then black text */
    viewruntime_frame_begin(surface);
    const uint16_t text[] = {'H', 'e', 'l', 'l', 'o'};
    viewruntime_draw_text(surface, 10.f, 10.f, 180.f, 40.f, text, 5, 28.f,
                          255, 0, 0, 0, 7, TEXT_ALIGN_LEFT, 0, 0);
    viewruntime_frame_end(surface);

    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);

    /* some pixels in the text box must have been painted (not all zero) */
    int painted = 0;
    for (int y = 10; y < 50; ++y) {
        for (int x = 10; x < 60; ++x) {
            if (px[y * pitch + x * 4 + 3] != 0) painted++;
        }
    }
    EXPECT(painted > 0);

    /* pixels far from the text stay transparent */
    EXPECT(px[99 * pitch + 190 * 4 + 3] == 0);

    viewruntime_surface_destroy(surface);
}

static void test_measure_draw_consistency() {
    const char* font_path = find_system_font();
    void* surface = viewruntime_surface_create(font_path);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 400, 100, 1.f);

    const uint16_t text[] = {'A', 'B', 'C'};
    float w10 = 0.f, h10 = 0.f, b10 = 0.f;
    viewruntime_measure_text(surface, text, 3, 10.f, 0.f, &w10, &h10, &b10);
    float w20 = 0.f, h20 = 0.f, b20 = 0.f;
    viewruntime_measure_text(surface, text, 3, 20.f, 0.f, &w20, &h20, &b20);

    EXPECT(w10 > 0.f);
    EXPECT(h10 > 0.f && b10 > 0.f && b10 < h10);
    /* doubling size doubles the width (linear font scale) */
    EXPECT_NEAR(w20, w10 * 2.f, 0.5f);

    viewruntime_surface_destroy(surface);
}

static void test_resize_clears() {
    void* surface = viewruntime_surface_create(nullptr);
    viewruntime_surface_resize(surface, 4, 4, 1.f);
    viewruntime_frame_begin(surface);
    viewruntime_draw_fill_rect(surface, 0.f, 0.f, 4.f, 4.f, 255, 0, 0, 255, 1);
    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    EXPECT(px[3] == 255); /* blue pixel present */

    /* resize resets the buffer to transparent */
    viewruntime_surface_resize(surface, 8, 8, 1.f);
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    EXPECT(w == 8 && h == 8);
    EXPECT(px[3] == 0);

    viewruntime_surface_destroy(surface);
}

/* Upload a 2x2 image (red top-left, green top-right, blue bottom-left,
 * white bottom-right) and draw it scaled into a 4x4 destination. */
static void test_draw_image_scaled() {
    void* surface = viewruntime_surface_create(nullptr);
    viewruntime_surface_resize(surface, 8, 8, 1.f);

    const uint8_t argb[16] = {
        255, 255, 0, 0,    /* A,R,G,B: red */
        255, 0, 255, 0,    /* green */
        255, 0, 0, 255,    /* blue */
        255, 255, 255, 255 /* white */
    };
    viewruntime_surface_set_image(surface, "img2x2", 2, 2, argb);

    viewruntime_frame_begin(surface);
    /* full image mapped into (1,1,4x4): 2x scale, nearest neighbor */
    viewruntime_draw_image(surface, "img2x2", 0.f, 0.f, 2.f, 2.f,
                           1.f, 1.f, 4.f, 4.f, 5);

    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);

    /* dst pixel at (1,1) = src (0,0) = red: b=0,g=0,r=255,a=255 */
    std::fprintf(stderr, "IMG: p(1,1)=%d,%d,%d,%d p(3,3)=%d,%d,%d,%d\n",
                 px[1 * pitch + 1 * 4 + 0], px[1 * pitch + 1 * 4 + 1],
                 px[1 * pitch + 1 * 4 + 2], px[1 * pitch + 1 * 4 + 3],
                 px[3 * pitch + 3 * 4 + 0], px[3 * pitch + 3 * 4 + 1],
                 px[3 * pitch + 3 * 4 + 2], px[3 * pitch + 3 * 4 + 3]);
    const uint8_t* p00 = px + 1 * pitch + 1 * 4;
    EXPECT(p00[0] == 0 && p00[1] == 0 && p00[2] == 255 && p00[3] == 255);
    /* dst pixel at (3,3) = src (1,1) = white (2x scale from dst (1,1)) */
    const uint8_t* p11 = px + 3 * pitch + 3 * 4;
    EXPECT(p11[0] == 255 && p11[1] == 255 && p11[2] == 255 && p11[3] == 255);
    /* outside the destination stays transparent */
    EXPECT(px[7 * pitch + 7 * 4 + 3] == 0);

    viewruntime_surface_destroy(surface);
}

/* Source-rect cropping: only the top-left half of the image is drawn. */
static void test_draw_image_cropped() {
    void* surface = viewruntime_surface_create(nullptr);
    viewruntime_surface_resize(surface, 8, 8, 1.f);

    const uint8_t argb[16] = {
        255, 255, 0, 0,
        255, 0, 255, 0,
        255, 0, 0, 255,
        255, 255, 255, 255
    };
    viewruntime_surface_set_image(surface, "img2x2", 2, 2, argb);

    viewruntime_frame_begin(surface);
    /* src (0,0,1x1) = just the red pixel -> 2x2 dst at (0,0) */
    viewruntime_draw_image(surface, "img2x2", 0.f, 0.f, 1.f, 1.f,
                           0.f, 0.f, 2.f, 2.f, 6);

    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    const uint8_t* p = px + 1 * pitch + 1 * 4;
    EXPECT(p[0] == 0 && p[1] == 0 && p[2] == 255 && p[3] == 255); /* red */
    /* the green half was cropped away */
    const uint8_t* g = px + 1 * pitch + 3 * 4;
    EXPECT(g[3] == 0);
    viewruntime_surface_destroy(surface);
}

static void test_draw_text_alignment_and_bold() {
    const char* font_path = find_system_font();
    if (!font_path) return; /* no font available: skip silently */

    /* Wide empty box; "AB" is narrow. LEFT draws near x=0, CENTER draws in
     * the middle — the x-range of painted pixels must shift right. */
    const uint16_t text[] = {'A', 'B'};

    void* surface = viewruntime_surface_create(font_path);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 300, 60, 1.f);

    int left_min = 999, left_max = -1;
    {
        viewruntime_frame_begin(surface);
        viewruntime_draw_text(surface, 10.f, 10.f, 280.f, 40.f, text, 2, 24.f,
                              255, 0, 0, 0, 1, TEXT_ALIGN_LEFT, 0, 0);
        viewruntime_frame_end(surface);
        const uint8_t* px = nullptr;
        int pitch = 0, w = 0, h = 0;
        viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
        for (int y = 10; y < 50; ++y)
            for (int x = 10; x < 290; ++x)
                if (px[y * pitch + x * 4 + 3] != 0) {
                    left_min = std::min(left_min, x);
                    left_max = std::max(left_max, x);
                }
    }

    int center_min = 999, center_max = -1;
    int center_painted = 0;
    {
        viewruntime_frame_begin(surface);
        viewruntime_draw_text(surface, 10.f, 10.f, 280.f, 40.f, text, 2, 24.f,
                              255, 0, 0, 0, 1, TEXT_ALIGN_CENTER, 0, 0);
        viewruntime_frame_end(surface);
        const uint8_t* px = nullptr;
        int pitch = 0, w = 0, h = 0;
        viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
        for (int y = 10; y < 50; ++y)
            for (int x = 10; x < 290; ++x)
                if (px[y * pitch + x * 4 + 3] != 0) {
                    center_min = std::min(center_min, x);
                    center_max = std::max(center_max, x);
                    center_painted++;
                }
    }

    /* CENTER must shift the run right of the LEFT position (Layout.java:1209). */
    EXPECT(center_min > left_min);
    /* and it must not hug the left edge: in a 280px box with a ~24px run the
     * centered start is roughly (280 - run_w)/2, far from x=10. */
    EXPECT(center_min > 40);

    /* Bold renders a second +1px pass: strictly more painted pixels. */
    int bold_painted = 0;
    {
        viewruntime_frame_begin(surface);
        viewruntime_draw_text(surface, 10.f, 10.f, 280.f, 40.f, text, 2, 24.f,
                              255, 0, 0, 0, 1, TEXT_ALIGN_LEFT, 1, 0);
        viewruntime_frame_end(surface);
        const uint8_t* px = nullptr;
        int pitch = 0, w = 0, h = 0;
        viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
        for (int y = 10; y < 50; ++y)
            for (int x = 10; x < 290; ++x)
                if (px[y * pitch + x * 4 + 3] != 0) bold_painted++;
    }
    EXPECT(bold_painted > center_painted);

    viewruntime_surface_destroy(surface);
}

/* Rounded rect: AOSP GradientDrawable clamps radius to min(r, min(w,h)/2)
 * (GradientDrawable.java:823-825). A 20x20 box with radius 5 must leave the
 * 4 corner pixels transparent while a plain rect fills them. */
static void test_draw_rounded_rect_corners() {
    void* surface = viewruntime_surface_create(nullptr);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 24, 24, 1.f);

    viewruntime_frame_begin(surface);
    viewruntime_draw_fill_rounded_rect(surface, 2.f, 2.f, 20.f, 20.f, 5.f,
                                       255, 255, 0, 0, 1);
    viewruntime_frame_end(surface);
    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    auto alpha_at = [&](int x, int y) -> int {
        return px[y * pitch + x * 4 + 3];
    };
    /* Corner pixel (2,2): distance sqrt(2) from corner center (7,7) with
     * radius 5 — inside, but (2,2) is at the very corner: distance from
     * (7,7) is sqrt(25+25)=7.07 > 5 → transparent. */
    EXPECT(alpha_at(2, 2) == 0);
    EXPECT(alpha_at(2, 21) == 0);
    EXPECT(alpha_at(21, 2) == 0);
    EXPECT(alpha_at(21, 21) == 0);
    /* Center is painted. */
    EXPECT(alpha_at(12, 12) == 255);
    /* Edge midpoints are painted (not corners). */
    EXPECT(alpha_at(2, 12) == 255);  /* left edge, middle */
    EXPECT(alpha_at(12, 2) == 255);  /* top edge, middle */

    /* Radius clamp: 20x20 with radius 100 → effectively 10 (min(w,h)/2). */
    viewruntime_frame_begin(surface);
    viewruntime_draw_fill_rounded_rect(surface, 2.f, 2.f, 20.f, 20.f, 100.f,
                                       255, 0, 0, 255, 2);
    viewruntime_frame_end(surface);
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    /* With clamped radius 10 the corner (2,2) is distance 11.3 from center
     * (12,12) > 10 → transparent. */
    EXPECT(alpha_at(2, 2) == 0);
    EXPECT(alpha_at(12, 12) == 255);

    viewruntime_surface_destroy(surface);
}

/* ScrollView clipping: a push_clip must restrict every later draw to the
 * clip rect — pixels outside stay transparent even when a fill covers them;
 * pop restores full drawing. This is the ScrollView viewport (RuntimeApiLab
 * gap #4: without it all 6 cards paint unclipped). */
static void test_clip_restricts_draws() {
    void* surface = viewruntime_surface_create(nullptr);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 40, 40, 1.f);

    viewruntime_frame_begin(surface);
    /* Clip to (10,10)-(30,30), then paint a full-surface rect. */
    viewruntime_clip_push(surface, 10.f, 10.f, 20.f, 20.f);
    viewruntime_draw_fill_rect(surface, 0.f, 0.f, 40.f, 40.f, 255, 255, 0, 0, 1);
    viewruntime_clip_pop(surface);
    viewruntime_frame_end(surface);

    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    auto alpha_at = [&](int x, int y) -> int {
        return px[y * pitch + x * 4 + 3];
    };
    /* Inside the clip rect: painted. */
    EXPECT(alpha_at(15, 15) == 255);
    EXPECT(alpha_at(20, 20) == 255);
    /* Outside the clip rect: transparent (would be 255 without clip). */
    EXPECT(alpha_at(5, 5) == 0);
    EXPECT(alpha_at(35, 5) == 0);
    EXPECT(alpha_at(5, 35) == 0);
    EXPECT(alpha_at(35, 35) == 0);
    /* Corner of the clip rect: painted (>= x0, < x1). */
    EXPECT(alpha_at(10, 10) == 255);
    EXPECT(alpha_at(29, 29) == 255);

    viewruntime_surface_destroy(surface);
}

/* Regression: frame_begin must clear the clip stack — a clip pushed in one
 * frame must not leak into the next (this is what made the flaky run look
 * like "clip ignored": a leftover clip would clamp the next frame's fills,
 * or an empty-surface clip could misbehave). */
static void test_frame_begin_clears_clip() {
    void* surface = viewruntime_surface_create(nullptr);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 40, 40, 1.f);

    /* Frame 1: push a clip, draw inside it, pop. */
    viewruntime_frame_begin(surface);
    viewruntime_clip_push(surface, 10.f, 10.f, 20.f, 20.f);
    viewruntime_draw_fill_rect(surface, 0.f, 0.f, 40.f, 40.f, 255, 255, 0, 0, 1);
    viewruntime_clip_pop(surface);
    viewruntime_frame_end(surface);

    /* Frame 2 WITHOUT any clip push: a full-surface fill must cover
     * EVERYTHING (a leaked clip from frame 1 would leave corners clear). */
    viewruntime_frame_begin(surface);
    viewruntime_draw_fill_rect(surface, 0.f, 0.f, 40.f, 40.f, 255, 0, 0, 255, 1);
    viewruntime_frame_end(surface);

    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    auto alpha_at = [&](int x, int y) -> int {
        return px[y * pitch + x * 4 + 3];
    };
    EXPECT(alpha_at(5, 5) == 255);   /* corner: must be painted in frame 2 */
    EXPECT(alpha_at(35, 35) == 255); /* opposite corner */

    viewruntime_surface_destroy(surface);
}

/* Linear gradient TOP_BOTTOM (angle 270, GradientDrawable.java:1842-1843):
 * a 20x20 box with red top → blue bottom must show red at the top edge,
 * blue at the bottom edge, and the midpoint interpolated (Skia LinearGradient
 * CLAMP: t = projection on the axis, java:1339-1340). */
static void test_gradient_linear_top_bottom() {
    void* surface = viewruntime_surface_create(nullptr);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 24, 24, 1.f);

    viewruntime_frame_begin(surface);
    /* start red (255,0,0), end blue (0,0,255), angle 270 = TOP_BOTTOM. */
    viewruntime_draw_fill_rounded_rect_gradient(
        surface, 2.f, 2.f, 20.f, 20.f, 0.f, 270,
        255, 255, 0, 0, 255, 0, 0, 255, 1);
    viewruntime_frame_end(surface);

    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    /* BGRA bytes. Top edge (y=3): mostly red (r high, b low). */
    const uint8_t* top = px + 3 * pitch + 12 * 4;
    EXPECT(top[2] > 200 && top[0] < 60);   /* r > 200, b < 60 */
    /* Bottom edge (y=20): mostly blue (b high, r low). */
    const uint8_t* bot = px + 20 * pitch + 12 * 4;
    EXPECT(bot[0] > 200 && bot[2] < 60);   /* b > 200, r < 60 */
    /* Midpoint (y=11): interpolated — r and b both mid-range (not pure). */
    const uint8_t* mid = px + 11 * pitch + 12 * 4;
    EXPECT(mid[2] > 80 && mid[2] < 180 && mid[0] > 80 && mid[0] < 180);

    viewruntime_surface_destroy(surface);
}

/* Rounded-rect stroke: a 20x20 box with a 4px border must paint the border
 * band (edge pixels) but leave the interior clear (GradientDrawable strokes
 * over the same rect as the fill, java:825-827). */
static void test_stroke_rounded_rect_border() {
    void* surface = viewruntime_surface_create(nullptr);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 24, 24, 1.f);

    viewruntime_frame_begin(surface);
    /* 20x20 box at (2,2), radius 4, border 4px, black. */
    viewruntime_draw_stroke_rounded_rect(surface, 2.f, 2.f, 20.f, 20.f,
                                         4.f, 4.f, 0.f, 0.f,
                                         255, 0, 0, 0, 1);
    viewruntime_frame_end(surface);

    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    auto alpha_at = [&](int x, int y) -> int {
        return px[y * pitch + x * 4 + 3];
    };
    /* Border band: edge pixel painted. */
    EXPECT(alpha_at(3, 12) == 255);  /* left edge, middle */
    EXPECT(alpha_at(12, 3) == 255);  /* top edge, middle */
    /* Interior (center) clear — only the border is stroked. */
    EXPECT(alpha_at(12, 12) == 0);
    /* Band interior edge: with rad_c=4, half=2, the stroke band spans
     * x∈[2,6] on the left; x=6 must be CLEAR (interior), not stroke —
     * the old code painted [6,8] as stroke (bug B1: interior box inset by
     * rad_c instead of half). */
    EXPECT(alpha_at(6, 12) == 0);
    /* Outside the box clear. */
    EXPECT(alpha_at(1, 12) == 0);

    viewruntime_surface_destroy(surface);
}

/* OVAL shape (GradientDrawable.java:839-844): the ellipse inscribed in the
 * box — center painted, corners of the box clear. */
static void test_oval_fill() {
    void* surface = viewruntime_surface_create(nullptr);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 24, 24, 1.f);

    viewruntime_frame_begin(surface);
    viewruntime_draw_fill_oval(surface, 2.f, 2.f, 20.f, 20.f,
                               255, 255, 0, 0, 1);
    viewruntime_frame_end(surface);

    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    auto alpha_at = [&](int x, int y) -> int {
        return px[y * pitch + x * 4 + 3];
    };
    EXPECT(alpha_at(12, 12) == 255);  /* center of the ellipse */
    EXPECT(alpha_at(3, 12) == 255);   /* left edge (on the ellipse) */
    EXPECT(alpha_at(2, 2) == 0);      /* box corner — outside the ellipse */
    EXPECT(alpha_at(21, 21) == 0);

    viewruntime_surface_destroy(surface);
}

/* LINE shape (GradientDrawable.java:845-851): a horizontal line at the
 * vertical center, stroke width thick. */
/* Regression (audit round 7, MAJOR): draw_text must clip glyphs to the view
 * box — a long/downward run must not bleed outside the box. The old code
 * computed clip_x0..clip_y1 and then (void)-ed them; blit_glyph only clamped
 * the surface. AOSP clips every child to its laid-out box
 * (View.java:24905-24915). */
static void test_draw_text_clips_to_view_box() {
    void* surface = viewruntime_surface_create(nullptr);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 32, 32, 1.f);
    const char* font_path = find_system_font();
    if (font_path == nullptr) {
        viewruntime_surface_destroy(surface);
        std::printf("SKIP test_draw_text_clips_to_view_box: no font\n");
        return;
    }
    FILE* f = std::fopen(font_path, "rb");
    if (!f) { viewruntime_surface_destroy(surface); return; }
    std::fseek(f, 0, SEEK_END);
    const long len = std::ftell(f);
    std::fseek(f, 0, SEEK_SET);
    std::vector<uint8_t> data(static_cast<size_t>(len));
    const size_t rd = std::fread(data.data(), 1, static_cast<size_t>(len), f);
    std::fclose(f);
    if (rd != data.size()) { viewruntime_surface_destroy(surface); return; }
    viewruntime_surface_set_font(surface, data.data(), static_cast<int32_t>(data.size()));

    /* A "g" (descender) drawn in a box that ends right above the baseline
     * must not paint below y=12. Draw at y=0..12 so the descender would
     * land at ~16-20 if unclipped. */
    const uint16_t text[] = {'g'};
    viewruntime_frame_begin(surface);
    viewruntime_draw_text(surface, 0.f, 0.f, 12.f, 12.f, text, 1, 24.f,
                          255, 0, 0, 0, 1, TEXT_ALIGN_LEFT, 0, 0);
    viewruntime_frame_end(surface);
    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    auto alpha_at = [&](int x, int y) -> int {
        return px[y * pitch + x * 4 + 3];
    };
    /* Descender area below the view box (y > 12) stays transparent. */
    for (int y = 14; y < 24; ++y)
        EXPECT(alpha_at(3, y) == 0);

    viewruntime_surface_destroy(surface);
}

/* Regression (audit round 7, MAJOR): draw_text wrap=1 draws multi-line text
 * line by line with advancing baselines (Layout.java:926-936). The old code
 * reset pen_x on '\n' without advancing y, collapsing "a\nb" into one line. */
static void test_draw_text_wrap_multiline() {
    void* surface = viewruntime_surface_create(nullptr);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 64, 64, 1.f);
    const char* font_path = find_system_font();
    if (font_path == nullptr) {
        viewruntime_surface_destroy(surface);
        std::printf("SKIP test_draw_text_wrap_multiline: no font\n");
        return;
    }
    FILE* f = std::fopen(font_path, "rb");
    if (!f) { viewruntime_surface_destroy(surface); return; }
    std::fseek(f, 0, SEEK_END);
    const long len = std::ftell(f);
    std::fseek(f, 0, SEEK_SET);
    std::vector<uint8_t> data(static_cast<size_t>(len));
    const size_t rd = std::fread(data.data(), 1, static_cast<size_t>(len), f);
    std::fclose(f);
    if (rd != data.size()) { viewruntime_surface_destroy(surface); return; }
    viewruntime_surface_set_font(surface, data.data(), static_cast<int32_t>(data.size()));

    const uint16_t text[] = {'a', '\n', 'b'};
    viewruntime_frame_begin(surface);
    viewruntime_draw_text(surface, 0.f, 0.f, 60.f, 60.f, text, 3, 20.f,
                          255, 0, 0, 0, 1, TEXT_ALIGN_LEFT, 0, 1);
    viewruntime_frame_end(surface);
    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    auto alpha_at = [&](int x, int y) -> int {
        return px[y * pitch + x * 4 + 3];
    };
    /* Row 1 (baseline ~15): the "a" paints. Row 2 (baseline ~35): "b". */
    int row1 = 0, row2 = 0;
    for (int y = 8; y < 22; ++y) if (alpha_at(2, y) != 0) ++row1;
    for (int y = 28; y < 42; ++y) if (alpha_at(2, y) != 0) ++row2;
    EXPECT(row1 > 0);  /* first line painted */
    EXPECT(row2 > 0);  /* SECOND line painted — the old code had none */
}

/* Regression (audit round 7, MAJOR): draw_image must respect the active clip
 * stack (ScrollView/ListView viewports), not just the surface bounds — a
 * CENTER_CROP image inside a scrolled view bled outside the viewport. */
static void test_draw_image_respects_clip_stack() {
    void* surface = viewruntime_surface_create(nullptr);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 32, 32, 1.f);
    const uint8_t argb[4] = {255, 255, 0, 0}; /* red 1x1 */
    viewruntime_surface_set_image(surface, "clipimg", 1, 1, argb);

    viewruntime_frame_begin(surface);
    viewruntime_clip_push(surface, 4.f, 4.f, 8.f, 8.f); /* viewport */
    /* Image dst larger than the clip: must be cut at the clip edge. */
    viewruntime_draw_image(surface, "clipimg", 0.f, 0.f, 1.f, 1.f,
                           0.f, 0.f, 32.f, 32.f, 1);
    viewruntime_clip_pop(surface);
    viewruntime_frame_end(surface);
    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    auto alpha_at = [&](int x, int y) -> int {
        return px[y * pitch + x * 4 + 3];
    };
    EXPECT(alpha_at(5, 5) == 255);  /* inside clip: painted */
    EXPECT(alpha_at(13, 13) == 0);  /* outside clip: CLEAR (old code painted) */
    EXPECT(alpha_at(1, 1) == 0);    /* outside clip: CLEAR */
    viewruntime_surface_destroy(surface);
}

/* Regression (audit round 7, MINOR): clip_push must clamp to surface bounds
 * — an oversized clip could escape apply_clip's bounds (apply_clip trusts the
 * stack top) and write out of the pixel buffer. */
static void test_clip_push_clamps_to_surface() {
    void* surface = viewruntime_surface_create(nullptr);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 16, 16, 1.f);
    viewruntime_frame_begin(surface);
    /* Clip far larger than the 16x16 surface (and negative). */
    viewruntime_clip_push(surface, -100.f, -100.f, 500.f, 500.f);
    viewruntime_draw_fill_rect(surface, -50.f, -50.f, 200.f, 200.f,
                               255, 0, 0, 255, 1);
    viewruntime_clip_pop(surface);
    viewruntime_frame_end(surface);
    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    /* No crash; the surface is fully painted (clip covered everything). */
    EXPECT(px[0 * pitch + 0 * 4 + 3] == 255);
    EXPECT(px[15 * pitch + 15 * 4 + 3] == 255);
    viewruntime_surface_destroy(surface);
}

/* Regression (audit round 7, MAJOR): OVAL fill with a gradient — the REAL
 * ellipse, not a stadium for w != h. */
static void test_oval_gradient_ellipse() {
    void* surface = viewruntime_surface_create(nullptr);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 24, 48, 1.f);

    viewruntime_frame_begin(surface);
    viewruntime_draw_fill_oval_gradient(
        surface, 2.f, 2.f, 20.f, 40.f, 270,
        255, 255, 0, 0, 255, 0, 0, 255, 1);
    viewruntime_frame_end(surface);

    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    auto alpha_at = [&](int x, int y) -> int {
        return px[y * pitch + x * 4 + 3];
    };
    /* Center painted (inside the ellipse). */
    EXPECT(alpha_at(12, 21) == 255);
    /* Discriminating points: the OLD code was a rounded rect with radius
     * min(w,h)/2 = 10 in a 20x40 box, i.e. a STADIUM whose flat left/right
     * edges span the middle band (y∈[12,32]) at x=2 and x=21. The real
     * ellipse (rx=10, ry=20, cx=12, cy=22) is narrower at y=12: elx_max =
     * 10*sqrt(1-(9.5²/400)) ≈ 8.8 → x∈[3.2,20.8]. So (2,12) and (21,12) are
     * outside the ellipse but inside the stadium's flat band. */
    EXPECT(alpha_at(2, 12) == 0);   /* stadium flat left edge → clear */
    EXPECT(alpha_at(21, 12) == 0);  /* stadium flat right edge → clear */
    /* Inside the ellipse near the top: (12-12)²/100 + (4-22)²/400 =
     * 0 + 324/400 = 0.81 <= 1. */
    EXPECT(alpha_at(12, 4) == 255);
    /* Inside the ellipse lower area: (12-12)²/100 + (38-22)²/400 =
     * 256/400 = 0.64 <= 1. */
    EXPECT(alpha_at(12, 38) == 255);

    viewruntime_surface_destroy(surface);
}

/* measure_text_common max_width<=0 semantics: AOSP getDesiredWidth takes the
 * MAX over paragraphs and counts ONE LINE PER PARAGRAPH (Layout.java:230-231,
 * 277-293). "ab\ncd" must report 2 lines, not 1. */
static void test_measure_max_width_zero_paragraphs() {
    void* surface = viewruntime_surface_create(nullptr);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 64, 64, 1.f);

    const char* font_path = find_system_font();
    if (font_path == nullptr) {
        viewruntime_surface_destroy(surface);
        std::printf("SKIP test_measure_max_width_zero_paragraphs: no font\n");
        return;
    }
    FILE* f = std::fopen(font_path, "rb");
    if (!f) { viewruntime_surface_destroy(surface); return; }
    std::fseek(f, 0, SEEK_END);
    const long len = std::ftell(f);
    std::fseek(f, 0, SEEK_SET);
    std::vector<uint8_t> data(static_cast<size_t>(len));
    const size_t rd = std::fread(data.data(), 1, static_cast<size_t>(len), f);
    std::fclose(f);
    if (rd != data.size()) { viewruntime_surface_destroy(surface); return; }
    viewruntime_surface_set_font(surface, data.data(), static_cast<int32_t>(data.size()));

    const uint16_t text[] = {'a', 'b', '\n', 'c', 'd'};
    float w1 = 0, h1 = 0, b1 = 0;
    float w2 = 0, h2 = 0, b2 = 0;
    float wm = 0, hm = 0, bm = 0;
    viewruntime_measure_text(surface, text, 5, 10.f, 0.f, &wm, &hm, &bm);
    viewruntime_measure_text(surface, text, 2, 10.f, 0.f, &w1, &h1, &b1);
    viewruntime_measure_text(surface, text + 3, 2, 10.f, 0.f, &w2, &h2, &b2);
    /* width = MAX over paragraphs ("ab" and "cd" — the wider one wins) */
    EXPECT(wm >= w1 && wm >= w2);
    /* height = 2 lines (one per paragraph) — NOT 1 line */
    EXPECT(hm > h1);
    EXPECT_NEAR(hm, h1 * 2.f, h1 * 0.1f);

    viewruntime_surface_destroy(surface);
}

static void test_line_shape() {
    void* surface = viewruntime_surface_create(nullptr);
    EXPECT(surface != nullptr);
    viewruntime_surface_resize(surface, 24, 24, 1.f);

    viewruntime_frame_begin(surface);
    viewruntime_draw_fill_line(surface, 2.f, 2.f, 20.f, 20.f, 4.f,
                               255, 0, 0, 0, 1);
    viewruntime_frame_end(surface);

    const uint8_t* px = nullptr;
    int pitch = 0, w = 0, h = 0;
    viewruntime_surface_pixels(surface, &px, &pitch, &w, &h);
    auto alpha_at = [&](int x, int y) -> int {
        return px[y * pitch + x * 4 + 3];
    };
    EXPECT(alpha_at(12, 12) == 255);  /* center line */
    EXPECT(alpha_at(5, 12) == 255);   /* inside [4,18] (inset w/2 per side) */
    EXPECT(alpha_at(3, 12) == 0);     /* before the inset start — clear */
    EXPECT(alpha_at(12, 2) == 0);     /* above the line — clear */
    EXPECT(alpha_at(12, 21) == 0);    /* below the line — clear */

    viewruntime_surface_destroy(surface);
}

int main() {
    test_fill_rect_pixels();
    test_draw_text_paints_pixels();
    test_measure_draw_consistency();
    test_resize_clears();
    test_draw_image_scaled();
    test_draw_image_cropped();
    test_draw_text_alignment_and_bold();
    test_draw_rounded_rect_corners();
    test_clip_restricts_draws();
    test_frame_begin_clears_clip();
    test_gradient_linear_top_bottom();
    test_stroke_rounded_rect_border();
    test_oval_fill();
    test_line_shape();
    test_draw_text_clips_to_view_box();
    test_draw_text_wrap_multiline();
    test_draw_image_respects_clip_stack();
    test_clip_push_clamps_to_surface();
    test_oval_gradient_ellipse();
    test_measure_max_width_zero_paragraphs();
    return test_result();
}
