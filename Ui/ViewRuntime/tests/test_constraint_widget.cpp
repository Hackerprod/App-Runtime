/* Widget model tests ported from androidx.constraintlayout.core.widgets
 * ConstraintWidgetTest (reference: .tmp/constraintlayout/tests/ConstraintWidgetTest.java).
 * Expected values are the AOSP oracle. */

#include "android_test_util.h"

#include "../src/android/constraint_widget.h"

using namespace viewruntime::android::constraint;

/* testAddingWidgets: children list + re-parenting moves a widget between
 * containers (the old parent drops it). */
static void test_adding_widgets() {
    ConstraintWidgetContainer container(1000.f, 1000.f);
    EXPECT(container.children.empty());

    ConstraintWidget* widget = new ConstraintWidget(100.f, 200.f);
    container.add(widget);
    EXPECT(container.children.size() == 1);
    EXPECT(widget->parent == &container);

    ConstraintWidgetContainer container2(1000.f, 1000.f);
    container2.add(widget);
    EXPECT(container.children.empty());
    EXPECT(container2.children.size() == 1);
    EXPECT(widget->parent == &container2);

    delete widget;
}

/* testWidgetTopRightPositioning: right+top against the parent places the
 * widget at the top-right corner of the 1000x1000 container. */
static void test_widget_top_right_positioning() {
    ConstraintWidgetContainer root(1000.f, 1000.f);
    root.set_debug_name("root");

    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    a->set_debug_name("A");
    root.add(a);

    a->connect(ConstraintAnchor::Type::RIGHT, &root, ConstraintAnchor::Type::RIGHT, 0.f);
    a->connect(ConstraintAnchor::Type::TOP, &root, ConstraintAnchor::Type::TOP, 0.f);

    root.layout();

    EXPECT_NEAR(a->get_left(), 900.f, 1e-3f);
    EXPECT_NEAR(a->get_top(), 0.f, 1e-3f);
    EXPECT_NEAR(a->get_right(), 1000.f, 1e-3f);
    EXPECT_NEAR(a->get_bottom(), 20.f, 1e-3f);

    delete a;
}

/* testCentering: left/right + top/bottom against the parent centers the
 * widget (fixed size, 0.5 bias). */
static void test_centering() {
    ConstraintWidgetContainer root(1000.f, 1000.f);
    root.set_debug_name("root");

    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    a->set_debug_name("A");
    root.add(a);

    a->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT, 0.f);
    a->connect(ConstraintAnchor::Type::RIGHT, &root, ConstraintAnchor::Type::RIGHT, 0.f);
    a->connect(ConstraintAnchor::Type::TOP, &root, ConstraintAnchor::Type::TOP, 0.f);
    a->connect(ConstraintAnchor::Type::BOTTOM, &root, ConstraintAnchor::Type::BOTTOM, 0.f);

    root.layout();

    EXPECT_NEAR(a->get_left(), 450.f, 1e-3f);
    EXPECT_NEAR(a->get_top(), 490.f, 1e-3f);
    EXPECT_NEAR(a->get_right(), 550.f, 1e-3f);
    EXPECT_NEAR(a->get_bottom(), 510.f, 1e-3f);

    delete a;
}

/* testSimpleMinMatch (MatchConstraintTest): root wraps content; A is
 * MATCH_CONSTRAINT spread with min 150 / max 200, B is fixed. */
static void test_simple_min_match() {
    ConstraintWidgetContainer root(0.f, 0.f, 800.f, 600.f);
    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    ConstraintWidget* b = new ConstraintWidget(100.f, 20.f);
    a->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT);
    a->connect(ConstraintAnchor::Type::RIGHT, &root, ConstraintAnchor::Type::RIGHT);
    b->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT);
    b->connect(ConstraintAnchor::Type::RIGHT, &root, ConstraintAnchor::Type::RIGHT);
    a->set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT);
    a->set_horizontal_match_style(ConstraintWidget::MATCH_CONSTRAINT_SPREAD, 150, 200, 1.f);
    root.add(a);
    root.add(b);
    root.set_debug_name("root");
    a->set_debug_name("A");
    b->set_debug_name("B");
    root.set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::WRAP_CONTENT);
    root.layout();
    EXPECT_NEAR(a->get_width(), 150.f, 1e-3f);
    EXPECT_NEAR(b->get_width(), 100.f, 1e-3f);
    EXPECT_NEAR(root.get_width(), 150.f, 1e-3f);

    b->set_width(200.f);
    root.set_width(0.f);
    root.layout();
    EXPECT_NEAR(a->get_width(), 200.f, 1e-3f);
    EXPECT_NEAR(b->get_width(), 200.f, 1e-3f);
    EXPECT_NEAR(root.get_width(), 200.f, 1e-3f);

    b->set_width(300.f);
    root.set_width(0.f);
    root.layout();
    EXPECT_NEAR(a->get_width(), 200.f, 1e-3f);
    EXPECT_NEAR(b->get_width(), 300.f, 1e-3f);
    EXPECT_NEAR(root.get_width(), 300.f, 1e-3f);

    delete a;
    delete b;
}

