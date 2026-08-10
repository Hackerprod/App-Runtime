/* Layout fidelity: gravity, baseline alignment, relative anchors, scroll,
 * hit testing. */

#include "android_test_util.h"

#include "../src/android/android_types.h"

#include <cstring>

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
    /* MC1: AOSP does not filter touch targeting by enabled — a disabled view
     * is still the hit target (it receives the event, just does not consume
     * it; canReceivePointerEvents gates on VISIBILITY only, View.java:
     * 16638-16640 / ViewGroup.java:2756). */
    EXPECT(android_ui_hit_test(ui, frame, 100.f, 150.f) == child);
    android_ui_destroy(ui);
}

/* AOSP gravity sentinel: lp.gravity defaults to -1 (UNSPECIFIED_GRAVITY), NOT
 * Gravity.NO_GRAVITY (0). A child without an explicit layout_gravity inherits
 * the container's cross-axis gravity in a LinearLayout
 * (LinearLayout.java:1702-1705: `if (gravity < 0) gravity = minorGravity;`).
 * This is the regression test for the SKYNET title offset (child was NOT
 * inheriting the container's CENTER gravity). */
static void test_linear_unspecified_gravity_inherits_container() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_set_gravity(root, ANDROID_GRAVITY_CENTER_HORIZONTAL);
    android_view_t child = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    set_wrap(child); /* no layout_gravity -> lp.gravity = -1 */
    android_view_set_text(child, "A");
    android_view_add_child(ui, root, child);
    frame_and_layout(ui, root, 200.f, 300.f);
    rectf b{};
    android_view_get_bounds(child, &b);
    /* centered on the cross axis: (200 - 18) / 2 */
    EXPECT_NEAR(b.x, (200.f - 18.f) / 2.f, 0.01);
    EXPECT_NEAR(b.y, 0.0, 0.01);
    android_ui_destroy(ui);
}

/* AOSP FrameLayout.layoutChildren (FrameLayout.java:293-296): a child with
 * UNSPECIFIED_GRAVITY uses DEFAULT_CHILD_GRAVITY (TOP|START), never the
 * container's gravity (FrameLayout has no container gravity). */
