/* android.view input dispatch — EXACT AOSP reverse-engineering port (C++).

 * Sources of truth (Ui/ViewRuntime/.tmp/):
 *   - View.java:
 *       dispatchTouchEvent           16551-16591
 *       performOnTouchCallback       16597-16625
 *       onFilterTouchEventForSecurity 16650-16658
 *       performClick                 8072-8092  (decides + calls listener)
 *       performLongClick             8118-8141
 *       onTouchEvent                 18059-18266 (clickable/disabled/pressed/
 *                                    prepressed/tap/slop/long-press/up)
 *       checkForLongClick            29831-29844
 *       isInScrollingContainer       18290-18296 (ViewGroup.shouldDelayChildPressedState)
 *   - ViewGroup.java:
 *       dispatchTouchEvent           2647-2766+ (intercept, mFirstTouchTarget)
 *   - ViewConfiguration.java:
 *       PRESSED_STATE_DURATION=64, DEFAULT_LONG_PRESS_TIMEOUT=400,
 *       TAP_TIMEOUT=100, TOUCH_SLOP=8dp.
 *   - KeyEvent.java: ACTION_DOWN=0/UP=1, KEYCODE_ENTER=66/SPACE=62/DPAD_CENTER=23.
 *
 * The host (ViewRootImpl role) dispatches raw ACTION_* / key events; this file
 * owns the ENTIRE view-tree gesture machine (mFirstTouchTarget, interception,
 * touch slop, long-press, pressed visuals, performClick decision). When the
 * click decision is made it calls the registered callback with the view's
 * resource id — the host then runs the guest DEX OnClickListener. No guest
 * bytecode runs here.
 */

#include "android_types.h"

#include <chrono>
#include <cmath>

namespace viewruntime::android {

static uint64_t now_ms() {
    return static_cast<uint64_t>(
        std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now().time_since_epoch()).count());
}

/* View.onTouchEvent clickable (View.java:18065-18067): CLICKABLE |
 * LONG_CLICKABLE | CONTEXT_CLICKABLE. The runtime models CLICKABLE only
 * (set via android_view_set_clickable); the checkable classes consume too. */
static bool is_clickable(const android_view_s* view) {
    if (view->clickable) return true;
    switch (view->cls) {
        case ANDROID_VIEW_BUTTON:
        case ANDROID_VIEW_CHECK_BOX:
        case ANDROID_VIEW_RADIO_BUTTON:
            return true;
        default:
            return false;
    }
}

/* View.isInScrollingContainer (View.java:18290-18296): any ancestor with
 * shouldDelayChildPressedState (ScrollView/ListView/RecyclerView). */
static bool is_in_scrolling_container(const android_view_s* view) {
    const android_view_s* p = view->parent;
    while (p != nullptr) {
        if (p->cls == ANDROID_VIEW_SCROLL_VIEW ||
            p->cls == ANDROID_VIEW_LIST_VIEW ||
            p->cls == ANDROID_VIEW_RECYCLER_VIEW) {
            return true;
        }
        p = p->parent;
    }
    return false;
}

/* View.pointInView with slop (used by ACTION_MOVE, View.java:18236). */
static bool point_in_view(const android_view_s* v, float x, float y, float slop) {
    return x >= v->bounds.x - slop && y >= v->bounds.y - slop &&
           x < v->bounds.x + v->bounds.width + slop &&
           y < v->bounds.y + v->bounds.height + slop;
}

/* Dispatch to the view's own onTouchEvent (View.java:18059). Returns true when
 * the view consumed the event (clickable). Mutates ui->gesture state. */
