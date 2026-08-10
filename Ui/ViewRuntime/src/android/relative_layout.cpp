#include "android_types.h"

#include <algorithm>
#include <limits>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace viewruntime::android {

/* ── RelativeLayout ────────────────────────────────────────────────── */
/* Faithful port of android.widget.RelativeLayout: dependency graph with
 * topological sort (AOSP DependencyGraph), two-pass measure (horizontal rules
 * then vertical rules), VALUE_NOT_SET edge semantics. The rule array passed
 * around is the LTR-resolved form (AOSP LayoutParams.getRules(resolved)),
 * exactly like resolveRules() resolves START/END to LEFT/RIGHT. */

namespace {

constexpr float RL_UNSET = -(std::numeric_limits<float>::max)();

/* AOSP RULES_VERTICAL / RULES_HORIZONTAL (RelativeLayout.java:197/201): the
 * verbs that create dependencies for each sort pass. */
const int kRulesVertical[] = {
    ANDROID_RELATIVE_ABOVE, ANDROID_RELATIVE_BELOW, ANDROID_RELATIVE_ALIGN_BASELINE,
    ANDROID_RELATIVE_ALIGN_TOP, ANDROID_RELATIVE_ALIGN_BOTTOM};
const int kRulesHorizontal[] = {
    ANDROID_RELATIVE_LEFT_OF, ANDROID_RELATIVE_RIGHT_OF, ANDROID_RELATIVE_ALIGN_LEFT,
    ANDROID_RELATIVE_ALIGN_RIGHT, ANDROID_RELATIVE_START_OF, ANDROID_RELATIVE_END_OF,
    ANDROID_RELATIVE_ALIGN_START, ANDROID_RELATIVE_ALIGN_END};

/* AOSP LayoutParams.resolveRules (RelativeLayout.java:1549), LTR branch
 * (JB MR1+): START/END verbs resolve to LEFT/RIGHT and take precedence over
 * explicit LEFT/RIGHT verbs. Produces the rules array the layout algorithms
 * actually read. AOSP re-copies from the immutable mInitialRules snapshot
 * first (RelativeLayout.java:1553), so resolution ALWAYS starts from the
 * original rules — never from an already-resolved array. */
void relative_resolve_rules_ltr(const android_view_s* view,
                                int32_t out[ANDROID_RELATIVE_VERB_COUNT]) {
    std::copy(std::begin(view->relative_rules_initial),
              std::end(view->relative_rules_initial), out);
    if ((out[ANDROID_RELATIVE_ALIGN_START] != 0 || out[ANDROID_RELATIVE_ALIGN_END] != 0) &&
        (out[ANDROID_RELATIVE_ALIGN_LEFT] != 0 || out[ANDROID_RELATIVE_ALIGN_RIGHT] != 0)) {
        out[ANDROID_RELATIVE_ALIGN_LEFT] = 0;
        out[ANDROID_RELATIVE_ALIGN_RIGHT] = 0;
    }
    if (out[ANDROID_RELATIVE_ALIGN_START] != 0) {
        out[ANDROID_RELATIVE_ALIGN_LEFT] = out[ANDROID_RELATIVE_ALIGN_START];
        out[ANDROID_RELATIVE_ALIGN_START] = 0;
    }
    if (out[ANDROID_RELATIVE_ALIGN_END] != 0) {
        out[ANDROID_RELATIVE_ALIGN_RIGHT] = out[ANDROID_RELATIVE_ALIGN_END];
        out[ANDROID_RELATIVE_ALIGN_END] = 0;
    }
    if ((out[ANDROID_RELATIVE_START_OF] != 0 || out[ANDROID_RELATIVE_END_OF] != 0) &&
        (out[ANDROID_RELATIVE_LEFT_OF] != 0 || out[ANDROID_RELATIVE_RIGHT_OF] != 0)) {
        out[ANDROID_RELATIVE_LEFT_OF] = 0;
        out[ANDROID_RELATIVE_RIGHT_OF] = 0;
    }
    if (out[ANDROID_RELATIVE_START_OF] != 0) {
        out[ANDROID_RELATIVE_LEFT_OF] = out[ANDROID_RELATIVE_START_OF];
        out[ANDROID_RELATIVE_START_OF] = 0;
    }
    if (out[ANDROID_RELATIVE_END_OF] != 0) {
        out[ANDROID_RELATIVE_RIGHT_OF] = out[ANDROID_RELATIVE_END_OF];
        out[ANDROID_RELATIVE_END_OF] = 0;
    }
    if ((out[ANDROID_RELATIVE_ALIGN_PARENT_START] != 0 || out[ANDROID_RELATIVE_ALIGN_PARENT_END] != 0) &&
        (out[ANDROID_RELATIVE_ALIGN_PARENT_LEFT] != 0 || out[ANDROID_RELATIVE_ALIGN_PARENT_RIGHT] != 0)) {
        out[ANDROID_RELATIVE_ALIGN_PARENT_LEFT] = 0;
        out[ANDROID_RELATIVE_ALIGN_PARENT_RIGHT] = 0;
    }
    if (out[ANDROID_RELATIVE_ALIGN_PARENT_START] != 0) {
        out[ANDROID_RELATIVE_ALIGN_PARENT_LEFT] = out[ANDROID_RELATIVE_ALIGN_PARENT_START];
        out[ANDROID_RELATIVE_ALIGN_PARENT_START] = 0;
    }
    if (out[ANDROID_RELATIVE_ALIGN_PARENT_END] != 0) {
        out[ANDROID_RELATIVE_ALIGN_PARENT_RIGHT] = out[ANDROID_RELATIVE_ALIGN_PARENT_END];
        out[ANDROID_RELATIVE_ALIGN_PARENT_END] = 0;
    }
}

struct RelativeNode {
    android_view_s* view = nullptr;
    int32_t rules[ANDROID_RELATIVE_VERB_COUNT] = {}; /* LTR-resolved (AOSP mRules after resolveRules) */
    std::vector<RelativeNode*> dependents;   /* nodes needing this node first */
    std::vector<std::pair<int32_t, RelativeNode*>> dependencies; /* (target_id, node) */
};

/* Port of AOSP DependencyGraph (RelativeLayout.java:1871). The graph is
 * rebuilt per measure pass; node pointers are stable because nodes are
 * reserved up front. */
struct RelativeGraph {
    std::vector<RelativeNode> nodes;
    std::unordered_map<int32_t, RelativeNode*> key_nodes; /* by resource id */

