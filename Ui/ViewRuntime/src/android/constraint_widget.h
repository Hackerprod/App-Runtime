#pragma once
/* Faithful port of androidx.constraintlayout.core.widgets.ConstraintWidget +
 * ConstraintWidgetContainer + ConstraintAnchor (the widget model that feeds
 * the LinearSystem solver). Reference sources: .tmp/constraintlayout/. */

#include "constraint_solver.h"

#include <limits>
#include <string>
#include <vector>

namespace viewruntime::android::constraint {

struct ConstraintWidget;
struct ConstraintWidgetContainer;
struct ChainHead;
struct Guideline;
struct HelperWidget;
struct Barrier;

struct ConstraintAnchor {
    enum class Type { NONE, LEFT, TOP, RIGHT, BOTTOM, BASELINE, CENTER, CENTER_X, CENTER_Y };

    ConstraintWidget* owner = nullptr;
    Type type = Type::NONE;
    ConstraintAnchor* target = nullptr;
    float margin = 0.f;
    float gone_margin = 0.f;
    bool has_gone_margin = false;
    int solver_variable = -1;
    std::vector<ConstraintAnchor*> dependents; /* AOSP mDependents */

    bool is_connected() const { return target != nullptr; }
    ConstraintWidget* get_owner() const { return owner; }
    Type get_type() const { return type; }
    void connect(ConstraintAnchor* to, float m) {
        if (to != nullptr) {
            to->dependents.push_back(this);
        }
        target = to;
        margin = m;
    }
    void set_gone_margin(float value) { gone_margin = value; has_gone_margin = true; }
    float get_margin() const; /* defined in the .cpp (needs complete widget type) */
    bool has_dependents() const { return !dependents.empty(); }
    /* AOSP ConstraintAnchor.hasCenteredDependents: a dependent whose opposite
     * anchor is also connected (a centered pair). */
    bool has_centered_dependents() const {
        for (const ConstraintAnchor* dependent : dependents) {
            if (dependent->get_opposite() != nullptr && dependent->get_opposite()->is_connected()) {
                return true;
            }
        }
        return false;
    }
    ConstraintAnchor* get_opposite() const;
};

struct ConstraintWidget {
    enum class DimensionBehaviour { FIXED, WRAP_CONTENT, MATCH_PARENT, MATCH_CONSTRAINT };
    static constexpr int GONE = 8;
    static constexpr int UNKNOWN = -1;
    static constexpr int HORIZONTAL = 0;
    static constexpr int VERTICAL = 1;
    static constexpr int DIRECT = 2;
    static constexpr int WRAP_SENTINEL = -2; /* AOSP: private static final int WRAP = -2 */

    static constexpr int MATCH_CONSTRAINT_SPREAD = 0;
    static constexpr int MATCH_CONSTRAINT_WRAP = 1;
    static constexpr int MATCH_CONSTRAINT_PERCENT = 2;
    static constexpr int MATCH_CONSTRAINT_RATIO = 3;
    static constexpr int MATCH_CONSTRAINT_RATIO_RESOLVED = 4;

    static constexpr int CHAIN_SPREAD = 0;
    static constexpr int CHAIN_SPREAD_INSIDE = 1;
    static constexpr int CHAIN_PACKED = 2;

    float x = 0.f, y = 0.f, width = 0.f, height = 0.f;
    float min_width = 0.f, min_height = 0.f;
    float max_width = (std::numeric_limits<float>::max)();
    float max_height = (std::numeric_limits<float>::max)();

    DimensionBehaviour h_behavior = DimensionBehaviour::FIXED;
    DimensionBehaviour v_behavior = DimensionBehaviour::FIXED;
    float horizontal_bias = 0.5f;
    float vertical_bias = 0.5f;

    float dimension_ratio = 0.f;
    int dimension_ratio_side = UNKNOWN;
    int match_constraint_default_w = MATCH_CONSTRAINT_SPREAD;
    int match_constraint_default_h = MATCH_CONSTRAINT_SPREAD;
    float match_constraint_min_w = 0.f, match_constraint_max_w = 0.f;
    float match_constraint_min_h = 0.f, match_constraint_max_h = 0.f;
    float match_constraint_percent_w = 0.f, match_constraint_percent_h = 0.f;

    float baseline_distance = 0.f;
    int visibility = 0;

    /* AOSP defaults isTerminalWidget[] to {true, true}. */
    bool is_terminal_widget_h = true;
    bool is_terminal_widget_v = true;
    bool in_horizontal_chain = false;
    bool in_vertical_chain = false;