/* testSimpleHorizontalMatch (MatchConstraintTest): three fixed widgets, then
 * C becomes MATCH_CONSTRAINT spread, then WRAP with explicit widths. */
static void test_simple_horizontal_match() {
    ConstraintWidgetContainer root(0.f, 0.f, 800.f, 600.f);
    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    ConstraintWidget* b = new ConstraintWidget(100.f, 20.f);
    ConstraintWidget* c = new ConstraintWidget(100.f, 20.f);

    a->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT, 0.f);
    b->connect(ConstraintAnchor::Type::RIGHT, &root, ConstraintAnchor::Type::RIGHT, 0.f);
    c->connect(ConstraintAnchor::Type::LEFT, a, ConstraintAnchor::Type::RIGHT, 0.f);
    c->connect(ConstraintAnchor::Type::RIGHT, b, ConstraintAnchor::Type::LEFT, 0.f);

    root.add(a);
    root.add(b);
    root.add(c);

    root.layout();
    EXPECT_NEAR(a->get_width(), 100.f, 1e-3f);
    EXPECT_NEAR(b->get_width(), 100.f, 1e-3f);
    EXPECT_NEAR(c->get_width(), 100.f, 1e-3f);
    EXPECT(c->get_left() >= a->get_right());
    EXPECT(c->get_right() <= b->get_left());
    EXPECT_NEAR(c->get_left() - a->get_right(), b->get_left() - c->get_right(), 1e-3f);

    c->set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT);
    root.layout();
    EXPECT_NEAR(a->get_width(), 100.f, 1e-3f);
    EXPECT_NEAR(b->get_width(), 100.f, 1e-3f);
    EXPECT_NEAR(c->get_width(), 600.f, 1e-3f);
    EXPECT(c->get_left() >= a->get_right());
    EXPECT(c->get_right() <= b->get_left());
    EXPECT_NEAR(c->get_left() - a->get_right(), b->get_left() - c->get_right(), 1e-3f);

    c->set_width(144.f);
    c->set_horizontal_match_style(ConstraintWidget::MATCH_CONSTRAINT_WRAP, 0, 0, 0.f);
    root.layout();
    EXPECT_NEAR(a->get_width(), 100.f, 1e-3f);
    EXPECT_NEAR(b->get_width(), 100.f, 1e-3f);
    EXPECT_NEAR(c->get_width(), 144.f, 1e-3f);
    EXPECT(c->get_left() >= a->get_right());
    EXPECT(c->get_right() <= b->get_left());
    EXPECT_NEAR(c->get_left() - a->get_right(), b->get_left() - c->get_right(), 1e-3f);

    c->set_width(1000.f);
    c->set_horizontal_match_style(ConstraintWidget::MATCH_CONSTRAINT_WRAP, 0, 0, 0.f);
    root.layout();
    EXPECT_NEAR(a->get_width(), 100.f, 1e-3f);
    EXPECT_NEAR(b->get_width(), 100.f, 1e-3f);
    EXPECT_NEAR(c->get_width(), 600.f, 1e-3f);
    EXPECT(c->get_left() >= a->get_right());
    EXPECT(c->get_right() <= b->get_left());
    EXPECT_NEAR(c->get_left() - a->get_right(), b->get_left() - c->get_right(), 1e-3f);

    delete a;
    delete b;
    delete c;
}

/* testDanglingRatio (MatchConstraintTest): smoke — MATCH_CONSTRAINT both
 * dimensions with WRAP style; AOSP has no assertions here. */
static void test_dangling_ratio() {
    ConstraintWidgetContainer root(0.f, 0.f, 1000.f, 600.f);
    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    root.set_debug_name("root");
    a->set_debug_name("A");
    root.add(a);
    a->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT);
    a->connect(ConstraintAnchor::Type::TOP, &root, ConstraintAnchor::Type::TOP);
    a->connect(ConstraintAnchor::Type::RIGHT, &root, ConstraintAnchor::Type::RIGHT);
    a->set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT);
    a->set_vertical_dimension_behaviour(ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT);
    a->set_horizontal_match_style(ConstraintWidget::MATCH_CONSTRAINT_WRAP, 0, 0, 0.f);
    root.layout();

    delete a;
}

