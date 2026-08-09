/* Faithful port of AOSP GridLayout (frameworks/base/core/java/android/widget/
 * GridLayout.java). The core is a linear-constraint solver: each axis (rows,
 * columns) builds "arcs" from the children's measured sizes and alignments,
 * topologically sorts them, and runs a Bellman-Ford variant to find the grid
 * line locations. Weighted axes distribute excess space through a binary
 * search over per-child deltas, re-evaluating the constraints each step.
 * Layout then applies the AOSP alignment-group math (bounds before/after,
 * gravity offsets, FILL sizing). */

#include "android_types.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <map>
#include <vector>

namespace viewruntime::android::grid {

constexpr float NEG_INF = -1e9f;
constexpr float POS_INF = 1e9f;
constexpr float MAX_SIZE = 100000.f;

/* Alignment kinds (GridLayout.Alignment). */
constexpr int ALIGN_UNDEFINED = 0;
constexpr int ALIGN_LEADING = 1;
constexpr int ALIGN_TRAILING = 2;
constexpr int ALIGN_CENTER = 3;
constexpr int ALIGN_FILL = 4;
constexpr int ALIGN_BASELINE = 5;

constexpr int FLEX_INFLEXIBLE = 0;
constexpr int FLEX_STRETCH = 2;

struct interval_s {
    int min = 0, max = 0;
    bool operator==(const interval_s& o) const { return min == o.min && max == o.max; }
    bool operator<(const interval_s& o) const { return min != o.min ? min < o.min : max < o.max; }
    int size() const { return max - min; }
    interval_s inverse() const { return {max, min}; }
};

/* AOSP Alignment.getAlignmentValue: distance from the leading edge to the
 * alignment point. FILL/UNDEFINED and missing baselines return the sentinel
 * so they never constrain the group minimum. */
float align_value(int alignment, float view_size, float baseline) {
    switch (alignment) {
        case ALIGN_LEADING: return 0.f;
        case ALIGN_TRAILING: return view_size;
        case ALIGN_CENTER: return view_size * 0.5f;
        case ALIGN_BASELINE: return baseline >= 0.f ? baseline : NEG_INF;
        default: return NEG_INF; /* ALIGN_UNDEFINED, ALIGN_FILL */
    }
}

float gravity_offset(int alignment, float cell_delta) {
    switch (alignment) {
        case ALIGN_TRAILING: return cell_delta;
        case ALIGN_CENTER: return cell_delta * 0.5f;
        default: return 0.f; /* LEADING, FILL, BASELINE */
    }
}

float size_in_cell(int alignment, float view_size, float cell_size) {
    return alignment == ALIGN_FILL ? cell_size : view_size;
}

/* One alignment group: the shared bounds of all views with the same span and
 * alignment on an axis. */
struct bounds_s {
    float before = NEG_INF, after = NEG_INF;
    int flexibility = FLEX_STRETCH;
    float max_size = NEG_INF; /* tracks the largest view (BASELINE rows) */

    void reset() {
        before = after = NEG_INF;
        flexibility = FLEX_STRETCH;
        max_size = NEG_INF;
    }
    bool can_stretch() const { return (flexibility & FLEX_STRETCH) != 0; }
    float size(bool min) const {
        const float base = std::max(before + after, max_size);
        if (!min && can_stretch()) return POS_INF;
        return base;
    }
    float get_offset(int alignment, float view_size, float baseline) const {
        if (alignment == ALIGN_FILL) return 0.f; /* AOSP: INT_MIN - INT_MIN = 0 */
        const float value = align_value(alignment, view_size, baseline);
        if (value == NEG_INF) return 0.f;
        float offset = before - value;
        if (alignment == ALIGN_BASELINE && offset < 0.f) offset = 0.f; /* BASELINE Bounds override */
        return offset;
    }
    void include(int alignment, float view_size, float baseline) {
        if (alignment == ALIGN_FILL) {
            /* AOSP FILL contributes size (its before/after degenerate values
             * wrap back to the view size in int arithmetic; we model that
             * directly). The alignment point is undefined, so layout offsets
             * resolve to zero. */
            before = std::max(before, 0.f);
            after = std::max(after, view_size);
            max_size = std::max(max_size, view_size);
            return;
        }
        const float value = align_value(alignment, view_size, baseline);
        if (value == NEG_INF) return; /* no baseline / undefined: no contribution */
        before = std::max(before, value);
        after = std::max(after, view_size - value);
        max_size = std::max(max_size, view_size);
    }
};

struct arc_s {
    int u = 0, v = 0;
    float value = 0.f;
    bool valid = true;
};

struct axis_s {
    const android_view_s* owner = nullptr;
    bool horizontal = true;
    bool order_preserved = true;
    int defined_count = ANDROID_GRID_UNDEFINED;
    int max_index = ANDROID_GRID_UNDEFINED;