    /* AOSP mIsInBarrier[2] — widget referenced by a barrier. */
    bool in_barrier_h = false;
    bool in_barrier_v = false;
    void set_in_barrier(int orientation, bool value) {
        if (orientation == HORIZONTAL) in_barrier_h = value; else in_barrier_v = value;
    }
    bool is_in_barrier(int orientation) const {
        return orientation == HORIZONTAL ? in_barrier_h : in_barrier_v;
    }
    virtual bool allowed_in_barrier() { return true; }

    /* Chains (AOSP mHorizontalChainStyle/mVerticalChainStyle, mWeight[2],
     * mNextChainWidget[2], mListNextMatchConstraintsWidget[2],
     * mResolvedMatchConstraintDefault[2]). */
    int horizontal_chain_style = CHAIN_SPREAD;
    int vertical_chain_style = CHAIN_SPREAD;
    float weight_h = -1.f;
    float weight_v = -1.f;
    ConstraintWidget* next_chain_widget_h = nullptr;
    ConstraintWidget* next_chain_widget_v = nullptr;
    ConstraintWidget* next_match_constraint_widget_h = nullptr;
    ConstraintWidget* next_match_constraint_widget_v = nullptr;
    int resolved_match_constraint_default_w = MATCH_CONSTRAINT_SPREAD;
    int resolved_match_constraint_default_h = MATCH_CONSTRAINT_SPREAD;

    std::string debug_name;

    ConstraintWidget* parent = nullptr;
    std::vector<ConstraintWidget*> children;
    bool added = false;

    ConstraintAnchor anchors[9]; /* indexed by Type */

    ConstraintWidget();
    ConstraintWidget(float w, float h);
    ConstraintWidget(float px, float py, float w, float h);
    virtual ~ConstraintWidget() = default;

    virtual bool is_container() const { return false; }

    void set_debug_name(const char* name) { debug_name = name ? name : ""; }
    const char* get_debug_name() const { return debug_name.c_str(); }

    virtual ConstraintAnchor* get_anchor(ConstraintAnchor::Type type) {
        return &anchors[static_cast<int>(type)];
    }

    void connect(ConstraintAnchor::Type from, ConstraintWidget* target,
                 ConstraintAnchor::Type to, float margin);
    void connect(ConstraintAnchor::Type from, ConstraintWidget* target,
                 ConstraintAnchor::Type to) {
        connect(from, target, to, 0.f);
    }
    void connect(ConstraintAnchor* from, ConstraintAnchor* to, float margin) {
        if (from->owner == this) connect(from->type, to->owner, to->type, margin);
    }
    void add(ConstraintWidget* child);
    void reset_anchors();

    void set_horizontal_dimension_behaviour(DimensionBehaviour b) { h_behavior = b; }
    void set_vertical_dimension_behaviour(DimensionBehaviour b) { v_behavior = b; }
    void set_width(float w) { width = w; }
    void set_height(float h) { height = h; }
    void set_dimension_ratio(float ratio, int side) {
        dimension_ratio = ratio;
        dimension_ratio_side = side;
    }
    void set_dimension_ratio(const char* ratio); /* "W:H", "H,W:H", "0.75" */
    void set_horizontal_bias_percent(float b) { horizontal_bias = b; }
    void set_vertical_bias_percent(float b) { vertical_bias = b; }
    void set_match_constraint_default_width(int value) { match_constraint_default_w = value; }
    void set_match_constraint_default_height(int value) { match_constraint_default_h = value; }
    void set_match_constraint_min_width(float v) { match_constraint_min_w = v; }
    void set_match_constraint_max_width(float v) { match_constraint_max_w = v; }
    void set_match_constraint_min_height(float v) { match_constraint_min_h = v; }
    void set_match_constraint_max_height(float v) { match_constraint_max_h = v; }
    /* AOSP setHorizontalMatchStyle / setVerticalMatchStyle (min/max are ints,
     * MATCH_CONSTRAINT_WRAP uses the WRAP_SENTINEL value). */
    void set_horizontal_match_style(int style, int min, int max, float percent) {
        match_constraint_default_w = style;
        match_constraint_min_w = static_cast<float>(min);
        match_constraint_max_w = static_cast<float>(max);
        match_constraint_percent_w = percent;
    }
    void set_vertical_match_style(int style, int min, int max, float percent) {
        match_constraint_default_h = style;
        match_constraint_min_h = static_cast<float>(min);
        match_constraint_max_h = static_cast<float>(max);
        match_constraint_percent_h = percent;
    }
    void set_baseline_distance(float d) { baseline_distance = d; }
    float get_baseline_distance() const { return baseline_distance; }
    void set_horizontal_chain_style(int style) { horizontal_chain_style = style; }
    void set_vertical_chain_style(int style) { vertical_chain_style = style; }
    int get_horizontal_chain_style() const { return horizontal_chain_style; }
    int get_vertical_chain_style() const { return vertical_chain_style; }
    void set_weight(int orientation, float w) {
        if (orientation == HORIZONTAL) weight_h = w; else weight_v = w;
    }
    void set_visibility(int value) { visibility = value; }
    void set_gone_margin(ConstraintAnchor::Type type, float value) {
        get_anchor(type)->set_gone_margin(value);
    }

