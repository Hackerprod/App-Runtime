/* GridLayout fidelity: auto cell assignment, explicit cells, spans, weights
 * (excess distribution through the constraint solver), FILL sizing.
 *
 * Expected geometry follows the AOSP solver exactly: inflexible (no-gravity,
 * no-weight) columns absorb no excess, so the trailing column/row takes the
 * leftover space; FILL-aligned children stretch to their cell. */

#include "android_test_util.h"

#include <exception>

static android_view_t grid_view(android_ui_t ui, int columns) {
    android_view_t grid = make_view(ui, ANDROID_VIEW_GRID_LAYOUT, 100);
    android_view_set_column_count(grid, columns);
    return grid;
}

static void add_text(android_ui_t ui, android_view_t grid, const char* text, int id) {
    android_view_t tv = make_view(ui, ANDROID_VIEW_TEXT_VIEW, id);
    set_wrap(tv);
    android_view_set_text(tv, text);
    android_view_add_child(ui, grid, tv);
}

static void test_auto_placement() {
    android_ui_t ui = make_ui();
    android_view_t grid = grid_view(ui, 2);
    set_match(grid);
    for (int i = 0; i < 4; ++i) add_text(ui, grid, "A", 1 + i);
    frame_and_layout(ui, grid, 200.f, 300.f);
    rectf b0{}, b1{}, b2{}, b3{};
    android_view_get_bounds(android_view_get_child(grid, 0), &b0);
    android_view_get_bounds(android_view_get_child(grid, 1), &b1);
    android_view_get_bounds(android_view_get_child(grid, 2), &b2);
    android_view_get_bounds(android_view_get_child(grid, 3), &b3);
    /* auto-assignment flows left-to-right, wrapping rows: [A B; C D]. The
     * children are inflexible, so the excess goes to the last column/row. */
    EXPECT_NEAR(b0.x, 0.0, 0.01);
    EXPECT_NEAR(b0.y, 0.0, 0.01);
    EXPECT_NEAR(b1.x, 200.0 - b0.width, 0.01);
    EXPECT_NEAR(b1.y, 0.0, 0.01);
    EXPECT_NEAR(b2.x, 0.0, 0.01);
    EXPECT_NEAR(b2.y, 300.0 - b0.height, 0.01);
    EXPECT_NEAR(b3.x, 200.0 - b0.width, 0.01);
    EXPECT_NEAR(b3.y, 300.0 - b0.height, 0.01);
    android_ui_destroy(ui);
}

static void test_explicit_cell_and_span() {
    android_ui_t ui = make_ui();
    android_view_t grid = grid_view(ui, 3);
    set_match(grid);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 1);
    set_wrap(a);
    android_view_set_text(a, "A");
    android_view_set_grid_gravity(a, ANDROID_GRAVITY_FILL_HORIZONTAL | ANDROID_GRAVITY_TOP);
    android_view_add_child(ui, grid, a);
    android_view_t span = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 2);
    set_wrap(span);
    android_view_set_text(span, "B");
    android_view_set_grid_cell(span, 1, 0, 1, 2); /* row 1, columns 0..1 */
    android_view_set_grid_gravity(span, ANDROID_GRAVITY_FILL_HORIZONTAL | ANDROID_GRAVITY_TOP);
    android_view_add_child(ui, grid, span);
    android_view_t cell = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 3);
    set_wrap(cell);
    android_view_set_text(cell, "C");
    android_view_set_grid_cell(cell, 0, 2, 1, 1); /* row 0, column 2 */
    android_view_set_grid_gravity(cell, ANDROID_GRAVITY_FILL_HORIZONTAL | ANDROID_GRAVITY_TOP);
    android_view_add_child(ui, grid, cell);
    frame_and_layout(ui, grid, 200.f, 300.f);
    rectf ba{}, bs{}, bc{};
    android_view_get_bounds(a, &ba);
    android_view_get_bounds(span, &bs);
    android_view_get_bounds(cell, &bc);
    /* FILL children stretch to their cells: col0 = 17.92 (A), col2 = 182.08
     * (C, trailing excess), B spans the two middle columns below. */
    EXPECT_NEAR(ba.x, 0.0, 0.01);
    EXPECT_NEAR(ba.y, 0.0, 0.01);
    EXPECT_NEAR(bc.x, ba.width, 0.01);
    EXPECT_NEAR(bc.y, 0.0, 0.01);
    EXPECT_NEAR(bs.x, 0.0, 0.01);
    EXPECT_NEAR(bs.y, ba.height, 0.01);
    /* B spans cols 0..1; col1's natural width is zero, so the span cell
     * equals col0's width and the trailing col2 absorbs the excess. */
    EXPECT_NEAR(bs.width, ba.width, 0.01);
    android_ui_destroy(ui);
}

