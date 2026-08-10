#pragma once
/* Shared helpers for the Android UI pipeline tests. */

#include <viewruntime/viewruntime.h>
#include <viewruntime/android.h>

#include <cmath>
#include <cstdio>
#include <cstdlib>

static int g_failures = 0;

#define EXPECT(cond)                                                             \
    do {                                                                         \
        if (!(cond)) {                                                           \
            ++g_failures;                                                        \
            std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond);          \
        }                                                                        \
    } while (0)

#define EXPECT_NEAR(a, b, eps)                                                   \
    do {                                                                         \
        const double _a = (a), _b = (b);                                         \
        if (std::fabs(_a - _b) > (eps)) {                                        \
            ++g_failures;                                                        \
            std::printf("FAIL %s:%d: %s ~= %s (%.4f vs %.4f)\n",                 \
                        __FILE__, __LINE__, #a, #b, _a, _b);                     \
        }                                                                        \
    } while (0)

/* Deterministic proportional-ish text measurer, mirrors the host-side
 * approximation so metrics are stable and dependency-free. */
inline android_text_metrics_t test_text_measurer(
    const char* text, float size, float max_width, void*) {
    float width = 0.f;
    for (const char* p = text; p && *p; ++p) {
        const bool space = *p == ' ' || *p == '\t' || *p == '\n';
        width += (space ? 0.33f : 0.56f) * size;
    }
    if (max_width > 0.f && width > max_width) width = max_width;
    return {width, size * 1.2f, size * 0.8f};
}

inline android_ui_t make_ui(float density = 2.f) {
    android_ui_t ui = nullptr;
    const android_ui_options_t options = {density, density};
    android_ui_create(&options, &ui);
    android_ui_set_text_measurer(ui, test_text_measurer, nullptr);
    return ui;
}

inline android_view_t make_view(android_ui_t ui,
                                       android_view_class_t cls,
                                       int32_t id = 0) {
    android_view_t view = nullptr;
    android_view_create(ui, cls, id, &view);
    return view;
}

inline void set_wrap(android_view_t view) {
    android_layout_params_t lp{};
    lp.width.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lp.height.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lp.gravity = ANDROID_GRAVITY_UNSPECIFIED;
    android_view_set_layout_params(view, &lp);
}

inline void set_match(android_view_t view) {
    android_layout_params_t lp{};
    lp.width.kind = ANDROID_SIZE_KIND_MATCH_PARENT;
    lp.height.kind = ANDROID_SIZE_KIND_MATCH_PARENT;
    lp.gravity = ANDROID_GRAVITY_UNSPECIFIED;
    android_view_set_layout_params(view, &lp);
}

inline void frame_and_layout(android_ui_t ui, android_view_t root,
                             float w = 320.f, float h = 640.f) {
    android_ui_measure(ui, root, w, h);
    android_ui_layout(ui, root, 0.f, 0.f, w, h);
}

inline int test_result() {
    if (g_failures == 0) {
        std::printf("OK\n");
        return 0;
    }
    std::printf("%d FAILURES\n", g_failures);
    return 1;
}
