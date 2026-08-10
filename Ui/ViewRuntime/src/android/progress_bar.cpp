#include "android_types.h"

#include <algorithm>

namespace viewruntime::android {

/* ── ProgressBar ───────────────────────────────────────────────────── */

/* Port of AOSP ProgressBar.onMeasure (ProgressBar.java:2198). AOSP clamps the
 * horizontal progress drawable's intrinsic size (100x16dp) to
 * [mMinWidth, mMaxWidth] = [24, 48] and [mMinHeight, mMaxHeight] = [24, 48]
 * (ProgressBar.java:657-660, 2204-2205) before adding padding and resolving
 * against the parent spec.
 *
 * DIVERGENCE: the runtime has no drawable model, so it uses the intrinsic
 * 100x16dp directly and does NOT apply the mMinWidth/mMaxWidth/mMinHeight/
 * mMaxHeight clamp (ProgressBar.java:2204-2205). The only floor is the view's
 * own min_width_dp/min_height_dp. The clamp below is intentionally NOT
 * implemented — do not add it without a drawable/attribute model. */
android_measured_size_t measure_progress(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    /* intrinsic 100x16dp + padding; resolve_size absorbs the parent spec
     * (AT_MOST/EXACTLY). */
    float desired_w = std::max(dp(ui, 100.f), dp(ui, view->min_width_dp)) + padding_h(view, ui);
    float desired_h = std::max(dp(ui, 16.f), dp(ui, view->min_height_dp)) + padding_v(view, ui);
    const android_measured_size_t result{resolve_size(desired_w, spec_w),
                                                resolve_size(desired_h, spec_h)};
    view->measured_baseline = -1.f;
    return result;
}

} // namespace viewruntime::android