/* testBasicRatio (RatioTest): both dimensions MATCH_CONSTRAINT with ratio
 * "1:1" against the parent; bias 0 pins top/left, then bias 1 pins bottom. */
static void test_basic_ratio() {
    ConstraintWidgetContainer root(0.f, 0.f, 600.f, 1000.f);
    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    root.set_debug_name("root");
    a->set_debug_name("A");
    root.add(a);
    a->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT);
    a->connect(ConstraintAnchor::Type::RIGHT, &root, ConstraintAnchor::Type::RIGHT);
    a->connect(ConstraintAnchor::Type::TOP, &root, ConstraintAnchor::Type::TOP);
    a->connect(ConstraintAnchor::Type::BOTTOM, &root, ConstraintAnchor::Type::BOTTOM);
    a->set_vertical_bias_percent(0.f);
    a->set_horizontal_bias_percent(0.f);
    a->set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT);
    a->set_vertical_dimension_behaviour(ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT);
    a->set_dimension_ratio("1:1");
    root.layout();
    EXPECT_NEAR(a->get_left(), 0.f, 1e-3f);
    EXPECT_NEAR(a->get_top(), 0.f, 1e-3f);
    EXPECT_NEAR(a->get_width(), 600.f, 1e-3f);
    EXPECT_NEAR(a->get_height(), 600.f, 1e-3f);

    a->set_vertical_bias_percent(1.f);
    root.layout();
    EXPECT_NEAR(a->get_left(), 0.f, 1e-3f);
    EXPECT_NEAR(a->get_top(), 400.f, 1e-3f);
    EXPECT_NEAR(a->get_width(), 600.f, 1e-3f);
    EXPECT_NEAR(a->get_height(), 600.f, 1e-3f);

    delete a;
}

/* testBasicRatio2 (RatioTest): only height MATCH_CONSTRAINT with ratio "1:1"
 * resolved against the fixed 100 width. */
static void test_basic_ratio2() {
    ConstraintWidgetContainer root(0.f, 0.f, 1000.f, 600.f);
    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    root.set_debug_name("root");
    a->set_debug_name("A");
    root.add(a);
    a->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT);
    a->connect(ConstraintAnchor::Type::RIGHT, &root, ConstraintAnchor::Type::RIGHT);
    a->connect(ConstraintAnchor::Type::TOP, &root, ConstraintAnchor::Type::TOP);
    a->connect(ConstraintAnchor::Type::BOTTOM, &root, ConstraintAnchor::Type::BOTTOM);
    a->set_vertical_dimension_behaviour(ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT);
    a->set_dimension_ratio("1:1");
    root.layout();
    EXPECT_NEAR(a->get_left(), 450.f, 1e-3f);
    EXPECT_NEAR(a->get_top(), 250.f, 1e-3f);
    EXPECT_NEAR(a->get_width(), 100.f, 1e-3f);
    EXPECT_NEAR(a->get_height(), 100.f, 1e-3f);

    delete a;
}

/* testSimpleRatio (RatioTest): ratio 3:2 then 1:2, centered both axes. */
static void test_simple_ratio() {
    ConstraintWidgetContainer root(0.f, 0.f, 200.f, 600.f);
    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    root.set_debug_name("root");
    a->set_debug_name("A");
    root.add(a);
    a->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT);
    a->connect(ConstraintAnchor::Type::RIGHT, &root, ConstraintAnchor::Type::RIGHT);
    a->connect(ConstraintAnchor::Type::TOP, &root, ConstraintAnchor::Type::TOP);
    a->connect(ConstraintAnchor::Type::BOTTOM, &root, ConstraintAnchor::Type::BOTTOM);
    a->set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT);
    a->set_vertical_dimension_behaviour(ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT);
    a->set_dimension_ratio("3:2");
    root.layout();
    EXPECT_NEAR(a->get_width() / a->get_height(), 3.f / 2.f, 0.1f);
    EXPECT(a->get_top() >= 0.f);
    EXPECT(a->get_left() >= 0.f);
    EXPECT_NEAR(a->get_top(), root.get_height() - a->get_bottom(), 1e-3f);
    EXPECT_NEAR(a->get_left(), root.get_right() - a->get_right(), 1e-3f);

    a->set_dimension_ratio("1:2");
    root.layout();
    EXPECT_NEAR(a->get_width() / a->get_height(), 1.f / 2.f, 0.1f);
    EXPECT(a->get_top() >= 0.f);
    EXPECT(a->get_left() >= 0.f);
    EXPECT_NEAR(a->get_top(), root.get_height() - a->get_bottom(), 1e-3f);
    EXPECT_NEAR(a->get_left(), root.get_right() - a->get_right(), 1e-3f);

    delete a;
}

