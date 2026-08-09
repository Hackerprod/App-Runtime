/* Phase 1 render backend tests: rasterization of fill rects and text into
 * the ARGB8888 buffer, clip behavior, and measure/draw font consistency. */

#include "android_test_util.h"

#include <viewruntime/viewruntime_backend.h>

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
                          255, 0, 0, 0, 7);
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

int main() {
    test_fill_rect_pixels();
    test_draw_text_paints_pixels();
    test_measure_draw_consistency();
    test_resize_clears();
    return test_result();
}
