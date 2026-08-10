/* Phase 1 render backend tests: rasterization of fill rects and text into
 * the ARGB8888 buffer, clip behavior, and measure/draw font consistency. */

#include "android_test_util.h"

#include <viewruntime/viewruntime_backend.h>

#include <algorithm>

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
                          255, 0, 0, 0, 7, TEXT_ALIGN_LEFT, 0);
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
                              255, 0, 0, 0, 1, TEXT_ALIGN_LEFT, 0);
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
                              255, 0, 0, 0, 1, TEXT_ALIGN_CENTER, 0);
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
                              255, 0, 0, 0, 1, TEXT_ALIGN_LEFT, 1);
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

int main() {
    test_fill_rect_pixels();
    test_draw_text_paints_pixels();
    test_measure_draw_consistency();
    test_resize_clears();
    test_draw_image_scaled();
    test_draw_image_cropped();
    test_draw_text_alignment_and_bold();
    return test_result();
}
