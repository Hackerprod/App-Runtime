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
VIEWRUNTIME_BACKEND_API void viewruntime_clip_push(
    void* surface, float x, float y, float w, float h);
VIEWRUNTIME_BACKEND_API void viewruntime_clip_pop(void* surface);
VIEWRUNTIME_BACKEND_API void viewruntime_draw_fill_rect(
    void* surface, float x, float y, float w, float h,
    uint8_t a, uint8_t r, uint8_t g, uint8_t b, int32_t view_id);
/* Rounded-rect fill. radius_px clamps to min(radius, min(w,h)*0.5) exactly
 * like AOSP GradientDrawable.draw (GradientDrawable.java:823-825); 0 = square
 * (identical to viewruntime_draw_fill_rect). */
VIEWRUNTIME_BACKEND_API void viewruntime_draw_fill_rounded_rect(
    void* surface, float x, float y, float w, float h,
    float radius_px, uint8_t a, uint8_t r, uint8_t g, uint8_t b, int32_t view_id);
/* Rounded-rect fill with a LINEAR gradient between two colors along the
 * orientation axis given by angle (0=left→right, 90=bottom→top, 270=top→
 * bottom — AOSP GradientDrawable angle→Orientation, java:1822-1851, with the
 * Skia LinearGradient CLAMP semantics: t = clamp(projection,0,1), color =
 * lerp(start,end,t)). radius_px clamps like GradientDrawable.java:823. */
VIEWRUNTIME_BACKEND_API void viewruntime_draw_fill_rounded_rect_gradient(
    void* surface, float x, float y, float w, float h,
    float radius_px, int32_t angle_deg,
    uint8_t a0, uint8_t r0, uint8_t g0, uint8_t b0,
    uint8_t a1, uint8_t r1, uint8_t g1, uint8_t b1, int32_t view_id);
/* Rounded-rect STROKE (border) — GradientDrawable's mStrokePaint drawn over
 * the same rect as the fill with the same corner radius
 * (GradientDrawable.java:825-827 drawRoundRect(mRect, rad, rad,
 * mStrokePaint)). width_px is the border thickness; dash 0 = solid. */
VIEWRUNTIME_BACKEND_API void viewruntime_draw_stroke_rounded_rect(
    void* surface, float x, float y, float w, float h,
    float radius_px, float width_px, float dash_width_px, float dash_gap_px,
    uint8_t a, uint8_t r, uint8_t g, uint8_t b, int32_t view_id);
/* OVAL fill — GradientDrawable OVAL shape (java:839-844 canvas.drawOval):
 * the ellipse inscribed in the box. */
VIEWRUNTIME_BACKEND_API void viewruntime_draw_fill_oval(
    void* surface, float x, float y, float w, float h,
    uint8_t a, uint8_t r, uint8_t g, uint8_t b, int32_t view_id);
/* OVAL fill with a LINEAR gradient — GradientDrawable OVAL shape drawn with
 * the LinearGradient shader (java:840 drawOval(mRect, mFillPaint)). A REAL
 * ellipse (rx=w/2, ry=h/2), NOT a rounded rect (a rounded rect with
 * radius min(w,h)/2 degenerates into a stadium for w != h). Same axis/lerp
 * semantics as viewruntime_draw_fill_rounded_rect_gradient. */
VIEWRUNTIME_BACKEND_API void viewruntime_draw_fill_oval_gradient(
    void* surface, float x, float y, float w, float h,
    int32_t angle_deg,
    uint8_t a0, uint8_t r0, uint8_t g0, uint8_t b0,
    uint8_t a1, uint8_t r1, uint8_t g1, uint8_t b1, int32_t view_id);
/* LINE fill — GradientDrawable LINE shape (java:845-851): a horizontal line
 * at the vertical center, stroke width thick. */
VIEWRUNTIME_BACKEND_API void viewruntime_draw_fill_line(
    void* surface, float x, float y, float w, float h,
    float width_px, uint8_t a, uint8_t r, uint8_t g, uint8_t b, int32_t view_id);
VIEWRUNTIME_BACKEND_API void viewruntime_draw_text(
    void* surface, float x, float y, float w, float h,
    const uint16_t* utf16_text, int32_t text_len,
    float text_size_px, uint8_t a, uint8_t r, uint8_t g, uint8_t b,
    int32_t view_id,
    int32_t text_align, int32_t bold, int32_t wrap);
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

/* Install the SAME TrueType bytes the UI session measures with, so paint and
 * measure agree pixel-for-pixel. The surface owns a copy; call once after
 * android_ui_set_font (the surface may also be created with a font_path). */
VIEWRUNTIME_BACKEND_API void viewruntime_surface_set_font(
    void* surface, const uint8_t* font_data, int32_t font_size);

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