static void test_frame_unspecified_default_child_gravity() {
    android_ui_t ui = make_ui();
    android_view_t frame = make_view(ui, ANDROID_VIEW_FRAME_LAYOUT);
    set_match(frame);
    android_view_set_gravity(frame, ANDROID_GRAVITY_CENTER);
    android_view_t child = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    set_wrap(child); /* no layout_gravity -> lp.gravity = -1 */
    android_view_set_text(child, "A");
    android_view_add_child(ui, frame, child);
    frame_and_layout(ui, frame, 200.f, 300.f);
    rectf b{};
    android_view_get_bounds(child, &b);
    /* DEFAULT_CHILD_GRAVITY TOP|START -> top-left */
    EXPECT_NEAR(b.x, 0.0, 0.01);
    EXPECT_NEAR(b.y, 0.0, 0.01);
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

/* ── ImageView (AOSP onMeasure + configureBounds) ─────────────────── */

static bool_t fake_image_dimensions(const char* source, sizef* out_size, void*) {
    if (std::strcmp(source, "img100x50") == 0) {
        *out_size = {100.f, 50.f};
        return TRUE;
    }
    if (std::strcmp(source, "img100x100") == 0) {
        *out_size = {100.f, 100.f};
        return TRUE;
    }
    if (std::strcmp(source, "img200x100") == 0) {
        *out_size = {200.f, 100.f};
        return TRUE;
    }
    return FALSE;
}

/* adjustViewBounds: a wrap-content ImageView preserves the drawable aspect
 * ratio (100x50) inside a wrap-content frame. */
static void test_image_adjust_view_bounds() {
    android_ui_t ui = make_ui();
    android_ui_set_image_dimensions(ui, fake_image_dimensions, nullptr);
    android_view_t frame = make_view(ui, ANDROID_VIEW_FRAME_LAYOUT);
    set_wrap(frame);
    android_view_t image = make_view(ui, ANDROID_VIEW_IMAGE_VIEW);
    set_wrap(image);
    android_view_set_image_source(image, "img100x50");
    android_view_set_adjust_view_bounds(image, TRUE);
    android_view_add_child(ui, frame, image);
    frame_and_layout(ui, frame, 400.f, 400.f);
    sizef is{};
    android_view_get_measured_size(image, &is);
    EXPECT_NEAR(is.width, 100.f, 0.5f);
    EXPECT_NEAR(is.height, 50.f, 0.5f);
    android_ui_destroy(ui);
}

/* adjustViewBounds + maxWidth (60dp = 120px): an image larger than the max
 * (200x100) is clamped to 120px wide and the height follows the aspect
 * ratio (120x60). */
static void test_image_max_width() {
    android_ui_t ui = make_ui();
    android_ui_set_image_dimensions(ui, fake_image_dimensions, nullptr);
    android_view_t frame = make_view(ui, ANDROID_VIEW_FRAME_LAYOUT);
    set_wrap(frame);
    android_view_t image = make_view(ui, ANDROID_VIEW_IMAGE_VIEW);
    set_wrap(image);
    android_view_set_image_source(image, "img200x100");
    android_view_set_adjust_view_bounds(image, TRUE);
    android_view_set_max_image_size_dp(image, 60.f, 0.f);
    android_view_add_child(ui, frame, image);
    frame_and_layout(ui, frame, 400.f, 400.f);
    sizef is{};
    android_view_get_measured_size(image, &is);
    EXPECT_NEAR(is.width, 120.f, 0.5f);
    EXPECT_NEAR(is.height, 60.f, 0.5f);
    android_ui_destroy(ui);
}

/* CENTER_CROP geometry: a 100x100 image in a 100x50 view keeps its aspect,
 * scales to fill (scale 1) and is vertically centered (dst y = -25). */
static void test_image_center_crop_geometry() {
    android_ui_t ui = make_ui();
    android_ui_set_image_dimensions(ui, fake_image_dimensions, nullptr);
    android_view_t image = make_view(ui, ANDROID_VIEW_IMAGE_VIEW);
    android_layout_params_t lp{};
    lp.width.kind = ANDROID_SIZE_KIND_EXACT; lp.width.value_dp = 50.f;
    lp.height.kind = ANDROID_SIZE_KIND_EXACT; lp.height.value_dp = 25.f;
    android_view_set_layout_params(image, &lp);
    android_view_set_image_source(image, "img100x100");
    android_view_set_scale_type(image, ANDROID_SCALE_CENTER_CROP);
    frame_and_layout(ui, image, 100.f, 50.f);
    EXPECT_NEAR(image->image_dst_rect.width, 100.f, 0.5f);
    EXPECT_NEAR(image->image_dst_rect.height, 100.f, 0.5f);
    EXPECT_NEAR(image->image_dst_rect.y, -25.f, 0.5f); /* crop offset */
    EXPECT_NEAR(image->image_dst_rect.x, 0.f, 0.5f);
    android_ui_destroy(ui);
}

/* The display list carries a PAINT_DRAW_IMAGE command with the resolved
 * source/destination rectangles. */
static void test_image_draw_command() {
    android_ui_t ui = make_ui();
    android_ui_set_image_dimensions(ui, fake_image_dimensions, nullptr);
    android_view_t image = make_view(ui, ANDROID_VIEW_IMAGE_VIEW);
    set_wrap(image);
    android_view_set_image_source(image, "img100x50");
    frame_and_layout(ui, image, 200.f, 100.f);

    display_list_t list = nullptr;
    EXPECT(android_ui_record(ui, image, &list) == OK);
    bool found = false;
    const int32_t count = display_list_get_count(list);
    for (int32_t i = 0; i < count; ++i) {
        paint_command_t cmd{};
        if (display_list_get_command(list, i, &cmd) != OK) continue;
        if (cmd.tag == PAINT_DRAW_IMAGE) {
            found = true;
            EXPECT_NEAR(cmd.data.draw_image.source_rect.width, 100.f, 0.5f);
            EXPECT_NEAR(cmd.data.draw_image.source_rect.height, 50.f, 0.5f);
            paint_command_free(&cmd);
            break;
        }
        paint_command_free(&cmd);
    }
    EXPECT(found);
    display_list_destroy(list);
    android_ui_destroy(ui);
}

/* LL1: the END divider in an RTL horizontal LinearLayout must be emitted even
 * when every child is GONE — AOSP drawDividersHorizontal falls back to
 * getPaddingLeft() when getLastNonGoneChild() == null
 * (LinearLayout.java:497-502). The pre-fix code only handled the LTR
 * all-GONE fallback. */
static void test_linear_rtl_all_gone_end_divider() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_set_orientation(root, ANDROID_HORIZONTAL);
    android_view_set_layout_direction(root, ANDROID_LAYOUT_DIRECTION_RTL);
    android_view_set_show_dividers(root, ANDROID_SHOW_DIVIDER_END);
    color_rgba divider_color{0.8f, 0.8f, 0.8f, 1.f};
    android_view_set_divider(root, 4.f, 0.f, divider_color); /* 4px thick */
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 0);
    set_wrap(a);
    android_view_set_text(a, "A");
    android_view_set_visibility(a, ANDROID_GONE);
    android_view_add_child(ui, root, a);
    frame_and_layout(ui, root, 200.f, 100.f);
    const std::vector<rectf>& rects = root->divider_rects;
    EXPECT(rects.size() == 1);
    if (rects.size() == 1) {
        /* RTL all-GONE: position = getPaddingLeft() = 0, not the LTR
         * width - paddingRight - thickness formula. */
        EXPECT_NEAR(rects[0].x, 0.0, 0.01);
        EXPECT_NEAR(rects[0].width, 4.0, 0.01);
    }
    android_ui_destroy(ui);
}

