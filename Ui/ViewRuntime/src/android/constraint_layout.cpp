#include "android_types.h"

#include "constraint_widget.h"

#include <algorithm>

namespace viewruntime::android {

/* ── ConstraintLayout ──────────────────────────────────────────────── */
/* Drives the ported androidx.constraintlayout.core solver. Children are
 * measured first (their measured size is the intrinsic for WRAP/FIXED),
 * then the layout pass builds a ConstraintWidgetContainer, solves, and
 * writes the resolved frames back to the views. */

using constraint::Barrier;
using constraint::ConstraintAnchor;
using constraint::ConstraintWidget;
using constraint::ConstraintWidgetContainer;

static ConstraintWidget::DimensionBehaviour constraint_behavior(
    const android_size_t& size) {
    switch (size.kind) {
        case ANDROID_SIZE_KIND_MATCH_PARENT:
            return ConstraintWidget::DimensionBehaviour::MATCH_PARENT;
        case ANDROID_SIZE_KIND_WRAP_CONTENT:
            return ConstraintWidget::DimensionBehaviour::WRAP_CONTENT;
        default:
            /* EXACT: value 0 is the 0dp MATCH_CONSTRAINT convention */
            if (size.value_dp <= 0.f) {
                return ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT;
            }
            return ConstraintWidget::DimensionBehaviour::FIXED;
    }
}

static ConstraintAnchor::Type constraint_anchor_type(int32_t side) {
    switch (side) {
        case ANDROID_CONSTRAINT_RIGHT: return ConstraintAnchor::Type::RIGHT;
        case ANDROID_CONSTRAINT_TOP: return ConstraintAnchor::Type::TOP;
        case ANDROID_CONSTRAINT_BOTTOM: return ConstraintAnchor::Type::BOTTOM;
        case ANDROID_CONSTRAINT_END: return ConstraintAnchor::Type::RIGHT;  /* LTR */
        case ANDROID_CONSTRAINT_START: /* fall through */
        case ANDROID_CONSTRAINT_LEFT:
        default: return ConstraintAnchor::Type::LEFT;
    }
}

static void apply_constraint_params(
    ConstraintWidget* widget, const android_view_s* view,
    ConstraintWidgetContainer* container, const android_ui_s* ui) {
    const android_constraint_params_t& cp = view->lp.constraint;
    widget->set_horizontal_bias_percent(cp.bias_h);
    widget->set_vertical_bias_percent(cp.bias_v);
    widget->set_horizontal_chain_style(cp.chain_style_h);
    widget->set_vertical_chain_style(cp.chain_style_v);
    if (cp.dimension_ratio > 0.f) {
        widget->set_dimension_ratio(cp.dimension_ratio, ConstraintWidget::UNKNOWN);
    }
    /* AOSP LayoutParams.matchConstraintMinWidth/MaxWidth are already PIXELS
     * (widgetConstraintLayout.java:1506-1511, parsed via
     * getDimensionPixelSize at :3501-3548). This port stores them in dp, so
     * convert before handing them to the solver (which treats them as px). */
    widget->set_horizontal_match_style(
        cp.match_default_w, static_cast<int>(dp(ui, cp.match_min_w_dp)),
        static_cast<int>(dp(ui, cp.match_max_w_dp)), cp.match_percent_w);
    widget->set_vertical_match_style(
        cp.match_default_h, static_cast<int>(dp(ui, cp.match_min_h_dp)),
        static_cast<int>(dp(ui, cp.match_max_h_dp)), cp.match_percent_h);
    for (int i = 0; i < cp.constraint_count; ++i) {
        const android_constraint_t& c = cp.constraints[i];
        ConstraintWidget* target = container;
        if (c.target_id != -1) {
            if (android_view_s* tv = android_ui_find_view_by_id(
                    const_cast<android_ui_s*>(ui), c.target_id)) {
                target = tv->constraint_widget;
            } else {
                continue; /* unresolved target: skip the connection */
            }
        }
        if (target == nullptr) {
            continue; /* target widget not created yet (e.g. barrier pass): skip */
        }
        const ConstraintAnchor::Type side = constraint_anchor_type(c.side);
        widget->connect(side, target, constraint_anchor_type(c.target_side),
                        dp(ui, c.margin_dp));
        if (c.gone_margin_dp != c.margin_dp) {
            widget->get_anchor(side)->set_gone_margin(dp(ui, c.gone_margin_dp));
        }
    }
}

void layout_constraint(android_view_s* view, float x, float y, float w, float h,
                       const android_ui_s* ui) {
    if (view->visibility == ANDROID_GONE) {
        view->bounds = {x, y, 0.f, 0.f};
        return;
    }
    const float pad_w = padding_h(view, ui);
    const float pad_h = padding_v(view, ui);
    /* AOSP resolveSystem subtracts the layout padding from the available size
     * before measuring (widgetConstraintLayout.java:1627-1628), and
     * setSelfDimensionBehaviour drives the container with the size minus
     * padding (widgetConstraintLayout.java:1848). The container therefore
     * spans the CONTENT box; the child read-out re-adds padding_left/
     * padding_top so the final right/bottom edge lands at w - padding_right /
     * h - padding_bottom. */
    ConstraintWidgetContainer container(0.f, 0.f, w - pad_w, h - pad_h);
    /* AOSP ConstraintWidgetContainer.setPadding (ConstraintWidgetContainer.java:490):
     * the container carries the layout's padding; children are positioned
     * offset by it (ConstraintWidget.getX/getY add the parent's padding,
     * ConstraintWidget.java:1093-1107). */
    container.padding_left = dp(ui, view->padding_left_dp);
    container.padding_top = dp(ui, view->padding_top_dp);
    container.padding_right = dp(ui, view->padding_right_dp);
    container.padding_bottom = dp(ui, view->padding_bottom_dp);

    /* Pass 1: create every child widget (regular + barriers) so that
     * references resolve in any order. GONE children are NOT skipped: AOSP
     * keeps their 0x0 widgets in the graph with visibility GONE (widget
     * setVisibility mirrors the view, widgetConstraintLayout.java:1312;
     * BasicMeasure measures them 0x0, ConstraintWidgetContainer.java:533-542),
     * and connections to them use goneMargin via ConstraintAnchor.getMargin
     * (ConstraintAnchor.java:192-196). Dropping them made set_gone_margin
     * dead code. */
    for (android_view_s* child : view->children) {
        const bool is_gone = child->visibility == ANDROID_GONE;
        if (child->cls == ANDROID_VIEW_BARRIER) {
            /* AOSP helper widgets are added to the graph ALWAYS, even when
             * GONE (applyConstraintsFromLayoutParams -> mLayoutWidget.add,
             * widgetConstraintLayout.java:1296; setVisibility mirrors the
             * view, :1312), and onLayout does NOT skip GONE helpers
             * (:1925-1934). A GONE barrier must still exist as a widget so a
             * child constraining to it resolves the connection instead of
             * dropping it. */
            Barrier* barrier = new Barrier();
            child->constraint_widget = barrier;
            barrier->set_debug_name("barrier");
            barrier->set_barrier_type(child->barrier_type);
            barrier->set_margin(dp(ui, child->barrier_margin_dp));
            barrier->set_allows_gone_widget(child->barrier_allows_gone);
            if (is_gone) barrier->set_visibility(ConstraintWidget::GONE);
            container.add(barrier);
            continue;
        }
        ConstraintWidget* widget =
            new ConstraintWidget(is_gone ? 0.f : child->measured.width,
                                 is_gone ? 0.f : child->measured.height);
        child->constraint_widget = widget;
        widget->set_debug_name("view");
        if (is_gone) {
            /* The solver collapses GONE widgets to dimension 0
             * (ConstraintWidget.java:3057-3060). */
            widget->set_visibility(ConstraintWidget::GONE);
        }
        const ConstraintWidget::DimensionBehaviour bh =
            constraint_behavior(child->lp.width);
        const ConstraintWidget::DimensionBehaviour bv =
            constraint_behavior(child->lp.height);
        widget->set_horizontal_dimension_behaviour(bh);
        widget->set_vertical_dimension_behaviour(bv);
        /* AOSP MATCH_PARENT accounts for margin as well as padding
         * (widgetConstraintLayout.java:706-711: getChildMeasureSpec(
         * mLayoutWidthSpec, widthPadding + widget.getHorizontalMargin(),
         * MATCH_PARENT)); the resolved width comes from that measure. The
         * container spans the content box (w - pad_w), so the child fills it
         * minus its own margins. */
        if (!is_gone && bh == ConstraintWidget::DimensionBehaviour::MATCH_PARENT) {
            /* AOSP getChildMeasureSpec clamps a negative size to 0
             * (ViewGroup.java:7047-7049): when margins + padding exceed the
             * available space a MATCH_PARENT child resolves to 0, not a
             * negative width. */
            widget->width = std::max(0.f, w - pad_w - margin_h(child->lp, ui));
        }
        if (!is_gone && bv == ConstraintWidget::DimensionBehaviour::MATCH_PARENT) {
            widget->height = std::max(0.f, h - pad_h - margin_v(child->lp, ui));
        }
        container.add(widget);
    }

    /* Pass 2: wire barrier references (all widgets now exist). */
    for (android_view_s* child : view->children) {
        if (child->cls != ANDROID_VIEW_BARRIER) continue;
        Barrier* barrier = static_cast<Barrier*>(child->constraint_widget);
        if (barrier == nullptr) continue;
        for (int32_t ref : child->barrier_references) {
            if (android_view_s* tv = android_ui_find_view_by_id(
                    const_cast<android_ui_s*>(ui), ref)) {
                if (tv->constraint_widget != nullptr) {
                    barrier->add_helper_widget(tv->constraint_widget);
                }
            }
        }
    }

    /* Pass 3: wire constraint connections on regular children (GONE children
     * included: AOSP applyConstraintsFromLayoutParams runs for every child,
     * widgetConstraintLayout.java:1303-1312). */
    for (android_view_s* child : view->children) {
        if (child->cls == ANDROID_VIEW_BARRIER) continue;
        if (ConstraintWidget* widget = child->constraint_widget) {
            apply_constraint_params(widget, child, &container, ui);
        }
    }

    container.layout();

    for (android_view_s* child : view->children) {
        ConstraintWidget* widget = child->constraint_widget;
        const bool is_gone = child->visibility == ANDROID_GONE;
        if (widget == nullptr) {
            if (is_gone) child->bounds = {x, y, 0.f, 0.f};
            continue;
        }
        /* AOSP getX/getY add the container padding (ConstraintWidget.java:1093-1107). */
        const float cx = x + container.padding_left + widget->get_left();
        const float cy = y + container.padding_top + widget->get_top();
        const float cw = widget->get_width();
        const float ch = widget->get_height();
        if (is_gone && child->cls != ANDROID_VIEW_BARRIER) {
            /* AOSP onLayout skips GONE children entirely, EXCEPT helpers and
             * guidelines (widgetConstraintLayout.java:1925-1934): a GONE
             * barrier is still laid out at its solved position. */
            child->bounds = {x, y, 0.f, 0.f};
        } else if (child->cls != ANDROID_VIEW_BARRIER) {
            /* AOSP onLayout dispatches to the child's own layout
             * (widgetConstraintLayout.java:1959: child.layout(l, t, r, b)). */
            child->measured = {cw, ch};
            layout_view(child, cx, cy, cw, ch, ui);
        } else {
            child->bounds = {cx, cy, cw, ch};
        }
        child->constraint_widget = nullptr;
        delete widget;
    }
    view->bounds = {x, y, w, h};
}

android_measured_size_t measure_constraint(
    android_view_s* view, android_measure_spec_t spec_w,
    android_measure_spec_t spec_h, const android_ui_s* ui) {
    const float padding_w = padding_h(view, ui);
    const float padding_vh = padding_v(view, ui);
    float max_w = 0.f, max_h = 0.f;
    /* AOSP BasicMeasure builds the child spec per dimension behaviour:
     * MATCH_PARENT subtracts margin AS WELL AS padding
     * (widgetConstraintLayout.java:706-711); 0dp MATCH_CONSTRAINT is measured
     * WRAP_CONTENT initially (:713-715) so the intrinsic size feeds the
     * solver instead of collapsing to EXACTLY(0); FIXED/WRAP use padding only
     * (getChildMeasureSpec(mLayoutWidthSpec, widthPadding, ...)). */
    const auto child_spec = [&](const android_size_t& dim, float avail,
                                int32_t mode, float margin) -> android_measure_spec_t {
        if (dim.kind == ANDROID_SIZE_KIND_MATCH_PARENT) {
            return get_child_measure_spec({avail, mode}, margin, dim, ui);
        }
        if (dim.kind == ANDROID_SIZE_KIND_EXACT && dim.value_dp == 0.f) {
            const android_size_t wrap{ANDROID_SIZE_KIND_WRAP_CONTENT, 0.f};
            return get_child_measure_spec({avail, mode}, 0.f, wrap, ui);
        }
        return get_child_measure_spec({avail, mode}, 0.f, dim, ui);
    };
    for (android_view_s* child : view->children) {
        if (child->visibility == ANDROID_GONE) continue;
        if (child->cls == ANDROID_VIEW_BARRIER) continue; /* virtual helper */
        const float avail_w = std::max(0.f, spec_w.size - padding_w);
        const float avail_h = std::max(0.f, spec_h.size - padding_vh);
        const android_measure_spec_t cw =
            child_spec(child->lp.width, avail_w, spec_w.mode, margin_h(child->lp, ui));
        const android_measure_spec_t ch =
            child_spec(child->lp.height, avail_h, spec_h.mode, margin_v(child->lp, ui));
        child->measured = measure_view(child, cw, ch, ui);
        max_w = std::max(max_w, child->measured.width + margin_h(child->lp, ui));
        max_h = std::max(max_h, child->measured.height + margin_v(child->lp, ui));
    }
    const float desired_w = std::max(dp(ui, view->min_width_dp), max_w) + padding_w;
    const float desired_h = std::max(dp(ui, view->min_height_dp), max_h) + padding_vh;
    const android_measured_size_t result{resolve_size(desired_w, spec_w),
                                         resolve_size(desired_h, spec_h)};
    view->measured_baseline = -1.f;
    return result;
}

} // namespace viewruntime::android