    void clear() {
        nodes.clear();
        key_nodes.clear();
    }
    void add(android_view_s* view) {
        RelativeNode node;
        node.view = view;
        std::copy(std::begin(view->relative_rules), std::end(view->relative_rules), node.rules);
        nodes.push_back(node);
        if (view->resource_id != 0) {
            key_nodes[view->resource_id] = &nodes.back();
        }
    }

    /* AOSP findRoots (RelativeLayout.java:1970): builds dependents/
     * dependencies from the filter rules and returns the roots (nodes with no
     * dependencies). */
    std::vector<RelativeNode*> find_roots(const int* filter, int filter_count) {
        for (RelativeNode& node : nodes) {
            node.dependents.clear();
            node.dependencies.clear();
        }
        for (RelativeNode& node : nodes) {
            for (int j = 0; j < filter_count; ++j) {
                const int32_t target_id = node.rules[filter[j]];
                /* AOSP: only positive/valid resource ids create dependencies
                 * (parent rules are TRUE = -1). */
                if (target_id <= 0) continue;
                auto it = key_nodes.find(target_id);
                if (it == key_nodes.end()) continue;
                RelativeNode* dep = it->second;
                if (dep == &node) continue; /* skip self dependencies */
                if (std::find(dep->dependents.begin(), dep->dependents.end(), &node) ==
                    dep->dependents.end()) {
                    dep->dependents.push_back(&node);
                }
                node.dependencies.emplace_back(target_id, dep);
            }
        }
        std::vector<RelativeNode*> roots;
        for (RelativeNode& node : nodes) {
            if (node.dependencies.empty()) roots.push_back(&node);
        }
        return roots;
    }

