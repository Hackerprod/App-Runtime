#include "android_types.h"

#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <limits>

namespace viewruntime::android {

android_text_metrics_t measure_text(
    const android_ui_s* ui, const char* text, float size_px, float max_width) {
    if (ui && ui->text_measurer) {
        return ui->text_measurer(text, size_px, max_width, ui->text_measurer_data);
    }
    /* Deterministic fallback: proportional-ish estimate so callers without a
     * host measurer still get stable, bounded metrics. */
    float width = 0.f;
    if (text) {
        for (const char* p = text; *p; ++p) {
            const bool space = *p == ' ' || *p == '\t' || *p == '\n';
            width += (space ? 0.33f : 0.56f) * size_px;
        }
    }
    if (max_width > 0.f && width > max_width) width = max_width;
    return {width, size_px * 1.2f, size_px * 0.8f};
}

void apply_class_defaults(android_view_s* view) {
    switch (view->cls) {
        case ANDROID_VIEW_LINEAR_LAYOUT:
            /* AOSP LinearLayout default gravity is START|TOP (the relative
             * bit, NOT a pre-resolved LEFT). Layout resolves it per direction
             * via getAbsoluteGravity (LinearLayout.java:1786): LEFT for LTR,
             * RIGHT for RTL. */
            view->gravity = ANDROID_GRAVITY_START | ANDROID_GRAVITY_TOP;
            break;
        case ANDROID_VIEW_GRID_LAYOUT:
            /* AOSP GridLayout defaults to HORIZONTAL orientation. */
            view->orientation = ANDROID_HORIZONTAL;
            break;
        case ANDROID_VIEW_BUTTON:
            view->text_gravity = ANDROID_GRAVITY_CENTER;
            view->background_color = {1.f, 0.878f, 0.878f, 0.878f};
            view->has_background = true;
            view->min_width_dp = 88.f;
            view->min_height_dp = 48.f;
            break;
        case ANDROID_VIEW_EDIT_TEXT:
            view->single_line = true;
            view->text_gravity = ANDROID_GRAVITY_LEFT | ANDROID_GRAVITY_CENTER_VERTICAL;
            view->background_color = {1.f, 0.961f, 0.961f, 0.961f};
            view->has_background = true;
            view->padding_left_dp = 8.f;
            view->padding_right_dp = 8.f;
            break;
        case ANDROID_VIEW_CHECK_BOX:
        case ANDROID_VIEW_RADIO_BUTTON:
            view->text_gravity = ANDROID_GRAVITY_LEFT | ANDROID_GRAVITY_CENTER_VERTICAL;
            break;
        case ANDROID_VIEW_PROGRESS_BAR:
            view->progress_color = {1.f, 0.20f, 0.55f, 0.95f};
            view->track_color = {1.f, 0.85f, 0.85f, 0.85f};
            break;
        default:
            break;
    }
}

} // namespace viewruntime::android