/* LL4: the cross-axis childSpace is NOT clamped to 0 — a CENTER child in a
 * box whose padding exceeds its width resolves to a negative space and the
 * child is offset (clipped) accordingly (AOSP LinearLayout.java:1667/1773). */
static void test_linear_negative_cross_center() {
    android_ui_t ui = make_ui(1.f); /* density 1 keeps the arithmetic exact */
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_set_padding_edges_dp(root, {60.f, 0.f, 60.f, 0.f});
    android_view_t child = make_view(ui, ANDROID_VIEW_TEXT_VIEW);
    android_layout_params_t lp{};
    lp.width.kind = ANDROID_SIZE_KIND_EXACT; lp.width.value_dp = 10.f;
    lp.height.kind = ANDROID_SIZE_KIND_EXACT; lp.height.value_dp = 10.f;
    lp.gravity = ANDROID_GRAVITY_CENTER_HORIZONTAL;
    android_view_set_layout_params(child, &lp);
    android_view_add_child(ui, root, child);
    frame_and_layout(ui, root, 100.f, 100.f);
    rectf b{};
    android_view_get_bounds(child, &b);
    /* content_w = 100 - 60 - 60 = -20; cx = 60 + (-20 - 10) / 2 = 45. A
     * clamp (the old bug) would give cx = 60 + (0 - 10) / 2 = 55. */
    EXPECT_NEAR(b.width, 10.0, 0.01);
    EXPECT_NEAR(b.x, 45.0, 0.01);
    android_ui_destroy(ui);
}

/* RL2: RelativeLayout.getBaseline delegates to the top-start-most visible
 * child (RelativeLayout.java:540-555, compareLayoutPosition :667-673). The
 * container baseline equals that child's own baseline, not -1. */