static void test_column_weights() {
    android_ui_t ui = make_ui();
    android_view_t grid = grid_view(ui, 2);
    set_match(grid);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 1);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 2);
    set_wrap(a); set_wrap(b);
    android_view_set_text(a, "A");
    android_view_set_text(b, "B");
    android_view_set_grid_cell(a, 0, 0, 1, 1);
    android_view_set_grid_cell(b, 0, 1, 1, 1);
    android_view_set_grid_weights(a, 0.f, 1.f);
    android_view_set_grid_weights(b, 0.f, 1.f);
    android_view_add_child(ui, grid, a);
    android_view_add_child(ui, grid, b);
    frame_and_layout(ui, grid, 200.f, 300.f);
    rectf ba{}, bb{};
    android_view_get_bounds(a, &ba);
    android_view_get_bounds(b, &bb);
    /* excess split evenly -> ~100px columns; the integer delta search leaves
     * sub-pixel remainder in the last column (AOSP behaves identically with
     * int pixel sizes). */
    EXPECT_NEAR(ba.x, 0.0, 0.01);
    EXPECT_NEAR(ba.width, 100.0, 0.1);
    EXPECT_NEAR(bb.x, 100.0, 0.1);
    EXPECT_NEAR(bb.width, 100.0, 0.1);
    android_ui_destroy(ui);
}

static void test_fill_gravity() {
    android_ui_t ui = make_ui();
    android_view_t grid = grid_view(ui, 2);
    set_match(grid);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 1);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 2);
    set_wrap(a); set_wrap(b);
    android_view_set_text(a, "A");
    android_view_set_text(b, "B");
    android_view_set_grid_cell(a, 0, 0, 1, 1);
    android_view_set_grid_cell(b, 0, 1, 1, 1);
    /* FILL_HORIZONTAL (0x07) on the column spec -> child fills the column */
    android_view_set_grid_gravity(a, ANDROID_GRAVITY_FILL_HORIZONTAL | ANDROID_GRAVITY_TOP);
    android_view_set_grid_gravity(b, ANDROID_GRAVITY_FILL_HORIZONTAL | ANDROID_GRAVITY_TOP);
    android_view_add_child(ui, grid, a);
    android_view_add_child(ui, grid, b);
    frame_and_layout(ui, grid, 200.f, 300.f);
    rectf ba{}, bb{};
    android_view_get_bounds(a, &ba);
    android_view_get_bounds(b, &bb);
    /* FILL children are flexible: no max constraints, so columns keep their
     * natural width and the trailing column absorbs the excess. */
    EXPECT_NEAR(ba.x, 0.0, 0.01);
    EXPECT_NEAR(ba.width, 17.92, 0.01);
    EXPECT_NEAR(bb.x, 17.92, 0.01);
    EXPECT_NEAR(bb.width, 200.0 - 17.92, 0.01);
    android_ui_destroy(ui);
}

static void test_row_weights() {
    android_ui_t ui = make_ui();
    android_view_t grid = make_view(ui, ANDROID_VIEW_GRID_LAYOUT, 100);
    set_match(grid);
    android_view_set_row_count(grid, 2);
    android_view_set_orientation(grid, ANDROID_VERTICAL);
    android_view_t a = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 1);
    android_view_t b = make_view(ui, ANDROID_VIEW_TEXT_VIEW, 2);
    set_wrap(a); set_wrap(b);
    android_view_set_text(a, "A");
    android_view_set_text(b, "B");
    android_view_set_grid_weights(a, 1.f, 0.f);
    android_view_set_grid_weights(b, 1.f, 0.f);
    android_view_add_child(ui, grid, a);
    android_view_add_child(ui, grid, b);
    frame_and_layout(ui, grid, 200.f, 300.f);
    rectf ba{}, bb{};
    android_view_get_bounds(a, &ba);
    android_view_get_bounds(b, &bb);
    /* vertical orientation assigns rows first: A row 0, B row 1; the integer
     * delta search splits 300 into 112/111 pixel deltas (AOSP-identical). */
    EXPECT_NEAR(ba.y, 0.0, 0.01);
    EXPECT_NEAR(ba.height, 150.4, 0.1);
    EXPECT_NEAR(bb.y, 150.4, 0.1);
    EXPECT_NEAR(bb.height, 149.6, 0.1);
    android_ui_destroy(ui);
}

int main() {
    try {
        test_auto_placement();
        test_explicit_cell_and_span();
        test_column_weights();
        test_fill_gravity();
        test_row_weights();
    } catch (const std::exception& e) {
        std::printf("EXC: %s\n", e.what());
        fflush(stdout);
        return 2;
    }
    return test_result();
}