extern "C" {

API status_t android_ui_create(
    const android_ui_options_t* options, android_ui_t* out_ui) {
    if (!out_ui) return ERROR_NULL_ARG;
    *out_ui = nullptr;
    auto* ui = new (std::nothrow) android_ui_s();
    if (!ui) return ERROR_OUT_OF_MEMORY;
    if (options) {
        ui->density = options->density > 0.f ? options->density : 1.f;
        ui->scaled_density = options->scaled_density > 0.f ? options->scaled_density : ui->density;
    }
    *out_ui = ui;
    return OK;
}

API void android_ui_destroy(android_ui_t ui) {
    if (!ui) return;
    for (android_view_s* view : ui->all_views) {
        delete view;
    }
    viewruntime::android::android_ui_release_font(ui);
    delete ui;
}

API void android_ui_set_text_measurer(
    android_ui_t ui, android_text_measurer_fn measurer, void* user_data) {
    if (!ui) return;
    ui->text_measurer = measurer;
    ui->text_measurer_data = user_data;
}

API void android_ui_set_image_dimensions(
    android_ui_t ui, android_image_dimensions_fn dimensions, void* user_data) {
    if (!ui) return;
    ui->image_dimensions = dimensions;
    ui->image_dimensions_data = user_data;
}

API status_t android_ui_clear(android_ui_t ui) {
    if (!ui) return ERROR_NULL_ARG;
    for (android_view_s* view : ui->all_views) {
        delete view;
    }
    ui->roots.clear();
    ui->all_views.clear();
    ui->id_index.clear();
    return OK;
}

API status_t android_view_create(
    android_ui_t ui, android_view_class_t view_class,
    int32_t resource_id, android_view_t* out_view) {
    if (!ui || !out_view) return ERROR_NULL_ARG;
    *out_view = nullptr;
    if (view_class < ANDROID_VIEW_VIEW || view_class > ANDROID_VIEW_BARRIER) {
        return ERROR_INVALID_STATE;
    }
    auto* view = new (std::nothrow) android_view_s();
    if (!view) return ERROR_OUT_OF_MEMORY;
    view->ui = ui;
    view->cls = view_class;
    view->resource_id = resource_id;
    /* ConstraintLayout.LayoutParams bias defaults to 0.5 (AOSP field init). */
    view->lp.constraint.bias_h = 0.5f;
    view->lp.constraint.bias_v = 0.5f;
    viewruntime::android::apply_class_defaults(view);
    ui->all_views.push_back(view);
    if (resource_id != 0) {
        ui->id_index[resource_id] = view;
    }
    *out_view = view;
    return OK;
}

API status_t android_view_add_child(
    android_ui_t ui, android_view_t parent, android_view_t child) {
    if (!ui || !parent || !child) return ERROR_NULL_ARG;
    if (child->ui != ui || parent->ui != ui) return ERROR_INVALID_STATE;
    if (child->parent != nullptr) return ERROR_INVALID_STATE; /* already has a parent */
    if (child == parent) return ERROR_INVALID_STATE;           /* no self-adoption */
    parent->children.push_back(child);
    child->parent = parent;
    auto it = std::find(ui->roots.begin(), ui->roots.end(), child);
    if (it != ui->roots.end()) ui->roots.erase(it);
    return OK;
}

API status_t android_view_remove_child(
    android_ui_t ui, android_view_t parent, android_view_t child) {
    if (!ui || !parent || !child) return ERROR_NULL_ARG;
    if (child->parent != parent) return ERROR_INVALID_STATE;
    auto it = std::find(parent->children.begin(), parent->children.end(), child);
    if (it == parent->children.end()) return ERROR_INVALID_STATE;
    parent->children.erase(it);
    child->parent = nullptr;
    ui->roots.push_back(child);
    return OK;
}

API status_t android_view_detach(
    android_ui_t ui, android_view_t view) {
    if (!ui || !view) return ERROR_NULL_ARG;
    if (view->ui != ui) return ERROR_INVALID_STATE;
    if (!view->parent) return OK; /* already detached */
    return android_view_remove_child(ui, view->parent, view);
}

API android_view_t android_view_get_parent(android_view_t view) {
    return view ? view->parent : nullptr;
}

API int32_t android_view_get_child_count(android_view_t view) {
    return view ? static_cast<int32_t>(view->children.size()) : 0;
}

API android_view_t android_view_get_child(
    android_view_t view, int32_t index) {
    if (!view || index < 0 || index >= static_cast<int32_t>(view->children.size())) return nullptr;
    return view->children[static_cast<size_t>(index)];
}

API android_view_t android_ui_find_view_by_id(
    android_ui_t ui, int32_t resource_id) {
    if (!ui) return nullptr;
    auto it = ui->id_index.find(resource_id);
    return it == ui->id_index.end() ? nullptr : it->second;
}

API int32_t android_view_get_class(android_view_t view) {
    return view ? static_cast<int32_t>(view->cls) : 0;
}

API int32_t android_view_get_resource_id(android_view_t view) {
    return view ? view->resource_id : 0;
}

API status_t android_view_set_layout_params(
    android_view_t view, const android_layout_params_t* params) {
    if (!view || !params) return ERROR_NULL_ARG;
    if (params->width.kind < ANDROID_SIZE_KIND_MATCH_PARENT ||
        params->width.kind > ANDROID_SIZE_KIND_EXACT ||
        params->height.kind < ANDROID_SIZE_KIND_MATCH_PARENT ||
        params->height.kind > ANDROID_SIZE_KIND_EXACT) {
        return ERROR_INVALID_STATE;
    }
    if (params->width.value_dp < 0.f || params->height.value_dp < 0.f ||
        params->margins_dp.left < 0.f || params->margins_dp.top < 0.f ||
        params->margins_dp.right < 0.f || params->margins_dp.bottom < 0.f ||
        params->weight < 0.f) {
        return ERROR_INVALID_STATE;
    }
    const android_constraint_params_t previous_constraint = view->lp.constraint;
    view->lp = *params;
    const android_constraint_params_t& c = view->lp.constraint;
    /* An all-zero constraint block means the caller did not touch it; keep
     * the view's defaults (bias 0.5) instead of resetting to 0. */
    if (c.constraint_count == 0 && c.bias_h == 0.f && c.bias_v == 0.f &&
        c.dimension_ratio == 0.f && c.match_default_w == 0 &&
        c.match_default_h == 0 && c.chain_style_h == 0 && c.chain_style_v == 0 &&
        c.match_min_w_dp == 0.f && c.match_max_w_dp == 0.f &&
        c.match_min_h_dp == 0.f && c.match_max_h_dp == 0.f &&
        c.match_percent_w == 0.f && c.match_percent_h == 0.f) {
        view->lp.constraint = previous_constraint;
    }
    return OK;
}

API status_t android_view_set_visibility(
    android_view_t view, int32_t visibility) {
    if (!view) return ERROR_NULL_ARG;
    if (visibility != ANDROID_VISIBLE &&
        visibility != ANDROID_INVISIBLE &&
        visibility != ANDROID_GONE) {
        return ERROR_INVALID_STATE;
    }
    view->visibility = visibility;
    return OK;
}

API status_t android_view_set_enabled(
    android_view_t view, bool_t enabled) {
    if (!view) return ERROR_NULL_ARG;
    view->enabled = enabled != FALSE;
    return OK;
}

API status_t android_view_set_background_color(
    android_view_t view, color_rgba color) {
    if (!view) return ERROR_NULL_ARG;
    view->background_color = color;
    view->has_background = true;
    /* A flat color set directly replaces any drawable source. */
    view->background_drawable_id = 0;
    return OK;
}

API status_t android_view_set_pressed(
    android_view_t view, bool_t pressed) {
    if (!view) return ERROR_NULL_ARG;
    view->pressed = pressed != FALSE;
    return OK;
}

API status_t android_view_set_hovered(
    android_view_t view, bool_t hovered) {
    if (!view) return ERROR_NULL_ARG;
    view->hovered = hovered != FALSE;
    return OK;
}

API status_t android_view_set_padding_dp(
    android_view_t view, float padding_dp) {
    if (!view || padding_dp < 0.f) return ERROR_NULL_ARG;
    view->padding_left_dp = view->padding_top_dp = view->padding_right_dp = view->padding_bottom_dp = padding_dp;
    return OK;
}

API status_t android_view_set_padding_edges_dp(
    android_view_t view, thicknessf padding_dp) {
    if (!view || padding_dp.left < 0.f || padding_dp.top < 0.f ||
        padding_dp.right < 0.f || padding_dp.bottom < 0.f) {
        return ERROR_NULL_ARG;
    }
    view->padding_left_dp = padding_dp.left;
    view->padding_top_dp = padding_dp.top;
    view->padding_right_dp = padding_dp.right;
    view->padding_bottom_dp = padding_dp.bottom;
    return OK;
}

API status_t android_view_set_min_size_dp(
    android_view_t view, float min_width_dp, float min_height_dp) {
    if (!view || min_width_dp < 0.f || min_height_dp < 0.f) return ERROR_NULL_ARG;
    view->min_width_dp = min_width_dp;
    view->min_height_dp = min_height_dp;
    return OK;
}

API status_t android_view_set_content_description(
    android_view_t view, const char* description) {
    if (!view) return ERROR_NULL_ARG;
    view->content_description = description ? description : "";
    return OK;
}

API status_t android_view_set_click_handler(
    android_view_t view, const char* handler) {
    if (!view) return ERROR_NULL_ARG;
    view->click_handler = handler ? handler : "";
    /* View.java:7868 setOnClickListener sets CLICKABLE; the XML onClick
     * (DeclaredOnClickListener) also makes the view clickable. */
    if (handler && *handler) view->clickable = true;
    return OK;
}

API status_t android_view_set_clickable(
    android_view_t view, bool_t clickable) {
    if (!view) return ERROR_NULL_ARG;
    view->clickable = clickable != FALSE;
    return OK;
}

API status_t android_view_set_orientation(
    android_view_t view, int32_t orientation) {
    if (!view) return ERROR_NULL_ARG;
    if (orientation != ANDROID_HORIZONTAL && orientation != ANDROID_VERTICAL) {
        return ERROR_INVALID_STATE;
    }
    view->orientation = orientation;
    return OK;
}

API status_t android_view_set_baseline_aligned(
    android_view_t view, bool_t baseline_aligned) {
    if (!view) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_LINEAR_LAYOUT) return ERROR_INVALID_STATE;
    view->baseline_aligned = baseline_aligned != FALSE;
    return OK;
}