    void set_frame(float l, float t, float r, float b) {
        x = l; y = t; width = r - l; height = b - t;
    }
    float get_x() const { return x; }
    float get_y() const { return y; }
    float get_width() const { return width; }
    float get_height() const { return height; }
    float get_left() const { return x; }
    float get_top() const { return y; }
    float get_right() const { return x + width; }
    float get_bottom() const { return y + height; }

    virtual void create_object_variables(ConstraintSystem& system);
    virtual void add_to_solver(ConstraintSystem& system, bool optimize);
    virtual void update_from_solver(ConstraintSystem& system, bool optimize);

    /* Chains (port of ConstraintWidget chain helpers) */
    bool is_in_horizontal_chain();
    bool is_in_vertical_chain();
    bool is_chain_head(int orientation);
    ConstraintWidget* get_previous_chain_member(int orientation);
    ConstraintWidget* get_next_chain_member(int orientation);
    ConstraintAnchor* begin_anchor(int orientation) {
        return orientation == HORIZONTAL ? get_anchor(ConstraintAnchor::Type::LEFT)
                                         : get_anchor(ConstraintAnchor::Type::TOP);
    }
    ConstraintAnchor* end_anchor(int orientation) {
        return orientation == HORIZONTAL ? get_anchor(ConstraintAnchor::Type::RIGHT)
                                         : get_anchor(ConstraintAnchor::Type::BOTTOM);
    }
    ConstraintWidget*& next_chain_widget(int orientation) {
        return orientation == HORIZONTAL ? next_chain_widget_h : next_chain_widget_v;
    }
    ConstraintWidget*& next_match_constraint_widget(int orientation) {
        return orientation == HORIZONTAL ? next_match_constraint_widget_h
                                         : next_match_constraint_widget_v;
    }
    int resolved_match_constraint_default(int orientation) const {
        return orientation == HORIZONTAL ? resolved_match_constraint_default_w
                                         : resolved_match_constraint_default_h;
    }
    float weight(int orientation) const {
        return orientation == HORIZONTAL ? weight_h : weight_v;
    }

    void setup_dimension_ratio(bool h_parent_wrap, bool v_parent_wrap,
                               bool h_dim_fixed, bool v_dim_fixed);

    void apply_constraints(ConstraintSystem& system, bool is_horizontal,
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
                           float match_percent_dimension, bool apply_position);
};

struct ConstraintWidgetContainer : public ConstraintWidget {
    float padding_left = 0.f, padding_top = 0.f;
    float padding_right = 0.f, padding_bottom = 0.f;
    ConstraintSystem system;

    /* Chains (AOSP mHorizontalChainsArray/mVerticalChainsArray + sizes). */
    std::vector<ChainHead*> horizontal_chains;
    std::vector<ChainHead*> vertical_chains;

    ConstraintWidgetContainer() = default;
    ConstraintWidgetContainer(float w, float h) : ConstraintWidget(0.f, 0.f, w, h) {}
    ConstraintWidgetContainer(float px, float py, float w, float h)
        : ConstraintWidget(px, py, w, h) {}

    bool is_container() const override { return true; }

    void reset_chains();
    void add_chain(ConstraintWidget* widget, int type);

    void add_children_to_solver(ConstraintSystem& s);
    void solve_linear_system();
    void update_children_from_solver(ConstraintSystem& s);
    void layout();
};

/* Chain head — port of androidx.constraintlayout.core.widgets.ChainHead. */
struct ChainHead {
    ConstraintWidget* first = nullptr;
    ConstraintWidget* first_visible_widget = nullptr;
    ConstraintWidget* last = nullptr;
    ConstraintWidget* last_visible_widget = nullptr;
    ConstraintWidget* head = nullptr;
    ConstraintWidget* first_match_constraint_widget = nullptr;
    ConstraintWidget* last_match_constraint_widget = nullptr;
    std::vector<ConstraintWidget*> weighted_match_constraints_widgets;
    int widgets_count = 0;
    int widgets_match_count = 0;
    int visible_widgets = 0;
    int total_size = 0;
    int total_margins = 0;
    bool optimizable = true;
    int orientation = ConstraintWidget::HORIZONTAL;
    bool is_rtl = false;
    bool has_undefined_weights = false;
    bool has_defined_weights = false;
    bool has_complex_match_weights = false;
    bool has_ratio = false;
    float total_weight = 0.f;
    bool defined = false;