static bool view_on_touch_event(android_ui_s* ui, android_view_s* view,
                                int32_t action, float x, float y) {
    auto& g = ui->gesture;
    const bool clickable = is_clickable(view);

    /* View.java:18069-18078 — disabled: consume but don't respond; only clear
     * the press visual on UP. */
    if (!view->enabled) {
        if (action == ANDROID_ACTION_UP && g.pressed) {
            view->pressed = false; /* setPressed(false) */
        }
        return clickable;
    }

    if (clickable) {
        switch (action) {
            case ANDROID_ACTION_UP: {
                /* View.java:18087-18150 */
                const bool prepressed = g.prepressed;
                if (g.pressed || prepressed) {
                    if (prepressed) {
                        /* Released before the tap timeout: show the press
                         * visual now, before the click (java:18109-18115). */
                        view->pressed = true;
                        g.pressed = true;
                    }
                    if (!g.has_performed_long_press && !g.ignore_next_up) {
                        /* Tap → performClick (java:18117-18132). */
                        if (g.on_click && view->resource_id != 0) {
                            g.on_click(view->resource_id, g.click_user_data);
                        }
                    }
                    /* Unpress after pressed-state duration (java:18135-18145). */
                    view->pressed = false;
                    g.pressed = false;
                    g.prepressed = false;
                }
                g.ignore_next_up = false;
                g.has_performed_long_press = false;
                g.long_press_pending = false;
                g.tap_pending = false;
                g.touch_target = nullptr;
                break;
            }
            case ANDROID_ACTION_DOWN: {
                /* View.java:18152-18192 */
                g.has_performed_long_press = false;
                g.down_x = x; g.down_y = y;
                g.last_x = x; g.last_y = y;
                g.down_ms = now_ms();
                if (is_in_scrolling_container(view)) {
                    /* PREPRESSED + tap timeout (java:18176-18183). */
                    g.prepressed = true;
                    g.pressed = false;
                    view->pressed = false;
                    g.tap_deadline_ms = now_ms() + ANDROID_VIEW_CONFIG_TAP_TIMEOUT_MS;
                    g.tap_pending = true;
                } else {
                    /* Immediate pressed feedback + long-press (java:18184-18192). */
                    view->pressed = true;
                    g.pressed = true;
                    g.prepressed = false;
                }
                /* checkForLongClick (java:18187-18191, 29831-29844): only
                 * when LONG_CLICKABLE; the runtime models long-click for
                 * clickable views with an on_long_click callback. */
                if (g.on_long_click && view->resource_id != 0) {
                    g.long_press_deadline_ms =
                        now_ms() + ANDROID_VIEW_CONFIG_LONG_PRESS_TIMEOUT_MS;
                    g.long_press_pending = true;
                }
                g.touch_target = view;
                break;
            }
            case ANDROID_ACTION_MOVE: {
                /* View.java:18207-18245 — leave the touch slop → cancel
                 * press + long-press (the gesture becomes a scroll). */
                g.last_x = x; g.last_y = y;
                const float slop = ANDROID_VIEW_CONFIG_TOUCH_SLOP_DP * ui->density;
                if (!point_in_view(view, x, y, slop)) {
                    g.tap_pending = false;
                    g.long_press_pending = false;
                    if (g.pressed || g.prepressed) {
                        view->pressed = false;
                        g.pressed = false;
                        g.prepressed = false;
                    }
                }
                break;
            }
            case ANDROID_ACTION_CANCEL: {
                /* View.java:18195-18205 */
                view->pressed = false;
                g.pressed = false;
                g.prepressed = false;
                g.tap_pending = false;
                g.long_press_pending = false;
                g.has_performed_long_press = false;
                g.ignore_next_up = false;
                g.touch_target = nullptr;
                break;
            }
        }
        return true; /* clickable consumes (View.java:18262) */
    }
    return false;
}

/* ViewGroup.dispatchTouchEvent (ViewGroup.java:2647): on ACTION_DOWN pick the
 * deepest clickable target and fix it as mFirstTouchTarget; afterwards deliver
 * the whole gesture to that target without re-hit-testing. Scroll containers
 * (onInterceptTouchEvent) consume the gesture for scrolling — modeled by the
 * runtime as: a scroll container never becomes the click target, and the
 * dispatch walks past it to clickable descendants. */
static void dispatch_touch(android_ui_s* ui, android_view_s* root,
                           int32_t action, float x, float y) {
    auto& g = ui->gesture;

    if (action == ANDROID_ACTION_DOWN) {
        /* New gesture: reset all previous state (ViewGroup.java:2664-2670). */
        g.touch_target = nullptr;
        g.pressed = false;
        g.prepressed = false;
        g.has_performed_long_press = false;
        g.ignore_next_up = false;
        g.long_press_pending = false;
        g.tap_pending = false;

        /* Find the target: the deepest view under the point that can receive
         * pointer events (ViewGroup.java:2756-2760: canReceivePointerEvents +
         * point in view). The existing hit_test walks children reverse and
         * gates on VISIBILITY — exactly ViewGroup's scan. */
        android_view_s* hit = hit_test(root, x, y);
        if (hit != nullptr) {
            /* Walk up from the hit leaf to the deepest CLICKABLE view (a leaf
             * may be inside a clickable parent, e.g. a Button's child). */
            android_view_s* target = hit;
            while (target != nullptr && !is_clickable(target) && target->parent != nullptr) {
                /* The leaf itself can be the target if clickable; otherwise
                 * check ancestors for clickable (Button, checkable). */
                android_view_s* cur = target->parent;
                bool found = false;
                while (cur != nullptr) {
                    if (is_clickable(cur)) { target = cur; found = true; break; }
                    cur = cur->parent;
                }
                if (found) break;
                target = nullptr;
                break;
            }
            if (target == nullptr && is_clickable(hit)) target = hit;
            g.touch_target = target;
            if (target != nullptr) {
                g.focused = target; /* touch-mode focus (View.java:18105) */
                view_on_touch_event(ui, target, action, x, y);
            }
        }
        return;
    }

    /* Non-DOWN: deliver to the fixed target (mFirstTouchTarget,
     * ViewGroup.java:2675/2694). No target → nothing (the gesture is not ours
     * to handle; AOSP dispatches to the ViewGroup itself, but a non-clickable
     * group with no target returns false). */
    if (g.touch_target != nullptr) {
        view_on_touch_event(ui, g.touch_target, action, x, y);
    }
}