API status_t android_view_set_weight_sum(
    android_view_t view, float weight_sum) {
    if (!view) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_LINEAR_LAYOUT) return ERROR_INVALID_STATE;
    view->weight_sum = weight_sum > 0.f ? weight_sum : 0.f;
    return OK;
}

API status_t android_view_set_layout_direction(
    android_view_t view, int32_t direction) {
    if (!view) return ERROR_NULL_ARG;
    if (direction != ANDROID_LAYOUT_DIRECTION_LTR &&
        direction != ANDROID_LAYOUT_DIRECTION_RTL) {
        return ERROR_INVALID_STATE;
    }
    view->layout_direction = direction;
    return OK;
}

API status_t android_view_set_measure_with_largest_child(
    android_view_t view, bool_t enabled) {
    if (!view) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_LINEAR_LAYOUT) return ERROR_INVALID_STATE;
    view->use_largest_child = enabled != FALSE;
    return OK;
}

API status_t android_view_set_show_dividers(
    android_view_t view, int32_t show_dividers) {
    if (!view) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_LINEAR_LAYOUT) return ERROR_INVALID_STATE;
    view->show_dividers = show_dividers;
    return OK;
}

API status_t android_view_set_divider(
    android_view_t view, float thickness_px, float padding_px, color_rgba color) {
    if (!view) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_LINEAR_LAYOUT) return ERROR_INVALID_STATE;
    if (thickness_px < 0.f || padding_px < 0.f) return ERROR_INVALID_STATE;
    view->divider_thickness_px = thickness_px;
    view->divider_padding_px = padding_px;
    view->linear_divider_color = color;
    return OK;
}