    /* AOSP getSortedViews (RelativeLayout.java:1931): Kahn's algorithm over
     * the roots, polling from the tail (LIFO, ArrayDeque.pollLast). */
    std::vector<android_view_s*> get_sorted_views(const int* filter, int filter_count) {
        std::vector<android_view_s*> sorted;
        std::vector<RelativeNode*> stack = find_roots(filter, filter_count);
        while (!stack.empty()) {
            RelativeNode* node = stack.back();
            stack.pop_back();
            sorted.push_back(node->view);
            const int32_t key = node->view->resource_id;
            for (RelativeNode* dependent : node->dependents) {
                auto& deps = dependent->dependencies;
                deps.erase(std::remove_if(deps.begin(), deps.end(),
                    [key](const auto& kv) { return kv.first == key; }), deps.end());
                if (deps.empty()) {
                    stack.push_back(dependent);
                }
            }
        }
        /* AOSP throws IllegalStateException("Circular dependencies cannot
         * exist in RelativeLayout") when the sort does not consume every node
         * (RelativeLayout.java:1955-1958). The runtime cannot throw, so the
         * remaining (cyclic) nodes degrade to declaration order instead of
         * being left silently unmeasured at (0,0). */
        if (sorted.size() < nodes.size()) {
            std::unordered_set<android_view_s*> emitted;
            emitted.reserve(sorted.size());
            for (android_view_s* v : sorted) emitted.insert(v);
            for (RelativeNode& node : nodes) {
                if (emitted.find(node.view) == emitted.end()) {
                    sorted.push_back(node.view);
                }
            }
        }
        return sorted;
    }
};

/* AOSP getRelatedView (RelativeLayout.java:1028): the target of a rule,
 * skipping GONE views up the chain (re-resolving the same verb on each GONE
 * view's own rules). */
android_view_s* relative_get_related_view(RelativeGraph& graph,
                                          const int* rules, int relation) {
    const int32_t id = rules[relation];
    if (id == 0) return nullptr;
    auto it = graph.key_nodes.find(id);
    if (it == graph.key_nodes.end()) return nullptr;
    android_view_s* v = it->second->view;
    while (v->visibility == ANDROID_GONE) {
        RelativeNode& gone_node = *graph.key_nodes.find(v->resource_id)->second;
        rules = gone_node.rules;
        it = graph.key_nodes.find(rules[relation]);
        if (it == graph.key_nodes.end() || v == it->second->view) return nullptr;
        v = it->second->view;
    }
    return v;
}

/* AOSP LayoutParams margin accessor (0=left, 1=top, 2=right, 3=bottom). */
float relative_margin(const android_layout_params_t& lp, int which, const android_ui_s* ui) {
    switch (which) {
        case 0: return dp(ui, lp.margins_dp.left);
        case 1: return dp(ui, lp.margins_dp.top);
        case 2: return dp(ui, lp.margins_dp.right);
        default: return dp(ui, lp.margins_dp.bottom);
    }
}

/* AOSP applyHorizontalSizeRules (RelativeLayout.java:907): resolve child
 * rl_left/rl_right from the rules; edges stay RL_UNSET ("soft requirement")
 * when not fixed. */
void relative_apply_h_size_rules(android_view_s* child, RelativeGraph& graph,
                                 float my_width, const android_ui_s* ui) {
    android_view_s* parent = child->parent;
    const int* rules = child->relative_rules;
    const float left_margin = dp(ui, child->lp.margins_dp.left);
    const float right_margin = dp(ui, child->lp.margins_dp.right);
    const float pad_left = dp(ui, parent->padding_left_dp);
    const float pad_right = dp(ui, parent->padding_right_dp);
    child->rl_left = RL_UNSET;
    child->rl_right = RL_UNSET;

    android_view_s* anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_LEFT_OF);
    if (anchor != nullptr) {
        /* AOSP: childParams.mRight = anchorParams.mLeft - (anchorParams.leftMargin +
         * childParams.rightMargin) (RelativeLayout.java:921-922) — the ANCHOR's
         * LEFT margin, not its right margin. */
        child->rl_right = anchor->rl_left - (relative_margin(anchor->lp, 0, ui) + right_margin);
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_LEFT_OF] != 0) {
        if (my_width >= 0.f) child->rl_right = my_width - pad_right - right_margin;
    }

    anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_RIGHT_OF);
    if (anchor != nullptr) {
        child->rl_left = anchor->rl_right + (relative_margin(anchor->lp, 2, ui) + left_margin);
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_RIGHT_OF] != 0) {
        child->rl_left = pad_left + left_margin;
    }

    anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_ALIGN_LEFT);
    if (anchor != nullptr) {
        child->rl_left = anchor->rl_left + left_margin;
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_ALIGN_LEFT] != 0) {
        child->rl_left = pad_left + left_margin;
    }

    anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_ALIGN_RIGHT);
    if (anchor != nullptr) {
        child->rl_right = anchor->rl_right - right_margin;
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_ALIGN_RIGHT] != 0) {
        if (my_width >= 0.f) child->rl_right = my_width - pad_right - right_margin;
    }

    if (rules[ANDROID_RELATIVE_ALIGN_PARENT_LEFT] != 0) {
        child->rl_left = pad_left + left_margin;
    }
    if (rules[ANDROID_RELATIVE_ALIGN_PARENT_RIGHT] != 0) {
        if (my_width >= 0.f) child->rl_right = my_width - pad_right - right_margin;
    }
}

/* AOSP getRelatedViewBaselineOffset (RelativeLayout.java:1061) for
 * ALIGN_BASELINE; -1 when no baseline target exists. */