/* testMinMaxMatch (MatchConstraintTest): widget pinned between two vertical
 * guidelines, MATCH_CONSTRAINT spread/wrap with min 150 / max 200. */
static void test_min_max_match() {
    ConstraintWidgetContainer root(0.f, 0.f, 800.f, 600.f);
    Guideline* guideline_a = new Guideline();
    guideline_a->set_orientation(Guideline::VERTICAL_GUIDELINE);
    guideline_a->set_guide_begin(100.f);
    Guideline* guideline_b = new Guideline();
    guideline_b->set_orientation(Guideline::VERTICAL_GUIDELINE);
    guideline_b->set_guide_end(100.f);
    root.add(guideline_a);
    root.add(guideline_b);
    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    a->connect(ConstraintAnchor::Type::LEFT, guideline_a, ConstraintAnchor::Type::LEFT);
    a->connect(ConstraintAnchor::Type::RIGHT, guideline_b, ConstraintAnchor::Type::RIGHT);
    a->set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT);
    a->set_horizontal_match_style(ConstraintWidget::MATCH_CONSTRAINT_SPREAD, 150, 200, 1.f);
    root.add(a);
    root.set_debug_name("root");
    guideline_a->set_debug_name("guideline A");
    guideline_b->set_debug_name("guideline B");
    a->set_debug_name("A");
    root.layout();
    EXPECT_NEAR(root.get_width(), 800.f, 1e-3f);
    EXPECT_NEAR(a->get_width(), 200.f, 1e-3f);

    root.set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::WRAP_CONTENT);
    a->set_width(100.f);
    root.layout();
    EXPECT_NEAR(root.get_width(), 350.f, 1e-3f);
    EXPECT_NEAR(a->get_width(), 150.f, 1e-3f);

    a->set_horizontal_match_style(ConstraintWidget::MATCH_CONSTRAINT_WRAP, 150, 200, 1.f);
    root.layout();
    EXPECT_NEAR(root.get_width(), 350.f, 1e-3f);
    EXPECT_NEAR(a->get_width(), 150.f, 1e-3f);

    root.set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::FIXED);
    root.set_width(800.f);
    root.layout();
    EXPECT_NEAR(root.get_width(), 800.f, 1e-3f);
    EXPECT_NEAR(a->get_width(), 150.f, 1e-3f); /* because it's wrap */

    a->set_width(250.f);
    root.layout();
    EXPECT_NEAR(root.get_width(), 800.f, 1e-3f);
    EXPECT_NEAR(a->get_width(), 200.f, 1e-3f);

    a->set_width(700.f);
    a->set_horizontal_match_style(ConstraintWidget::MATCH_CONSTRAINT_SPREAD, 150, 0, 1.f);
    root.layout();
    EXPECT_NEAR(root.get_width(), 800.f, 1e-3f);
    EXPECT_NEAR(a->get_width(), 600.f, 1e-3f);

    a->set_width(700.f);
    a->set_horizontal_match_style(ConstraintWidget::MATCH_CONSTRAINT_WRAP, 150, 0, 1.f);
    root.layout();
    EXPECT_NEAR(root.get_width(), 800.f, 1e-3f);
    EXPECT_NEAR(a->get_width(), 600.f, 1e-3f);

    a->set_width(700.f);
    root.set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::WRAP_CONTENT);
    root.set_width(0.f);
    root.layout();
    EXPECT_NEAR(root.get_width(), 900.f, 1e-3f);
    EXPECT_NEAR(a->get_width(), 700.f, 1e-3f);

    a->set_width(700.f);
    a->set_horizontal_match_style(ConstraintWidget::MATCH_CONSTRAINT_SPREAD, 150, 0, 1.f);
    root.layout();
    EXPECT_NEAR(root.get_width(), 350.f, 1e-3f);
    EXPECT_NEAR(a->get_width(), 150.f, 1e-3f);

    delete a;
    delete guideline_a;
    delete guideline_b;
}