API status_t android_view_set_use_default_margins(
    android_view_t view, bool_t use_default_margins) {
    if (!view) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_LINEAR_LAYOUT) return ERROR_INVALID_STATE;
    view->use_default_margins = use_default_margins != FALSE;
    return OK;
}

API status_t android_view_set_gravity(
    android_view_t view, int32_t gravity) {
    if (!view) return ERROR_NULL_ARG;
    if (!viewruntime::android::is_group(view->cls)) return ERROR_INVALID_STATE;
    view->gravity = gravity;
    return OK;
}

API status_t android_view_add_constraint(
    android_view_t view, int32_t target_id, int32_t side,
    int32_t target_side, float margin_dp) {
    if (!view) return ERROR_NULL_ARG;
    if (side < ANDROID_CONSTRAINT_LEFT || side > ANDROID_CONSTRAINT_END ||
        target_side < ANDROID_CONSTRAINT_LEFT || target_side > ANDROID_CONSTRAINT_END ||
        margin_dp < 0.f) {
        return ERROR_INVALID_STATE;
    }
    if (view->lp.constraint.constraint_count >= 8) return ERROR_INVALID_STATE;
    android_constraint_t* c =
        &view->lp.constraint.constraints[view->lp.constraint.constraint_count++];
    c->target_id = target_id;
    c->side = side;
    c->target_side = target_side;
    c->margin_dp = margin_dp;
    c->gone_margin_dp = margin_dp;
    return OK;
}

