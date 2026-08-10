#include "android_types.h"

#include <algorithm>
#include <cmath>
#include <limits>

namespace viewruntime::android {

/* ── TextView ──────────────────────────────────────────────────────── */

/* Port of AOSP TextView.onMeasure (frameworks/base/core/java/android/widget/
 * TextView.java:11275). This runtime stores textSize in sp and converts to
 * px during measure (AOSP converts in inflate via
 * applyDimension(COMPLEX_UNIT_SP)); the result is equivalent. The critical
 * detail is the desired width: AOSP measures with getDesiredWidthWithLimit
 * against mTextPaint and then applies (int) Math.ceil(...) to the raw text
 * width (TextView.java:11317/11358) before adding the padding. */
android_measured_size_t measure_text_view(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    const float size_px = sp(ui, view->text_size_sp);
    const float pad_h = padding_h(view, ui);
    const float pad_v = padding_v(view, ui);
    /* AOSP widthLimit is the RAW widthSize for AT_MOST — the padding is NOT
     * subtracted from the wrap limit (TextView.java:11293-11294); all other
     * modes use Float.MAX_VALUE. The final resolve_size (min of desired vs
     * spec size, TextView.java:11389-11391) absorbs the padding. */
    const float avail_w = spec_w.mode == ANDROID_MEASURE_UNSPECIFIED
        ? (std::numeric_limits<float>::max)()
        : spec_w.size;
    const android_text_metrics_t metrics =
        measure_text(ui, display_text(view), size_px, avail_w);
    /* AOSP: des = (int) Math.ceil(getDesiredWidthWithLimit(...)) */
    float desired_w = std::ceil(metrics.width) + pad_h;
    float desired_h = metrics.height + pad_v;
    /* TV7: AOSP clamps the desired width to mMaxWidth FIRST
     * (TextView.java:11374-11378) and only then raises it to mMinWidth
     * (:11380-11384); when min > max the MIN wins. 0 = unbounded
     * (android_types.h). */
    if (view->max_width_dp > 0.f) desired_w = std::min(desired_w, dp(ui, view->max_width_dp));
    desired_w = std::max(desired_w, dp(ui, view->min_width_dp));
    if (view->max_height_dp > 0.f) desired_h = std::min(desired_h, dp(ui, view->max_height_dp));
    desired_h = std::max(desired_h, dp(ui, view->min_height_dp));
    /* The final layout wraps to want = width - getCompoundPaddingLeft() -
     * getCompoundPaddingRight() (TextView.java:11394 -> makeNewLayout
     * 11403/11422), where width is the RESOLVED width (spec size under
     * EXACTLY, min(desired, widthSize) under AT_MOST, the desired width under
     * UNSPECIFIED). The height therefore depends on want, NOT the raw
     * widthSize: AOSP builds that layout ALWAYS — even for UNSPECIFIED
     * (TextView.java:11394-11403) — so re-measure against want whenever the
     * desired width is finite (TV6).
     *
     * TV5: a singleLine / horizontally-scrolling TextView overrides want with
     * VERY_WIDE (TextView.java:11397), so the text sits on one line and the
     * height is measured WITHOUT the reduced wrap width.
     *
     * TV8: a want <= 0 would make stb_text_measurer return a single unwrapped
     * line; AOSP's layout at that width wraps one word per line instead
     * (DynamicLayout word wrap against want, TextView.java:11394-11397), so a
     * minimum positive wrap width is forced here. */
    if (std::isfinite(desired_w)) {
        float want;
        if (view->single_line) {
            want = (std::numeric_limits<float>::max)(); /* VERY_WIDE, one line */
        } else {
            want = resolve_size(desired_w, spec_w) - pad_h;
            if (want <= 0.f) want = 1.f;
        }
        const android_text_metrics_t h_metrics =
            measure_text(ui, display_text(view), size_px, want);
        desired_h = h_metrics.height + pad_v;
        /* TV7: AOSP getDesiredHeight clamps max first (TextView.java:11608)
         * then min (:11627). */
        if (view->max_height_dp > 0.f) desired_h = std::min(desired_h, dp(ui, view->max_height_dp));
        desired_h = std::max(desired_h, dp(ui, view->min_height_dp));
    }
    const android_measured_size_t result{resolve_size(desired_w, spec_w),
                                                resolve_size(desired_h, spec_h)};
    view->measured_baseline = dp(ui, view->padding_top_dp) + metrics.baseline;
    return result;
}

/* CheckBox / RadioButton: same TextView.onMeasure structure with the
 * compound drawable (16dp indicator + 8dp gap) reserved before the text. */
android_measured_size_t measure_checkable(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    const float indicator = dp(ui, 16.f) + dp(ui, 8.f); /* box + gap before text */
    const float size_px = sp(ui, view->text_size_sp);
    const float pad_h = padding_h(view, ui);
    const float pad_v = padding_v(view, ui);
    /* The audit's TV4 finding stands: for checkable widgets the wrap limit
     * spec.size - padding - indicator IS the final AOSP wrap
     * (compoundPadding includes the drawable, TextView.java:11394) — do NOT
     * change it. Only the HEIGHT needs the two-step re-measure below. */
    const float avail_w = spec_w.mode == ANDROID_MEASURE_UNSPECIFIED
        ? (std::numeric_limits<float>::max)()
        : std::max(0.f, spec_w.size - pad_h - indicator);
    const android_text_metrics_t metrics =
        measure_text(ui, display_text(view), size_px, avail_w);
    float desired_w = std::ceil(metrics.width) + indicator + pad_h;
    float desired_h = metrics.height + pad_v;
    /* TV7: max first, then min — AOSP TextView.java:11374-11384 (width) /
     * 11608/11627 (height); min wins when min > max. */
    if (view->max_width_dp > 0.f) desired_w = std::min(desired_w, dp(ui, view->max_width_dp));
    desired_w = std::max(desired_w, dp(ui, view->min_width_dp));
    if (view->max_height_dp > 0.f) desired_h = std::min(desired_h, dp(ui, view->max_height_dp));
    desired_h = std::max(desired_h, dp(ui, view->min_height_dp));
    /* Two-step height (TV3): the layout wraps to want = width - compoundPadding
     * where compoundPadding includes the indicator (TextView.java:11394), so
     * re-measure the text against the resolved width minus padding+indicator
     * for the real line count. TV6 removes the UNSPECIFIED guard (AOSP always
     * builds the layout, TextView.java:11394-11403); TV5 single-line measures
     * at VERY_WIDE (TextView.java:11397); TV8 forces a minimum positive wrap
     * width when want <= 0. */
    if (std::isfinite(desired_w)) {
        float want;
        if (view->single_line) {
            want = (std::numeric_limits<float>::max)(); /* VERY_WIDE, one line */
        } else {
            want = resolve_size(desired_w, spec_w) - pad_h - indicator;
            if (want <= 0.f) want = 1.f;
        }
        const android_text_metrics_t h_metrics =
            measure_text(ui, display_text(view), size_px, want);
        desired_h = h_metrics.height + pad_v;
        /* TV7: AOSP getDesiredHeight clamps max first (TextView.java:11608)
         * then min (:11627). */
        if (view->max_height_dp > 0.f) desired_h = std::min(desired_h, dp(ui, view->max_height_dp));
        desired_h = std::max(desired_h, dp(ui, view->min_height_dp));
    }
    const android_measured_size_t result{resolve_size(desired_w, spec_w),
                                                resolve_size(desired_h, spec_h)};
    view->measured_baseline = dp(ui, view->padding_top_dp) + metrics.baseline;
    return result;
}

} // namespace viewruntime::android