/* testWrapRatio (RatioTest): A is a square ratio widget that heads a
 * vertical SPREAD_INSIDE chain (A->B->C); root wraps both axes. */
static void test_wrap_ratio() {
    ConstraintWidgetContainer root(0.f, 0.f, 700.f, 1920.f);
    ConstraintWidget* a = new ConstraintWidget(231.f, 126.f);
    ConstraintWidget* b = new ConstraintWidget(231.f, 126.f);
    ConstraintWidget* c = new ConstraintWidget(231.f, 126.f);

    root.set_debug_name("root");
    root.add(a);
    root.add(b);
    root.add(c);
    a->set_debug_name("A");
    b->set_debug_name("B");
    c->set_debug_name("C");

    a->set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT);
    a->set_vertical_dimension_behaviour(ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT);
    a->set_dimension_ratio("1:1");
    a->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT);
    a->connect(ConstraintAnchor::Type::RIGHT, &root, ConstraintAnchor::Type::RIGHT);
    a->connect(ConstraintAnchor::Type::TOP, &root, ConstraintAnchor::Type::TOP);
    a->connect(ConstraintAnchor::Type::BOTTOM, b, ConstraintAnchor::Type::TOP);
    a->set_horizontal_chain_style(ConstraintWidget::CHAIN_PACKED);
    a->set_horizontal_bias_percent(0.3f);
    a->set_vertical_chain_style(ConstraintWidget::CHAIN_SPREAD_INSIDE);

    b->connect(ConstraintAnchor::Type::LEFT, a, ConstraintAnchor::Type::LEFT, 171.f);
    b->connect(ConstraintAnchor::Type::TOP, a, ConstraintAnchor::Type::BOTTOM);
    b->connect(ConstraintAnchor::Type::BOTTOM, c, ConstraintAnchor::Type::TOP);

    c->connect(ConstraintAnchor::Type::LEFT, b, ConstraintAnchor::Type::LEFT);
    c->connect(ConstraintAnchor::Type::RIGHT, b, ConstraintAnchor::Type::RIGHT);
    c->connect(ConstraintAnchor::Type::TOP, b, ConstraintAnchor::Type::BOTTOM);
    c->connect(ConstraintAnchor::Type::BOTTOM, &root, ConstraintAnchor::Type::BOTTOM);

    root.set_vertical_dimension_behaviour(ConstraintWidget::DimensionBehaviour::WRAP_CONTENT);
    root.set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::WRAP_CONTENT);
    root.layout();

    EXPECT(a->get_left() >= 0.f);
    EXPECT_NEAR(a->get_width(), a->get_height(), 1e-3f);
    EXPECT_NEAR(a->get_width(), 402.f, 1e-3f);
    EXPECT_NEAR(root.get_width(), 402.f, 1e-3f);
    EXPECT_NEAR(root.get_height(), 654.f, 1e-3f);
    EXPECT_NEAR(a->get_left(), 0.f, 1e-3f);
    EXPECT_NEAR(b->get_top(), 402.f, 1e-3f);
    EXPECT_NEAR(b->get_left(), 171.f, 1e-3f);
    EXPECT_NEAR(c->get_top(), 528.f, 1e-3f);
    EXPECT_NEAR(c->get_left(), 171.f, 1e-3f);

    delete a;
    delete b;
    delete c;
}

/* barrierConstrainedWidth (BarrierTest): LEFT barrier over two fixed widgets
 * pinned between two guidelines, root wraps. */