int relative_get_related_view_baseline_offset(RelativeGraph& graph, const int* rules) {
    android_view_s* v = relative_get_related_view(graph, rules, ANDROID_RELATIVE_ALIGN_BASELINE);
    if (v == nullptr) return -1;
    const float baseline = v->measured_baseline;
    if (baseline < 0.f) return -1;
    return static_cast<int>(v->rl_top + baseline);
}

/* AOSP applyVerticalSizeRules (RelativeLayout.java:964): baseline alignment
 * overrides explicit top/bottom; otherwise resolve rl_top/rl_bottom from the
 * rules. */
void relative_apply_v_size_rules(android_view_s* child, RelativeGraph& graph,
                                 float my_height, float my_baseline, const android_ui_s* ui) {
    android_view_s* parent = child->parent;
    const int* rules = child->relative_rules;
    const float top_margin = dp(ui, child->lp.margins_dp.top);
    const float bottom_margin = dp(ui, child->lp.margins_dp.bottom);
    const float pad_top = dp(ui, parent->padding_top_dp);
    const float pad_bottom = dp(ui, parent->padding_bottom_dp);

    const int baseline_offset = relative_get_related_view_baseline_offset(graph, rules);
    if (baseline_offset != -1) {
        float offset = static_cast<float>(baseline_offset);
        if (my_baseline != -1.f) offset -= my_baseline;
        child->rl_top = offset;
        child->rl_bottom = RL_UNSET;
        return;
    }

    child->rl_top = RL_UNSET;
    child->rl_bottom = RL_UNSET;

    android_view_s* anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_ABOVE);
    if (anchor != nullptr) {
        child->rl_bottom = anchor->rl_top - (relative_margin(anchor->lp, 1, ui) + bottom_margin);
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_ABOVE] != 0) {
        if (my_height >= 0.f) child->rl_bottom = my_height - pad_bottom - bottom_margin;
    }

    anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_BELOW);
    if (anchor != nullptr) {
        /* AOSP: childParams.mTop = anchorParams.mBottom + (anchorParams.bottomMargin +
         * childParams.topMargin) (RelativeLayout.java:995-996) — the ANCHOR's
         * BOTTOM margin, not its top margin. */
        child->rl_top = anchor->rl_bottom + (relative_margin(anchor->lp, 3, ui) + top_margin);
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_BELOW] != 0) {
        child->rl_top = pad_top + top_margin;
    }

    anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_ALIGN_TOP);
    if (anchor != nullptr) {
        child->rl_top = anchor->rl_top + top_margin;
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_ALIGN_TOP] != 0) {
        child->rl_top = pad_top + top_margin;
    }

    anchor = relative_get_related_view(graph, rules, ANDROID_RELATIVE_ALIGN_BOTTOM);
    if (anchor != nullptr) {
        child->rl_bottom = anchor->rl_bottom - bottom_margin;
    } else if (child->relative_align_with_parent && rules[ANDROID_RELATIVE_ALIGN_BOTTOM] != 0) {
        if (my_height >= 0.f) child->rl_bottom = my_height - pad_bottom - bottom_margin;
    }

    if (rules[ANDROID_RELATIVE_ALIGN_PARENT_TOP] != 0) {
        child->rl_top = pad_top + top_margin;
    }
    if (rules[ANDROID_RELATIVE_ALIGN_PARENT_BOTTOM] != 0) {
        if (my_height >= 0.f) child->rl_bottom = my_height - pad_bottom - bottom_margin;
    }
}

/* AOSP getChildMeasureSpec (RelativeLayout.java:754): size constraints from
 * the resolved edges, the child's desired size, margins and padding.
 * my_size < 0 means UNSPECIFIED. */
