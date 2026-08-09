/* Layout fidelity: gravity, baseline alignment, relative anchors, scroll,
 * hit testing. */

#include "android_test_util.h"

static void test_frame_gravity_bottom_right() {
    android_ui_t ui = make_ui();
    android_view_t frame = make_view(ui, ANDROID_VIEW_FRAME_LAYOUT);
    set_match(frame);
    android_view_t child = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_layout_params_t lp{};
    lp.width.kind = ANDROID_SIZE_KIND_EXACT; lp.width.value_dp = 50.f;
    lp.height.kind = ANDROID_SIZE_KIND_EXACT; lp.height.value_dp = 50.f;
    lp.gravity = ANDROID_GRAVITY_BOTTOM | ANDROID_GRAVITY_RIGHT;
    android_view_set_layout_params(child, &lp);
    android_view_add_child(ui, frame, child);
    frame_and_layout(ui, frame, 200.f, 300.f);
    rectf b{};
    android_view_get_bounds(child, &b);
    EXPECT_NEAR(b.x, 100.0, 0.01);
    EXPECT_NEAR(b.y, 200.0, 0.01);
    EXPECT_NEAR(b.width, 100.0, 0.01);
    EXPECT_NEAR(b.height, 100.0, 0.01);
    android_ui_destroy(ui);
}

static void test_frame_gravity_center() {
    android_ui_t ui = make_ui();
    android_view_t frame = make_view(ui, ANDROID_VIEW_FRAME_LAYOUT);
    set_match(frame);
    android_view_t child = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_layout_params_t lp{};
    lp.width.kind = ANDROID_SIZE_KIND_EXACT; lp.width.value_dp = 50.f;
    lp.height.kind = ANDROID_SIZE_KIND_EXACT; lp.height.value_dp = 50.f;
    lp.gravity = ANDROID_GRAVITY_CENTER;
    android_view_set_layout_params(child, &lp);
    android_view_add_child(ui, frame, child);
    frame_and_layout(ui, frame, 200.f, 300.f);
    rectf b{};
    android_view_get_bounds(child, &b);
    EXPECT_NEAR(b.x, 50.0, 0.01);
    EXPECT_NEAR(b.y, 100.0, 0.01);
    android_ui_destroy(ui);
}

static void test_linear_margin_flow() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    set_wrap(a); set_wrap(b);
    android_view_set_text(a, "A");
    android_view_set_text(b, "B");
    android_layout_params_t lpb{};
    lpb.width.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lpb.height.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lpb.margins_dp.top = 5.f;
    android_view_set_layout_params(b, &lpb);
    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, b);
    frame_and_layout(ui, root, 200.f, 300.f);
    rectf ba{}, bb{};
    android_view_get_bounds(a, &ba);
    android_view_get_bounds(b, &bb);
    EXPECT_NEAR(ba.y, 0.0, 0.01);
    /* b sits below a (38.4) plus its top margin (10px). */
    EXPECT_NEAR(bb.y, 48.4, 0.01);
    android_ui_destroy(ui);
}

static void test_linear_horizontal_gravity() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_set_orientation(root, ANDROID_HORIZONTAL);
    android_view_set_baseline_aligned(root, FALSE);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    set_wrap(a);
    android_view_set_text(a, "A");
    android_layout_params_t lp{};
    lp.width.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lp.height.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lp.gravity = ANDROID_GRAVITY_CENTER_VERTICAL;
    android_view_set_layout_params(a, &lp);
    android_view_add_child(ui, root, a);
    frame_and_layout(ui, root, 200.f, 300.f);
    rectf b{};
    android_view_get_bounds(a, &b);
    EXPECT_NEAR(b.y, (300.f - 38.4f) / 2.f, 0.01);
    android_ui_destroy(ui);
}

static void test_linear_horizontal_baseline_alignment() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_set_orientation(root, ANDROID_HORIZONTAL);
    android_view_t small = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_view_t big = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    set_wrap(small); set_wrap(big);
    android_view_set_text(small, "A");
    android_view_set_text(big, "B");
    android_view_set_text_size_sp(big, 32.f);
    android_view_add_child(ui, root, small);
    android_view_add_child(ui, root, big);
    frame_and_layout(ui, root, 400.f, 300.f);
    rectf bs{}, bb{};
    android_view_get_bounds(small, &bs);
    android_view_get_bounds(big, &bb);
    /* big baseline 51.2, small baseline 25.6 -> small y = 51.2 - 25.6 = 25.6 */
    EXPECT_NEAR(bb.y, 0.0, 0.01);
    EXPECT_NEAR(bs.y, 25.6, 0.01);
    android_ui_destroy(ui);
}