static void test_barrier_constrained_width() {
    ConstraintWidgetContainer root(0.f, 0.f, 800.f, 600.f);
    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    ConstraintWidget* b = new ConstraintWidget(200.f, 20.f);
    Barrier* barrier = new Barrier();
    Guideline* guideline_start = new Guideline();
    Guideline* guideline_end = new Guideline();
    guideline_start->set_orientation(Guideline::VERTICAL_GUIDELINE);
    guideline_end->set_orientation(Guideline::VERTICAL_GUIDELINE);
    guideline_start->set_guide_begin(30.f);
    guideline_end->set_guide_end(20.f);

    barrier->set_barrier_type(Barrier::LEFT);
    barrier->add_helper_widget(a);
    barrier->add_helper_widget(b);

    root.add(a);
    root.add(b);
    root.add(guideline_start);
    root.add(guideline_end);
    root.add(barrier);

    a->connect(ConstraintAnchor::Type::LEFT, guideline_start, ConstraintAnchor::Type::LEFT);
    a->connect(ConstraintAnchor::Type::RIGHT, guideline_end, ConstraintAnchor::Type::RIGHT);
    b->connect(ConstraintAnchor::Type::LEFT, guideline_start, ConstraintAnchor::Type::LEFT);
    b->connect(ConstraintAnchor::Type::RIGHT, guideline_end, ConstraintAnchor::Type::RIGHT);
    a->set_horizontal_bias_percent(1.f);
    b->set_horizontal_bias_percent(1.f);

    root.set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::WRAP_CONTENT);
    root.layout();

    EXPECT_NEAR(root.get_width(), 250.f, 1e-3f);
    EXPECT_NEAR(guideline_start->get_left(), 30.f, 1e-3f);
    EXPECT_NEAR(guideline_end->get_left(), 230.f, 1e-3f);
    EXPECT_NEAR(a->get_left(), 130.f, 1e-3f);
    EXPECT_NEAR(a->get_width(), 100.f, 1e-3f);
    EXPECT_NEAR(b->get_left(), 30.f, 1e-3f);
    EXPECT_NEAR(b->get_width(), 200.f, 1e-3f);
    EXPECT_NEAR(barrier->get_left(), 30.f, 1e-3f);

    delete a; delete b; delete barrier; delete guideline_start; delete guideline_end;
}

/* barrierImage (BarrierTest): RIGHT barrier behind two widgets; C pinned to
 * the barrier on the right edge. */
static void test_barrier_image() {
    ConstraintWidgetContainer root(0.f, 0.f, 800.f, 600.f);
    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    ConstraintWidget* b = new ConstraintWidget(200.f, 20.f);
    ConstraintWidget* c = new ConstraintWidget(60.f, 60.f);
    Barrier* barrier = new Barrier();
    barrier->set_barrier_type(Barrier::RIGHT);
    barrier->add_helper_widget(a);
    barrier->add_helper_widget(b);

    root.add(a);
    root.add(b);
    root.add(c);
    root.add(barrier);

    a->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT);
    a->connect(ConstraintAnchor::Type::TOP, &root, ConstraintAnchor::Type::TOP);
    a->connect(ConstraintAnchor::Type::BOTTOM, b, ConstraintAnchor::Type::TOP);

    b->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT);
    b->connect(ConstraintAnchor::Type::TOP, a, ConstraintAnchor::Type::BOTTOM);
    b->connect(ConstraintAnchor::Type::BOTTOM, &root, ConstraintAnchor::Type::BOTTOM);

    a->set_vertical_chain_style(ConstraintWidget::CHAIN_SPREAD_INSIDE);

    c->set_horizontal_bias_percent(1.f);
    c->connect(ConstraintAnchor::Type::TOP, &root, ConstraintAnchor::Type::TOP);
    c->connect(ConstraintAnchor::Type::BOTTOM, &root, ConstraintAnchor::Type::BOTTOM);
    c->connect(ConstraintAnchor::Type::LEFT, barrier, ConstraintAnchor::Type::RIGHT);
    c->connect(ConstraintAnchor::Type::RIGHT, &root, ConstraintAnchor::Type::RIGHT);

    root.layout();

    EXPECT_NEAR(a->get_left(), 0.f, 1e-3f);
    EXPECT_NEAR(a->get_top(), 0.f, 1e-3f);
    EXPECT_NEAR(b->get_left(), 0.f, 1e-3f);
    EXPECT_NEAR(b->get_top(), 580.f, 1e-3f);
    EXPECT_NEAR(c->get_left(), 740.f, 1e-3f);
    EXPECT_NEAR(c->get_top(), 270.f, 1e-3f);
    EXPECT_NEAR(barrier->get_left(), 200.f, 1e-3f);

    delete a; delete b; delete c; delete barrier;
}

/* barrierMax (BarrierTest): B is MATCH_CONSTRAINT spread with max 150 between
 * the barrier and the root edge. */