android_measure_spec_t relative_get_child_measure_spec(
    float child_start, float child_end, const android_size_t& child_size,
    float start_margin, float end_margin, float start_padding, float end_padding,
    float my_size, const android_ui_s* ui) {
    const bool is_unspecified = my_size < 0.f;
    /* AOSP LayoutParams size: >=0 exact px, MATCH_PARENT = -1, WRAP = -2 */
    float child_size_px = 0.f;
    if (child_size.kind == ANDROID_SIZE_KIND_EXACT) {
        child_size_px = dp(ui, child_size.value_dp);
    } else if (child_size.kind == ANDROID_SIZE_KIND_WRAP_CONTENT) {
        child_size_px = -2.f;
    } else {
        child_size_px = -1.f; /* MATCH_PARENT */
    }

    if (is_unspecified) {
        if (child_start != RL_UNSET && child_end != RL_UNSET) {
            return {std::max(0.f, child_end - child_start), ANDROID_MEASURE_EXACTLY};
        }
        if (child_size_px >= 0.f) {
            return {child_size_px, ANDROID_MEASURE_EXACTLY};
        }
        return {0.f, ANDROID_MEASURE_UNSPECIFIED};
    }

    const float temp_start =
        child_start != RL_UNSET ? child_start : start_padding + start_margin;
    const float temp_end =
        child_end != RL_UNSET ? child_end : my_size - end_padding - end_margin;
    const float max_available = temp_end - temp_start;

    if (child_start != RL_UNSET && child_end != RL_UNSET) {
        return {std::max(0.f, max_available), ANDROID_MEASURE_EXACTLY};
    }
    if (child_size_px >= 0.f) {
        if (max_available >= 0.f) {
            return {std::min(max_available, child_size_px), ANDROID_MEASURE_EXACTLY};
        }
        return {child_size_px, ANDROID_MEASURE_EXACTLY};
    }
    if (child_size_px == -1.f) { /* MATCH_PARENT */
        return {std::max(0.f, max_available), ANDROID_MEASURE_EXACTLY};
    }
    /* WRAP_CONTENT */
    if (max_available >= 0.f) {
        return {max_available, ANDROID_MEASURE_AT_MOST};
    }
    return {0.f, ANDROID_MEASURE_UNSPECIFIED};
}

/* AOSP measureChildHorizontal (RelativeLayout.java:699): width from the
 * horizontal rules; height is AT_MOST (EXACTLY when MATCH_PARENT) bounded by
 * the parent. */
void relative_measure_child_horizontal(android_view_s* child, float my_width,
                                       float my_height, const android_ui_s* ui) {
    android_view_s* parent = child->parent;
    const android_layout_params_t& lp = child->lp;
    const android_measure_spec_t cw = relative_get_child_measure_spec(
        child->rl_left, child->rl_right, lp.width,
        dp(ui, lp.margins_dp.left), dp(ui, lp.margins_dp.right),
        dp(ui, parent->padding_left_dp), dp(ui, parent->padding_right_dp), my_width, ui);

    android_measure_spec_t ch;
    if (my_height < 0.f) {
        if (lp.height.kind == ANDROID_SIZE_KIND_EXACT) {
            ch = {dp(ui, lp.height.value_dp), ANDROID_MEASURE_EXACTLY};
        } else {
            ch = {0.f, ANDROID_MEASURE_UNSPECIFIED};
        }
    } else {
        const float max_height = std::max(0.f, my_height
            - dp(ui, parent->padding_top_dp) - dp(ui, parent->padding_bottom_dp)
            - dp(ui, lp.margins_dp.top) - dp(ui, lp.margins_dp.bottom));
        const int32_t mode = lp.height.kind == ANDROID_SIZE_KIND_MATCH_PARENT
            ? ANDROID_MEASURE_EXACTLY : ANDROID_MEASURE_AT_MOST;
        ch = {max_height, mode};
    }

    child->measured = measure_view(child, cw, ch, ui);
}

/* AOSP measureChild (RelativeLayout.java:685): both axes from
 * getChildMeasureSpec (vertical pass). */
void relative_measure_child(android_view_s* child, float my_width, float my_height,
                            const android_ui_s* ui) {
    android_view_s* parent = child->parent;
    const android_layout_params_t& lp = child->lp;
    const android_measure_spec_t cw = relative_get_child_measure_spec(
        child->rl_left, child->rl_right, lp.width,
        dp(ui, lp.margins_dp.left), dp(ui, lp.margins_dp.right),
        dp(ui, parent->padding_left_dp), dp(ui, parent->padding_right_dp), my_width, ui);
    const android_measure_spec_t ch = relative_get_child_measure_spec(
        child->rl_top, child->rl_bottom, lp.height,
        dp(ui, lp.margins_dp.top), dp(ui, lp.margins_dp.bottom),
        dp(ui, parent->padding_top_dp), dp(ui, parent->padding_bottom_dp), my_height, ui);

    child->measured = measure_view(child, cw, ch, ui);
}

/* AOSP centerHorizontal (RelativeLayout.java:1076). */
void relative_center_horizontal(android_view_s* child, float my_width) {
    const float left = (my_width - child->measured.width) / 2.f;
    child->rl_left = left;
    child->rl_right = left + child->measured.width;
}

/* AOSP centerVertical (RelativeLayout.java:1084). */
void relative_center_vertical(android_view_s* child, float my_height) {
    const float top = (my_height - child->measured.height) / 2.f;
    child->rl_top = top;
    child->rl_bottom = top + child->measured.height;
}