static void test_relative_anchors() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_RELATIVE_LAYOUT);
    set_match(root);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 1);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 2);
    android_view_t c = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 3);
    set_wrap(a); set_wrap(b); set_wrap(c);
    android_view_set_text(a, "A");
    android_view_set_text(b, "B");
    android_view_set_text(c, "C");
    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, b);
    android_view_add_child(ui, root, c);
    android_view_set_relative_rule(b, ANDROID_RELATIVE_BELOW, 1);
    android_view_set_relative_rule(c, ANDROID_RELATIVE_ALIGN_PARENT_RIGHT, ANDROID_RELATIVE_TRUE);
    android_view_set_relative_rule(c, ANDROID_RELATIVE_ALIGN_PARENT_TOP, ANDROID_RELATIVE_TRUE);
    frame_and_layout(ui, root, 200.f, 300.f);
    rectf ba{}, bb{}, bc{};
    android_view_get_bounds(a, &ba);
    android_view_get_bounds(b, &bb);
    android_view_get_bounds(c, &bc);
    EXPECT_NEAR(bb.y, ba.height, 0.01);
    EXPECT_NEAR(bc.x, 200.f - bc.width, 0.01);
    EXPECT_NEAR(bc.y, 0.0, 0.01);
    android_ui_destroy(ui);
}

/* Dependency graph: C is added last but referenced by B (declared before it);
 * the topological sort must position B below C regardless of insertion order.
 * C is pinned to the top-right; B sits below C's left edge. */
static void test_relative_dependency_order() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_RELATIVE_LAYOUT);
    set_match(root);
    android_view_t c = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 30);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 20);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 10);
    set_wrap(a); set_wrap(b); set_wrap(c);
    android_view_set_text(a, "A");
    android_view_set_text(b, "B");
    android_view_set_text(c, "C");
    /* Insert C first, then B, then A — reverse declaration order. */
    android_view_add_child(ui, root, c);
    android_view_add_child(ui, root, b);
    android_view_add_child(ui, root, a);
    android_view_set_relative_rule(a, ANDROID_RELATIVE_ALIGN_PARENT_LEFT, ANDROID_RELATIVE_TRUE);
    android_view_set_relative_rule(a, ANDROID_RELATIVE_ALIGN_PARENT_TOP, ANDROID_RELATIVE_TRUE);
    android_view_set_relative_rule(b, ANDROID_RELATIVE_BELOW, 10);   /* below A */
    android_view_set_relative_rule(c, ANDROID_RELATIVE_BELOW, 20);   /* below B */
    frame_and_layout(ui, root, 200.f, 300.f);
    rectf ba{}, bb{}, bc{};
    android_view_get_bounds(a, &ba);
    android_view_get_bounds(b, &bb);
    android_view_get_bounds(c, &bc);
    EXPECT_NEAR(ba.x, 0.0, 0.01);
    EXPECT_NEAR(ba.y, 0.0, 0.01);
    EXPECT_NEAR(bb.x, 0.0, 0.01);
    EXPECT_NEAR(bb.y, ba.height, 0.01);      /* B directly below A */
    EXPECT_NEAR(bc.x, 0.0, 0.01);
    EXPECT_NEAR(bc.y, ba.height + bb.height, 0.01); /* C below B (chained) */
    android_ui_destroy(ui);
}