    std::vector<bounds_s> group_bounds;
    std::vector<interval_s> group_span;
    std::vector<int> group_alignment;
    std::vector<int> child_group; /* per child -> group index */

    std::vector<interval_s> forward_span, backward_span;
    std::vector<float> forward_value, backward_value;
    std::vector<arc_s> arcs;
    std::vector<float> locations;
    std::vector<float> deltas;
    bool has_weights = false;
    float total_weight = 0.f;

    float parent_min = 0.f, parent_max = -MAX_SIZE;

    int count() const { return std::max(defined_count, max_index); }
};

/* ── Spec helpers ──────────────────────────────────────────────────── */

int spec_alignment(const android_view_s::grid_spec_s& spec, bool horizontal, float* weight_out) {
    *weight_out = spec.weight;
    if (spec.alignment != ALIGN_UNDEFINED) return spec.alignment;
    if (spec.weight == 0.f) return horizontal ? ALIGN_LEADING : ALIGN_BASELINE;
    return ALIGN_FILL;
}

int spec_flexibility(const android_view_s::grid_spec_s& spec) {
    return (spec.alignment == ALIGN_UNDEFINED && spec.weight == 0.f) ? FLEX_INFLEXIBLE : FLEX_STRETCH;
}

float total_margin(const android_view_s* child, bool horizontal, const android_ui_s* ui) {
    return horizontal ? margin_h(child->lp, ui) : margin_v(child->lp, ui);
}

/* ── validateLayoutParams (auto cell assignment) ───────────────────── */

bool fits(const std::vector<int>& a, int value, int start, int end) {
    if (end > static_cast<int>(a.size())) return false;
    for (int i = start; i < end; ++i) {
        if (a[i] > value) return false;
    }
    return true;
}

void procrustean_fill(std::vector<int>& a, int start, int end, int value) {
    const int n = static_cast<int>(a.size());
    for (int i = std::min(start, n); i < std::min(end, n); ++i) a[i] = value;
}

int clip(const interval_s& range, bool was_defined, int count) {
    const int size = range.size();
    if (count == 0) return size;
    const int min = was_defined ? std::min(range.min, count) : 0;
    return std::min(size, count - min);
}

void validate_layout_params(android_view_s* view) {
    const bool horizontal = view->orientation == ANDROID_HORIZONTAL;
    const int count = horizontal
        ? (view->grid_column_count != ANDROID_GRID_UNDEFINED ? view->grid_column_count : 0)
        : (view->grid_row_count != ANDROID_GRID_UNDEFINED ? view->grid_row_count : 0);

    int major = 0, minor = 0;
    std::vector<int> max_sizes(static_cast<size_t>(std::max(0, count)), 0);

    for (android_view_s* child : view->children) {
        auto& major_spec = horizontal ? child->grid_row : child->grid_column;
        auto& minor_spec = horizontal ? child->grid_column : child->grid_row;
        const bool major_defined = major_spec.start_defined();
        const int major_span = major_spec.size;
        if (major_defined) major = major_spec.start;

        const bool minor_defined = minor_spec.start_defined();
        const interval_s minor_range = {minor_spec.start, minor_spec.end()};
        const int minor_span = clip(minor_range, minor_defined, count);
        if (minor_defined) minor = minor_spec.start;

        if (count != 0) {
            if (!major_defined || !minor_defined) {
                while (!fits(max_sizes, major, minor, minor + minor_span)) {
                    if (minor_defined) {
                        ++major;
                    } else {
                        if (minor + minor_span <= count) ++minor;
                        else { minor = 0; ++major; }
                    }
                }
            }
            procrustean_fill(max_sizes, minor, minor + minor_span, major + major_span);
        }

        if (horizontal) {
            child->grid_row.start = major;
            child->grid_row.size = major_span;
            child->grid_column.start = minor;
            child->grid_column.size = minor_span;
        } else {
            child->grid_row.start = minor;
            child->grid_row.size = minor_span;
            child->grid_column.start = major;
            child->grid_column.size = major_span;
        }
        minor += minor_span;
    }
}

/* ── Axis ──────────────────────────────────────────────────────────── */

int axis_get_max_index(const android_view_s* view, bool horizontal) {
    int result = -1;
    for (const android_view_s* child : view->children) {
        const auto& spec = horizontal ? child->grid_column : child->grid_row;
        result = std::max(result, spec.start);
        result = std::max(result, spec.end());
        result = std::max(result, spec.size);
    }
    return result == -1 ? ANDROID_GRID_UNDEFINED : result;
}

void axis_build_groups(const android_view_s* view, axis_s& axis, bool horizontal,
                       const android_ui_s* ui) {
    axis.group_bounds.clear();
    axis.group_span.clear();
    axis.group_alignment.clear();
    axis.child_group.assign(view->children.size(), -1);

    std::map<std::pair<interval_s, int>, int> key_to_group;
    for (size_t i = 0; i < view->children.size(); ++i) {
        const android_view_s* child = view->children[i];
        const auto& spec = horizontal ? child->grid_column : child->grid_row;
        float weight = 0.f;
        const int alignment = spec_alignment(spec, horizontal, &weight);
        const interval_s span = {spec.start, spec.end()};
        const auto key = std::make_pair(span, alignment);
        int group = -1;
        auto found = key_to_group.find(key);
        if (found == key_to_group.end()) {
            group = static_cast<int>(axis.group_span.size());
            key_to_group.emplace(key, group);
            axis.group_span.push_back(span);
            axis.group_alignment.push_back(alignment);
            axis.group_bounds.emplace_back();
        } else {
            group = found->second;
        }
        axis.child_group[i] = group;
    }
}

void axis_compute_group_bounds(const android_view_s* view, axis_s& axis, bool horizontal,
                               const android_ui_s* ui) {
    for (auto& b : axis.group_bounds) b.reset();
    for (size_t i = 0; i < view->children.size(); ++i) {
        const android_view_s* child = view->children[i];
        const auto& spec = horizontal ? child->grid_column : child->grid_row;
        float weight = 0.f;
        const int alignment = spec_alignment(spec, horizontal, &weight);
        const int group = axis.child_group[i];
        if (group < 0) continue;
        float size = (horizontal ? child->measured.width : child->measured.height) +
                     total_margin(child, horizontal, ui);
        if (weight != 0.f && i < axis.deltas.size()) size += axis.deltas[i];
        if (child->visibility == ANDROID_GONE) size = 0.f;
        bounds_s& b = axis.group_bounds[static_cast<size_t>(group)];
        b.flexibility &= spec_flexibility(spec);
        b.include(alignment, size, child->measured_baseline);
    }
}

/* Links: one entry per interval (deduped), value = max over groups of
 * (size) forward and (-size) backward, exactly like AOSP computeLinks.
 * Forward and backward links keep separate storage. */
void axis_compute_links(const android_view_s* view, axis_s& axis, bool horizontal,
                        bool backward) {
    std::vector<interval_s>& span = backward ? axis.backward_span : axis.forward_span;
    std::vector<float>& value = backward ? axis.backward_value : axis.forward_value;
    span.clear();
    value.clear();
    for (size_t i = 0; i < axis.group_bounds.size(); ++i) {
        const interval_s key = backward ? axis.group_span[i].inverse() : axis.group_span[i];
        float v = axis.group_bounds[i].size(!backward);
        if (backward) v = -v;
        bool merged = false;
        for (size_t j = 0; j < span.size(); ++j) {
            if (span[j] == key) {
                value[j] = std::max(value[j], v);
                merged = true;
                break;
            }
        }
        if (!merged) {
            span.push_back(key);
            value.push_back(v);
        }
    }
}

void axis_build_arcs(const android_view_s* view, axis_s& axis, bool horizontal) {
    if (axis.max_index == ANDROID_GRID_UNDEFINED) {
        axis.max_index = std::max(0, axis_get_max_index(view, horizontal));
    }
    axis.arcs.clear();
    for (size_t i = 0; i < axis.forward_span.size(); ++i) {
        axis.arcs.push_back({axis.forward_span[i].min, axis.forward_span[i].max, axis.forward_value[i], true});
    }
    for (size_t i = 0; i < axis.backward_span.size(); ++i) {
        axis.arcs.push_back({axis.backward_span[i].min, axis.backward_span[i].max, axis.backward_value[i], true});
    }
    if (axis.order_preserved) {
        for (int i = 0; i < axis.count(); ++i) {
            axis.arcs.push_back({i, i + 1, 0.f, true});
        }
    }
    const int n = axis.count();
    axis.arcs.push_back({0, n, axis.parent_min, true});
    axis.arcs.push_back({n, 0, axis.parent_max, true});
}

bool axis_relax(std::vector<float>& locations, const arc_s& arc) {
    if (!arc.valid) return false;
    const float candidate = locations[static_cast<size_t>(arc.u)] + arc.value;
    if (candidate > locations[static_cast<size_t>(arc.v)]) {
        locations[static_cast<size_t>(arc.v)] = candidate;
        return true;
    }
    return false;
}

/* Bellman-Ford variant with culprit removal (AOSP Axis.solve). Only max
 * (backward) arcs — u >= v — can be removed when inconsistent. */
bool axis_solve(axis_s& axis, std::vector<float>& locations, bool modify_on_error) {
    const int n = axis.count() + 1;
    for (size_t p = 0; p < axis.arcs.size(); ++p) {
        std::fill(locations.begin(), locations.end(), 0.f);
        for (int i = 0; i < n; ++i) {
            bool changed = false;
            for (auto& arc : axis.arcs) changed |= axis_relax(locations, arc);
            if (!changed) return true;
        }
        if (!modify_on_error) return false;

        std::vector<bool> culprits(axis.arcs.size(), false);
        for (int i = 0; i < n; ++i) {
            for (size_t j = 0; j < axis.arcs.size(); ++j) {
                if (axis_relax(locations, axis.arcs[j])) culprits[j] = true;
            }
        }
        for (size_t i = 0; i < axis.arcs.size(); ++i) {
            if (culprits[i] && axis.arcs[i].u >= axis.arcs[i].v) {
                axis.arcs[i].valid = false;
                break;
            }
        }
    }
    return true;
}

void axis_share_out_delta(const android_view_s* view, axis_s& axis, bool horizontal,
                          float total_delta, float total_weight) {
    std::fill(axis.deltas.begin(), axis.deltas.end(), 0.f);
    float remaining_delta = total_delta;
    float remaining_weight = total_weight;
    for (size_t i = 0; i < view->children.size(); ++i) {
        const android_view_s* child = view->children[i];
        if (child->visibility == ANDROID_GONE) continue;
        const auto& spec = horizontal ? child->grid_column : child->grid_row;
        const float weight = spec.weight;
        if (weight != 0.f && remaining_weight > 0.f) {
            const float delta = std::round(weight * remaining_delta / remaining_weight);
            axis.deltas[i] = delta;
            remaining_delta -= delta;
            remaining_weight -= weight;
        }
    }
}

void axis_solve_and_distribute(const android_view_s* view, axis_s& axis, bool horizontal,
                               const android_ui_s* ui, std::vector<float>& locations) {
    std::fill(axis.deltas.begin(), axis.deltas.end(), 0.f);
    axis_solve(axis, locations, true);

    const int child_count = static_cast<int>(view->children.size());
    int delta_max = static_cast<int>(axis.parent_min) * child_count + 1; /* exclusive */
    if (delta_max < 2) return;
    int delta_min = 0;
    float valid_delta = -1.f;
    bool valid_solution = true;
    while (delta_min < delta_max) {
        const int delta = (delta_min + delta_max) / 2;
        axis_share_out_delta(view, axis, horizontal, static_cast<float>(delta), axis.total_weight);
        axis_compute_group_bounds(view, axis, horizontal, ui);
        axis_compute_links(view, axis, horizontal, false);
        axis_compute_links(view, axis, horizontal, true);
        axis_build_arcs(view, axis, horizontal);
        valid_solution = axis_solve(axis, locations, false);
        if (valid_solution) {
            valid_delta = static_cast<float>(delta);
            delta_min = delta + 1;
        } else {
            delta_max = delta;
        }
    }
    if (valid_delta > 0.f && !valid_solution) {
        axis_share_out_delta(view, axis, horizontal, valid_delta, axis.total_weight);
        axis_compute_group_bounds(view, axis, horizontal, ui);
        axis_compute_links(view, axis, horizontal, false);
        axis_compute_links(view, axis, horizontal, true);
        axis_build_arcs(view, axis, horizontal);
        axis_solve(axis, locations, true);
    }
}

void axis_compute_locations(const android_view_s* view, axis_s& axis, bool horizontal,
                            const android_ui_s* ui) {
    /* max_index must be resolved before locations are sized: a defined count
     * may be UNDEFINED (auto grid), in which case count() falls back to it. */
    if (axis.max_index == ANDROID_GRID_UNDEFINED) {
        axis.max_index = std::max(0, axis_get_max_index(view, horizontal));
    }
    axis.locations.assign(static_cast<size_t>(axis.count() + 1), 0.f);
    if (!axis.has_weights) {
        axis_build_arcs(view, axis, horizontal);
        axis_solve(axis, axis.locations, true);
    } else {
        axis_build_arcs(view, axis, horizontal);
        axis_solve_and_distribute(view, axis, horizontal, ui, axis.locations);
    }
    if (!axis.order_preserved && !axis.locations.empty()) {
        const float a0 = axis.locations[0];
        for (auto& loc : axis.locations) loc -= a0;
    }
}

float axis_get_measure(const android_view_s* view, axis_s& axis, bool horizontal,
                       const android_ui_s* ui, float spec_size, int spec_mode) {
    switch (spec_mode) {
        case ANDROID_MEASURE_UNSPECIFIED:
            axis.parent_min = 0.f; axis.parent_max = -MAX_SIZE; break;
        case ANDROID_MEASURE_EXACTLY:
            axis.parent_min = spec_size; axis.parent_max = -spec_size; break;
        case ANDROID_MEASURE_AT_MOST:
        default:
            axis.parent_min = 0.f; axis.parent_max = -spec_size; break;
    }
    axis_compute_locations(view, axis, horizontal, ui);
    return axis.locations[static_cast<size_t>(axis.count())];
}

void axis_layout(const android_view_s* view, axis_s& axis, bool horizontal,
                 const android_ui_s* ui, float size) {
    axis.parent_min = size;
    axis.parent_max = -size;
    axis_compute_locations(view, axis, horizontal, ui);
}

/* ── Grid measure/layout ───────────────────────────────────────────── */

void axis_prepare(const android_view_s* view, axis_s& axis, bool horizontal,
                  const android_ui_s* ui) {
    axis.owner = view;
    axis.horizontal = horizontal;
    axis.defined_count = horizontal ? view->grid_column_count : view->grid_row_count;
    axis_build_groups(view, axis, horizontal, ui);
    axis.deltas.assign(view->children.size(), 0.f);
    axis.has_weights = false;
    axis.total_weight = 0.f;
    for (const android_view_s* child : view->children) {
        if (child->visibility == ANDROID_GONE) continue;
        const auto& spec = horizontal ? child->grid_column : child->grid_row;
        if (spec.weight != 0.f) {
            axis.has_weights = true;
            axis.total_weight += spec.weight;
        }
    }
}

void measure_children_first_pass(android_view_s* view, android_measure_spec_t spec_w,
                                 android_measure_spec_t spec_h, const android_ui_s* ui) {
    const float content_w = std::max(0.f, spec_w.size - padding_h(view, ui));
    const float content_h = std::max(0.f, spec_h.size - padding_v(view, ui));
    for (android_view_s* child : view->children) {
        if (child->visibility == ANDROID_GONE) continue;
        child->measured = measure_view(child,
            get_child_measure_spec({content_w, spec_w.mode}, total_margin(child, true, ui), child->lp.width, ui),
            get_child_measure_spec({content_h, spec_h.mode}, total_margin(child, false, ui), child->lp.height, ui), ui);
    }
}

void remeasure_fill_children(android_view_s* view, bool horizontal,
                             android_measure_spec_t spec_w, android_measure_spec_t spec_h,
                             axis_s& axis, const android_ui_s* ui) {
    const float content_w = std::max(0.f, spec_w.size - padding_h(view, ui));
    const float content_h = std::max(0.f, spec_h.size - padding_v(view, ui));
    for (android_view_s* child : view->children) {
        if (child->visibility == ANDROID_GONE) continue;
        const auto& spec = horizontal ? child->grid_column : child->grid_row;
        float weight = 0.f;
        if (spec_alignment(spec, horizontal, &weight) != ALIGN_FILL) continue;
        const interval_s span = {spec.start, spec.end()};
        const float cell_size = axis.locations[static_cast<size_t>(span.max)] -
                                axis.locations[static_cast<size_t>(span.min)];
        const float view_size = cell_size - total_margin(child, horizontal, ui);
        android_measure_spec_t cw, ch;
        if (horizontal) {
            cw = {view_size, ANDROID_MEASURE_EXACTLY};
            ch = get_child_measure_spec({content_h, spec_h.mode}, total_margin(child, false, ui),
                                        child->lp.height, ui);
        } else {
            cw = get_child_measure_spec({content_w, spec_w.mode}, total_margin(child, true, ui),
                                        child->lp.width, ui);
            ch = {view_size, ANDROID_MEASURE_EXACTLY};
        }
        child->measured = measure_view(child, cw, ch, ui);
    }
}

} // namespace viewruntime::android::grid