static void test_relative_baseline() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_RELATIVE_LAYOUT);
    set_match(root);
    android_view_t small = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 1);
    set_wrap(small);
    android_view_set_text(small, "A");
    android_view_set_relative_rule(small, ANDROID_RELATIVE_ALIGN_PARENT_TOP, ANDROID_RELATIVE_TRUE);
    android_view_set_relative_rule(small, ANDROID_RELATIVE_ALIGN_PARENT_LEFT, ANDROID_RELATIVE_TRUE);
    android_view_t big = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 2);
    set_wrap(big);
    android_view_set_text(big, "B");
    android_view_set_text_size_sp(big, 32.f);
    android_view_set_relative_rule(big, ANDROID_RELATIVE_BELOW, 1);
    android_view_set_relative_rule(big, ANDROID_RELATIVE_ALIGN_PARENT_LEFT, ANDROID_RELATIVE_TRUE);
    android_view_add_child(ui, root, small);
    android_view_add_child(ui, root, big);
    frame_and_layout(ui, root, 200.f, 300.f);
    /* small is top-start-most (top 0): its baseline (0.8 * 32 = 25.6) is the
     * container baseline; big (top 38.4) is not selected even though its
     * baseline (51.2) is larger. */
    EXPECT_NEAR(root->measured_baseline, 25.6, 0.01);
    EXPECT_NEAR(big->measured_baseline, 51.2, 0.01);
    android_ui_destroy(ui);
}

/* CA3: a MATCH_PARENT child in a ConstraintLayout subtracts its margins in
 * addition to the padding (widgetConstraintLayout.java:706-711). */
static void test_constraint_match_parent_margin() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_CONSTRAINT_LAYOUT);
    set_match(root);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 6001);
    android_layout_params_t lpa{};
    lpa.width.kind = ANDROID_SIZE_KIND_MATCH_PARENT;
    lpa.height.kind = ANDROID_SIZE_KIND_EXACT; lpa.height.value_dp = 50.f;
    lpa.margins_dp = {20.f, 0.f, 10.f, 0.f};
    android_view_set_layout_params(a, &lpa);
    android_view_add_child(ui, root, a);
    frame_and_layout(ui, root, 320.f, 640.f);
    rectf ba{};
    android_view_get_bounds(a, &ba);
    /* 320 - 0 padding - (20dp + 10dp margins @ density 2 = 60) = 260. */
    EXPECT_NEAR(ba.width, 260.0, 0.5);
    EXPECT_NEAR(ba.x, 0.0, 0.5);
    EXPECT_NEAR(ba.height, 100.0, 0.5);
    android_ui_destroy(ui);
}

/* CA4: a 0dp MATCH_CONSTRAINT child is measured WRAP_CONTENT initially
 * (widgetConstraintLayout.java:713-715), so a single-side constraint keeps
 * its intrinsic width instead of collapsing to EXACTLY(0). */
static void test_constraint_0dp_single_side() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_CONSTRAINT_LAYOUT);
    set_match(root);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 6002);
    android_layout_params_t lpa{};
    lpa.width.kind = ANDROID_SIZE_KIND_EXACT; lpa.width.value_dp = 0.f; /* 0dp -> MATCH_CONSTRAINT */
    lpa.height.kind = ANDROID_SIZE_KIND_WRAP_CONTENT;
    android_view_set_layout_params(a, &lpa);
    android_view_set_text(a, "Hi");
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_LEFT, ANDROID_CONSTRAINT_LEFT, 0);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_TOP, ANDROID_CONSTRAINT_TOP, 0);
    android_view_add_child(ui, root, a);
    frame_and_layout(ui, root, 320.f, 640.f);
    rectf ba{};
    android_view_get_bounds(a, &ba);
    /* Single-side MATCH_CONSTRAINT -> d = max(min, dimension) with dimension
     * the measured WRAP width ("Hi" = ceil(2*17.92) = 36). EXACTLY(0) (the
     * old bug) would collapse it to 0. */
    EXPECT_NEAR(ba.width, 36.0, 0.5);
    EXPECT_NEAR(ba.x, 0.0, 0.5);
    android_ui_destroy(ui);
}

/* CA5: matchConstraintMinWidth is already in PIXELS in AOSP
 * (widgetConstraintLayout.java:1506-1511, getDimensionPixelSize). The port
 * stores dp, so 40dp must reach the solver as 80px at density 2. */