static void test_scroll_view() {
    android_ui_t ui = make_ui();
    android_view_t scroll = make_view(ui, ANDROID_VIEW_SCROLL_VIEW);
    set_match(scroll);
    android_view_t content = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_wrap(content);
    for (int i = 0; i < 20; ++i) {
        android_view_t item = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
        set_wrap(item);
        android_view_set_text(item, "X");
        android_view_add_child(ui, content, item);
    }
    android_view_add_child(ui, scroll, content);
    frame_and_layout(ui, scroll, 200.f, 300.f);
    scroll_metrics_t metrics = android_view_get_scroll_metrics(scroll);
    /* 20 * 38.4 = 768; overflow = 768 - 300 = 468 */
    EXPECT_NEAR(metrics.scrollable_overflow_y, 468.0, 0.01);
    android_view_set_scroll_offset(scroll, 0.f, 100.f);
    android_ui_layout(ui, scroll, 0.f, 0.f, 200.f, 300.f);
    rectf first{};
    android_view_get_bounds(android_view_get_child(content, 0), &first);
    EXPECT_NEAR(first.y, -100.0, 0.01);
    android_view_set_scroll_offset(scroll, 0.f, 9999.f);
    android_ui_layout(ui, scroll, 0.f, 0.f, 200.f, 300.f);
    rectf clamped{};
    android_view_get_bounds(android_view_get_child(content, 0), &clamped);
    EXPECT_NEAR(clamped.y, -468.0, 0.01);
    android_ui_destroy(ui);
}

static void test_hit_test() {
    android_ui_t ui = make_ui();
    android_view_t frame = make_view(ui, ANDROID_VIEW_FRAME_LAYOUT);
    set_match(frame);
    android_view_t child = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 42);
    android_layout_params_t lp{};
    lp.width.kind = ANDROID_SIZE_KIND_EXACT; lp.width.value_dp = 50.f;
    lp.height.kind = ANDROID_SIZE_KIND_EXACT; lp.height.value_dp = 50.f;
    lp.gravity = ANDROID_GRAVITY_CENTER;
    android_view_set_layout_params(child, &lp);
    android_view_add_child(ui, frame, child);
    frame_and_layout(ui, frame, 200.f, 300.f);
    android_view_t hit = android_ui_hit_test(ui, frame, 100.f, 150.f);
    EXPECT(hit == child);
    EXPECT(android_ui_hit_test(ui, frame, 5.f, 5.f) == frame);
    EXPECT(android_ui_hit_test(ui, frame, 250.f, 350.f) == nullptr);
    android_view_set_enabled(child, FALSE);
    EXPECT(android_ui_hit_test(ui, frame, 100.f, 150.f) == frame);
    android_ui_destroy(ui);
}

/* ConstraintLayout integration: the ported solver drives the view tree.
 * density 2: 1dp = 2px. */
static void test_constraint_layout_centering() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_CONSTRAINT_LAYOUT);
    set_match(root);

    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 1001);
    android_layout_params_t lpa{};
    lpa.width.kind = ANDROID_SIZE_KIND_EXACT; lpa.width.value_dp = 100.f;
    lpa.height.kind = ANDROID_SIZE_KIND_EXACT; lpa.height.value_dp = 50.f;
    android_view_set_layout_params(a, &lpa);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_LEFT, ANDROID_CONSTRAINT_LEFT, 0);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_RIGHT, ANDROID_CONSTRAINT_RIGHT, 0);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_TOP, ANDROID_CONSTRAINT_TOP, 0);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_BOTTOM, ANDROID_CONSTRAINT_BOTTOM, 0);

    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 1002);
    android_layout_params_t lpb{};
    lpb.width.kind = ANDROID_SIZE_KIND_EXACT; lpb.width.value_dp = 100.f;
    lpb.height.kind = ANDROID_SIZE_KIND_EXACT; lpb.height.value_dp = 50.f;
    android_view_set_layout_params(b, &lpb);
    android_view_add_constraint(b, -1, ANDROID_CONSTRAINT_LEFT, ANDROID_CONSTRAINT_LEFT, 20);
    android_view_add_constraint(b, -1, ANDROID_CONSTRAINT_TOP, ANDROID_CONSTRAINT_TOP, 20);

    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, b);
    frame_and_layout(ui, root, 320.f, 640.f);

    rectf ba{}, bb{};
    android_view_get_bounds(a, &ba);
    android_view_get_bounds(b, &bb);
    EXPECT_NEAR(ba.x, 60.0, 0.5);   /* (320-200)/2 */
    EXPECT_NEAR(ba.y, 270.0, 0.5);  /* (640-100)/2 */
    EXPECT_NEAR(ba.width, 200.0, 0.5);
    EXPECT_NEAR(ba.height, 100.0, 0.5);
    EXPECT_NEAR(bb.x, 40.0, 0.5);
    EXPECT_NEAR(bb.y, 40.0, 0.5);
    android_ui_destroy(ui);
}

