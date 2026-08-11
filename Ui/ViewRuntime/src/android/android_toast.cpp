/* android.widget.Toast — EXACT AOSP reverse-engineering port (C++).

 * Sources of truth (Ui/ViewRuntime/.tmp/):
 *   - Toast.java          (aosp-mirror master): makeText/show/cancel/setText/
 *                         setDuration/getDuration; LENGTH_SHORT=0/LONG=1.
 *   - ToastPresenter.java: SHORT_DURATION_TIMEOUT=4000, LONG_DURATION_TIMEOUT=
 *                         7000; transient_notification layout; "Only one toast
 *                         at a time".
 *   - transient_notification.xml: LinearLayout horizontal, padding 16dp sides,
 *                         background ?colorBackground, TextView@message
 *                         maxLines=2, ellipsize=end, padding 12dp vertical,
 *                         textAppearance=TextAppearance.Toast (14sp).
 *   - dimens.xml: toast_y_offset=48dp, toast_width=300dp, toast_text_size=14sp,
 *                         toast_elevation=2dp.
 *   - config.xml: config_toastDefaultGravity=0x51 (CENTER_HORIZONTAL|BOTTOM).
 *
 * The toast state is OWNED HERE (not in the host). The host polls
 * android_toast_is_active each frame and calls android_toast_render AFTER the
 * app frame; ViewRuntime deactivates the toast itself when the SHORT/LONG
 * deadline passes, exactly like the TN handler's SHOW/HIDE messages
 * (ToastPresenter.java timeouts). No host-side toast timer/logic exists.
 */

#include "android_types.h"
#include "../include/viewruntime/viewruntime_backend.h"

#include <chrono>
#include <vector>

namespace viewruntime::android {

/* Steady-clock helper: milliseconds since the session started. */
static uint64_t now_ms() {
    return static_cast<uint64_t>(
        std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now().time_since_epoch()).count());
}

} // namespace viewruntime::android