/* AOSP positionAtEdge (RelativeLayout.java:868), LTR: left = paddingLeft +
 * leftMargin. */
void relative_position_at_edge(android_view_s* child, const android_ui_s* ui) {
    android_view_s* parent = child->parent;
    child->rl_left = dp(ui, parent->padding_left_dp) + dp(ui, child->lp.margins_dp.left);
    child->rl_right = child->rl_left + child->measured.width;
}

/* AOSP positionChildHorizontal (RelativeLayout.java:838); returns true when
 * the axis was offset (wrap-content re-centering will be needed). */
bool relative_position_child_horizontal(android_view_s* child, float my_width,
                                        bool wrap_content, const android_ui_s* ui) {
    const int* rules = child->relative_rules;
    if (child->rl_left == RL_UNSET && child->rl_right != RL_UNSET) {
        child->rl_left = child->rl_right - child->measured.width;
    } else if (child->rl_left != RL_UNSET && child->rl_right == RL_UNSET) {
        child->rl_right = child->rl_left + child->measured.width;
    } else if (child->rl_left == RL_UNSET && child->rl_right == RL_UNSET) {
        if (rules[ANDROID_RELATIVE_CENTER_IN_PARENT] != 0 ||
            rules[ANDROID_RELATIVE_CENTER_HORIZONTAL] != 0) {
            if (!wrap_content) {
                relative_center_horizontal(child, my_width);
            } else {
                relative_position_at_edge(child, ui);
            }
            return true;
        }
        relative_position_at_edge(child, ui);
    }
    return rules[ANDROID_RELATIVE_ALIGN_PARENT_END] != 0;
}

/* AOSP positionChildVertical (RelativeLayout.java:878). */
bool relative_position_child_vertical(android_view_s* child, float my_height,
                                      bool wrap_content, const android_ui_s* ui) {
    const int* rules = child->relative_rules;
    if (child->rl_top == RL_UNSET && child->rl_bottom != RL_UNSET) {
        child->rl_top = child->rl_bottom - child->measured.height;
    } else if (child->rl_top != RL_UNSET && child->rl_bottom == RL_UNSET) {
        child->rl_bottom = child->rl_top + child->measured.height;
    } else if (child->rl_top == RL_UNSET && child->rl_bottom == RL_UNSET) {
        if (rules[ANDROID_RELATIVE_CENTER_IN_PARENT] != 0 ||
            rules[ANDROID_RELATIVE_CENTER_VERTICAL] != 0) {
            if (!wrap_content) {
                relative_center_vertical(child, my_height);
            } else {
                child->rl_top = dp(ui, child->parent->padding_top_dp) + dp(ui, child->lp.margins_dp.top);
                child->rl_bottom = child->rl_top + child->measured.height;
            }
            return true;
        }
        child->rl_top = dp(ui, child->parent->padding_top_dp) + dp(ui, child->lp.margins_dp.top);
        child->rl_bottom = child->rl_top + child->measured.height;
    }
    return rules[ANDROID_RELATIVE_ALIGN_PARENT_BOTTOM] != 0;
}

} // namespace (RelativeLayout helpers)

/* AOSP RelativeLayout.onMeasure (RelativeLayout.java:406): two-pass measure
 * (horizontal rules then vertical rules), dependency-sorted children, wrap
 * content passes and the Gravity.apply offset pass. */