static void test_constraint_layout_relative() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_CONSTRAINT_LAYOUT);
    set_match(root);

    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 2001);
    android_layout_params_t lpa{};
    lpa.width.kind = ANDROID_SIZE_KIND_EXACT; lpa.width.value_dp = 100.f;
    lpa.height.kind = ANDROID_SIZE_KIND_EXACT; lpa.height.value_dp = 50.f;
    android_view_set_layout_params(a, &lpa);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_LEFT, ANDROID_CONSTRAINT_LEFT, 0);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_TOP, ANDROID_CONSTRAINT_TOP, 0);

    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 2002);
    android_layout_params_t lpb{};
    lpb.width.kind = ANDROID_SIZE_KIND_EXACT; lpb.width.value_dp = 100.f;
    lpb.height.kind = ANDROID_SIZE_KIND_EXACT; lpb.height.value_dp = 50.f;
    android_view_set_layout_params(b, &lpb);
    android_view_add_constraint(b, 2001, ANDROID_CONSTRAINT_TOP, ANDROID_CONSTRAINT_BOTTOM, 10);
    android_view_add_constraint(b, 2001, ANDROID_CONSTRAINT_LEFT, ANDROID_CONSTRAINT_LEFT, 0);

    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, b);
    frame_and_layout(ui, root, 320.f, 640.f);

    rectf ba{}, bb{};
    android_view_get_bounds(a, &ba);
    android_view_get_bounds(b, &bb);
    EXPECT_NEAR(ba.x, 0.0, 0.5);
    EXPECT_NEAR(ba.y, 0.0, 0.5);
    EXPECT_NEAR(bb.x, 0.0, 0.5);
    EXPECT_NEAR(bb.y, 120.0, 0.5); /* A.bottom (100) + 10dp (20) */
    android_ui_destroy(ui);
}

static void test_constraint_layout_ratio() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_CONSTRAINT_LAYOUT);
    set_match(root);

    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 3001);
    android_layout_params_t lpa{};
    lpa.width.kind = ANDROID_SIZE_KIND_EXACT; lpa.width.value_dp = 0.f; /* 0dp -> MATCH_CONSTRAINT */
    lpa.height.kind = ANDROID_SIZE_KIND_EXACT; lpa.height.value_dp = 0.f;
    android_view_set_layout_params(a, &lpa);
    android_view_set_constraint_ratio(a, 1.0f);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_LEFT, ANDROID_CONSTRAINT_LEFT, 0);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_RIGHT, ANDROID_CONSTRAINT_RIGHT, 0);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_TOP, ANDROID_CONSTRAINT_TOP, 0);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_BOTTOM, ANDROID_CONSTRAINT_BOTTOM, 0);

    android_view_add_child(ui, root, a);
    frame_and_layout(ui, root, 320.f, 640.f);

    rectf ba{};
    android_view_get_bounds(a, &ba);
    EXPECT_NEAR(ba.width, 320.0, 0.5);  /* square: min dimension */
    EXPECT_NEAR(ba.height, 320.0, 0.5);
    EXPECT_NEAR(ba.x, 0.0, 0.5);
    EXPECT_NEAR(ba.y, 160.0, 0.5);      /* (640-320)/2 */
    android_ui_destroy(ui);
}