API status_t android_view_set_constraint_bias(
    android_view_t view, float bias_h, float bias_v) {
    if (!view) return ERROR_NULL_ARG;
    if (bias_h < 0.f || bias_h > 1.f || bias_v < 0.f || bias_v > 1.f) {
        return ERROR_INVALID_STATE;
    }
    view->lp.constraint.bias_h = bias_h;
    view->lp.constraint.bias_v = bias_v;
    return OK;
}

API status_t android_view_set_constraint_ratio(
    android_view_t view, float dimension_ratio) {
    if (!view) return ERROR_NULL_ARG;
    if (dimension_ratio < 0.f) return ERROR_INVALID_STATE;
    view->lp.constraint.dimension_ratio = dimension_ratio;
    return OK;
}

API status_t android_view_set_constraint_match_style(
    android_view_t view, int32_t default_w, int32_t default_h,
    float min_w_dp, float max_w_dp, float min_h_dp, float max_h_dp) {
    if (!view) return ERROR_NULL_ARG;
    view->lp.constraint.match_default_w = default_w;
    view->lp.constraint.match_default_h = default_h;
    view->lp.constraint.match_min_w_dp = min_w_dp;
    view->lp.constraint.match_max_w_dp = max_w_dp;
    view->lp.constraint.match_min_h_dp = min_h_dp;
    view->lp.constraint.match_max_h_dp = max_h_dp;
    return OK;
}

API status_t android_view_set_constraint_chain_style(
    android_view_t view, int32_t chain_style_h, int32_t chain_style_v) {
    if (!view) return ERROR_NULL_ARG;
    view->lp.constraint.chain_style_h = chain_style_h;
    view->lp.constraint.chain_style_v = chain_style_v;
    return OK;
}

API status_t android_view_set_barrier_type(
    android_view_t view, int32_t barrier_type) {
    if (!view) return ERROR_NULL_ARG;
    if (barrier_type < ANDROID_BARRIER_LEFT || barrier_type > ANDROID_BARRIER_BOTTOM) {
        return ERROR_INVALID_STATE;
    }
    view->barrier_type = barrier_type;
    return OK;
}

API status_t android_view_set_barrier_margin(
    android_view_t view, float margin_dp) {
    if (!view || margin_dp < 0.f) return ERROR_NULL_ARG;
    view->barrier_margin_dp = margin_dp;
    return OK;
}

API status_t android_view_set_barrier_allows_gone(
    android_view_t view, bool_t allows_gone) {
    if (!view) return ERROR_NULL_ARG;
    view->barrier_allows_gone = allows_gone != FALSE;
    return OK;
}

API status_t android_view_add_barrier_reference(
    android_view_t view, int32_t target_id) {
    if (!view) return ERROR_NULL_ARG;
    if (target_id == 0) return ERROR_INVALID_STATE;
    for (int32_t ref : view->barrier_references) {
        if (ref == target_id) return OK; /* already referenced */
    }
    view->barrier_references.push_back(target_id);
    return OK;
}