android_measured_size_t measure_relative(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    const float my_width = spec_w.mode == ANDROID_MEASURE_UNSPECIFIED ? -1.f : spec_w.size;
    const float my_height = spec_h.mode == ANDROID_MEASURE_UNSPECIFIED ? -1.f : spec_h.size;

    float width = 0.f, height = 0.f;
    if (spec_w.mode == ANDROID_MEASURE_EXACTLY) width = my_width;
    if (spec_h.mode == ANDROID_MEASURE_EXACTLY) height = my_height;

    const bool is_wrap_content_width = spec_w.mode != ANDROID_MEASURE_EXACTLY;
    const bool is_wrap_content_height = spec_h.mode != ANDROID_MEASURE_EXACTLY;

    /* AOSP LayoutParams.resolveRules (via getRules(layoutDirection), invoked
     * from sortChildren before every measure): resolve START/END verbs to
     * LEFT/RIGHT. The resolution always starts from the immutable initial
     * snapshot (RelativeLayout.java:1553) and writes the resolved form into
     * relative_rules (AOSP mRules); relative_rules_initial is never mutated. */
    for (android_view_s* child : view->children) {
        int32_t resolved[ANDROID_RELATIVE_VERB_COUNT];
        relative_resolve_rules_ltr(child, resolved);
        std::copy(std::begin(resolved), std::end(resolved), child->relative_rules);
    }

    RelativeGraph graph;
    graph.nodes.reserve(view->children.size());
    for (android_view_s* child : view->children) {
        graph.add(child);
    }
    const std::vector<android_view_s*> sorted_h =
        graph.get_sorted_views(kRulesHorizontal, 8);
    const std::vector<android_view_s*> sorted_v =
        graph.get_sorted_views(kRulesVertical, 5);

    /* AOSP positionChildHorizontal/Vertical return whether the axis was offset
     * (CENTER rules under wrap content); onMeasure gates the wrap-content
     * recentering passes with those flags (RelativeLayout.java:483-484,
     * 500-501, 553-568, 570-585). */
    bool offset_horizontal_axis = false;
    for (android_view_s* child : sorted_h) {
        if (child->visibility == ANDROID_GONE) continue;
        relative_apply_h_size_rules(child, graph, my_width, ui);
        relative_measure_child_horizontal(child, my_width, my_height, ui);
        offset_horizontal_axis |= relative_position_child_horizontal(
            child, my_width, is_wrap_content_width, ui);
    }

    float left = (std::numeric_limits<float>::max)();
    float top = (std::numeric_limits<float>::max)();
    float right = -(std::numeric_limits<float>::max)();
    float bottom = -(std::numeric_limits<float>::max)();

    bool offset_vertical_axis = false;
    for (android_view_s* child : sorted_v) {
        if (child->visibility == ANDROID_GONE) continue;
        relative_apply_v_size_rules(child, graph, my_height, child->measured_baseline, ui);
        relative_measure_child(child, my_width, my_height, ui);
        offset_vertical_axis |= relative_position_child_vertical(
            child, my_height, is_wrap_content_height, ui);

        if (is_wrap_content_width) {
            width = std::max(width, child->rl_right + dp(ui, child->lp.margins_dp.right));
        }
        if (is_wrap_content_height) {
            height = std::max(height, child->rl_bottom + dp(ui, child->lp.margins_dp.bottom));
        }
        left = std::min(left, child->rl_left - dp(ui, child->lp.margins_dp.left));
        top = std::min(top, child->rl_top - dp(ui, child->lp.margins_dp.top));
        right = std::max(right, child->rl_right + dp(ui, child->lp.margins_dp.right));
        bottom = std::max(bottom, child->rl_bottom + dp(ui, child->lp.margins_dp.bottom));
    }

    /* AOSP onMeasure picks mBaselineView as the top-start-most laid-out
     * visible child (RelativeLayout.java:540-555); compareLayoutPosition
     * orders by mTop first, then mLeft (:667-673). RelativeLayout.getBaseline
     * simply delegates to that child's baseline (:374-376), so the container
     * baseline is the child's own baseline (no position offset) or -1 when the
     * selected child has none. No baseline filter: AOSP selects regardless of
     * whether the child itself exposes a baseline. */
    view->measured_baseline = -1.f;
    android_view_s* baseline_view = nullptr;
    float baseline_top = (std::numeric_limits<float>::max)();
    float baseline_left = (std::numeric_limits<float>::max)();
    for (android_view_s* child : sorted_v) {
        if (child->visibility == ANDROID_GONE) continue;
        const float t = child->rl_top;
        const float l = child->rl_left;
        if (baseline_view == nullptr || t < baseline_top ||
            (t == baseline_top && l < baseline_left)) {
            baseline_view = child;
            baseline_top = t;
            baseline_left = l;
        }
    }
    if (baseline_view != nullptr) {
        view->measured_baseline = baseline_view->measured_baseline;
    }

    if (is_wrap_content_width) {
        width += dp(ui, view->padding_right_dp);
        /* AOSP: the view's own widthLayoutParams clamp (mLayoutParams.width >= 0,
         * RelativeLayout.java:562-564). */
        if (view->lp.width.kind == ANDROID_SIZE_KIND_EXACT &&
            view->lp.width.value_dp >= 0.f) {
            width = std::max(width, dp(ui, view->lp.width.value_dp));
        }
        width = std::max(width, dp(ui, view->min_width_dp));
        width = resolve_size(width, spec_w);
        /* AOSP recenters only when positionChildHorizontal actually offset the
         * axis (offsetHorizontalAxis, RelativeLayout.java:569). */
        if (offset_horizontal_axis) {
            for (android_view_s* child : sorted_v) {
                if (child->visibility == ANDROID_GONE) continue;
                const int* rules = child->relative_rules;
                if (rules[ANDROID_RELATIVE_CENTER_IN_PARENT] != 0 ||
                    rules[ANDROID_RELATIVE_CENTER_HORIZONTAL] != 0) {
                    relative_center_horizontal(child, width);
                } else if (rules[ANDROID_RELATIVE_ALIGN_PARENT_RIGHT] != 0) {
                    child->rl_left = width - dp(ui, view->padding_right_dp) - child->measured.width;
                    child->rl_right = child->rl_left + child->measured.width;
                }
            }
        }
    }

    if (is_wrap_content_height) {
        height += dp(ui, view->padding_bottom_dp);
        /* AOSP: the view's own heightLayoutParams clamp (mLayoutParams.height >= 0,
         * RelativeLayout.java:592-594). */
        if (view->lp.height.kind == ANDROID_SIZE_KIND_EXACT &&
            view->lp.height.value_dp >= 0.f) {
            height = std::max(height, dp(ui, view->lp.height.value_dp));
        }
        height = std::max(height, dp(ui, view->min_height_dp));
        height = resolve_size(height, spec_h);
        /* AOSP gates with offsetVerticalAxis (RelativeLayout.java:599). */
        if (offset_vertical_axis) {
            for (android_view_s* child : sorted_v) {
                if (child->visibility == ANDROID_GONE) continue;
                const int* rules = child->relative_rules;
                if (rules[ANDROID_RELATIVE_CENTER_IN_PARENT] != 0 ||
                    rules[ANDROID_RELATIVE_CENTER_VERTICAL] != 0) {
                    relative_center_vertical(child, height);
                } else if (rules[ANDROID_RELATIVE_ALIGN_PARENT_BOTTOM] != 0) {
                    child->rl_top = height - dp(ui, view->padding_bottom_dp) - child->measured.height;
                    child->rl_bottom = child->rl_top + child->measured.height;
                }
            }
        }
    }

    /* AOSP gravity pass (RelativeLayout.java:617): Gravity.apply against the
     * content bounds, then offset the whole group. Only the axes whose
     * gravity is set are offset (horizontalGravity/verticalGravity flags). */
    const int32_t hgrav = view->gravity & (ANDROID_GRAVITY_RELATIVE_LAYOUT_DIRECTION |
                                           ANDROID_GRAVITY_FILL_HORIZONTAL);
    const bool horizontal_gravity = hgrav != ANDROID_GRAVITY_START && hgrav != 0;
    const int32_t vgrav = view->gravity & ANDROID_GRAVITY_FILL_VERTICAL;
    const bool vertical_gravity = vgrav != ANDROID_GRAVITY_TOP && vgrav != 0;
    if (horizontal_gravity || vertical_gravity) {
        const float content_w = std::max(0.f, width - padding_h(view, ui));
        const float content_h = std::max(0.f, height - padding_v(view, ui));
        const float box_w = std::max(0.f, right - left);
        const float box_h = std::max(0.f, bottom - top);
        float ox = 0.f, oy = 0.f;
        apply_gravity(view->gravity, box_w, box_h, content_w, content_h, &ox, &oy);
        /* AOSP: selfBounds starts at (paddingLeft, paddingTop), so the offset
         * is contentBounds.left - left with contentBounds.left = paddingLeft + x
         * (RelativeLayout.java:617-627). */
        const float offset_x = dp(ui, view->padding_left_dp) + ox - left;
        const float offset_y = dp(ui, view->padding_top_dp) + oy - top;
        if (offset_x != 0.f || offset_y != 0.f) {
            for (android_view_s* child : sorted_v) {
                if (child->visibility == ANDROID_GONE) continue;
                if (horizontal_gravity) {
                    child->rl_left += offset_x;
                    child->rl_right += offset_x;
                }
                if (vertical_gravity) {
                    child->rl_top += offset_y;
                    child->rl_bottom += offset_y;
                }
            }
        }
    }

    return {width, height};
}

void layout_relative(android_view_s* view, float x, float y, float w, float h,
                     const android_ui_s* ui) {
    view->bounds = {x, y, w, h};
    /* AOSP onLayout (RelativeLayout.java:1093): the positions were already
     * computed during onMeasure and cached in the layout params; apply them
     * verbatim. */
    for (android_view_s* child : view->children) {
        if (child->visibility == ANDROID_GONE) {
            child->bounds = {x, y, 0.f, 0.f};
            continue;
        }
        layout_view(child, x + child->rl_left, y + child->rl_top,
                    child->rl_right - child->rl_left,
                    child->rl_bottom - child->rl_top, ui);
    }
}

} // namespace viewruntime::android
