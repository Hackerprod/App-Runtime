#include "android_types.h"

#include <algorithm>
#include <cmath>
#include <limits>

namespace viewruntime::android {

/* ── Gravity ───────────────────────────────────────────────────────── */

/* Places a child box of size (child_w, child_h) inside a container of size
 * (container_w, container_h) according to gravity flags. Margins are already
 * excluded from the container size by callers. Returns the top-left offset.
 *
 * Mirrors android.view.Gravity.apply: the gravity is normalized to the (LTR)
 * layout direction, then each axis is masked and switched on the exact masked
 * value (HORIZONTAL_GRAVITY_MASK / VERTICAL_GRAVITY_MASK), exactly like AOSP
 * Gravity.apply -> getAbsoluteGravity -> masked switch. */
void apply_gravity(int32_t gravity, float child_w, float child_h,
                   float container_w, float container_h,
                   float* out_x, float* out_y) {
    /* LTR runtime: START resolves to LEFT, END to RIGHT (AOSP applies the
     * layout direction to the relative bits before layout). */
    const int32_t g = gravity_normalize_ltr(gravity);
    const int32_t hgrav = g & ANDROID_GRAVITY_FILL_HORIZONTAL;   /* 0x07 */
    const int32_t vgrav = g & ANDROID_GRAVITY_FILL_VERTICAL;     /* 0x70 */
    float x = 0.f, y = 0.f;
    switch (hgrav) {
        case ANDROID_GRAVITY_CENTER_HORIZONTAL:
            x = (container_w - child_w) * 0.5f;
            break;
        case ANDROID_GRAVITY_RIGHT:
            x = container_w - child_w;
            break;
        case ANDROID_GRAVITY_LEFT:
        default:
            x = 0.f;
            break;
    }
    switch (vgrav) {
        case ANDROID_GRAVITY_CENTER_VERTICAL:
            y = (container_h - child_h) * 0.5f;
            break;
        case ANDROID_GRAVITY_BOTTOM:
            y = container_h - child_h;
            break;
        case ANDROID_GRAVITY_TOP:
        default:
            y = 0.f;
            break;
    }
    /* MC4: AOSP Gravity.apply does NOT clamp the offset — a child larger than
     * its container under CENTER/RIGHT gravity resolves to a NEGATIVE offset
     * (used by RelativeLayout's gravity pass, RelativeLayout.java:617-627), so
     * the clamp is removed to allow overflowing content to place/clip. */
    *out_x = x;
    *out_y = y;
}

/* ── Display text ──────────────────────────────────────────────────── */

const char* display_text(const android_view_s* view) {
    if (view->has_hint && view->text.empty()) return view->hint.c_str();
    return view->text.c_str();
}

/* ── Measure: plain View ───────────────────────────────────────────── */

/* AOSP View.onMeasure base (View.java:28452-28455): setMeasuredDimension(
 * getDefaultSize(getSuggestedMinimumWidth(), widthMeasureSpec), ...). The
 * desired size is getSuggestedMinimumWidth() = mMinWidth WITHOUT padding
 * (View.java:28612-28614, no background in this runtime). getDefaultSize
 * (View.java:28568-28583) returns specSize for EXACTLY/AT_MOST and the
 * desired size for UNSPECIFIED — this is NOT resolveSizeAndState, which would
 * clamp the desired size under AT_MOST. */
android_measured_size_t measure_base(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    const float desired_w = dp(ui, view->min_width_dp);
    const float desired_h = dp(ui, view->min_height_dp);
    auto default_size = [](float size, android_measure_spec_t spec) -> float {
        if (spec.mode == ANDROID_MEASURE_UNSPECIFIED) return size;
        return spec.size; /* AT_MOST and EXACTLY */
    };
    const android_measured_size_t result{default_size(desired_w, spec_w),
                                                default_size(desired_h, spec_h)};
    view->measured_baseline = -1.f; /* View.getBaseline() returns -1 */
    return result;
}

/* ── Measure dispatch ──────────────────────────────────────────────── */

android_measured_size_t measure_view(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    if (view->visibility == ANDROID_GONE) {
        view->measured = {0.f, 0.f};
        view->measured_baseline = 0.f;
        return view->measured;
    }
    android_measured_size_t result{};
    switch (view->cls) {
        case ANDROID_VIEW_LINEAR_LAYOUT: result = measure_linear(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_FRAME_LAYOUT:  result = measure_frame(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_RELATIVE_LAYOUT: result = measure_relative(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_SCROLL_VIEW:   result = measure_scroll(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_GRID_LAYOUT:   result = measure_grid(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_LIST_VIEW:
        case ANDROID_VIEW_RECYCLER_VIEW:    result = measure_list(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_CONSTRAINT_LAYOUT:
            result = measure_constraint(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_TEXT_VIEW:
        case ANDROID_VIEW_BUTTON:
        case ANDROID_VIEW_EDIT_TEXT:     result = measure_text_view(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_CHECK_BOX:
        case ANDROID_VIEW_RADIO_BUTTON:  result = measure_checkable(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_IMAGE_VIEW:    result = measure_image(view, spec_w, spec_h, ui); break;
        case ANDROID_VIEW_PROGRESS_BAR:  result = measure_progress(view, spec_w, spec_h, ui); break;
        default: result = measure_base(view, spec_w, spec_h, ui); break;
    }
    view->measured = result;
    return result;
}

/* ── Layout dispatch ───────────────────────────────────────────────── */

void layout_view(android_view_s* view, float x, float y, float w, float h,
                 const android_ui_s* ui) {
    if (view->visibility == ANDROID_GONE) {
        view->bounds = {x, y, 0.f, 0.f};
        return;
    }
    switch (view->cls) {
        case ANDROID_VIEW_LINEAR_LAYOUT: layout_linear(view, x, y, w, h, ui); break;
        case ANDROID_VIEW_FRAME_LAYOUT:  layout_frame(view, x, y, w, h, ui); break;
        case ANDROID_VIEW_RELATIVE_LAYOUT: layout_relative(view, x, y, w, h, ui); break;
        case ANDROID_VIEW_SCROLL_VIEW:   layout_scroll(view, x, y, w, h, ui); break;
        case ANDROID_VIEW_GRID_LAYOUT:   layout_grid(view, x, y, w, h, ui); break;
        case ANDROID_VIEW_LIST_VIEW:
        case ANDROID_VIEW_RECYCLER_VIEW:   layout_list(view, x, y, w, h, ui); break;
        case ANDROID_VIEW_CONSTRAINT_LAYOUT:
            layout_constraint(view, x, y, w, h, ui); break;
        default: view->bounds = {x, y, w, h}; break;
    }
}

/* ── Hit testing ───────────────────────────────────────────────────── */

android_view_s* hit_test(android_view_s* view, float px, float py) {
    /* AOSP View.canReceivePointerEvents gates on VISIBILITY only
     * (View.java:16638-16640); a disabled view is still hit (dispatchTouchEvent
     * delivers to it, it just does not consume the event). */
    if (view->visibility != ANDROID_VISIBLE) return nullptr;
    if (px < view->bounds.x || py < view->bounds.y ||
        px >= view->bounds.x + view->bounds.width ||
        py >= view->bounds.y + view->bounds.height) {
        return nullptr;
    }
    for (auto it = view->children.rbegin(); it != view->children.rend(); ++it) {
        if (android_view_s* hit = hit_test(*it, px, py)) return hit;
    }
    return view;
}

} // namespace viewruntime::android

extern "C" {

API status_t android_ui_measure(
    android_ui_t ui, android_view_t root, float width_px, float height_px) {
    if (!ui || !root || root->ui != ui) return ERROR_NULL_ARG;
    if (width_px <= 0.f || height_px <= 0.f || !std::isfinite(width_px) || !std::isfinite(height_px)) {
        return ERROR_INVALID_STATE;
    }
    viewruntime::android::measure_view(root,
        {width_px, ANDROID_MEASURE_EXACTLY},
        {height_px, ANDROID_MEASURE_EXACTLY}, ui);
    return OK;
}

API status_t android_ui_layout(
    android_ui_t ui, android_view_t root,
    float x, float y, float width_px, float height_px) {
    if (!ui || !root || root->ui != ui) return ERROR_NULL_ARG;
    if (width_px < 0.f || height_px < 0.f || !std::isfinite(width_px) || !std::isfinite(height_px)) {
        return ERROR_INVALID_STATE;
    }
    viewruntime::android::layout_view(root, x, y, width_px, height_px, ui);
    return OK;
}

API status_t android_view_get_bounds(
    android_view_t view, rectf* out_bounds) {
    if (!view || !out_bounds) return ERROR_NULL_ARG;
    *out_bounds = view->bounds;
    return OK;
}

API status_t android_view_get_measured_size(
    android_view_t view, sizef* out_size) {
    if (!view || !out_size) return ERROR_NULL_ARG;
    *out_size = {view->measured.width, view->measured.height};
    return OK;
}

API android_view_t android_ui_hit_test(
    android_ui_t ui, android_view_t root, float x, float y) {
    if (!ui || !root || root->ui != ui) return nullptr;
    return viewruntime::android::hit_test(root, x, y);
}

API status_t android_view_set_scroll_offset(
    android_view_t view, float x, float y) {
    if (!view) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_SCROLL_VIEW &&
        view->cls != ANDROID_VIEW_LIST_VIEW &&
        view->cls != ANDROID_VIEW_RECYCLER_VIEW) return ERROR_INVALID_STATE;
    view->scroll_x = x < 0.f ? 0.f : x;
    view->scroll_y = y < 0.f ? 0.f : y;
    return OK;
}

API scroll_metrics_t android_view_get_scroll_metrics(
    android_view_t view) {
    if (!view) return {};
    return view->scroll_metrics;
}

} // extern "C"
