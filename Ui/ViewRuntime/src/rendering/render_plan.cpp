#include <viewruntime/viewruntime.h>

namespace {

constexpr uint32_t k_layout_invalidation =
    VISUAL_INVALIDATION_LAYOUT;
constexpr uint32_t k_display_list_invalidation =
    VISUAL_INVALIDATION_DISPLAY_LIST;
constexpr uint32_t k_paint_chunk_invalidation =
    VISUAL_INVALIDATION_PAINT_CHUNKS;
constexpr uint32_t k_scroll_invalidation =
    VISUAL_INVALIDATION_SCROLL;

constexpr uint32_t k_style_impact_layout =
    STYLE_IMPACT_LAYOUT | STYLE_IMPACT_TEXT_LAYOUT;
constexpr uint32_t k_style_impact_paint =
    STYLE_IMPACT_PAINT | STYLE_IMPACT_COMPOSITE;

bool is_true(bool_t value) {
    return value != FALSE;
}

} // namespace

extern "C" {

API uint32_t visual_invalidation_from_style_impact(uint32_t impact) {
    if ((impact & k_style_impact_layout) != 0u) {
        return k_layout_invalidation;
    }
    if ((impact & k_style_impact_paint) != 0u) {
        return k_paint_chunk_invalidation;
    }
    return VISUAL_INVALIDATION_NONE;
}

API uint32_t visual_invalidation_for_scroll(bool_t display_list_contains_patched_chunks) {
    uint32_t invalidation = k_scroll_invalidation;
    if (is_true(display_list_contains_patched_chunks)) {
        invalidation |= k_display_list_invalidation;
    }
    return invalidation;
}

API status_t render_plan_evaluate(
    const render_plan_input_t* input,
    render_plan_t* out_plan)
{
    if (!input || !out_plan) return ERROR_NULL_ARG;

    uint32_t normalized = input->invalidation;
    if (is_true(input->markup_dirty)) {
        normalized |= VISUAL_INVALIDATION_STYLE;
    }

    const bool display_list_available = is_true(input->display_list_available);
    const bool has_pending_pointer_move = is_true(input->has_pending_pointer_move);
    const bool markup_dirty = is_true(input->markup_dirty);
    const bool renderer_supports_incremental = is_true(input->renderer_supports_incremental);

    const bool requires_style = (normalized & VISUAL_INVALIDATION_STYLE) != 0u;
    const bool requires_layout = (normalized & VISUAL_INVALIDATION_LAYOUT) != 0u;
    const bool requires_display_list = (normalized & VISUAL_INVALIDATION_DISPLAY_LIST) != 0u;
    const bool requires_paint_chunks = (normalized & VISUAL_INVALIDATION_PAINT_CHUNKS) != 0u;
    const bool scroll_invalidated = (normalized & VISUAL_INVALIDATION_SCROLL) != 0u;

    const bool scroll_only =
        !has_pending_pointer_move &&
        !markup_dirty &&
        normalized == VISUAL_INVALIDATION_SCROLL;

    const bool pointer_only =
        has_pending_pointer_move &&
        !markup_dirty &&
        normalized == VISUAL_INVALIDATION_NONE;

    const bool allow_incremental =
        renderer_supports_incremental &&
        display_list_available &&
        !scroll_invalidated &&
        !scroll_only &&
        normalized == VISUAL_INVALIDATION_NONE;

    out_plan->normalized_invalidation = normalized;
    out_plan->requires_style = requires_style ? TRUE : FALSE;
    out_plan->requires_layout = requires_layout ? TRUE : FALSE;
    out_plan->requires_display_list = requires_display_list ? TRUE : FALSE;
    out_plan->requires_paint_chunks = requires_paint_chunks ? TRUE : FALSE;
    out_plan->pointer_only_render = pointer_only ? TRUE : FALSE;
    out_plan->scroll_only_render = scroll_only ? TRUE : FALSE;
    out_plan->allow_incremental_render = allow_incremental ? TRUE : FALSE;
    out_plan->use_scroll_composition =
        (scroll_only && display_list_available) ? TRUE : FALSE;

    return OK;
}

} // extern "C"