static void test_barrier_max() {
    ConstraintWidgetContainer root(0.f, 0.f, 800.f, 600.f);
    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    ConstraintWidget* b = new ConstraintWidget(150.f, 20.f);
    Barrier* barrier = new Barrier();
    barrier->add_helper_widget(a);
    root.add(a);
    root.add(barrier);
    root.add(b);
    barrier->set_barrier_type(Barrier::RIGHT);

    a->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT);
    a->connect(ConstraintAnchor::Type::TOP, &root, ConstraintAnchor::Type::TOP);
    b->connect(ConstraintAnchor::Type::TOP, &root, ConstraintAnchor::Type::TOP);
    b->connect(ConstraintAnchor::Type::LEFT, barrier, ConstraintAnchor::Type::LEFT);
    b->connect(ConstraintAnchor::Type::RIGHT, &root, ConstraintAnchor::Type::RIGHT);
    b->set_horizontal_bias_percent(0.f);
    b->set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT);
    b->set_horizontal_match_style(ConstraintWidget::MATCH_CONSTRAINT_SPREAD, 0, 150, 1.f);
    root.layout();

    EXPECT_NEAR(a->get_left(), 0.f, 1e-3f);
    EXPECT_NEAR(barrier->get_left(), 100.f, 1e-3f);
    EXPECT_NEAR(b->get_left(), 100.f, 1e-3f);
    EXPECT_NEAR(b->get_width(), 150.f, 1e-3f);

    delete a; delete b; delete barrier;
}

/* barrierCenter (BarrierTest): widget right edge anchored to a RIGHT barrier
 * with a margin. */
static void test_barrier_center() {
    ConstraintWidgetContainer root(0.f, 0.f, 800.f, 600.f);
    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    Barrier* barrier = new Barrier();
    barrier->add_helper_widget(a);
    root.add(a);
    root.add(barrier);
    barrier->set_barrier_type(Barrier::RIGHT);

    a->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT, 10.f);
    a->connect(ConstraintAnchor::Type::RIGHT, barrier, ConstraintAnchor::Type::RIGHT, 30.f);
    a->connect(ConstraintAnchor::Type::TOP, &root, ConstraintAnchor::Type::TOP);
    root.layout();

    EXPECT_NEAR(a->get_left(), 10.f, 1e-3f);
    EXPECT_NEAR(barrier->get_left(), 140.f, 1e-3f);

    delete a; delete barrier;
}

/* barrierCenter2 (BarrierTest): widget left edge anchored to a LEFT barrier
 * with a margin. */
static void test_barrier_center2() {
    ConstraintWidgetContainer root(0.f, 0.f, 800.f, 600.f);
    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    Barrier* barrier = new Barrier();
    barrier->add_helper_widget(a);
    root.add(a);
    root.add(barrier);
    barrier->set_barrier_type(Barrier::LEFT);

    a->connect(ConstraintAnchor::Type::RIGHT, &root, ConstraintAnchor::Type::RIGHT, 10.f);
    a->connect(ConstraintAnchor::Type::LEFT, barrier, ConstraintAnchor::Type::LEFT, 30.f);
    a->connect(ConstraintAnchor::Type::TOP, &root, ConstraintAnchor::Type::TOP);
    root.layout();

    EXPECT_NEAR(a->get_right(), root.get_width() - 10.f, 1e-3f);
    EXPECT_NEAR(barrier->get_left(), a->get_left() - 30.f, 1e-3f);

    delete a; delete barrier;
}

/* testWrapGuideline (GuidelineTest): A pinned to a percent guideline on the
 * right and an end guideline at the bottom; root wraps vertically to 80. */
static void test_wrap_guideline() {
    ConstraintWidgetContainer root(0.f, 0.f, 800.f, 600.f);
    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    Guideline* guideline_right = new Guideline();
    guideline_right->set_orientation(Guideline::VERTICAL_GUIDELINE);
    Guideline* guideline_bottom = new Guideline();
    guideline_bottom->set_orientation(Guideline::HORIZONTAL_GUIDELINE);
    guideline_right->set_guide_percent(0.64f);
    guideline_bottom->set_guide_end(60.f);

    a->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT);
    a->connect(ConstraintAnchor::Type::RIGHT, guideline_right, ConstraintAnchor::Type::RIGHT);
    a->connect(ConstraintAnchor::Type::BOTTOM, guideline_bottom, ConstraintAnchor::Type::TOP);
    root.add(a);
    root.add(guideline_right);
    root.add(guideline_bottom);

    root.set_vertical_dimension_behaviour(ConstraintWidget::DimensionBehaviour::WRAP_CONTENT);
    root.layout();
    EXPECT_NEAR(root.get_height(), 80.f, 1e-3f);

    delete a; delete guideline_right; delete guideline_bottom;
}

/* testWrapGuideline2 (GuidelineTest): A MATCH_CONSTRAINT between a begin
 * guideline and the root; root wraps horizontally to 70. */