/* ConstraintLayout barrier: a RIGHT barrier over A places B to its right. */
static void test_constraint_layout_barrier() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_CONSTRAINT_LAYOUT);
    set_match(root);

    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 4001);
    android_layout_params_t lpa{};
    lpa.width.kind = ANDROID_SIZE_KIND_EXACT; lpa.width.value_dp = 100.f;
    lpa.height.kind = ANDROID_SIZE_KIND_EXACT; lpa.height.value_dp = 50.f;
    android_view_set_layout_params(a, &lpa);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_LEFT, ANDROID_CONSTRAINT_LEFT, 0);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_TOP, ANDROID_CONSTRAINT_TOP, 0);

    android_view_t barrier = make_view(ui, ANDROID_VIEW_BARRIER, 4002);
    android_view_set_barrier_type(barrier, ANDROID_BARRIER_RIGHT);
    android_view_set_barrier_margin(barrier, 10.f);
    android_view_add_barrier_reference(barrier, 4001);

    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 4003);
    android_layout_params_t lpb{};
    lpb.width.kind = ANDROID_SIZE_KIND_EXACT; lpb.width.value_dp = 100.f;
    lpb.height.kind = ANDROID_SIZE_KIND_EXACT; lpb.height.value_dp = 50.f;
    android_view_set_layout_params(b, &lpb);
    android_view_add_constraint(b, 4002, ANDROID_CONSTRAINT_LEFT, ANDROID_CONSTRAINT_LEFT, 0);
    android_view_add_constraint(b, -1, ANDROID_CONSTRAINT_TOP, ANDROID_CONSTRAINT_TOP, 0);

    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, barrier);
    android_view_add_child(ui, root, b);
    frame_and_layout(ui, root, 320.f, 640.f);

    rectf ba{}, bb{}, br{};
    android_view_get_bounds(a, &ba);
    android_view_get_bounds(b, &bb);
    android_view_get_bounds(barrier, &br);
    EXPECT_NEAR(ba.x, 0.0, 0.5);
    EXPECT_NEAR(ba.width, 200.0, 0.5);   /* 100dp @ density 2 */
    EXPECT_NEAR(br.x, 220.0, 0.5);        /* A.right (200) + 10dp margin (20) */
    EXPECT_NEAR(bb.x, 220.0, 0.5);        /* B.left pinned to the barrier */
    EXPECT_NEAR(bb.y, 0.0, 0.5);
    android_ui_destroy(ui);
}

/* START/END anchors map to LEFT/RIGHT (LTR runtime). */
static void test_constraint_layout_start_end() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_CONSTRAINT_LAYOUT);
    set_match(root);

    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 5001);
    android_layout_params_t lpa{};
    lpa.width.kind = ANDROID_SIZE_KIND_EXACT; lpa.width.value_dp = 100.f;
    lpa.height.kind = ANDROID_SIZE_KIND_EXACT; lpa.height.value_dp = 50.f;
    android_view_set_layout_params(a, &lpa);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_START, ANDROID_CONSTRAINT_START, 20);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_TOP, ANDROID_CONSTRAINT_TOP, 0);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_END, ANDROID_CONSTRAINT_END, 20);

    android_view_add_child(ui, root, a);
    frame_and_layout(ui, root, 320.f, 640.f);

    rectf ba{};
    android_view_get_bounds(a, &ba);
    /* START/END anchors land at left margin 20dp (40px) and right margin
     * 20dp (280px); fixed 200px width centers between them (bias 0.5). */
    EXPECT_NEAR(ba.x, 60.0, 0.5);
    EXPECT_NEAR(ba.width, 200.0, 0.5);
    EXPECT_NEAR(ba.x + ba.width, 260.0, 0.5);
    android_ui_destroy(ui);
}

/* RTL: horizontal LinearLayout lays children out in reverse order. */
static void test_linear_rtl() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_set_orientation(root, ANDROID_HORIZONTAL);
    android_view_set_layout_direction(root, ANDROID_LAYOUT_DIRECTION_RTL);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 0);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 0);
    set_wrap(a); set_wrap(b);
    android_view_set_text(a, "AA");
    android_view_set_text(b, "B");
    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, b);
    frame_and_layout(ui, root, 200.f, 100.f);
    rectf ba{}, bb{};
    android_view_get_bounds(a, &ba);
    android_view_get_bounds(b, &bb);
    /* In RTL, the FIRST child (A) sits on the RIGHT edge; the run is aligned
     * to the right (default START gravity -> RIGHT), so the free space stays
     * on the left and B sits at 200 - total. */
    EXPECT_NEAR(ba.x + ba.width, 200.0, 0.01);
    EXPECT_NEAR(bb.x, 200.0 - ba.width - bb.width, 0.01);
    EXPECT_NEAR(bb.x + bb.width, ba.x, 0.01);
    android_ui_destroy(ui);
}