namespace viewruntime::android {

using namespace viewruntime::android::grid;

android_measured_size_t measure_grid(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    validate_layout_params(view);

    const bool horizontal = view->orientation == ANDROID_HORIZONTAL;
    const float pad_h = padding_h(view, ui), pad_v = padding_v(view, ui);
    const float content_w = std::max(0.f, spec_w.size - pad_h);
    const float content_h = std::max(0.f, spec_h.size - pad_v);

    measure_children_first_pass(view, spec_w, spec_h, ui);

    axis_s h_axis, v_axis;
    axis_prepare(view, h_axis, true, ui);
    axis_prepare(view, v_axis, false, ui);

    /* The axis laid out first follows the orientation (AOSP onMeasure). */
    float w_sans = 0.f, h_sans = 0.f;
    if (horizontal) {
        axis_compute_group_bounds(view, h_axis, true, ui);
        axis_compute_links(view, h_axis, true, false);
        axis_compute_links(view, h_axis, true, true);
        w_sans = axis_get_measure(view, h_axis, true, ui, content_w, spec_w.mode);
        remeasure_fill_children(view, true, spec_w, spec_h, h_axis, ui);
        axis_compute_group_bounds(view, v_axis, false, ui);
        axis_compute_links(view, v_axis, false, false);
        axis_compute_links(view, v_axis, false, true);
        h_sans = axis_get_measure(view, v_axis, false, ui, content_h, spec_h.mode);
    } else {
        axis_compute_group_bounds(view, v_axis, false, ui);
        axis_compute_links(view, v_axis, false, false);
        axis_compute_links(view, v_axis, false, true);
        h_sans = axis_get_measure(view, v_axis, false, ui, content_h, spec_h.mode);
        remeasure_fill_children(view, false, spec_w, spec_h, v_axis, ui);
        axis_compute_group_bounds(view, h_axis, true, ui);
        axis_compute_links(view, h_axis, true, false);
        axis_compute_links(view, h_axis, true, true);
        w_sans = axis_get_measure(view, h_axis, true, ui, content_w, spec_w.mode);
    }

    const float measured_w = std::max(w_sans + pad_h, dp(ui, view->min_width_dp));
    const float measured_h = std::max(h_sans + pad_v, dp(ui, view->min_height_dp));
    view->measured_baseline = -1.f;
    return {resolve_size(measured_w, spec_w), resolve_size(measured_h, spec_h)};
}

void layout_grid(android_view_s* view, float x, float y, float w, float h,
                 const android_ui_s* ui) {
    view->bounds = {x, y, w, h};
    validate_layout_params(view);

    const float pad_left = dp(ui, view->padding_left_dp), pad_top = dp(ui, view->padding_top_dp);
    const float pad_right = dp(ui, view->padding_right_dp), pad_bottom = dp(ui, view->padding_bottom_dp);
    const float content_w = std::max(0.f, w - pad_left - pad_right);
    const float content_h = std::max(0.f, h - pad_top - pad_bottom);

    axis_s h_axis, v_axis;
    axis_prepare(view, h_axis, true, ui);
    axis_prepare(view, v_axis, false, ui);

    axis_compute_group_bounds(view, h_axis, true, ui);
    axis_compute_links(view, h_axis, true, false);
    axis_compute_links(view, h_axis, true, true);
    axis_layout(view, h_axis, true, ui, content_w);

    axis_compute_group_bounds(view, v_axis, false, ui);
    axis_compute_links(view, v_axis, false, false);
    axis_compute_links(view, v_axis, false, true);
    axis_layout(view, v_axis, false, ui, content_h);

    for (size_t i = 0; i < view->children.size(); ++i) {
        android_view_s* child = view->children[i];
        if (child->visibility == ANDROID_GONE) continue;
        const interval_s col_span = {child->grid_column.start, child->grid_column.end()};
        const interval_s row_span = {child->grid_row.start, child->grid_row.end()};
        const float x1 = h_axis.locations[static_cast<size_t>(col_span.min)];
        const float y1 = v_axis.locations[static_cast<size_t>(row_span.min)];
        const float x2 = h_axis.locations[static_cast<size_t>(col_span.max)];
        const float y2 = v_axis.locations[static_cast<size_t>(row_span.max)];
        const float cell_w = x2 - x1, cell_h = y2 - y1;
        const float p_w = child->measured.width, p_h = child->measured.height;

        float weight = 0.f;
        const int h_align = spec_alignment(child->grid_column, true, &weight);
        const int v_align = spec_alignment(child->grid_row, false, &weight);

        const int group_x = h_axis.child_group[i];
        const int group_y = v_axis.child_group[i];
        const bounds_s& bounds_x = h_axis.group_bounds[static_cast<size_t>(group_x)];
        const bounds_s& bounds_y = v_axis.group_bounds[static_cast<size_t>(group_y)];

        const float gravity_x = gravity_offset(h_align, cell_w - bounds_x.size(true));
        const float gravity_y = gravity_offset(v_align, cell_h - bounds_y.size(true));

        const float m_left = dp(ui, child->lp.margins_dp.left), m_top = dp(ui, child->lp.margins_dp.top);
        const float m_right = dp(ui, child->lp.margins_dp.right), m_bottom = dp(ui, child->lp.margins_dp.bottom);
        const float sum_mx = m_left + m_right, sum_my = m_top + m_bottom;

        const float align_x = bounds_x.get_offset(h_align, p_w + sum_mx, child->measured_baseline);
        const float align_y = bounds_y.get_offset(v_align, p_h + sum_my, child->measured_baseline);

        const float width = size_in_cell(h_align, p_w, cell_w - sum_mx);
        const float height = size_in_cell(v_align, p_h, cell_h - sum_my);

        const float dx = x1 + gravity_x + align_x;
        const float cx = x + pad_left + m_left + dx;
        const float cy = y + pad_top + y1 + gravity_y + align_y + m_top;

        if (width != p_w || height != p_h) {
            child->measured = measure_view(child,
                {width, ANDROID_MEASURE_EXACTLY},
                {height, ANDROID_MEASURE_EXACTLY}, ui);
        }
        layout_view(child, cx, cy, width, height, ui);
    }
}

} // namespace viewruntime::android