/* View.performLongClick path (View.java:8118, CheckForLongPress.run):
 * fires when the long-press deadline passes without a move-out-of-slop. */
static void fire_long_press(android_ui_s* ui) {
    auto& g = ui->gesture;
    android_view_s* target = g.touch_target;
    if (target == nullptr || !g.long_press_pending) return;
    if (!is_clickable(target)) { g.long_press_pending = false; return; }
    g.long_press_pending = false;
    g.has_performed_long_press = true;
    g.ignore_next_up = true; /* the UP after a long-press must NOT click */
    if (g.on_long_click && target->resource_id != 0) {
        g.on_long_click(target->resource_id, g.click_user_data);
    }
    /* AOSP keeps the pressed visual until UP; long-press does not unpress. */
}

} // namespace viewruntime::android

extern "C" {

API status_t android_ui_dispatch_touch(
    android_ui_t ui, android_view_t root, int32_t action, float x, float y) {
    if (!ui || !root || root->ui != ui) return ERROR_NULL_ARG;
    if (action < ANDROID_ACTION_DOWN || action > ANDROID_ACTION_CANCEL)
        return ERROR_INVALID_STATE;
    viewruntime::android::dispatch_touch(ui, root, action, x, y);
    return OK;
}

API status_t android_ui_dispatch_key(
    android_ui_t ui, android_view_t root, int32_t action, int32_t key_code) {
    if (!ui || !root || root->ui != ui) return ERROR_NULL_ARG;
    /* Enter/Space (and DPAD_CENTER) on the focused view → performClick, like
     * ViewRootImpl key dispatch to the focused view. Only the DOWN triggers
     * the click (AOSP KEY_UP after KEY_DOWN is ignored for clicks). */
    if (action != ANDROID_KEY_ACTION_DOWN) return OK;
    if (key_code != ANDROID_KEYCODE_ENTER &&
        key_code != ANDROID_KEYCODE_DPAD_CENTER &&
        key_code != ANDROID_KEYCODE_SPACE) {
        return OK;
    }
    auto& g = ui->gesture;
    if (g.focused != nullptr && viewruntime::android::is_clickable(g.focused)) {
        if (g.on_click && g.focused->resource_id != 0) {
            g.on_click(g.focused->resource_id, g.click_user_data);
        }
    }
    return OK;
}

API void android_ui_set_click_callback(
    android_ui_t ui,
    android_on_click_fn on_click,
    android_on_click_fn on_long_click,
    void* user_data) {
    if (!ui) return;
    ui->gesture.on_click = on_click;
    ui->gesture.on_long_click = on_long_click;
    ui->gesture.click_user_data = user_data;
}

API int32_t android_ui_gesture_poll(android_ui_t ui) {
    if (!ui) return 0;
    auto& g = ui->gesture;
    const uint64_t now = viewruntime::android::now_ms();
    int32_t fired = 0;
    /* Long-press deadline (ViewConfiguration.getLongPressTimeout). */
    if (g.long_press_pending && now >= g.long_press_deadline_ms) {
        viewruntime::android::fire_long_press(ui);
        fired = 1;
    }
    /* Tap deadline (prepressed in a scrolling container): show the press now
     * (View.java:18183 CheckForTap.run sets pressed). */
    if (g.tap_pending && now >= g.tap_deadline_ms) {
        g.tap_pending = false;
        if (g.touch_target != nullptr && !g.pressed) {
            g.touch_target->pressed = true;
            g.pressed = true;
            g.prepressed = false;
        }
        fired = 1;
    }
    return fired;
}

API bool_t android_ui_gesture_active(android_ui_t ui) {
    if (!ui) return FALSE;
    return ui->gesture.touch_target != nullptr ? TRUE : FALSE;
}

} // extern "C"