API status_t android_view_set_text(
    android_view_t view, const char* text) {
    if (!view) return ERROR_NULL_ARG;
    view->text = text ? text : "";
    return OK;
}

API status_t android_view_set_text_size_sp(
    android_view_t view, float text_size_sp) {
    if (!view || text_size_sp <= 0.f) return ERROR_NULL_ARG;
    view->text_size_sp = text_size_sp;
    return OK;
}

API status_t android_view_set_text_color(
    android_view_t view, color_rgba color) {
    if (!view) return ERROR_NULL_ARG;
    view->text_color = color;
    return OK;
}

API status_t android_view_set_text_gravity(
    android_view_t view, int32_t gravity) {
    if (!view) return ERROR_NULL_ARG;
    view->text_gravity = gravity;
    return OK;
}

API status_t android_view_set_single_line(
    android_view_t view, bool_t single_line) {
    if (!view) return ERROR_NULL_ARG;
    view->single_line = single_line != FALSE;
    return OK;
}

API status_t android_view_set_hint(
    android_view_t view, const char* hint) {
    if (!view) return ERROR_NULL_ARG;
    view->hint = hint ? hint : "";
    view->has_hint = hint != nullptr;
    return OK;
}

API status_t android_view_set_image_source(
    android_view_t view, const char* source) {
    if (!view) return ERROR_NULL_ARG;
    view->image_source = source ? source : "";
    return OK;
}

API status_t android_view_set_scale_type(
    android_view_t view, int32_t scale_type) {
    if (!view) return ERROR_NULL_ARG;
    if (scale_type < ANDROID_SCALE_MATRIX ||
        scale_type > ANDROID_SCALE_CENTER_INSIDE) {
        return ERROR_INVALID_STATE;
    }
    view->scale_type = scale_type;
    return OK;
}

API status_t android_view_set_adjust_view_bounds(
    android_view_t view, bool_t adjust) {
    if (!view) return ERROR_NULL_ARG;
    view->adjust_view_bounds = adjust != FALSE;
    return OK;
}

API status_t android_view_set_max_image_size_dp(
    android_view_t view, float max_width_dp, float max_height_dp) {
    if (!view) return ERROR_NULL_ARG;
    if (max_width_dp < 0.f || max_height_dp < 0.f) return ERROR_INVALID_STATE;
    view->max_width_dp = max_width_dp;
    view->max_height_dp = max_height_dp;
    return OK;
}

API status_t android_view_set_checked(
    android_view_t view, bool_t checked) {
    if (!view) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_CHECK_BOX &&
        view->cls != ANDROID_VIEW_RADIO_BUTTON) {
        return ERROR_INVALID_STATE;
    }
    view->checked = checked != FALSE;
    return OK;
}

API status_t android_view_set_progress(
    android_view_t view, int32_t min_value, int32_t max_value, int32_t value) {
    if (!view) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_PROGRESS_BAR) return ERROR_INVALID_STATE;
    if (max_value <= min_value) return ERROR_INVALID_STATE;
    view->progress_min = min_value;
    view->progress_max = max_value;
    view->progress_value = value < min_value ? min_value : (value > max_value ? max_value : value);
    return OK;
}

API status_t android_view_set_progress_colors(
    android_view_t view, color_rgba track_color, color_rgba progress_color) {
    if (!view) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_PROGRESS_BAR) return ERROR_INVALID_STATE;
    view->track_color = track_color;
    view->progress_color = progress_color;
    return OK;
}