/* Dividers: MIDDLE divider reserves its thickness between children. */
static void test_linear_dividers() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_set_orientation(root, ANDROID_VERTICAL);
    android_view_set_show_dividers(root, ANDROID_SHOW_DIVIDER_MIDDLE);
    color_rgba divider_color{0.8f, 0.8f, 0.8f, 1.f};
    android_view_set_divider(root, 4.f, 0.f, divider_color); /* 4px thick */
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 0);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 0);
    set_wrap(a); set_wrap(b);
    android_view_set_text(a, "A");
    android_view_set_text(b, "B");
    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, b);
    frame_and_layout(ui, root, 200.f, 300.f);
    rectf ba{}, bb{};
    android_view_get_bounds(a, &ba);
    android_view_get_bounds(b, &bb);
    EXPECT_NEAR(ba.y, 0.0, 0.01);
    EXPECT_NEAR(bb.y, ba.height + 4.0, 0.01); /* divider reserves 4px */
    android_ui_destroy(ui);
}

/* measureWithLargestChild: weighted WRAP children all take the largest
 * child's size (AOSP remeasures weighted children with EXACTLY(largest)). */
static void test_linear_largest_child() {
    android_ui_t ui = make_ui();
    /* A wrap-content frame measures the linear layout with AT_MOST, which is
     * the mode that enables the measureWithLargestChild pass. */
    android_view_t frame = make_view(ui, ANDROID_VIEW_FRAME_LAYOUT);
    set_wrap(frame);
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_wrap(root);
    android_view_set_orientation(root, ANDROID_HORIZONTAL);
    android_view_set_measure_with_largest_child(root, TRUE);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 0);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 0);
    set_wrap(a); set_wrap(b);
    android_view_set_text(a, "A");
    android_view_set_text(b, "BBBB");
    /* both weighted with equal weights: each gets half the free space, and
     * with measureWithLargestChild each is at least as wide as the largest */
    android_layout_params_t lpa{};
    lpa.width.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lpa.height.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lpa.weight = 1.f;
    android_view_set_layout_params(a, &lpa);
    android_layout_params_t lpb{};
    lpb.width.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lpb.height.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    lpb.weight = 1.f;
    android_view_set_layout_params(b, &lpb);
    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, b);
    android_view_add_child(ui, frame, root);
    frame_and_layout(ui, frame, 200.f, 100.f);
    rectf ba{}, bb{};
    android_view_get_bounds(a, &ba);
    android_view_get_bounds(b, &bb);
    EXPECT_NEAR(ba.width, bb.width, 0.01); /* both equal the largest */
    EXPECT(ba.width >= 71.68f - 0.5f);     /* at least the largest intrinsic */
    android_ui_destroy(ui);
}

/* useDefaultMargins: a child without explicit margins gets the divider size
 * as its main-axis margin when MIDDLE dividers are shown. */
static void test_linear_default_margins() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_set_orientation(root, ANDROID_VERTICAL);
    android_view_set_show_dividers(root, ANDROID_SHOW_DIVIDER_MIDDLE);
    color_rgba divider_color{0.8f, 0.8f, 0.8f, 1.f};
    android_view_set_divider(root, 6.f, 0.f, divider_color);
    android_view_set_use_default_margins(root, TRUE);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 0);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 0);
    set_wrap(a); set_wrap(b);
    android_view_set_text(a, "A");
    android_view_set_text(b, "B");
    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, b);
    frame_and_layout(ui, root, 200.f, 300.f);
    rectf ba{}, bb{};
    android_view_get_bounds(a, &ba);
    android_view_get_bounds(b, &bb);
    /* useDefaultMargins: the four unspecified main-axis margins default to
     * the divider size (6): A.top(6) + A.height + A.bottom(6) + divider(6)
     * + B.top(6) places B 18px below A's bottom edge. */
    EXPECT_NEAR(bb.y, ba.y + ba.height + 18.0, 0.01);
    android_ui_destroy(ui);
}

int main() {
    test_frame_gravity_bottom_right();
    test_frame_gravity_center();
    test_linear_margin_flow();
    test_linear_horizontal_gravity();
    test_linear_horizontal_baseline_alignment();
    test_linear_rtl();
    test_linear_dividers();
    test_linear_largest_child();
    test_linear_default_margins();
    test_relative_anchors();
    test_relative_dependency_order();
    test_scroll_view();
    test_hit_test();
    test_constraint_layout_centering();
    test_constraint_layout_relative();
    test_constraint_layout_ratio();
    test_constraint_layout_barrier();
    test_constraint_layout_start_end();
    return test_result();
}