extern "C" {

API bool_t android_toast_is_active(android_ui_t ui) {
    if (!ui) return FALSE;
    std::lock_guard<std::mutex> lock(ui->toast_mutex);
    if (!ui->toast_active) return FALSE;
    /* Expired: deactivate now (the TN HIDE message fired). */
    if (viewruntime::android::now_ms() >= ui->toast_deadline_ms) {
        ui->toast_active = false;
        return FALSE;
    }
    return TRUE;
}

API status_t android_toast_make_text(android_ui_t ui, const char* text, int32_t duration) {
    if (!ui) return ERROR_NULL_ARG;
    if (duration != ANDROID_TOAST_LENGTH_SHORT && duration != ANDROID_TOAST_LENGTH_LONG)
        return ERROR_INVALID_STATE;
    std::lock_guard<std::mutex> lock(ui->toast_mutex);
    ui->toast_text = text ? text : "";
    ui->toast_duration = duration;
    ui->toast_active = false;
    ui->has_toast = true;
    return OK;
}

API status_t android_toast_set_text(android_ui_t ui, const char* text) {
    if (!ui) return ERROR_NULL_ARG;
    std::lock_guard<std::mutex> lock(ui->toast_mutex);
    if (!ui->has_toast) return ERROR_INVALID_STATE;
    ui->toast_text = text ? text : "";
    return OK;
}

API status_t android_toast_set_duration(android_ui_t ui, int32_t duration) {
    if (!ui) return ERROR_NULL_ARG;
    if (duration != ANDROID_TOAST_LENGTH_SHORT && duration != ANDROID_TOAST_LENGTH_LONG)
        return ERROR_INVALID_STATE;
    std::lock_guard<std::mutex> lock(ui->toast_mutex);
    if (!ui->has_toast) return ERROR_INVALID_STATE;
    ui->toast_duration = duration;
    return OK;
}

API int32_t android_toast_get_duration(android_ui_t ui) {
    if (!ui) return ANDROID_TOAST_LENGTH_SHORT;
    std::lock_guard<std::mutex> lock(ui->toast_mutex);
    return ui->toast_duration;
}

API status_t android_toast_show(android_ui_t ui) {
    if (!ui) return ERROR_NULL_ARG;
    std::lock_guard<std::mutex> lock(ui->toast_mutex);
    if (!ui->has_toast) return ERROR_INVALID_STATE;
    /* TN.handleShow: if a cancel/hide is pending no need to show — the runtime
     * re-shows unconditionally, matching Toast.show()'s enqueue semantics. */
    ui->toast_active = true;
    ui->toast_deadline_ms = viewruntime::android::now_ms() +
        (ui->toast_duration == ANDROID_TOAST_LENGTH_LONG
             ? ANDROID_TOAST_LONG_TIMEOUT_MS
             : ANDROID_TOAST_SHORT_TIMEOUT_MS);
    return OK;
}

API status_t android_toast_cancel(android_ui_t ui) {
    if (!ui) return ERROR_NULL_ARG;
    std::lock_guard<std::mutex> lock(ui->toast_mutex);
    /* TN.handleHide: only hides when a view is showing. */
    ui->toast_active = false;
    return OK;
}

/* Render the transient_notification view: a rounded-rect panel (background
 * ?colorBackground — the runtime's dark toast surface) at BOTTOM|CENTER with
 * y_offset 48dp, maxWidth 300dp, padding 16dp sides / 12dp vertical, holding
 * the message at 14sp. Drawn AFTER the app frame so it overlays it, like a
 * TYPE_TOAST window (ToastPresenter.addToastView). */
API void android_toast_render(android_ui_t ui) {
    if (!ui || !ui->surface) return;
    std::lock_guard<std::mutex> lock(ui->toast_mutex);
    if (!ui->toast_active || ui->toast_text.empty()) return;
    if (viewruntime::android::now_ms() >= ui->toast_deadline_ms) {
        ui->toast_active = false;
        return;
    }

    const float density = ui->density > 0.f ? ui->density : 1.f;
    const float sp_scale = ui->scaled_density > 0.f ? ui->scaled_density : density;

    /* Convert the message (UTF-8) to UTF-16 — the backend's measure/draw path
     * is UTF-16 (same as PAINT_DRAW_TEXT's executor conversion). */
    std::vector<uint16_t> utf16;
    {
        const char* p = ui->toast_text.c_str();
        while (p && *p) {
            const unsigned char c0 = static_cast<unsigned char>(*p);
            uint32_t cp = 0;
            int n = 1;
            if (c0 < 0x80) { cp = c0; }
            else if ((c0 & 0xE0) == 0xC0 && p[1]) { cp = ((c0 & 0x1F) << 6) | (p[1] & 0x3F); n = 2; }
            else if ((c0 & 0xF0) == 0xE0 && p[1] && p[2]) { cp = ((c0 & 0x0F) << 12) | ((p[1] & 0x3F) << 6) | (p[2] & 0x3F); n = 3; }
            else if ((c0 & 0xF8) == 0xF0 && p[1] && p[2] && p[3]) {
                cp = ((c0 & 0x07) << 18) | ((p[1] & 0x3F) << 12) | ((p[2] & 0x3F) << 6) | (p[3] & 0x3F);
                n = 4;
                if (cp >= 0x10000) {
                    utf16.push_back(static_cast<uint16_t>(0xD800 + ((cp - 0x10000) >> 10)));
                    utf16.push_back(static_cast<uint16_t>(0xDC00 + ((cp - 0x10000) & 0x3FF)));
                    p += n;
                    continue;
                }
            } else { cp = 0xFFFD; }
            utf16.push_back(static_cast<uint16_t>(cp));
            p += n;
        }
    }
    const int32_t text_len = static_cast<int32_t>(utf16.size());
    if (text_len == 0) return;

    /* Measure the message run at TextAppearance.Toast size (14sp). */
    float text_w = 0.f, text_h = 0.f, baseline = 0.f;
    viewruntime_measure_text(ui->surface, utf16.data(), text_len,
                             14.f * sp_scale, 300.f * density,
                             &text_w, &text_h, &baseline);

    /* Padding (transient_notification.xml): 16dp sides, 12dp vertical. */
    const float pad_h = 16.f * density;
    const float pad_v = 12.f * density;
    /* Max width (dimens.xml toast_width=300dp); the panel wraps content. */
    const float max_w = 300.f * density;
    const float content_w = text_w + 2.f * pad_h;
    const float panel_w = std::min(max_w, content_w);
    const float panel_h = text_h + 2.f * pad_v;

    /* Position: config_toastDefaultGravity = 0x51 = CENTER_HORIZONTAL | BOTTOM
     * with y offset 48dp above the bottom (dimens.xml toast_y_offset). */
    int sw = 0, sh = 0;
    {
        const uint8_t* px = nullptr;
        int pitch = 0, w = 0, h = 0;
        viewruntime_surface_pixels(ui->surface, &px, &pitch, &w, &h);
        sw = w; sh = h;
    }
    if (sw <= 0 || sh <= 0) return;
    const float panel_x = (static_cast<float>(sw) - panel_w) * 0.5f;
    const float panel_y = static_cast<float>(sh) - 48.f * density - panel_h;

    /* Panel background: dark rounded surface (AOSP theme colorBackground on a
     * dark theme; the runtime uses the system toast surface). Slight corner
     * rounding matches the Material toast appearance. */
    viewruntime_draw_fill_rounded_rect(
        ui->surface, panel_x, panel_y, panel_w, panel_h, 8.f * density,
        255, 50, 50, 50, /* opaque dark gray (a,r,g,b) */
        0);

    /* Message (TextView@id/message): light text on the dark panel, centered
     * horizontally, vertically centered within the panel. */
    viewruntime_draw_text(
        ui->surface,
        panel_x + pad_h,
        panel_y + pad_v,
        panel_w - 2.f * pad_h,
        panel_h - 2.f * pad_v,
        utf16.data(), text_len,
        14.f * sp_scale,
        255, 245, 245, 245, /* near-white text (TextAppearance.Toast) */
        0, TEXT_ALIGN_CENTER, 0, 0);
}

} // extern "C"