static void test_constraint_match_min_px() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_CONSTRAINT_LAYOUT);
    set_match(root);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 6003);
    android_layout_params_t lpa{};
    lpa.width.kind = ANDROID_SIZE_KIND_EXACT; lpa.width.value_dp = 0.f; /* MATCH_CONSTRAINT */
    lpa.height.kind = ANDROID_SIZE_KIND_EXACT; lpa.height.value_dp = 20.f;
    android_view_set_layout_params(a, &lpa);
    android_view_set_constraint_match_style(a, ANDROID_CONSTRAINT_MATCH_SPREAD,
                                            ANDROID_CONSTRAINT_MATCH_SPREAD,
                                            40.f, 0.f, 0.f, 0.f);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_LEFT, ANDROID_CONSTRAINT_LEFT, 0);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_TOP, ANDROID_CONSTRAINT_TOP, 0);
    android_view_add_child(ui, root, a);
    frame_and_layout(ui, root, 320.f, 640.f);
    rectf ba{};
    android_view_get_bounds(a, &ba);
    /* Single-side MATCH_CONSTRAINT: d = max(min, dimension). Empty text wraps
     * to 0, so the 40dp min (80px, not 40px raw) is binding. */
    EXPECT_NEAR(ba.width, 80.0, 0.5);
    android_ui_destroy(ui);
}

/* LL5: in RTL the BEGINNING divider sits to the RIGHT of the first non-GONE
 * child (child.getRight() + rightMargin, LinearLayout.java:481-489). The
 * pre-fix RTL loop only emitted MIDDLE/END rects to the LEFT of each child, so
 * the BEGINNING rect was never produced. */
static void test_linear_rtl_beginning_divider() {
    android_ui_t ui = make_ui(1.f); /* density 1 keeps the arithmetic exact */
    android_view_t root = make_view(ui, ANDROID_VIEW_LINEAR_LAYOUT);
    set_match(root);
    android_view_set_orientation(root, ANDROID_HORIZONTAL);
    android_view_set_layout_direction(root, ANDROID_LAYOUT_DIRECTION_RTL);
    android_view_set_show_dividers(root, ANDROID_SHOW_DIVIDER_BEGINNING |
                                          ANDROID_SHOW_DIVIDER_MIDDLE |
                                          ANDROID_SHOW_DIVIDER_END);
    color_rgba divider_color{0.8f, 0.8f, 0.8f, 1.f};
    android_view_set_divider(root, 4.f, 0.f, divider_color); /* 4px thick */
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 0);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 0);
    set_wrap(a); set_wrap(b);
    android_view_set_text(a, "A");
    android_view_set_text(b, "B");
    android_view_add_child(ui, root, a);
    android_view_add_child(ui, root, b);
    frame_and_layout(ui, root, 200.f, 100.f);
    const std::vector<rectf>& rects = root->divider_rects;
    /* 3 dividers: END (far left, before the last child), MIDDLE (between the
     * two children) and BEGINNING (far right, after the first child). */
    EXPECT(rects.size() == 3);
    if (rects.size() == 3) {
        EXPECT_NEAR(rects[0].x, 170.0, 0.01); /* END at b.getLeft() - 4 = 170 */
        EXPECT_NEAR(rects[1].x, 183.0, 0.01); /* MIDDLE between b and a */
        EXPECT_NEAR(rects[2].x, 196.0, 0.01); /* BEGINNING at a.getRight() = 196 */
    }
    android_ui_destroy(ui);
}

/* FS2: an empty ScrollView must clamp mScrollY to 0 and reset the scroll
 * metrics (AOSP onLayout: childHeight = 0 -> scrollRange = 0,
 * ScrollView.java:1857-1870); the pre-fix layout returned early leaving stale
 * overflow and a clamped-but-unapplied offset. */