    ChainHead(ConstraintWidget* f, int o, bool rtl)
        : first(f), orientation(o), is_rtl(rtl) {}

    void define();
    void define_chain_properties();
};

/* Chain constraint application — port of
 * androidx.constraintlayout.core.widgets.Chain. */
namespace Chain {
void apply_chain_constraints(ConstraintWidgetContainer& container,
                             ConstraintSystem& system, int orientation);
void apply_chain_constraints_inner(ConstraintWidgetContainer& container,
                                   ConstraintSystem& system, int orientation,
                                   int offset, ChainHead& chain_head);
} // namespace Chain

/* Guideline — port of androidx.constraintlayout.core.widgets.Guideline. */
struct Guideline : public ConstraintWidget {
    static constexpr int HORIZONTAL_GUIDELINE = 0;
    static constexpr int VERTICAL_GUIDELINE = 1;

    int guideline_orientation = HORIZONTAL_GUIDELINE;
    float relative_begin = -1.f;
    float relative_end = -1.f;
    float relative_percent = -1.f;
    ConstraintAnchor* guide_anchor = nullptr;

    Guideline();

    void set_orientation(int orientation);
    int get_orientation() const { return guideline_orientation; }
    void set_guide_begin(float value);
    void set_guide_end(float value);
    void set_guide_percent(float value);

    /* AOSP Guideline.getAnchor: the guideline exposes a single shared anchor
     * (LEFT/RIGHT for vertical, TOP/BOTTOM for horizontal) and null for the
     * other types. */
    ConstraintAnchor* get_anchor(ConstraintAnchor::Type type) override;
    void create_object_variables(ConstraintSystem& system) override;

    void add_to_solver(ConstraintSystem& system, bool optimize) override;
    void update_from_solver(ConstraintSystem& system, bool optimize) override;
};

/* HelperWidget — port of androidx.constraintlayout.core.widgets.HelperWidget:
 * base for widgets that reference other widgets (Barrier, Flow, ...). */
struct HelperWidget : public ConstraintWidget {
    std::vector<ConstraintWidget*> helper_widgets; /* AOSP mWidgets[] */

    HelperWidget() = default;

    void add_helper_widget(ConstraintWidget* widget) {
        if (widget == nullptr || widget == this) {
            return;
        }
        helper_widgets.push_back(widget);
    }
    void remove_all_ids() { helper_widgets.clear(); }
    virtual void update_constraints(ConstraintWidgetContainer& /*container*/) {}
};

/* Barrier — port of androidx.constraintlayout.core.widgets.Barrier.
 * AOSP mListAnchors order is {LEFT, RIGHT, TOP, BOTTOM}, which is exactly the
 * barrier-type index order (LEFT=0, RIGHT=1, TOP=2, BOTTOM=3). */
struct Barrier : public HelperWidget {
    static constexpr int LEFT = 0;
    static constexpr int RIGHT = 1;
    static constexpr int TOP = 2;
    static constexpr int BOTTOM = 3;

    int barrier_type = LEFT;
    bool allows_gone_widget = true;
    float margin = 0.f;

    Barrier() = default;
    explicit Barrier(const char* debug_name) { set_debug_name(debug_name); }

    bool allowed_in_barrier() override { return true; }

    int get_barrier_type() const { return barrier_type; }
    void set_barrier_type(int type) { barrier_type = type; }
    void set_allows_gone_widget(bool value) { allows_gone_widget = value; }
    bool get_allows_gone_widget() const { return allows_gone_widget; }
    void set_margin(float m) { margin = m; }
    float get_margin() const { return margin; }
    int get_orientation() const {
        if (barrier_type == LEFT || barrier_type == RIGHT) return HORIZONTAL;
        if (barrier_type == TOP || barrier_type == BOTTOM) return VERTICAL;
        return UNKNOWN;
    }

    void mark_widgets();
    void add_to_solver(ConstraintSystem& system, bool optimize) override;
};

} // namespace viewruntime::android::constraint
