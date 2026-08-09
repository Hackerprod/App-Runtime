#pragma once
/* ViewRuntime render backend — Phase 1 C ABI for App Runtime's
 * IAndroidRenderBackend seam (see docs/viewruntime-integration-spec.md).
 *
 * The surface owns an off-screen ARGB8888 buffer; App Runtime calls
 * frame_begin, then draw_* calls in display-list order, then frame_end, and
 * blits the finished frame (viewruntime_surface_pixels) into WPF.
 *
 * Text measurement and drawing share the same font/metrics path, so layout
 * and paint agree pixel-for-pixel (a fixed system font set once at
 * surface_create). */

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32) && defined(EXPORTS)
#define VIEWRUNTIME_BACKEND_API __declspec(dllexport)
#elif defined(_WIN32)
#define VIEWRUNTIME_BACKEND_API __declspec(dllimport)
#else
#define VIEWRUNTIME_BACKEND_API
#endif

/* Surface lifecycle. font_path is a TrueType file (e.g. a system font);
 * pass NULL to fall back to a deterministic proportional approximation. */
VIEWRUNTIME_BACKEND_API void* viewruntime_surface_create(const char* font_path);
VIEWRUNTIME_BACKEND_API void  viewruntime_surface_destroy(void* surface);
VIEWRUNTIME_BACKEND_API void  viewruntime_surface_resize(
    void* surface, int pixel_width, int pixel_height, float density);

/* Frame lifecycle. Draw calls paint in order, later over earlier. */
VIEWRUNTIME_BACKEND_API void viewruntime_frame_begin(void* surface);
VIEWRUNTIME_BACKEND_API void viewruntime_draw_fill_rect(
    void* surface, float x, float y, float w, float h,
    uint8_t a, uint8_t r, uint8_t g, uint8_t b, int32_t view_id);
VIEWRUNTIME_BACKEND_API void viewruntime_draw_text(
    void* surface, float x, float y, float w, float h,
    const uint16_t* utf16_text, int32_t text_len,
    float text_size_px, uint8_t a, uint8_t r, uint8_t g, uint8_t b,
    int32_t view_id);
/* Upload (or replace) a decoded image under `source`. Pixels are straight
 * ARGB8888, row-major, width*4 pitch. The surface owns a copy; call again
 * with the same source to update it. */
VIEWRUNTIME_BACKEND_API void viewruntime_surface_set_image(
    void* surface, const char* source, int width, int height,
    const uint8_t* argb_pixels);
/* Draw an image: map the source rect (in image pixel coordinates) into the
 * destination rect (in surface coordinates), scaled, clipped to the surface.
 * Matches AOSP ImageView.configureBounds geometry when the caller passes the
 * resolved src/dst rects. */
VIEWRUNTIME_BACKEND_API void viewruntime_draw_image(
    void* surface, const char* source,
    float src_x, float src_y, float src_w, float src_h,
    float dst_x, float dst_y, float dst_w, float dst_h,
    int32_t view_id);
VIEWRUNTIME_BACKEND_API void viewruntime_frame_end(void* surface);

/* Text measurement — same font/metrics path as viewruntime_draw_text. */
VIEWRUNTIME_BACKEND_API void viewruntime_measure_text(
    void* surface, const uint16_t* utf16_text, int32_t text_len,
    float text_size_px, float max_width_px,
    float* out_width_px, float* out_height_px, float* out_baseline_px);

/* Pixel access for the WPF blit (Option A): straight ARGB8888, row-major,
 * pitch = width * 4. Valid until the next frame_begin/resize/destroy. */
VIEWRUNTIME_BACKEND_API void viewruntime_surface_pixels(
    void* surface, const uint8_t** out_pixels, int* out_pitch,
    int* out_width, int* out_height);

#ifdef __cplusplus
} /* extern "C" */
#endif