static void test_scroll_view_empty_resets_metrics() {
    android_ui_t ui = make_ui();
    android_view_t scroll = make_view(ui, ANDROID_VIEW_SCROLL_VIEW);
    set_match(scroll);
    android_view_set_scroll_offset(scroll, 0.f, 100.f);
    frame_and_layout(ui, scroll, 200.f, 300.f);
    scroll_metrics_t metrics = android_view_get_scroll_metrics(scroll);
    EXPECT_NEAR(metrics.scrollable_overflow_y, 0.0, 0.01);
    EXPECT_NEAR(metrics.scrollable_overflow_x, 0.0, 0.01);
    EXPECT_NEAR(metrics.scroll_offset_y, 0.0, 0.01);
    EXPECT_NEAR(metrics.scroll_offset_x, 0.0, 0.01);
    EXPECT_NEAR(scroll->scroll_y, 0.0, 0.01);
    android_ui_destroy(ui);
}

/* MC3: a GONE barrier still exists as a widget in the graph (helpers are added
 * always, widgetConstraintLayout.java:1296) so a child constraining to it
 * resolves the connection instead of dropping it. */
static void test_constraint_gone_barrier() {
    android_ui_t ui = make_ui();
    android_view_t root = make_view(ui, ANDROID_VIEW_CONSTRAINT_LAYOUT);
    set_match(root);

    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 7001);
    android_layout_params_t lpa{};
    lpa.width.kind = ANDROID_SIZE_KIND_EXACT; lpa.width.value_dp = 100.f;
    lpa.height.kind = ANDROID_SIZE_KIND_EXACT; lpa.height.value_dp = 50.f;
    android_view_set_layout_params(a, &lpa);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_LEFT, ANDROID_CONSTRAINT_LEFT, 0);
    android_view_add_constraint(a, -1, ANDROID_CONSTRAINT_TOP, ANDROID_CONSTRAINT_TOP, 0);

    android_view_t barrier = make_view(ui, ANDROID_VIEW_BARRIER, 7002);
    android_view_set_barrier_type(barrier, ANDROID_BARRIER_RIGHT);
    android_view_set_barrier_margin(barrier, 10.f);
    android_view_add_barrier_reference(barrier, 7001);
    android_view_set_visibility(barrier, ANDROID_GONE);

    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 7003);
    android_layout_params_t lpb{};
    lpb.width.kind = ANDROID_SIZE_KIND_EXACT; lpb.width.value_dp = 100.f;
    lpb.height.kind = ANDROID_SIZE_KIND_EXACT; lpb.height.value_dp = 50.f;
    android_view_set_layout_params(b, &lpb);
    android_view_add_constraint(b, 7002, ANDROID_CONSTRAINT_LEFT, ANDROID_CONSTRAINT_LEFT, 0);
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
    /* The GONE barrier is still solved at A.right + margin (20px) and B keeps
     * its pin; a dropped connection (the old skip) would leave B at x = 0. */
    EXPECT_NEAR(br.x, 220.0, 0.5);
    EXPECT_NEAR(bb.x, 220.0, 0.5);
    android_ui_destroy(ui);
}

int main() {
    test_frame_gravity_bottom_right();
    test_frame_gravity_center();
    test_linear_unspecified_gravity_inherits_container();
    test_frame_unspecified_default_child_gravity();
    test_linear_margin_flow();
    test_linear_horizontal_gravity();
    test_linear_horizontal_baseline_alignment();
    test_linear_rtl();
    test_linear_rtl_all_gone_end_divider();
    test_linear_rtl_beginning_divider();
    test_linear_dividers();
    test_linear_largest_child();
    test_linear_default_margins();
    test_linear_negative_cross_center();
    test_relative_anchors();
    test_relative_dependency_order();
    test_relative_baseline();
    test_scroll_view();
    test_scroll_view_empty_resets_metrics();
    test_hit_test();
    test_constraint_layout_centering();
    test_constraint_layout_relative();
    test_constraint_layout_ratio();
    test_constraint_layout_barrier();
    test_constraint_layout_start_end();
    test_constraint_match_parent_margin();
    test_constraint_0dp_single_side();
    test_constraint_match_min_px();
    test_constraint_gone_barrier();
    test_image_adjust_view_bounds();
    test_image_max_width();
    test_image_center_crop_geometry();
    test_image_draw_command();
    return test_result();
}
