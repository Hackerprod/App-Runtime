/* Faithful port of ConstraintWidget/ConstraintWidgetContainer/ConstraintAnchor
 * (androidx.constraintlayout.core.widgets) — the widget model feeding the
 * LinearSystem solver. The addToSolver/applyConstraints logic follows the AOSP
 * source; branches for barriers/guidelines/virtual layouts/circles/graph
 * optimization are reduced to their neutral state (not present / not enabled). */

#include "constraint_widget.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <string>
#include <utility>

namespace viewruntime::android::constraint {

/* ── ConstraintAnchor ──────────────────────────────────────────────── */

ConstraintAnchor* ConstraintAnchor::get_opposite() const {
    switch (type) {
        case Type::LEFT: return owner ? owner->get_anchor(Type::RIGHT) : nullptr;
        case Type::RIGHT: return owner ? owner->get_anchor(Type::LEFT) : nullptr;
        case Type::TOP: return owner ? owner->get_anchor(Type::BOTTOM) : nullptr;
        case Type::BOTTOM: return owner ? owner->get_anchor(Type::TOP) : nullptr;
        case Type::BASELINE: return owner ? owner->get_anchor(Type::BASELINE) : nullptr;
        case Type::CENTER: return owner ? owner->get_anchor(Type::CENTER) : nullptr;
        case Type::CENTER_X: return owner ? owner->get_anchor(Type::CENTER_X) : nullptr;
        case Type::CENTER_Y: return owner ? owner->get_anchor(Type::CENTER_Y) : nullptr;
        default: return nullptr;
    }
}

float ConstraintAnchor::get_margin() const {
    if (owner->visibility == ConstraintWidget::GONE) return 0.f; /* gone margin semantics */
    if (has_gone_margin && target && target->owner->visibility == ConstraintWidget::GONE)
        return gone_margin;
    return margin;
}

/* ── ConstraintWidget ──────────────────────────────────────────────── */

ConstraintWidget::ConstraintWidget() {
    for (int i = 0; i < 9; ++i) anchors[i].owner = this;
    anchors[1].type = ConstraintAnchor::Type::LEFT;
    anchors[2].type = ConstraintAnchor::Type::TOP;
    anchors[3].type = ConstraintAnchor::Type::RIGHT;
    anchors[4].type = ConstraintAnchor::Type::BOTTOM;
    anchors[5].type = ConstraintAnchor::Type::BASELINE;
    anchors[6].type = ConstraintAnchor::Type::CENTER;
    anchors[7].type = ConstraintAnchor::Type::CENTER_X;
    anchors[8].type = ConstraintAnchor::Type::CENTER_Y;
}

ConstraintWidget::ConstraintWidget(float w, float h) : ConstraintWidget() {
    width = w;
    height = h;
}

ConstraintWidget::ConstraintWidget(float px, float py, float w, float h)
    : ConstraintWidget() {
    x = px;
    y = py;
    width = w;
    height = h;
}

void ConstraintWidget::connect(ConstraintAnchor::Type from, ConstraintWidget* target,
                               ConstraintAnchor::Type to, float margin) {
    if (from == ConstraintAnchor::Type::CENTER) {
        if (to == ConstraintAnchor::Type::CENTER) {
            ConstraintAnchor* left = get_anchor(ConstraintAnchor::Type::LEFT);
            ConstraintAnchor* right = get_anchor(ConstraintAnchor::Type::RIGHT);
            ConstraintAnchor* top = get_anchor(ConstraintAnchor::Type::TOP);
            ConstraintAnchor* bottom = get_anchor(ConstraintAnchor::Type::BOTTOM);
            bool center_x = false;
            bool center_y = false;
            if (!(left->is_connected() || right->is_connected())) {
                connect(ConstraintAnchor::Type::LEFT, target, ConstraintAnchor::Type::LEFT, 0.f);
                connect(ConstraintAnchor::Type::RIGHT, target, ConstraintAnchor::Type::RIGHT, 0.f);
                center_x = true;
            }
            if (!(top->is_connected() || bottom->is_connected())) {
                connect(ConstraintAnchor::Type::TOP, target, ConstraintAnchor::Type::TOP, 0.f);
                connect(ConstraintAnchor::Type::BOTTOM, target, ConstraintAnchor::Type::BOTTOM, 0.f);
                center_y = true;
            }
            if (center_x && center_y) {
                get_anchor(ConstraintAnchor::Type::CENTER)->connect(
                    target->get_anchor(ConstraintAnchor::Type::CENTER), 0.f);
            } else if (center_x) {
                get_anchor(ConstraintAnchor::Type::CENTER_X)->connect(
                    target->get_anchor(ConstraintAnchor::Type::CENTER_X), 0.f);
            } else if (center_y) {
                get_anchor(ConstraintAnchor::Type::CENTER_Y)->connect(
                    target->get_anchor(ConstraintAnchor::Type::CENTER_Y), 0.f);
            }
            return;
        }
        /* CENTER to a side: fall through to the side connections */
    }
    ConstraintAnchor* source = get_anchor(from);
    ConstraintAnchor* dest = target->get_anchor(to);
    source->connect(dest, margin);
}

void ConstraintWidget::add(ConstraintWidget* child) {
    if (child->parent != nullptr && child->parent != this) {
        std::vector<ConstraintWidget*>& siblings = child->parent->children;
        siblings.erase(std::remove(siblings.begin(), siblings.end(), child), siblings.end());
    }
    children.push_back(child);
    child->parent = this;
    child->added = true;
}

void ConstraintWidget::reset_anchors() {
    for (int i = 0; i < 9; ++i) {
        anchors[i].target = nullptr;
        anchors[i].margin = 0.f;
    }
}

/* Port of ConstraintWidget.setDimensionRatio(String): accepts "W:H",
 * "H,W:H" (side prefix), or a plain float. */
void ConstraintWidget::set_dimension_ratio(const char* ratio) {
    if (ratio == nullptr || ratio[0] == '\0') {
        dimension_ratio = 0.f;
        dimension_ratio_side = UNKNOWN;
        return;
    }
    std::string s(ratio);
    int side = UNKNOWN;
    float value = 0.f;
    size_t comma = s.find(',');
    if (comma != std::string::npos && comma < s.size() - 1) {
        std::string dim = s.substr(0, comma);
        if (dim == "W" || dim == "w") {
            side = HORIZONTAL;
        } else if (dim == "H" || dim == "h") {
            side = VERTICAL;
        }
        comma = comma + 1;
    } else {
        comma = 0;
    }
    size_t colon = s.find(':', comma);
    if (colon != std::string::npos && colon < s.size() - 1) {
        std::string numerator = s.substr(comma, colon - comma);
        std::string denominator = s.substr(colon + 1);
        if (!numerator.empty() && !denominator.empty()) {
            float n = std::strtof(numerator.c_str(), nullptr);
            float d = std::strtof(denominator.c_str(), nullptr);
            if (n > 0.f && d > 0.f) {
                if (side == VERTICAL) {
                    value = std::fabs(d / n);
                } else {
                    value = std::fabs(n / d);
                }
            }
        }
    } else {
        std::string r = s.substr(comma);
        if (!r.empty()) {
            value = std::strtof(r.c_str(), nullptr);
        }
    }
    dimension_ratio = value;
    dimension_ratio_side = side;
}

/* ── Chain helpers (port of ConstraintWidget) ──────────────────────── */

bool ConstraintWidget::is_in_horizontal_chain() {
    ConstraintAnchor* left = get_anchor(ConstraintAnchor::Type::LEFT);
    ConstraintAnchor* right = get_anchor(ConstraintAnchor::Type::RIGHT);
    return (left->target != nullptr && left->target->target == left) ||
           (right->target != nullptr && right->target->target == right);
}

bool ConstraintWidget::is_in_vertical_chain() {
    ConstraintAnchor* top = get_anchor(ConstraintAnchor::Type::TOP);
    ConstraintAnchor* bottom = get_anchor(ConstraintAnchor::Type::BOTTOM);
    return (top->target != nullptr && top->target->target == top) ||
           (bottom->target != nullptr && bottom->target->target == bottom);
}

bool ConstraintWidget::is_chain_head(int orientation) {
    ConstraintAnchor* b = begin_anchor(orientation);
    ConstraintAnchor* e = end_anchor(orientation);
    return (b->target != nullptr && b->target->target != b) &&
           (e->target != nullptr && e->target->target == e);
}

ConstraintWidget* ConstraintWidget::get_previous_chain_member(int orientation) {
    ConstraintAnchor* b = begin_anchor(orientation);
    if (b->target != nullptr && b->target->target == b) {
        return b->target->owner;
    }
    return nullptr;
}

ConstraintWidget* ConstraintWidget::get_next_chain_member(int orientation) {
    ConstraintAnchor* e = end_anchor(orientation);
    if (e->target != nullptr && e->target->target == e) {
        return e->target->owner;
    }
    return nullptr;
}

/* ── ChainHead (port of ChainHead.define/defineChainProperties) ────── */

static bool is_match_constraint_equality_candidate(ConstraintWidget* widget, int orientation) {
    return widget->visibility != ConstraintWidget::GONE &&
        widget->h_behavior == ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT &&
        (widget->resolved_match_constraint_default(orientation) ==
             ConstraintWidget::MATCH_CONSTRAINT_SPREAD ||
         widget->resolved_match_constraint_default(orientation) ==
             ConstraintWidget::MATCH_CONSTRAINT_RATIO);
}

void ChainHead::define_chain_properties() {
    ConstraintWidget* last_visited = first;
    optimizable = true;

    ConstraintWidget* widget = first;
    ConstraintWidget* next = first;
    bool done = false;
    auto behavior_of = [](ConstraintWidget* w, int o) {
        return o == ConstraintWidget::HORIZONTAL ? w->h_behavior : w->v_behavior;
    };
    auto length_of = [](ConstraintWidget* w, int o) {
        return o == ConstraintWidget::HORIZONTAL ? w->width : w->height;
    };
    while (!done) {
        widgets_count++;
        widget->next_chain_widget(orientation) = nullptr;
        widget->next_match_constraint_widget(orientation) = nullptr;
        if (widget->visibility != ConstraintWidget::GONE) {
            visible_widgets++;
            if (behavior_of(widget, orientation) !=
                ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT) {
                total_size += static_cast<int>(length_of(widget, orientation));
            }
            total_size += static_cast<int>(widget->begin_anchor(orientation)->get_margin());
            total_size += static_cast<int>(widget->end_anchor(orientation)->get_margin());
            total_margins += static_cast<int>(widget->begin_anchor(orientation)->get_margin());
            total_margins += static_cast<int>(widget->end_anchor(orientation)->get_margin());
            if (first_visible_widget == nullptr) {
                first_visible_widget = widget;
            }
            last_visible_widget = widget;

            if (behavior_of(widget, orientation) ==
                ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT) {
                const int resolved = widget->resolved_match_constraint_default(orientation);
                if (resolved == ConstraintWidget::MATCH_CONSTRAINT_SPREAD ||
                    resolved == ConstraintWidget::MATCH_CONSTRAINT_RATIO ||
                    resolved == ConstraintWidget::MATCH_CONSTRAINT_PERCENT) {
                    widgets_match_count++;
                    const float w = widget->weight(orientation);
                    if (w > 0.f) {
                        total_weight += w;
                    }
                    if (is_match_constraint_equality_candidate(widget, orientation)) {
                        if (w < 0.f) {
                            has_undefined_weights = true;
                        } else {
                            has_defined_weights = true;
                        }
                        weighted_match_constraints_widgets.push_back(widget);
                    }
                    if (first_match_constraint_widget == nullptr) {
                        first_match_constraint_widget = widget;
                    }
                    if (last_match_constraint_widget != nullptr) {
                        last_match_constraint_widget->next_match_constraint_widget(orientation) =
                            widget;
                    }
                    last_match_constraint_widget = widget;
                }
                const bool spread_ok =
                    widget->resolved_match_constraint_default(orientation) ==
                    ConstraintWidget::MATCH_CONSTRAINT_SPREAD;
                const float mc_min = orientation == ConstraintWidget::HORIZONTAL
                    ? widget->match_constraint_min_w : widget->match_constraint_min_h;
                const float mc_max = orientation == ConstraintWidget::HORIZONTAL
                    ? widget->match_constraint_max_w : widget->match_constraint_max_h;
                if (!spread_ok || mc_min != 0.f || mc_max != 0.f) {
                    optimizable = false;
                }
                if (widget->dimension_ratio != 0.f) {
                    optimizable = false;
                    has_ratio = true;
                }
            }
        }
        if (last_visited != widget) {
            last_visited->next_chain_widget(orientation) = widget;
        }
        last_visited = widget;

        ConstraintAnchor* next_anchor = widget->end_anchor(orientation)->target;
        if (next_anchor != nullptr) {
            next = next_anchor->owner;
            if (next->begin_anchor(orientation)->target == nullptr ||
                next->begin_anchor(orientation)->target->owner != widget) {
                next = nullptr;
            }
        } else {
            next = nullptr;
        }
        if (next != nullptr) {
            widget = next;
        } else {
            done = true;
        }
    }
    if (first_visible_widget != nullptr) {
        total_size -= static_cast<int>(first_visible_widget->begin_anchor(orientation)->get_margin());
    }
    if (last_visible_widget != nullptr) {
        total_size -= static_cast<int>(last_visible_widget->end_anchor(orientation)->get_margin());
    }
    last = widget;

    if (orientation == ConstraintWidget::HORIZONTAL && is_rtl) {
        head = last;
    } else {
        head = first;
    }

    has_complex_match_weights = has_defined_weights && has_undefined_weights;
}

void ChainHead::define() {
    if (!defined) {
        define_chain_properties();
    }
    defined = true;
}

/* ── Chain (port of Chain.applyChainConstraints) ───────────────────── */

void Chain::apply_chain_constraints(ConstraintWidgetContainer& container,
                                    ConstraintSystem& system, int orientation) {
    const int offset = orientation == ConstraintWidget::HORIZONTAL ? 0 : 2;
    std::vector<ChainHead*>& chains = orientation == ConstraintWidget::HORIZONTAL
        ? container.horizontal_chains : container.vertical_chains;
    for (ChainHead* head : chains) {
        if (head == nullptr) continue;
        head->define();
        if (head->first != nullptr) {
            apply_chain_constraints_inner(container, system, orientation, offset, *head);
        }
    }
}

void Chain::apply_chain_constraints_inner(ConstraintWidgetContainer& container,
                                          ConstraintSystem& system, int orientation,
                                          int offset, ChainHead& chain_head) {
    (void)offset; /* AOSP indexes mListAnchors[offset]; the port uses begin/end helpers */
    ConstraintWidget* first = chain_head.first;
    ConstraintWidget* last = chain_head.last;
    ConstraintWidget* first_visible_widget = chain_head.first_visible_widget;
    ConstraintWidget* last_visible_widget = chain_head.last_visible_widget;
    ConstraintWidget* head = chain_head.head;

    ConstraintWidget* widget = first;
    ConstraintWidget* next = nullptr;
    bool done = false;

    float total_weights = chain_head.total_weight;

    /* Wrap-content along the chain orientation only (AOSP
     * container.mListDimensionBehaviors[orientation] == WRAP_CONTENT). */
    const bool chain_wrap = orientation == ConstraintWidget::HORIZONTAL
        ? container.h_behavior == ConstraintWidget::DimensionBehaviour::WRAP_CONTENT
        : container.v_behavior == ConstraintWidget::DimensionBehaviour::WRAP_CONTENT;

    bool is_chain_spread = false;
    bool is_chain_spread_inside = false;
    bool is_chain_packed = false;
    if (orientation == ConstraintWidget::HORIZONTAL) {
        is_chain_spread = head->horizontal_chain_style == ConstraintWidget::CHAIN_SPREAD;
        is_chain_spread_inside =
            head->horizontal_chain_style == ConstraintWidget::CHAIN_SPREAD_INSIDE;
        is_chain_packed = head->horizontal_chain_style == ConstraintWidget::CHAIN_PACKED;
    } else {
        is_chain_spread = head->vertical_chain_style == ConstraintWidget::CHAIN_SPREAD;
        is_chain_spread_inside =
            head->vertical_chain_style == ConstraintWidget::CHAIN_SPREAD_INSIDE;
        is_chain_packed = head->vertical_chain_style == ConstraintWidget::CHAIN_PACKED;
    }

    /* USE_CHAIN_OPTIMIZATION/Direct path is not ported. */

    /* Traversal: basic ordering constraints + match-constraint linked list */
    while (!done) {
        ConstraintAnchor* begin = widget->begin_anchor(orientation);

        int strength = ST_HIGHEST;
        if (is_chain_packed) {
            strength = ST_LOW;
        }
        float margin = begin->get_margin();
        const bool is_spread_only =
            (orientation == ConstraintWidget::HORIZONTAL
                 ? widget->h_behavior : widget->v_behavior) ==
                ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT &&
            widget->resolved_match_constraint_default(orientation) ==
                ConstraintWidget::MATCH_CONSTRAINT_SPREAD;

        if (begin->target != nullptr && widget != first) {
            margin += begin->target->get_margin();
        }

        if (is_chain_packed && widget != first && widget != first_visible_widget) {
            strength = ST_FIXED;
        }

        if (begin->target != nullptr) {
            const int t = begin->target->solver_variable;
            if (widget == first_visible_widget) {
                system.addGreaterThan(begin->solver_variable, t, margin, ST_BARRIER);
            } else {
                system.addGreaterThan(begin->solver_variable, t, margin, ST_FIXED);
            }
            if (is_spread_only && !is_chain_packed) {
                strength = ST_EQUALITY;
            }
            system.addEquality(begin->solver_variable, t, margin, strength);
        }

        if (chain_wrap) {
            if (widget->visibility != ConstraintWidget::GONE &&
                (orientation == ConstraintWidget::HORIZONTAL
                     ? widget->h_behavior : widget->v_behavior) ==
                    ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT) {
                system.addGreaterThan(widget->end_anchor(orientation)->solver_variable,
                                      widget->begin_anchor(orientation)->solver_variable,
                                      0.f, ST_EQUALITY);
            }
            system.addGreaterThan(widget->begin_anchor(orientation)->solver_variable,
                                  container.begin_anchor(orientation)->solver_variable,
                                  0.f, ST_FIXED);
        }

        /* go to the next widget */
        ConstraintAnchor* next_anchor = widget->end_anchor(orientation)->target;
        if (next_anchor != nullptr) {
            next = next_anchor->owner;
            if (next->begin_anchor(orientation)->target == nullptr ||
                next->begin_anchor(orientation)->target->owner != widget) {
                next = nullptr;
            }
        } else {
            next = nullptr;
        }
        if (next != nullptr) {
            widget = next;
        } else {
            done = true;
        }
    }

    /* Make sure we have constraints for the last anchors / targets */
    if (last_visible_widget != nullptr && last->end_anchor(orientation)->target != nullptr) {
        ConstraintAnchor* end = last_visible_widget->end_anchor(orientation);
        const bool is_spread_only =
            (orientation == ConstraintWidget::HORIZONTAL
                 ? last_visible_widget->h_behavior : last_visible_widget->v_behavior) ==
                ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT &&
            last_visible_widget->resolved_match_constraint_default(orientation) ==
                ConstraintWidget::MATCH_CONSTRAINT_SPREAD;
        const int t = last->end_anchor(orientation)->target->solver_variable;
        if (is_spread_only && !is_chain_packed &&
            last->end_anchor(orientation)->target->owner == &container) {
            system.addEquality(end->solver_variable, t, -end->get_margin(), ST_EQUALITY);
        } else if (is_chain_packed &&
                   last->end_anchor(orientation)->target->owner == &container) {
            system.addEquality(end->solver_variable, t, -end->get_margin(), ST_HIGHEST);
        }
        system.addLowerThan(end->solver_variable, t, -end->get_margin(), ST_BARRIER);
    }

    /* ... and make sure the root end is constrained in wrap content. */
    if (chain_wrap) {
        system.addGreaterThan(container.end_anchor(orientation)->solver_variable,
                              last->end_anchor(orientation)->solver_variable,
                              last->end_anchor(orientation)->get_margin(), ST_FIXED);
    }

    /* Now, let's apply the centering / spreading for matched constraints widgets */
    std::vector<ConstraintWidget*>& list_match_constraints =
        chain_head.weighted_match_constraints_widgets;
    if (!list_match_constraints.empty()) {
        const size_t count = list_match_constraints.size();
        if (count > 1) {
            ConstraintWidget* last_match = nullptr;
            float last_weight = 0.f;

            if (chain_head.has_undefined_weights && !chain_head.has_complex_match_weights) {
                total_weights = static_cast<float>(chain_head.widgets_match_count);
            }

            for (size_t i = 0; i < count; i++) {
                ConstraintWidget* match = list_match_constraints[i];
                float current_weight = match->weight(orientation);

                if (current_weight < 0.f) {
                    if (chain_head.has_complex_match_weights) {
                        system.addEquality(match->end_anchor(orientation)->solver_variable,
                                           match->begin_anchor(orientation)->solver_variable,
                                           0.f, ST_HIGHEST);
                        continue;
                    }
                    current_weight = 1.f;
                }
                if (current_weight == 0.f) {
                    system.addEquality(match->end_anchor(orientation)->solver_variable,
                                       match->begin_anchor(orientation)->solver_variable,
                                       0.f, ST_FIXED);
                    continue;
                }

                if (last_match != nullptr) {
                    const int begin = last_match->begin_anchor(orientation)->solver_variable;
                    const int end = last_match->end_anchor(orientation)->solver_variable;
                    const int next_begin = match->begin_anchor(orientation)->solver_variable;
                    const int next_end = match->end_anchor(orientation)->solver_variable;
                    Row row;
                    if (total_weights == 0.f || last_weight == current_weight) {
                        row.put(begin, 1.f);
                        row.put(end, -1.f);
                        row.put(next_end, 1.f);
                        row.put(next_begin, -1.f);
                    } else {
                        if (last_weight == 0.f) {
                            row.put(begin, 1.f);
                            row.put(end, -1.f);
                        } else if (current_weight == 0.f) {
                            row.put(next_begin, 1.f);
                            row.put(next_end, -1.f);
                        } else {
                            const float cw = last_weight / total_weights;
                            const float nw = current_weight / total_weights;
                            const float w = cw / nw;
                            row.put(begin, 1.f);
                            row.put(end, -1.f);
                            row.put(next_end, w);
                            row.put(next_begin, -w);
                        }
                    }
                    system.addConstraint(std::move(row));
                }

                last_match = match;
                last_weight = current_weight;
            }
        }
    }

    /* Finally, let's apply the specific rules dealing with the different chain types */
    if (first_visible_widget != nullptr &&
        (first_visible_widget == last_visible_widget || is_chain_packed)) {
        ConstraintAnchor* begin = first->begin_anchor(orientation);
        ConstraintAnchor* end = last->end_anchor(orientation);
        int begin_target = begin->target != nullptr ? begin->target->solver_variable : -1;
        int end_target = end->target != nullptr ? end->target->solver_variable : -1;
        begin = first_visible_widget->begin_anchor(orientation);
        if (last_visible_widget != nullptr) {
            end = last_visible_widget->end_anchor(orientation);
        }
        if (begin_target != -1 && end_target != -1) {
            float bias = 0.5f;
            if (orientation == ConstraintWidget::HORIZONTAL) {
                bias = head->horizontal_bias;
            } else {
                bias = head->vertical_bias;
            }
            const float begin_margin = begin->get_margin();
            const float end_margin = end->get_margin();
            system.addCentering(begin->solver_variable, begin_target, begin_margin, bias,
                                end_target, end->solver_variable, end_margin, ST_CENTERING);
        }
    } else if (is_chain_spread && first_visible_widget != nullptr) {
        /* for chain spread, we need to add equal dimensions in between *visible* widgets */
        widget = first_visible_widget;
        ConstraintWidget* previous_visible_widget = first_visible_widget;
        const bool apply_fixed_equality = chain_head.widgets_match_count > 0 &&
            (chain_head.widgets_count == chain_head.widgets_match_count);
        while (widget != nullptr) {
            next = widget->next_chain_widget(orientation);
            while (next != nullptr && next->visibility == ConstraintWidget::GONE) {
                next = next->next_chain_widget(orientation);
            }
            if (next != nullptr || widget == last_visible_widget) {
                ConstraintAnchor* begin_anchor = widget->begin_anchor(orientation);
                const int begin = begin_anchor->solver_variable;
                int begin_target = begin_anchor->target != nullptr
                    ? begin_anchor->target->solver_variable : -1;
                if (previous_visible_widget != widget) {
                    begin_target =
                        previous_visible_widget->end_anchor(orientation)->solver_variable;
                } else if (widget == first_visible_widget) {
                    begin_target = first->begin_anchor(orientation)->target != nullptr
                        ? first->begin_anchor(orientation)->target->solver_variable : -1;
                }

                ConstraintAnchor* begin_next_anchor = nullptr;
                int begin_next = -1;
                float begin_margin = begin_anchor->get_margin();
                float next_margin = widget->end_anchor(orientation)->get_margin();

                if (next != nullptr) {
                    begin_next_anchor = next->begin_anchor(orientation);
                    begin_next = begin_next_anchor->solver_variable;
                } else {
                    begin_next_anchor = last->end_anchor(orientation)->target;
                    if (begin_next_anchor != nullptr) {
                        begin_next = begin_next_anchor->solver_variable;
                    }
                }
                const int begin_next_target = widget->end_anchor(orientation)->solver_variable;

                if (begin_next_anchor != nullptr) {
                    next_margin += begin_next_anchor->get_margin();
                }
                begin_margin += previous_visible_widget->end_anchor(orientation)->get_margin();
                if (begin != -1 && begin_target != -1 && begin_next != -1 &&
                    begin_next_target != -1) {
                    float margin1 = begin_margin;
                    if (widget == first_visible_widget) {
                        margin1 = first_visible_widget->begin_anchor(orientation)->get_margin();
                    }
                    float margin2 = next_margin;
                    if (widget == last_visible_widget) {
                        margin2 = last_visible_widget->end_anchor(orientation)->get_margin();
                    }
                    int strength = ST_EQUALITY;
                    if (apply_fixed_equality) {
                        strength = ST_FIXED;
                    }
                    system.addCentering(begin, begin_target, margin1, 0.5f,
                                        begin_next, begin_next_target, margin2, strength);
                }
            }
            if (widget->visibility != ConstraintWidget::GONE) {
                previous_visible_widget = widget;
            }
            widget = next;
        }
    } else if (is_chain_spread_inside && first_visible_widget != nullptr) {
        /* for chain spread inside, we need to add equal dimensions in between *visible* widgets */
        widget = first_visible_widget;
        ConstraintWidget* previous_visible_widget = first_visible_widget;
        const bool apply_fixed_equality = chain_head.widgets_match_count > 0 &&
            (chain_head.widgets_count == chain_head.widgets_match_count);
        while (widget != nullptr) {
            next = widget->next_chain_widget(orientation);
            while (next != nullptr && next->visibility == ConstraintWidget::GONE) {
                next = next->next_chain_widget(orientation);
            }
            if (widget != first_visible_widget && widget != last_visible_widget &&
                next != nullptr) {
                if (next == last_visible_widget) {
                    next = nullptr;
                }
                ConstraintAnchor* begin_anchor = widget->begin_anchor(orientation);
                const int begin = begin_anchor->solver_variable;
                int begin_target =
                    previous_visible_widget->end_anchor(orientation)->solver_variable;
                ConstraintAnchor* begin_next_anchor = nullptr;
                int begin_next = -1;
                int begin_next_target = -1;
                float begin_margin = begin_anchor->get_margin();
                float next_margin = widget->end_anchor(orientation)->get_margin();

                if (next != nullptr) {
                    begin_next_anchor = next->begin_anchor(orientation);
                    begin_next = begin_next_anchor->solver_variable;
                    begin_next_target = begin_next_anchor->target != nullptr
                        ? begin_next_anchor->target->solver_variable : -1;
                } else {
                    begin_next_anchor = last_visible_widget->begin_anchor(orientation);
                    if (begin_next_anchor != nullptr) {
                        begin_next = begin_next_anchor->solver_variable;
                    }
                    begin_next_target = widget->end_anchor(orientation)->solver_variable;
                }

                if (begin_next_anchor != nullptr) {
                    next_margin += begin_next_anchor->get_margin();
                }
                begin_margin += previous_visible_widget->end_anchor(orientation)->get_margin();
                int strength = ST_HIGHEST;
                if (apply_fixed_equality) {
                    strength = ST_FIXED;
                }
                if (begin != -1 && begin_target != -1 && begin_next != -1 &&
                    begin_next_target != -1) {
                    system.addCentering(begin, begin_target, begin_margin, 0.5f,
                                        begin_next, begin_next_target, next_margin, strength);
                }
            }
            if (widget->visibility != ConstraintWidget::GONE) {
                previous_visible_widget = widget;
            }
            widget = next;
        }
        ConstraintAnchor* begin = first_visible_widget->begin_anchor(orientation);
        ConstraintAnchor* begin_target = first->begin_anchor(orientation)->target;
        ConstraintAnchor* end = last_visible_widget->end_anchor(orientation);
        ConstraintAnchor* end_target = last->end_anchor(orientation)->target;
        const int end_points_strength = ST_EQUALITY;
        if (begin_target != nullptr) {
            if (first_visible_widget != last_visible_widget) {
                system.addEquality(begin->solver_variable, begin_target->solver_variable,
                                   begin->get_margin(), end_points_strength);
            } else if (end_target != nullptr) {
                system.addCentering(begin->solver_variable, begin_target->solver_variable,
                                    begin->get_margin(), 0.5f, end->solver_variable,
                                    end_target->solver_variable, end->get_margin(),
                                    end_points_strength);
            }
        }
        if (end_target != nullptr && (first_visible_widget != last_visible_widget)) {
            system.addEquality(end->solver_variable, end_target->solver_variable,
                               -end->get_margin(), end_points_strength);
        }
    }

    /* final centering, necessary if the chain is larger than the available space... */
    if ((is_chain_spread || is_chain_spread_inside) && first_visible_widget != nullptr &&
        first_visible_widget != last_visible_widget) {
        ConstraintAnchor* begin = first_visible_widget->begin_anchor(orientation);
        if (last_visible_widget == nullptr) {
            last_visible_widget = first_visible_widget;
        }
        ConstraintAnchor* end = last_visible_widget->end_anchor(orientation);
        int begin_target = begin->target != nullptr ? begin->target->solver_variable : -1;
        int end_target = end->target != nullptr ? end->target->solver_variable : -1;
        if (last != last_visible_widget) {
            ConstraintAnchor* real_end = last->end_anchor(orientation);
            end_target = real_end->target != nullptr ? real_end->target->solver_variable : -1;
        }
        if (first_visible_widget == last_visible_widget) {
            begin = first_visible_widget->begin_anchor(orientation);
            end = first_visible_widget->end_anchor(orientation);
        }
        if (begin_target != -1 && end_target != -1) {
            const float bias = 0.5f;
            const float begin_margin = begin->get_margin();
            const float end_margin = last_visible_widget->end_anchor(orientation)->get_margin();
            system.addCentering(begin->solver_variable, begin_target, begin_margin, bias,
                                end_target, end->solver_variable, end_margin, ST_EQUALITY);
        }
    }
}

void ConstraintWidget::create_object_variables(ConstraintSystem& system) {
    /* Reset stale solver ids from a previous solve: system.reset() restarts
     * the variable counter, so ids from the last layout are dangling. AOSP
     * does the same (ConstraintWidget.reset() nulls every solverVariable). */
    for (int i = 0; i < 9; ++i) {
        anchors[i].solver_variable = -1;
    }
    get_anchor(ConstraintAnchor::Type::LEFT)->solver_variable = system.createVariable();
    get_anchor(ConstraintAnchor::Type::TOP)->solver_variable = system.createVariable();
    get_anchor(ConstraintAnchor::Type::RIGHT)->solver_variable = system.createVariable();
    get_anchor(ConstraintAnchor::Type::BOTTOM)->solver_variable = system.createVariable();
    /* AOSP: baseline variable created when the baseline distance is set
     * (ConstraintWidget.createObjectVariables checks mBaselineDistance > 0). */
    if (baseline_distance > 0.f) {
        get_anchor(ConstraintAnchor::Type::BASELINE)->solver_variable = system.createVariable();
    }
}

/* ── addToSolver (port of ConstraintWidget.addToSolver) ────────────── */

void ConstraintWidget::add_to_solver(ConstraintSystem& system, bool optimize) {
    (void)optimize; /* graph optimization is not ported; always non-optimized */
    const int left = get_anchor(ConstraintAnchor::Type::LEFT)->solver_variable;
    const int right = get_anchor(ConstraintAnchor::Type::RIGHT)->solver_variable;
    const int top = get_anchor(ConstraintAnchor::Type::TOP)->solver_variable;
    const int bottom = get_anchor(ConstraintAnchor::Type::BOTTOM)->solver_variable;
    /* AOSP addToSolver creates the baseline variable lazily via
     * createObjectVariable(mBaseline), so it is never -1 even when the
     * baseline distance is 0 (the GONE path still uses it). */
    ConstraintAnchor* baseline_anchor = get_anchor(ConstraintAnchor::Type::BASELINE);
    if (baseline_anchor->solver_variable == -1) {
        baseline_anchor->solver_variable = system.createVariable();
    }
    const int baseline = baseline_anchor->solver_variable;

    bool horizontal_parent_wrap_content = false;
    bool vertical_parent_wrap_content = false;
    if (parent != nullptr) {
        horizontal_parent_wrap_content =
            parent->h_behavior == DimensionBehaviour::WRAP_CONTENT;
        vertical_parent_wrap_content =
            parent->v_behavior == DimensionBehaviour::WRAP_CONTENT;
    }

    /* AOSP early-returns for GONE without dependencies etc.; our port keeps
     * every visible widget in the system (mVisibility != GONE is the norm). */

    in_horizontal_chain = false;
    in_vertical_chain = false;
    if (parent != nullptr) {
        /* Add this widget to a chain if it is the Head of it. */
        if (parent->is_container() && is_chain_head(HORIZONTAL)) {
            static_cast<ConstraintWidgetContainer*>(parent)->add_chain(this, HORIZONTAL);
            in_horizontal_chain = true;
        } else {
            in_horizontal_chain = is_in_horizontal_chain();
        }
        if (parent->is_container() && is_chain_head(VERTICAL)) {
            static_cast<ConstraintWidgetContainer*>(parent)->add_chain(this, VERTICAL);
            in_vertical_chain = true;
        } else {
            in_vertical_chain = is_in_vertical_chain();
        }

        if (!in_horizontal_chain && horizontal_parent_wrap_content && visibility != GONE &&
            get_anchor(ConstraintAnchor::Type::LEFT)->target == nullptr &&
            get_anchor(ConstraintAnchor::Type::RIGHT)->target == nullptr) {
            system.addGreaterThan(parent->get_anchor(ConstraintAnchor::Type::RIGHT)->solver_variable,
                                  right, 0.f, ST_LOW);
        }
        if (!in_vertical_chain && vertical_parent_wrap_content && visibility != GONE &&
            get_anchor(ConstraintAnchor::Type::TOP)->target == nullptr &&
            get_anchor(ConstraintAnchor::Type::BOTTOM)->target == nullptr &&
            get_anchor(ConstraintAnchor::Type::BASELINE) == nullptr) {
            system.addGreaterThan(parent->get_anchor(ConstraintAnchor::Type::BOTTOM)->solver_variable,
                                  bottom, 0.f, ST_LOW);
        }
    }

    float w = width;
    if (w < min_width) w = min_width;
    float h = height;
    if (h < min_height) h = min_height;

    const bool h_dim_fixed = h_behavior != DimensionBehaviour::MATCH_CONSTRAINT;
    const bool v_dim_fixed = v_behavior != DimensionBehaviour::MATCH_CONSTRAINT;

    bool use_ratio = false;
    int resolved_ratio_side = dimension_ratio_side;
    float resolved_ratio = dimension_ratio;

    int match_constraint_default_width = match_constraint_default_w;
    int match_constraint_default_height = match_constraint_default_h;

    if (dimension_ratio > 0.f && visibility != GONE) {
        use_ratio = true;
        if (h_behavior == DimensionBehaviour::MATCH_CONSTRAINT &&
            match_constraint_default_width == MATCH_CONSTRAINT_SPREAD) {
            match_constraint_default_width = MATCH_CONSTRAINT_RATIO;
        }
        if (v_behavior == DimensionBehaviour::MATCH_CONSTRAINT &&
            match_constraint_default_height == MATCH_CONSTRAINT_SPREAD) {
            match_constraint_default_height = MATCH_CONSTRAINT_RATIO;
        }

        if (h_behavior == DimensionBehaviour::MATCH_CONSTRAINT &&
            v_behavior == DimensionBehaviour::MATCH_CONSTRAINT &&
            match_constraint_default_width == MATCH_CONSTRAINT_RATIO &&
            match_constraint_default_height == MATCH_CONSTRAINT_RATIO) {
            setup_dimension_ratio(horizontal_parent_wrap_content,
                                  vertical_parent_wrap_content, h_dim_fixed, v_dim_fixed);
        } else if (h_behavior == DimensionBehaviour::MATCH_CONSTRAINT &&
                   match_constraint_default_width == MATCH_CONSTRAINT_RATIO) {
            resolved_ratio_side = HORIZONTAL;
            w = resolved_ratio * h;
            if (v_behavior != DimensionBehaviour::MATCH_CONSTRAINT) {
                match_constraint_default_width = MATCH_CONSTRAINT_RATIO_RESOLVED;
                use_ratio = false;
            }
        } else if (v_behavior == DimensionBehaviour::MATCH_CONSTRAINT &&
                   match_constraint_default_height == MATCH_CONSTRAINT_RATIO) {
            resolved_ratio_side = VERTICAL;
            if (dimension_ratio_side == UNKNOWN) {
                resolved_ratio = 1.f / resolved_ratio;
            }
            h = resolved_ratio * w;
            if (h_behavior != DimensionBehaviour::MATCH_CONSTRAINT) {
                match_constraint_default_height = MATCH_CONSTRAINT_RATIO_RESOLVED;
                use_ratio = false;
            }
        }
    }

    const bool use_horizontal_ratio = use_ratio &&
        (resolved_ratio_side == HORIZONTAL || resolved_ratio_side == UNKNOWN);
    const bool use_vertical_ratio = use_ratio &&
        (resolved_ratio_side == VERTICAL || resolved_ratio_side == UNKNOWN);

    resolved_match_constraint_default_w = match_constraint_default_width;
    resolved_match_constraint_default_h = match_constraint_default_height;

    /* Horizontal resolution */
    bool wrap_content = (h_behavior == DimensionBehaviour::WRAP_CONTENT) && is_container();
    if (wrap_content) {
        w = 0.f;
    }

    bool apply_position = true;
    if (get_anchor(ConstraintAnchor::Type::CENTER)->is_connected()) {
        apply_position = false;
    }

    /* mHorizontalResolution == UNKNOWN != DIRECT -> always run (non-optimized). */
    {
        const int parent_max = parent != nullptr
            ? parent->get_anchor(ConstraintAnchor::Type::RIGHT)->solver_variable : -1;
        const int parent_min = parent != nullptr
            ? parent->get_anchor(ConstraintAnchor::Type::LEFT)->solver_variable : -1;
        apply_constraints(system, true, horizontal_parent_wrap_content,
                          vertical_parent_wrap_content, is_terminal_widget_h,
                          parent_min, parent_max, h_behavior, wrap_content,
                          get_anchor(ConstraintAnchor::Type::LEFT),
                          get_anchor(ConstraintAnchor::Type::RIGHT),
                          x, w, min_width, max_width, horizontal_bias, use_horizontal_ratio,
                          v_behavior == DimensionBehaviour::MATCH_CONSTRAINT,
                          in_horizontal_chain, in_vertical_chain, is_in_barrier(HORIZONTAL),
                          match_constraint_default_width, match_constraint_default_height,
                          match_constraint_min_w, match_constraint_max_w,
                          match_constraint_percent_w, apply_position);
    }

    /* Vertical resolution */
    {
        wrap_content = (v_behavior == DimensionBehaviour::WRAP_CONTENT) && is_container();
        if (wrap_content) {
            h = 0.f;
        }

        const int parent_max = parent != nullptr
            ? parent->get_anchor(ConstraintAnchor::Type::BOTTOM)->solver_variable : -1;
        const int parent_min = parent != nullptr
            ? parent->get_anchor(ConstraintAnchor::Type::TOP)->solver_variable : -1;

        if (baseline_distance > 0.f || visibility == GONE) {
            ConstraintAnchor* m_baseline = get_anchor(ConstraintAnchor::Type::BASELINE);
            if (m_baseline->target != nullptr) {
                system.addEquality(baseline, top, baseline_distance, ST_FIXED);
                int baseline_target = m_baseline->target->solver_variable;
                int baseline_margin = static_cast<int>(m_baseline->get_margin());
                system.addEquality(baseline, baseline_target, static_cast<float>(baseline_margin),
                                   ST_FIXED);
                apply_position = false;
                if (vertical_parent_wrap_content) {
                    system.addGreaterThan(parent_max, bottom, 0.f, ST_EQUALITY);
                }
            } else if (visibility == GONE) {
                system.addEquality(baseline, top, m_baseline->get_margin(), ST_FIXED);
            } else {
                system.addEquality(baseline, top, baseline_distance, ST_FIXED);
            }
        }

        apply_constraints(system, false, vertical_parent_wrap_content,
                          horizontal_parent_wrap_content, is_terminal_widget_v,
                          parent_min, parent_max, v_behavior, wrap_content,
                          get_anchor(ConstraintAnchor::Type::TOP),
                          get_anchor(ConstraintAnchor::Type::BOTTOM),
                          y, h, min_height, max_height, vertical_bias, use_vertical_ratio,
                          h_behavior == DimensionBehaviour::MATCH_CONSTRAINT,
                          in_vertical_chain, in_horizontal_chain, is_in_barrier(VERTICAL),
                          match_constraint_default_height, match_constraint_default_width,
                          match_constraint_min_h, match_constraint_max_h,
                          match_constraint_percent_h, apply_position);
    }

    if (use_ratio) {
        if (resolved_ratio_side == VERTICAL) {
            system.addRatio(bottom, top, right, left, resolved_ratio, ST_FIXED);
        } else {
            system.addRatio(right, left, bottom, top, resolved_ratio, ST_FIXED);
        }
    }
}

void ConstraintWidget::setup_dimension_ratio(bool h_parent_wrap, bool v_parent_wrap,
                                             bool h_dim_fixed, bool v_dim_fixed) {
    (void)h_parent_wrap;
    (void)v_parent_wrap;
    if (dimension_ratio_side == UNKNOWN) {
        if (h_dim_fixed && !v_dim_fixed) {
            dimension_ratio_side = HORIZONTAL;
        } else if (!h_dim_fixed && v_dim_fixed) {
            dimension_ratio_side = VERTICAL;
            if (dimension_ratio_side == UNKNOWN) {
                /* handled below */
            } else {
                dimension_ratio = 1.f / dimension_ratio;
            }
        }
    }

    if (dimension_ratio_side == HORIZONTAL &&
        !(get_anchor(ConstraintAnchor::Type::TOP)->is_connected() &&
          get_anchor(ConstraintAnchor::Type::BOTTOM)->is_connected())) {
        dimension_ratio_side = VERTICAL;
    } else if (dimension_ratio_side == VERTICAL &&
               !(get_anchor(ConstraintAnchor::Type::LEFT)->is_connected() &&
                 get_anchor(ConstraintAnchor::Type::RIGHT)->is_connected())) {
        dimension_ratio_side = HORIZONTAL;
    }

    if (dimension_ratio_side == UNKNOWN) {
        if (!(get_anchor(ConstraintAnchor::Type::TOP)->is_connected() &&
              get_anchor(ConstraintAnchor::Type::BOTTOM)->is_connected() &&
              get_anchor(ConstraintAnchor::Type::LEFT)->is_connected() &&
              get_anchor(ConstraintAnchor::Type::RIGHT)->is_connected())) {
            if (get_anchor(ConstraintAnchor::Type::TOP)->is_connected() &&
                get_anchor(ConstraintAnchor::Type::BOTTOM)->is_connected()) {
                dimension_ratio_side = HORIZONTAL;
            } else if (get_anchor(ConstraintAnchor::Type::LEFT)->is_connected() &&
                       get_anchor(ConstraintAnchor::Type::RIGHT)->is_connected()) {
                dimension_ratio = 1.f / dimension_ratio;
                dimension_ratio_side = VERTICAL;
            }
        }
    }

    if (dimension_ratio_side == UNKNOWN) {
        if (match_constraint_min_w > 0.f && match_constraint_min_h == 0.f) {
            dimension_ratio_side = HORIZONTAL;
        } else if (match_constraint_min_w == 0.f && match_constraint_min_h > 0.f) {
            dimension_ratio = 1.f / dimension_ratio;
            dimension_ratio_side = VERTICAL;
        }
    }
}

/* ── applyConstraints (port of ConstraintWidget.applyConstraints) ──── */

void ConstraintWidget::apply_constraints(
    ConstraintSystem& system, bool is_horizontal,
    bool parent_wrap_content, bool opposite_parent_wrap_content,
    bool is_terminal, int parent_min, int parent_max,
    DimensionBehaviour dimension_behaviour, bool wrap_content,
    ConstraintAnchor* begin_anchor, ConstraintAnchor* end_anchor,
    float begin_position, float dimension, float min_dimension,
    float max_dimension, float bias, bool use_ratio,
    bool opposite_variable, bool in_chain, bool opposite_in_chain,
    bool in_barrier,
    int match_constraint_default, int opposite_match_constraint_default,
    float match_min_dimension, float match_max_dimension,
    float match_percent_dimension, bool apply_position) {
    (void)in_barrier; /* barrier-specific wrap handling is not ported */
    const int begin = begin_anchor->solver_variable;
    const int end = end_anchor->solver_variable;
    const int begin_target = begin_anchor->target ? begin_anchor->target->solver_variable : -1;
    const int end_target = end_anchor->target ? end_anchor->target->solver_variable : -1;

    const bool is_begin_connected = begin_anchor->is_connected();
    const bool is_end_connected = end_anchor->is_connected();
    const bool is_center_connected = get_anchor(ConstraintAnchor::Type::CENTER)->is_connected();

    bool variable_size = false;

    int num_connections = 0;
    if (is_begin_connected) ++num_connections;
    if (is_end_connected) ++num_connections;
    if (is_center_connected) ++num_connections;

    if (use_ratio) {
        match_constraint_default = MATCH_CONSTRAINT_RATIO;
    }
    switch (dimension_behaviour) {
        case DimensionBehaviour::FIXED:
        case DimensionBehaviour::WRAP_CONTENT:
        case DimensionBehaviour::MATCH_PARENT:
            variable_size = false;
            break;
        case DimensionBehaviour::MATCH_CONSTRAINT:
            variable_size = match_constraint_default != MATCH_CONSTRAINT_RATIO_RESOLVED;
            break;
    }

    if (visibility == GONE) {
        dimension = 0.f;
        variable_size = false;
    }

    /* First apply starting direct connections (more solver-friendly) */
    if (apply_position) {
        if (!is_begin_connected && !is_end_connected && !is_center_connected) {
            system.addEquality(begin, begin_position);
        } else if (is_begin_connected && !is_end_connected) {
            system.addEquality(begin, begin_target, begin_anchor->get_margin(), ST_FIXED);
        }
    }

    /* Then apply the dimension */
    if (!variable_size) {
        if (wrap_content) {
            system.addEquality(end, begin, 0.f, ST_HIGH);
            if (min_dimension > 0.f) {
                system.addGreaterThan(end, begin, min_dimension, ST_FIXED);
            }
            if (max_dimension < (std::numeric_limits<float>::max)()) {
                system.addLowerThan(end, begin, max_dimension, ST_FIXED);
            }
        } else {
            system.addEquality(end, begin, dimension, ST_FIXED);
        }
    } else {
        if (num_connections != 2 && !use_ratio &&
            (match_constraint_default == MATCH_CONSTRAINT_WRAP ||
             match_constraint_default == MATCH_CONSTRAINT_SPREAD)) {
            variable_size = false;
            float d = std::max(match_min_dimension, dimension);
            if (match_max_dimension > 0.f) d = std::min(match_max_dimension, d);
            system.addEquality(end, begin, d, ST_FIXED);
        } else {
            if (match_min_dimension == static_cast<float>(WRAP_SENTINEL)) {
                match_min_dimension = dimension;
            }
            if (match_max_dimension == static_cast<float>(WRAP_SENTINEL)) {
                match_max_dimension = dimension;
            }
            if (dimension > 0.f && match_constraint_default != MATCH_CONSTRAINT_WRAP) {
                /* USE_WRAP_DIMENSION_FOR_SPREAD == false in the reference */
                dimension = 0.f;
            }

            if (match_min_dimension > 0.f) {
                system.addGreaterThan(end, begin, match_min_dimension, ST_FIXED);
                dimension = std::max(dimension, match_min_dimension);
            }
            if (match_max_dimension > 0.f) {
                bool apply_limit = true;
                if (parent_wrap_content && match_constraint_default == MATCH_CONSTRAINT_WRAP) {
                    apply_limit = false;
                }
                if (apply_limit) {
                    system.addLowerThan(end, begin, match_max_dimension, ST_FIXED);
                }
                dimension = std::min(dimension, match_max_dimension);
            }
            if (match_constraint_default == MATCH_CONSTRAINT_WRAP) {
                if (parent_wrap_content) {
                    system.addEquality(end, begin, dimension, ST_FIXED);
                } else if (in_chain) {
                    system.addEquality(end, begin, dimension, ST_EQUALITY);
                    system.addLowerThan(end, begin, dimension, ST_FIXED);
                } else {
                    system.addEquality(end, begin, dimension, ST_EQUALITY);
                    system.addLowerThan(end, begin, dimension, ST_FIXED);
                }
            } else if (match_constraint_default == MATCH_CONSTRAINT_PERCENT) {
                int percent_begin = -1, percent_end = -1;
                if (parent != nullptr) {
                    if (begin_anchor->type == ConstraintAnchor::Type::TOP ||
                        begin_anchor->type == ConstraintAnchor::Type::BOTTOM) {
                        percent_begin =
                            parent->get_anchor(ConstraintAnchor::Type::TOP)->solver_variable;
                        percent_end =
                            parent->get_anchor(ConstraintAnchor::Type::BOTTOM)->solver_variable;
                    } else {
                        percent_begin =
                            parent->get_anchor(ConstraintAnchor::Type::LEFT)->solver_variable;
                        percent_end =
                            parent->get_anchor(ConstraintAnchor::Type::RIGHT)->solver_variable;
                    }
                }
                if (percent_end != -1 && percent_begin != -1) {
                    Row row; /* -end + begin + percent*(percentEnd - percentBegin) = 0 */
                    row.put(end, -1.f);
                    row.put(begin, 1.f);
                    row.put(percent_end, match_percent_dimension);
                    row.put(percent_begin, -match_percent_dimension);
                    system.addConstraint(std::move(row));
                }
                if (parent_wrap_content) {
                    variable_size = false;
                }
            } else {
                is_terminal = true;
            }
        }
    }

    if (!apply_position || in_chain) {
        /* If we don't need to apply the position, let's finish now. */
        if (num_connections < 2 && parent_wrap_content && is_terminal) {
            if (begin != -1 && parent_min != -1) {
                system.addGreaterThan(begin, parent_min, 0.f, ST_FIXED);
            }
            bool apply_end = is_horizontal;
            if (!is_horizontal && get_anchor(ConstraintAnchor::Type::BASELINE)->target != nullptr) {
                ConstraintWidget* target =
                    get_anchor(ConstraintAnchor::Type::BASELINE)->target->owner;
                if (target->dimension_ratio != 0.f &&
                    target->h_behavior == DimensionBehaviour::MATCH_CONSTRAINT &&
                    target->v_behavior == DimensionBehaviour::MATCH_CONSTRAINT) {
                    apply_end = true;
                } else {
                    apply_end = false;
                }
            }
            if (apply_end) {
                if (end != -1 && parent_max != -1) {
                    system.addGreaterThan(parent_max, end, 0.f, ST_FIXED);
                }
            }
        }
        return;
    }

    /* Ok, we are dealing with single or centered constraints, let's apply them */

    int wrap_strength = ST_EQUALITY;

    if (!is_begin_connected && !is_end_connected && !is_center_connected) {
        /* note we already applied the start position before, no need to redo it... */
    } else if (is_begin_connected && !is_end_connected) {
        /* note we already applied the start position before, no need to redo it... */
    } else if (!is_begin_connected && is_end_connected) {
        system.addEquality(end, end_target, -end_anchor->get_margin(), ST_FIXED);
        if (parent_wrap_content) {
            if (begin != -1 && parent_min != -1) {
                system.addGreaterThan(begin, parent_min, 0.f, ST_EQUALITY);
            }
        }
    } else if (is_begin_connected && is_end_connected) {
        bool apply_bounds_check = true;
        bool apply_centering = false;
        bool apply_strong_checks = false;
        bool apply_range_check = false;
        int range_check_strength = ST_EQUALITY;

        int bounds_check_strength = ST_HIGHEST;
        int centering_strength = ST_BARRIER;

        if (parent_wrap_content) {
            range_check_strength = ST_EQUALITY;
        }
        ConstraintWidget* begin_widget = begin_anchor->target->owner;
        ConstraintWidget* end_widget = end_anchor->target->owner;
        ConstraintWidget* par = this->parent;

        if (variable_size) {
            if (match_constraint_default == MATCH_CONSTRAINT_SPREAD) {
                if (match_max_dimension == 0.f && match_min_dimension == 0.f) {
                    apply_strong_checks = true;
                    range_check_strength = ST_FIXED;
                    bounds_check_strength = ST_FIXED;
                } else {
                    apply_centering = true;
                    range_check_strength = ST_EQUALITY;
                    bounds_check_strength = ST_EQUALITY;
                    apply_bounds_check = true;
                    apply_range_check = true;
                }
                if (dynamic_cast<Barrier*>(begin_widget) != nullptr ||
                    dynamic_cast<Barrier*>(end_widget) != nullptr) {
                    bounds_check_strength = ST_HIGHEST;
                }
            } else if (match_constraint_default == MATCH_CONSTRAINT_PERCENT) {
                apply_centering = true;
                range_check_strength = ST_EQUALITY;
                bounds_check_strength = ST_EQUALITY;
                apply_bounds_check = true;
                apply_range_check = true;
                if (dynamic_cast<Barrier*>(begin_widget) != nullptr ||
                    dynamic_cast<Barrier*>(end_widget) != nullptr) {
                    bounds_check_strength = ST_HIGHEST;
                }
            } else if (match_constraint_default == MATCH_CONSTRAINT_WRAP) {
                apply_centering = true;
                apply_range_check = true;
                range_check_strength = ST_FIXED;
            } else if (match_constraint_default == MATCH_CONSTRAINT_RATIO) {
                if (dimension_ratio_side == UNKNOWN) {
                    apply_centering = true;
                    apply_range_check = true;
                    apply_strong_checks = true;
                    range_check_strength = ST_FIXED;
                    bounds_check_strength = ST_EQUALITY;
                    if (opposite_in_chain) {
                        bounds_check_strength = ST_EQUALITY;
                        centering_strength = ST_HIGHEST;
                        if (parent_wrap_content) {
                            centering_strength = ST_EQUALITY;
                        }
                    } else {
                        centering_strength = ST_FIXED;
                    }
                } else {
                    apply_centering = true;
                    apply_range_check = true;
                    apply_strong_checks = true;
                    if (use_ratio) {
                        const bool other_side_invariable =
                            opposite_match_constraint_default == MATCH_CONSTRAINT_PERCENT ||
                            opposite_match_constraint_default == MATCH_CONSTRAINT_WRAP;
                        if (!other_side_invariable) {
                            range_check_strength = ST_FIXED;
                            bounds_check_strength = ST_EQUALITY;
                        }
                    } else {
                        range_check_strength = ST_EQUALITY;
                        if (match_max_dimension > 0.f) {
                            bounds_check_strength = ST_EQUALITY;
                        } else if (match_max_dimension == 0.f && match_min_dimension == 0.f) {
                            if (!opposite_in_chain) {
                                bounds_check_strength = ST_FIXED;
                            } else {
                                if (begin_widget != par && end_widget != par) {
                                    range_check_strength = ST_HIGHEST;
                                } else {
                                    range_check_strength = ST_EQUALITY;
                                }
                                bounds_check_strength = ST_HIGHEST;
                            }
                        }
                    }
                }
            }
        } else {
            apply_centering = true;
            apply_range_check = true;
            /* AOSP isFinalValue fast-path skipped: variables are never final
             * in the non-optimized port. */
        }

        if (apply_range_check && begin_target == end_target && begin_widget != par) {
            /* no need to apply range / bounds check if we are centered on the same anchor */
            apply_range_check = false;
            apply_bounds_check = false;
        }

        if (apply_centering) {
            if (!variable_size && !opposite_variable && !opposite_in_chain &&
                begin_target == parent_min && end_target == parent_max) {
                /* for fixed size widgets, we can simplify the constraints */
                centering_strength = ST_FIXED;
                range_check_strength = ST_FIXED;
                apply_bounds_check = false;
                parent_wrap_content = false;
            }

            if (begin_target != -1 && end_target != -1 && begin != -1 && end != -1) {
                system.addCentering(begin, begin_target, begin_anchor->get_margin(), bias,
                                    end_target, end, end_anchor->get_margin(), centering_strength);
            }
        }

        if (visibility == GONE && !end_anchor->has_dependents()) {
            return;
        }

        if (apply_range_check) {
            if (begin_target != -1 && begin != -1) {
                system.addGreaterThan(begin, begin_target, begin_anchor->get_margin(),
                                      range_check_strength);
            }
            if (end_target != -1 && end != -1) {
                system.addLowerThan(end, end_target, -end_anchor->get_margin(),
                                    range_check_strength);
            }
        }

        if (apply_bounds_check) {
            if (apply_strong_checks && (!opposite_in_chain || opposite_parent_wrap_content)) {
                int strength = bounds_check_strength;
                if (begin_widget == par || end_widget == par) {
                    strength = ST_BARRIER;
                }
                if (opposite_in_chain) {
                    strength = ST_EQUALITY;
                }
                bounds_check_strength = std::max(strength, bounds_check_strength);
            }

            if (parent_wrap_content) {
                bounds_check_strength = std::min(range_check_strength, bounds_check_strength);
                if (use_ratio && !opposite_in_chain &&
                    (begin_widget == par || end_widget == par)) {
                    /* When using ratio, relax some strength to allow other parts of the
                     * system to take precedence rather than driving it */
                    bounds_check_strength = ST_HIGHEST;
                }
            }
            if (begin_target != -1 && begin != -1) {
                system.addEquality(begin, begin_target, begin_anchor->get_margin(),
                                   bounds_check_strength);
            }
            if (end_target != -1 && end != -1) {
                system.addEquality(end, end_target, -end_anchor->get_margin(),
                                   bounds_check_strength);
            }
        }

        if (parent_wrap_content) {
            float margin = 0.f;
            if (parent_min == begin_target) {
                margin = begin_anchor->get_margin();
            }
            if (begin_target != parent_min) { /* already done otherwise */
                if (begin != -1 && parent_min != -1) {
                    system.addGreaterThan(begin, parent_min, margin, wrap_strength);
                }
            }
        }

        if (parent_wrap_content && variable_size && min_dimension == 0.f &&
            match_min_dimension == 0.f) {
            if (match_constraint_default == MATCH_CONSTRAINT_RATIO) {
                system.addGreaterThan(end, begin, 0.f, ST_FIXED);
            } else {
                system.addGreaterThan(end, begin, 0.f, wrap_strength);
            }
        }
    }

    if (parent_wrap_content && is_terminal) {
        float margin = 0.f;
        if (end_anchor->target != nullptr) {
            margin = end_anchor->get_margin();
        }
        if (end_target != parent_max) { /* if not already applied */
            if (end != -1 && parent_max != -1) {
                system.addGreaterThan(parent_max, end, margin, wrap_strength);
            }
        }
    }
}

/* ── updateFromSolver ──────────────────────────────────────────────── */

void ConstraintWidget::update_from_solver(ConstraintSystem& system, bool optimize) {
    (void)optimize;
    float left = system.getValue(get_anchor(ConstraintAnchor::Type::LEFT)->solver_variable);
    float top = system.getValue(get_anchor(ConstraintAnchor::Type::TOP)->solver_variable);
    float right = system.getValue(get_anchor(ConstraintAnchor::Type::RIGHT)->solver_variable);
    float bottom = system.getValue(get_anchor(ConstraintAnchor::Type::BOTTOM)->solver_variable);

    float w = right - left;
    float h = bottom - top;
    if (w < 0.f || h < 0.f) {
        left = 0.f; top = 0.f; right = 0.f; bottom = 0.f;
        w = 0.f; h = 0.f;
    }
    set_frame(left, top, right, bottom);
}

/* ── ConstraintWidgetContainer ─────────────────────────────────────── */

void ConstraintWidgetContainer::reset_chains() {
    for (ChainHead* head : horizontal_chains) delete head;
    for (ChainHead* head : vertical_chains) delete head;
    horizontal_chains.clear();
    vertical_chains.clear();
}

void ConstraintWidgetContainer::add_chain(ConstraintWidget* widget, int type) {
    if (type == HORIZONTAL) {
        horizontal_chains.push_back(new ChainHead(widget, HORIZONTAL, false));
    } else if (type == VERTICAL) {
        vertical_chains.push_back(new ChainHead(widget, VERTICAL, false));
    }
}

void ConstraintWidgetContainer::add_children_to_solver(ConstraintSystem& s) {
    add_to_solver(s, false);

    /* Reset and re-mark barrier membership (AOSP addChildrenToSolver). */
    bool has_barriers = false;
    for (ConstraintWidget* child : children) {
        child->set_in_barrier(HORIZONTAL, false);
        child->set_in_barrier(VERTICAL, false);
        if (dynamic_cast<Barrier*>(child) != nullptr) {
            has_barriers = true;
        }
    }
    if (has_barriers) {
        for (ConstraintWidget* child : children) {
            if (Barrier* barrier = dynamic_cast<Barrier*>(child)) {
                barrier->mark_widgets();
            }
        }
    }

    for (ConstraintWidget* child : children) {
        child->add_to_solver(s, false);
    }
    if (!horizontal_chains.empty()) {
        Chain::apply_chain_constraints(*this, s, HORIZONTAL);
    }
    if (!vertical_chains.empty()) {
        Chain::apply_chain_constraints(*this, s, VERTICAL);
    }
}

void ConstraintWidgetContainer::update_children_from_solver(ConstraintSystem& s) {
    update_from_solver(s, false);
    for (ConstraintWidget* child : children) {
        child->update_from_solver(s, false);
    }
}

void ConstraintWidgetContainer::solve_linear_system() {
    system.reset();
    reset_chains();
    create_object_variables(system);
    for (ConstraintWidget* child : children) {
        child->create_object_variables(system);
    }
    add_children_to_solver(system);
    system.minimize();
    update_children_from_solver(system);
}

void ConstraintWidgetContainer::layout() {
    x = 0.f;
    y = 0.f;
    solve_linear_system();
}

/* ── Guideline (port of androidx.constraintlayout.core.widgets.Guideline) */

Guideline::Guideline() {
    /* The shared anchor lives in the widget's anchor array (AOSP keeps a
     * separate ConstraintAnchor object; here we point at the real slot). */
    guide_anchor = &anchors[static_cast<int>(ConstraintAnchor::Type::TOP)];
}

void Guideline::set_orientation(int orientation) {
    if (guideline_orientation == orientation) {
        return;
    }
    guideline_orientation = orientation;
    guide_anchor = guideline_orientation == VERTICAL_GUIDELINE
        ? &anchors[static_cast<int>(ConstraintAnchor::Type::LEFT)]
        : &anchors[static_cast<int>(ConstraintAnchor::Type::TOP)];
}

void Guideline::set_guide_begin(float value) {
    if (value > -1.f) {
        relative_percent = -1.f;
        relative_begin = value;
        relative_end = -1.f;
    }
}

void Guideline::set_guide_end(float value) {
    if (value > -1.f) {
        relative_percent = -1.f;
        relative_begin = -1.f;
        relative_end = value;
    }
}

void Guideline::set_guide_percent(float value) {
    if (value > -1.f) {
        relative_percent = value;
        relative_begin = -1.f;
        relative_end = -1.f;
    }
}

ConstraintAnchor* Guideline::get_anchor(ConstraintAnchor::Type type) {
    if (guideline_orientation == VERTICAL_GUIDELINE) {
        if (type == ConstraintAnchor::Type::LEFT || type == ConstraintAnchor::Type::RIGHT) {
            return guide_anchor;
        }
        return nullptr;
    }
    if (type == ConstraintAnchor::Type::TOP || type == ConstraintAnchor::Type::BOTTOM) {
        return guide_anchor;
    }
    return nullptr;
}

void Guideline::create_object_variables(ConstraintSystem& system) {
    guide_anchor->solver_variable = system.createVariable();
}

void Guideline::add_to_solver(ConstraintSystem& system, bool optimize) {
    (void)optimize;
    ConstraintWidgetContainer* par = static_cast<ConstraintWidgetContainer*>(this->parent);
    if (par == nullptr) {
        return;
    }
    ConstraintAnchor* begin = par->get_anchor(ConstraintAnchor::Type::LEFT);
    ConstraintAnchor* end = par->get_anchor(ConstraintAnchor::Type::RIGHT);
    bool parent_wrap_content = par->h_behavior == DimensionBehaviour::WRAP_CONTENT;
    if (guideline_orientation == HORIZONTAL_GUIDELINE) {
        begin = par->get_anchor(ConstraintAnchor::Type::TOP);
        end = par->get_anchor(ConstraintAnchor::Type::BOTTOM);
        parent_wrap_content = par->v_behavior == DimensionBehaviour::WRAP_CONTENT;
    }

    const int guide = guide_anchor->solver_variable;

    if (relative_begin != -1.f) {
        const int parent_left = begin->solver_variable;
        system.addEquality(guide, parent_left, relative_begin, ST_FIXED);
        if (parent_wrap_content) {
            system.addGreaterThan(end->solver_variable, guide, 0.f, ST_EQUALITY);
        }
    } else if (relative_end != -1.f) {
        const int parent_right = end->solver_variable;
        system.addEquality(guide, parent_right, -relative_end, ST_FIXED);
        if (parent_wrap_content) {
            system.addGreaterThan(guide, begin->solver_variable, 0.f, ST_EQUALITY);
            system.addGreaterThan(parent_right, guide, 0.f, ST_EQUALITY);
        }
    } else if (relative_percent != -1.f) {
        /* createRowDimensionPercent: guide = percent * parentRight */
        Row row;
        row.put(guide, -1.f);
        row.put(end->solver_variable, relative_percent);
        system.addConstraint(std::move(row));
    }
}

void Guideline::update_from_solver(ConstraintSystem& system, bool optimize) {
    (void)optimize;
    if (parent == nullptr) {
        return;
    }
    const float value = system.getValue(guide_anchor->solver_variable);
    if (guideline_orientation == VERTICAL_GUIDELINE) {
        set_frame(value, 0.f, value, parent->height);
        /* setX(value); setY(0); setHeight(parent.height); setWidth(0) */
    } else {
        set_frame(0.f, value, parent->width, value);
    }
}

/* ── Barrier (port of androidx.constraintlayout.core.widgets.Barrier) ── */

/* AOSP mListAnchors order {LEFT, RIGHT, TOP, BOTTOM} == barrier type order. */
static ConstraintAnchor* barrier_list_anchor(ConstraintWidget* widget, int barrier_type) {
    switch (barrier_type) {
        case Barrier::LEFT: return widget->get_anchor(ConstraintAnchor::Type::LEFT);
        case Barrier::RIGHT: return widget->get_anchor(ConstraintAnchor::Type::RIGHT);
        case Barrier::TOP: return widget->get_anchor(ConstraintAnchor::Type::TOP);
        case Barrier::BOTTOM: return widget->get_anchor(ConstraintAnchor::Type::BOTTOM);
        default: return nullptr;
    }
}

void Barrier::mark_widgets() {
    for (ConstraintWidget* widget : helper_widgets) {
        if (!allows_gone_widget && !widget->allowed_in_barrier()) {
            continue;
        }
        if (barrier_type == LEFT || barrier_type == RIGHT) {
            widget->set_in_barrier(HORIZONTAL, true);
        } else if (barrier_type == TOP || barrier_type == BOTTOM) {
            widget->set_in_barrier(VERTICAL, true);
        }
    }
}

void Barrier::add_to_solver(ConstraintSystem& system, bool optimize) {
    (void)optimize;
    ConstraintAnchor* position = barrier_list_anchor(this, barrier_type);
    if (position == nullptr || position->solver_variable == -1) {
        return;
    }

    /* AOSP USE_RESOLUTION path (allSolved) is not ported: widgets never
     * report pre-resolved positions in the non-optimized flow. */

    /* Widgets with MATCH_CONSTRAINT need relaxed barrier strength (AOSP
     * switches to EQUALITY error-based rows; this reference adds them hard,
     * so we mirror the hard row and only keep the flag for parity). */
    bool has_match_constraint_widgets = false;
    for (ConstraintWidget* widget : helper_widgets) {
        if (!allows_gone_widget && !widget->allowed_in_barrier()) {
            continue;
        }
        const bool is_h = barrier_type == LEFT || barrier_type == RIGHT;
        if (is_h && widget->h_behavior == DimensionBehaviour::MATCH_CONSTRAINT &&
            widget->get_anchor(ConstraintAnchor::Type::LEFT)->target != nullptr &&
            widget->get_anchor(ConstraintAnchor::Type::RIGHT)->target != nullptr) {
            has_match_constraint_widgets = true;
            break;
        }
        if (!is_h && widget->v_behavior == DimensionBehaviour::MATCH_CONSTRAINT &&
            widget->get_anchor(ConstraintAnchor::Type::TOP)->target != nullptr &&
            widget->get_anchor(ConstraintAnchor::Type::BOTTOM)->target != nullptr) {
            has_match_constraint_widgets = true;
            break;
        }
    }

    /* AOSP hasCenteredDependents is now tracked (ConstraintAnchor.dependents)
     * -> applyEqualityOnReferences is computed like the reference. */
    ConstraintAnchor* m_left = get_anchor(ConstraintAnchor::Type::LEFT);
    ConstraintAnchor* m_right = get_anchor(ConstraintAnchor::Type::RIGHT);
    ConstraintAnchor* m_top = get_anchor(ConstraintAnchor::Type::TOP);
    ConstraintAnchor* m_bottom = get_anchor(ConstraintAnchor::Type::BOTTOM);
    const bool has_h_centered_dependents =
        m_left->has_centered_dependents() || m_right->has_centered_dependents();
    const bool has_v_centered_dependents =
        m_top->has_centered_dependents() || m_bottom->has_centered_dependents();
    const bool apply_equality_on_references = !has_match_constraint_widgets &&
        ((barrier_type == LEFT && has_h_centered_dependents) ||
         (barrier_type == TOP && has_v_centered_dependents) ||
         (barrier_type == RIGHT && has_h_centered_dependents) ||
         (barrier_type == BOTTOM && has_v_centered_dependents));

    int equality_on_references_strength = ST_EQUALITY;
    if (!apply_equality_on_references) {
        equality_on_references_strength = ST_HIGHEST;
    }

    for (ConstraintWidget* widget : helper_widgets) {
        if (!allows_gone_widget && !widget->allowed_in_barrier()) {
            continue;
        }
        ConstraintAnchor* widget_anchor = barrier_list_anchor(widget, barrier_type);
        if (widget_anchor == nullptr || widget_anchor->solver_variable == -1) {
            continue;
        }
        const int target = widget_anchor->solver_variable;
        float widget_margin = 0.f;
        if (widget_anchor->target != nullptr && widget_anchor->target->owner == this) {
            widget_margin += widget_anchor->margin;
        }
        if (barrier_type == LEFT || barrier_type == TOP) {
            /* addLowerBarrier: position <= target - (margin) */
            system.addLowerThan(position->solver_variable, target, this->margin - widget_margin,
                                ST_FIXED);
        } else {
            /* addGreaterBarrier: position >= target - (margin) */
            system.addGreaterThan(position->solver_variable, target, widget_margin + this->margin,
                                  ST_FIXED);
        }
        system.addEquality(position->solver_variable, target, this->margin + widget_margin,
                           equality_on_references_strength);
    }

    (void)has_match_constraint_widgets;
    const int barrier_parent_strength = ST_HIGHEST;
    const int barrier_parent_strength_opposite = ST_NONE;

    ConstraintWidgetContainer* par = static_cast<ConstraintWidgetContainer*>(this->parent);
    if (par == nullptr) {
        return;
    }
    if (barrier_type == LEFT) {
        system.addEquality(m_right->solver_variable, m_left->solver_variable, 0.f, ST_FIXED);
        system.addEquality(m_left->solver_variable,
                           par->get_anchor(ConstraintAnchor::Type::RIGHT)->solver_variable,
                           0.f, barrier_parent_strength);
        system.addEquality(m_left->solver_variable,
                           par->get_anchor(ConstraintAnchor::Type::LEFT)->solver_variable,
                           0.f, barrier_parent_strength_opposite);
    } else if (barrier_type == RIGHT) {
        system.addEquality(m_left->solver_variable, m_right->solver_variable, 0.f, ST_FIXED);
        system.addEquality(m_left->solver_variable,
                           par->get_anchor(ConstraintAnchor::Type::LEFT)->solver_variable,
                           0.f, barrier_parent_strength);
        system.addEquality(m_left->solver_variable,
                           par->get_anchor(ConstraintAnchor::Type::RIGHT)->solver_variable,
                           0.f, barrier_parent_strength_opposite);
    } else if (barrier_type == TOP) {
        system.addEquality(m_bottom->solver_variable, m_top->solver_variable, 0.f, ST_FIXED);
        system.addEquality(m_top->solver_variable,
                           par->get_anchor(ConstraintAnchor::Type::BOTTOM)->solver_variable,
                           0.f, barrier_parent_strength);
        system.addEquality(m_top->solver_variable,
                           par->get_anchor(ConstraintAnchor::Type::TOP)->solver_variable,
                           0.f, barrier_parent_strength_opposite);
    } else if (barrier_type == BOTTOM) {
        system.addEquality(m_top->solver_variable, m_bottom->solver_variable, 0.f, ST_FIXED);
        system.addEquality(m_top->solver_variable,
                           par->get_anchor(ConstraintAnchor::Type::TOP)->solver_variable,
                           0.f, barrier_parent_strength);
        system.addEquality(m_top->solver_variable,
                           par->get_anchor(ConstraintAnchor::Type::BOTTOM)->solver_variable,
                           0.f, barrier_parent_strength_opposite);
    }
}

} // namespace viewruntime::android::constraint