API status_t android_view_set_relative_rule(
    android_view_t view, int32_t verb, int32_t target_id) {
    if (!view) return ERROR_NULL_ARG;
    if (verb < ANDROID_RELATIVE_LEFT_OF || verb >= ANDROID_RELATIVE_VERB_COUNT) {
        return ERROR_INVALID_STATE;
    }
    view->relative_rules[verb] = target_id;
    /* keep the mInitialRules snapshot in sync (AOSP RelativeLayout.java:1553) */
    view->relative_rules_initial[verb] = target_id;
    return OK;
}

API status_t android_view_set_relative_align_with_parent(
    android_view_t view, bool_t align_with_parent) {
    if (!view) return ERROR_NULL_ARG;
    view->relative_align_with_parent = align_with_parent != FALSE;
    return OK;
}

API status_t android_view_set_grid_cell(
    android_view_t view, int32_t row, int32_t column,
    int32_t row_span, int32_t column_span) {
    if (!view) return ERROR_NULL_ARG;
    if (row_span <= 0 || column_span <= 0) return ERROR_INVALID_STATE;
    view->grid_row.start = row;
    view->grid_row.size = row_span;
    view->grid_column.start = column;
    view->grid_column.size = column_span;
    return OK;
}

API status_t android_view_set_grid_weights(
    android_view_t view, float row_weight, float column_weight) {
    if (!view || row_weight < 0.f || column_weight < 0.f) return ERROR_NULL_ARG;
    view->grid_row.weight = row_weight;
    view->grid_column.weight = column_weight;
    return OK;
}

API status_t android_view_set_grid_gravity(
    android_view_t view, int32_t gravity) {
    if (!view) return ERROR_NULL_ARG;
    /* AOSP GridLayout.LayoutParams.setGravity: each spec alignment is derived
     * from the gravity's per-axis flags (LEFT/RIGHT/FILL_H/CENTER_H for the
     * column, TOP/BOTTOM/FILL_V/CENTER_V for the row). */
    constexpr int32_t HORIZONTAL_MASK = 0x07;
    constexpr int32_t VERTICAL_MASK = 0x70;
    const int hflags = gravity & HORIZONTAL_MASK;
    const int vflags = (gravity & VERTICAL_MASK) >> 4;
    /* grid alignment kinds: 0 undefined, 1 leading, 2 trailing, 3 center, 4 fill */
    view->grid_column.alignment = hflags == 0x03 ? 1 : hflags == 0x05 ? 2 : hflags == 0x01 ? 3
                                 : hflags == 0x07 ? 4 : 0;
    view->grid_row.alignment = vflags == 0x03 ? 1 : vflags == 0x05 ? 2 : vflags == 0x01 ? 3
                                : vflags == 0x07 ? 4 : 0;
    return OK;
}

API status_t android_view_set_row_count(
    android_view_t view, int32_t count) {    if (!view) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_GRID_LAYOUT) return ERROR_INVALID_STATE;
    if (count < 0) return ERROR_INVALID_STATE;
    view->grid_row_count = count;
    return OK;
}

API status_t android_view_set_column_count(
    android_view_t view, int32_t count) {
    if (!view) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_GRID_LAYOUT) return ERROR_INVALID_STATE;
    if (count < 0) return ERROR_INVALID_STATE;
    view->grid_column_count = count;
    return OK;
}

API status_t android_view_set_divider_height_dp(
    android_view_t view, float divider_height_dp) {
    if (!view || divider_height_dp < 0.f) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_LIST_VIEW) return ERROR_INVALID_STATE;
    view->divider_height_dp = divider_height_dp;
    return OK;
}

API status_t android_view_set_divider_color(
    android_view_t view, color_rgba color) {
    if (!view) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_LIST_VIEW) return ERROR_INVALID_STATE;
    view->divider_color = color;
    return OK;
}

API status_t android_view_set_divider_enabled(
    android_view_t view, bool_t enabled) {
    if (!view) return ERROR_NULL_ARG;
    if (view->cls != ANDROID_VIEW_LIST_VIEW) return ERROR_INVALID_STATE;
    view->divider_enabled = enabled != FALSE;
    return OK;
}

} // extern "C"