static void test_wrap_guideline2() {
    ConstraintWidgetContainer root(0.f, 0.f, 800.f, 600.f);
    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    Guideline* guideline = new Guideline();
    guideline->set_orientation(Guideline::VERTICAL_GUIDELINE);
    guideline->set_guide_begin(60.f);

    a->connect(ConstraintAnchor::Type::LEFT, guideline, ConstraintAnchor::Type::LEFT, 5.f);
    a->connect(ConstraintAnchor::Type::RIGHT, &root, ConstraintAnchor::Type::RIGHT, 5.f);
    a->set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT);
    root.add(a);
    root.add(guideline);

    root.set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::WRAP_CONTENT);
    root.layout();
    EXPECT_NEAR(root.get_width(), 70.f, 1e-3f);

    delete a; delete guideline;
}

/* testWrapPercent (BasicTest): MATCH_CONSTRAINT PERCENT with the WRAP sentinel
 * min; root wraps: A is half the root width, min 100 -> root 200, A 100. */
static void test_wrap_percent() {
    ConstraintWidgetContainer root(0.f, 0.f, 600.f, 800.f);
    ConstraintWidget* a = new ConstraintWidget(100.f, 30.f);

    a->set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::MATCH_CONSTRAINT);
    a->set_horizontal_match_style(ConstraintWidget::MATCH_CONSTRAINT_PERCENT,
                                  ConstraintWidget::WRAP_SENTINEL, 0, 0.5f);
    a->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT);
    root.add(a);

    root.set_horizontal_dimension_behaviour(ConstraintWidget::DimensionBehaviour::WRAP_CONTENT);
    root.layout();

    EXPECT_NEAR(a->get_width(), 100.f, 1e-3f);
    EXPECT_NEAR(root.get_width(), a->get_width() * 2.f, 1e-3f);

    delete a;
}

/* testGoneSingleConnection (VisibilityTest): GONE widget collapses to 0 and
 * dependents use the gone margin instead of the regular margin. */
static void test_gone_single_connection() {
    ConstraintWidgetContainer root(0.f, 0.f, 800.f, 600.f);
    ConstraintWidget* a = new ConstraintWidget(100.f, 20.f);
    ConstraintWidget* b = new ConstraintWidget(100.f, 20.f);
    const float margin = 175.f;
    const float gone_margin = 42.f;
    root.add(a);
    root.add(b);

    a->connect(ConstraintAnchor::Type::LEFT, &root, ConstraintAnchor::Type::LEFT, margin);
    a->connect(ConstraintAnchor::Type::TOP, &root, ConstraintAnchor::Type::TOP, margin);
    b->connect(ConstraintAnchor::Type::LEFT, a, ConstraintAnchor::Type::RIGHT, margin);
    b->connect(ConstraintAnchor::Type::TOP, a, ConstraintAnchor::Type::BOTTOM, margin);

    root.layout();
    EXPECT_NEAR(a->get_left(), margin, 1e-3f);
    EXPECT_NEAR(a->get_top(), margin, 1e-3f);
    EXPECT_NEAR(b->get_left(), a->get_right() + margin, 1e-3f);
    EXPECT_NEAR(b->get_top(), a->get_bottom() + margin, 1e-3f);

    a->set_visibility(ConstraintWidget::GONE);
    root.layout();
    EXPECT_NEAR(a->get_width(), 0.f, 1e-3f);
    EXPECT_NEAR(a->get_height(), 0.f, 1e-3f);
    EXPECT_NEAR(a->get_left(), 0.f, 1e-3f);
    EXPECT_NEAR(b->get_left(), a->get_right() + margin, 1e-3f);
    EXPECT_NEAR(b->get_top(), a->get_bottom() + margin, 1e-3f);

    b->set_gone_margin(ConstraintAnchor::Type::LEFT, gone_margin);
    b->set_gone_margin(ConstraintAnchor::Type::TOP, gone_margin);
    root.layout();
    EXPECT_NEAR(b->get_left(), a->get_right() + gone_margin, 1e-3f);
    EXPECT_NEAR(b->get_top(), a->get_bottom() + gone_margin, 1e-3f);

    delete a; delete b;
}

int main() {
    test_adding_widgets();
    test_widget_top_right_positioning();
    test_centering();
    test_simple_min_match();
    test_simple_horizontal_match();
    test_dangling_ratio();
    test_basic_ratio();
    test_basic_ratio2();
    test_simple_ratio();
    test_min_max_match();
    test_wrap_ratio();
    test_barrier_constrained_width();
    test_barrier_image();
    test_barrier_max();
    test_barrier_center();
    test_barrier_center2();
    test_wrap_guideline();
    test_wrap_guideline2();
    test_wrap_percent();
    test_gone_single_connection();
    return test_result();
}

