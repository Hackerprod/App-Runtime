#include <viewruntime/viewruntime.h>

#include <cstdio>
#include <cstdlib>

namespace {

void require(bool condition, const char* message) {
    if (!condition) {
        std::fprintf(stderr, "FAIL: %s\n", message);
        std::exit(1);
    }
}

render_plan_t evaluate(const render_plan_input_t& input) {
    render_plan_t plan{};
    const status_t status = render_plan_evaluate(&input, &plan);
    require(status == OK, "render plan should evaluate");
    return plan;
}

void style_impact_maps_to_minimal_invalidation() {
    require(visual_invalidation_from_style_impact(STYLE_IMPACT_PAINT) ==
                VISUAL_INVALIDATION_PAINT_CHUNKS,
        "paint impact should invalidate paint chunks only");
    require(visual_invalidation_from_style_impact(STYLE_IMPACT_COMPOSITE) ==
                VISUAL_INVALIDATION_PAINT_CHUNKS,
        "composite impact should invalidate paint chunks only");
    require(visual_invalidation_from_style_impact(STYLE_IMPACT_LAYOUT) ==
                VISUAL_INVALIDATION_LAYOUT,
        "layout impact should invalidate layout");
    require(visual_invalidation_from_style_impact(STYLE_IMPACT_TEXT_LAYOUT) ==
                VISUAL_INVALIDATION_LAYOUT,
        "text layout impact should invalidate layout");
    require(visual_invalidation_from_style_impact(STYLE_IMPACT_CURSOR |
                                                        STYLE_IMPACT_HITTEST) ==
                VISUAL_INVALIDATION_NONE,
        "cursor and hit-test impact should not rebuild visuals");
}

void scroll_invalidation_rebuilds_after_patched_chunks() {
    require(visual_invalidation_for_scroll(FALSE) ==
                VISUAL_INVALIDATION_SCROLL,
        "plain scroll should invalidate scroll only");
    require(visual_invalidation_for_scroll(TRUE) ==
                (VISUAL_INVALIDATION_SCROLL |
                 VISUAL_INVALIDATION_DISPLAY_LIST),
        "scroll after patched chunks should rebuild canonical display list");
}

void pure_scroll_uses_scroll_composition() {
    render_plan_input_t input{};
    input.invalidation = VISUAL_INVALIDATION_SCROLL;
    input.display_list_available = TRUE;
    input.renderer_supports_incremental = TRUE;

    const render_plan_t plan = evaluate(input);
    require(plan.scroll_only_render == TRUE, "pure scroll should be scroll-only");
    require(plan.use_scroll_composition == TRUE, "pure scroll should enable scroll composition");
    require(plan.allow_incremental_render == FALSE, "pure scroll should not use paint-chunk incremental path");
}

void mixed_scroll_and_display_list_disables_fast_paths() {
    render_plan_input_t input{};
    input.invalidation = VISUAL_INVALIDATION_SCROLL |
                         VISUAL_INVALIDATION_DISPLAY_LIST;
    input.display_list_available = TRUE;
    input.renderer_supports_incremental = TRUE;

    const render_plan_t plan = evaluate(input);
    require(plan.scroll_only_render == FALSE, "mixed scroll should not be scroll-only");
    require(plan.use_scroll_composition == FALSE, "mixed scroll should not use retained scroll composition");
    require(plan.allow_incremental_render == FALSE, "mixed scroll should not use incremental paint chunks");
    require(plan.requires_display_list == TRUE, "mixed scroll should rebuild display list");
}

void stable_visuals_allow_incremental_renderer() {
    render_plan_input_t input{};
    input.display_list_available = TRUE;
    input.renderer_supports_incremental = TRUE;

    const render_plan_t plan = evaluate(input);
    require(plan.allow_incremental_render == TRUE, "stable frame should allow incremental renderer");
    require(plan.use_scroll_composition == FALSE, "stable non-scroll frame should not use scroll composition");
}

void unavailable_display_list_blocks_fast_paths() {
    render_plan_input_t input{};
    input.renderer_supports_incremental = TRUE;

    const render_plan_t plan = evaluate(input);
    require(plan.allow_incremental_render == FALSE, "missing display list blocks incremental renderer");
    require(plan.use_scroll_composition == FALSE, "missing display list blocks scroll composition");
}

} // namespace

int main() {
    style_impact_maps_to_minimal_invalidation();
    scroll_invalidation_rebuilds_after_patched_chunks();
    pure_scroll_uses_scroll_composition();
    mixed_scroll_and_display_list_disables_fast_paths();
    stable_visuals_allow_incremental_renderer();
    unavailable_display_list_blocks_fast_paths();
    return 0;
}
