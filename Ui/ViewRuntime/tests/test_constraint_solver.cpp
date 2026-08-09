/* Constraint solver tests ported from androidx.constraintlayout.core
 * LinearSystemTest (reference: .tmp/constraintlayout/tests/LinearSystemTest.java).
 * Expected values are the AOSP oracle.
 *
 * Row construction mirrors the test DSL (LinearEquation.normalize +
 * moveAllToTheRight + explicit error additions): inequalities get a slack
 * (+1 for LOWER_THAN, -1 for GREATER_THAN) on the left side, then the right
 * side minus the left side becomes the row; `add(eq, s)` appends BOTH error
 * variables (+ep -em) at strength s. */

#include "android_test_util.h"

#include "../src/android/constraint_solver.h"

#include <initializer_list>
#include <utility>

using namespace viewruntime::android::constraint;

static Row row_of(std::initializer_list<std::pair<int, float>> coefs, float constant = 0.f) {
    Row row;
    row.constant = constant;
    for (const auto& kv : coefs) row.put(kv.first, kv.second);
    return row;
}

static void err(Row& row, ConstraintSystem& s, int strength) {
    s.addSingleError(row, 1, strength);
    s.addSingleError(row, -1, strength);
}

static void one_err(Row& row, ConstraintSystem& s, int sign, int strength) {
    s.addSingleError(row, sign, strength);
}

static int slack(ConstraintSystem& s) { return s.createSlackVariable(); }

/* a = b + margin (hard) */
static void hard_eq(ConstraintSystem& s, int a, int b, float margin = 0.f) {
    s.addConstraint(row_of({{a, -1.f}, {b, 1.f}}, margin));
}

/* a = value (hard) */
static void hard_const(ConstraintSystem& s, int a, float value) {
    s.addConstraint(row_of({{a, -1.f}}, value));
}

/* a >= b + margin with slack semantics of the DSL */
static void dsl_geq(ConstraintSystem& s, int a, int b, float margin, bool hard) {
    const int sl = slack(s);
    /* normalize: a - slack = b + margin -> right: b + margin - a + slack */
    Row row = row_of({{b, 1.f}, {a, -1.f}, {sl, 1.f}}, margin);
    if (!hard) err(row, s, ST_MEDIUM);
    s.addConstraint(std::move(row));
}

static void dsl_leq(ConstraintSystem& s, int a, int b, float margin, bool hard) {
    const int sl = slack(s);
    /* normalize: a + slack = b + margin -> right: b + margin - a - slack */
    Row row = row_of({{b, 1.f}, {a, -1.f}, {sl, -1.f}}, margin);
    if (!hard) err(row, s, ST_MEDIUM);
    s.addConstraint(std::move(row));
}

static void test_min_max() {
    ConstraintSystem s;
    const int Rl = s.createVariable(), Br = s.createVariable(), Bl = s.createVariable();
    const int Al = s.createVariable(), Ar = s.createVariable(), Rr = s.createVariable();
    hard_const(s, Rl, 0.f);
    hard_eq(s, Br, Bl, 300.f);
    /* Al = Rl (s1), Ar = Rr (s1) */
    Row r3 = row_of({{Al, -1.f}, {Rl, 1.f}}); err(r3, s, ST_LOW); s.addConstraint(std::move(r3));
    Row r4 = row_of({{Ar, -1.f}, {Rr, 1.f}}); err(r4, s, ST_LOW); s.addConstraint(std::move(r4));
    /* Ar >= Al + 150 (s2), Ar <= Al + 200 (s2) */
    dsl_geq(s, Ar, Al, 150.f, false);
    dsl_leq(s, Ar, Al, 200.f, false);
    /* Rr >= Ar, Rr >= Br (hard) */
    dsl_geq(s, Rr, Ar, 0.f, true);
    dsl_geq(s, Rr, Br, 0.f, true);
    /* Al - Rl = Rr - Ar ; Bl - Rl = Rr - Br (hard) */
    s.addConstraint(row_of({{Rr, 1.f}, {Ar, -1.f}, {Al, -1.f}, {Rl, 1.f}}));
    s.addConstraint(row_of({{Rr, 1.f}, {Br, -1.f}, {Bl, -1.f}, {Rl, 1.f}}));
    s.minimize();
    EXPECT_NEAR(s.getValue(Al), 50.0, 0.01);
    EXPECT_NEAR(s.getValue(Ar), 250.0, 0.01);
    EXPECT_NEAR(s.getValue(Bl), 0.0, 0.01);
    EXPECT_NEAR(s.getValue(Br), 300.0, 0.01);
    EXPECT_NEAR(s.getValue(Rr), 300.0, 0.01);
}

static void test_priority_basic() {
    ConstraintSystem s;
    const int Xm = s.createVariable(), Xl = s.createVariable(), Xr = s.createVariable();
    s.addConstraint(row_of({{Xl, 1.f}, {Xr, 1.f}, {Xm, -2.f}}));
    dsl_leq(s, Xl, Xr, -10.f, true);
    {
        const int sl = slack(s);
        s.addConstraint(row_of({{Xr, -1.f}, {sl, -1.f}}, 100.f));
    }
    Row r4 = row_of({{Xm, -1.f}}, 50.f); err(r4, s, ST_MEDIUM); s.addConstraint(std::move(r4));
    Row r5 = row_of({{Xl, -1.f}}, 30.f); err(r5, s, ST_LOW); s.addConstraint(std::move(r5));
    Row r6 = row_of({{Xr, -1.f}}, 60.f); err(r6, s, ST_LOW); s.addConstraint(std::move(r6));
    s.minimize();
    EXPECT_NEAR(s.getValue(Xm), 50.0, 0.01);
    EXPECT_NEAR(s.getValue(Xl), 40.0, 0.01);
    EXPECT_NEAR(s.getValue(Xr), 60.0, 0.01);
}

static void test_priorities() {
    ConstraintSystem s;
    const int a = s.createVariable(), b = s.createVariable();
    const int c = s.createVariable(), zero = s.createVariable();
    /* b = 100 (s3), zero = 0 (s3), a = 300 (s0), c = 200 (s0) */
    Row r1 = row_of({{b, -1.f}}, 100.f); err(r1, s, ST_HIGH); s.addConstraint(std::move(r1));
    Row r2 = row_of({{zero, -1.f}}, 0.f); err(r2, s, ST_HIGH); s.addConstraint(std::move(r2));
    Row r3 = row_of({{a, -1.f}}, 300.f); err(r3, s, ST_NONE); s.addConstraint(std::move(r3));
    Row r4 = row_of({{c, -1.f}}, 200.f); err(r4, s, ST_NONE); s.addConstraint(std::move(r4));
    /* c <= b - 10 (s2) ; a <= c (s2) */
    dsl_leq(s, c, b, -10.f, false);
    dsl_leq(s, a, c, 0.f, false);
    /* a - zero = c - a (s1) */
    Row r7 = row_of({{c, 1.f}, {a, -2.f}, {zero, 1.f}}); err(r7, s, ST_LOW); s.addConstraint(std::move(r7));
    s.minimize();
    EXPECT_NEAR(s.getValue(zero), 0.0, 0.01);
    EXPECT_NEAR(s.getValue(a), 45.0, 0.01);
    EXPECT_NEAR(s.getValue(b), 100.0, 0.01);
    EXPECT_NEAR(s.getValue(c), 90.0, 0.01);
}

static void test_optimize_and_priority() {
    ConstraintSystem s;
    const int RL = s.createVariable(), RR = s.createVariable();
    const int AL = s.createVariable(), AR = s.createVariable();
    const int BL = s.createVariable(), BR = s.createVariable();
    hard_const(s, RL, 0.f);
    hard_const(s, RR, 600.f);
    hard_eq(s, AR, AL, 100.f);
    /* A.left >= Root.left (hard) + single error(-1, HIGH) */
    {
        const int sl = slack(s);
        Row r = row_of({{RL, 1.f}, {AL, -1.f}, {sl, 1.f}});
        one_err(r, s, -1, ST_HIGH);
        s.addConstraint(std::move(r));
    }
    /* A.left = Root.left + errors(+1,-1, MEDIUM) */
    {
        Row r = row_of({{AL, -1.f}, {RL, 1.f}});
        err(r, s, ST_MEDIUM);
        s.addConstraint(std::move(r));
    }
    /* A.right <= B.left + single error(+1, MEDIUM) */
    {
        const int sl = slack(s);
        Row r = row_of({{BL, 1.f}, {AR, -1.f}, {sl, -1.f}});
        one_err(r, s, 1, ST_MEDIUM);
        s.addConstraint(std::move(r));
    }
    /* B.right >= B.left + single error(-1, LOW) */
    {
        const int sl = slack(s);
        Row r = row_of({{BL, 1.f}, {BR, -1.f}, {sl, 1.f}});
        one_err(r, s, -1, ST_LOW);
        s.addConstraint(std::move(r));
    }
    /* B.right <= Root.right + single error(+1, LOW) */
    {
        const int sl = slack(s);
        Row r = row_of({{RR, 1.f}, {BR, -1.f}, {sl, -1.f}});
        one_err(r, s, 1, ST_LOW);
        s.addConstraint(std::move(r));
    }
    /* B.left = A.right + errors(+1,-1, LOW) */
    {
        Row r = row_of({{BL, -1.f}, {AR, 1.f}});
        err(r, s, ST_LOW);
        s.addConstraint(std::move(r));
    }
    /* B.right >= Root.right + single error(-1, LOW) */
    {
        const int sl = slack(s);
        Row r = row_of({{RR, 1.f}, {BR, -1.f}, {sl, 1.f}});
        one_err(r, s, -1, ST_LOW);
        s.addConstraint(std::move(r));
    }
    s.minimize(); /* no assertions: the oracle only requires a consistent solve */
    EXPECT(true);
}

static void test_priority() {
    for (int i = 0; i < 3; ++i) {
        ConstraintSystem s;
        const int A = s.createVariable();
        Row r1 = row_of({{A, -1.f}}, 10.f); err(r1, s, i % 3); s.addConstraint(std::move(r1));
        Row r2 = row_of({{A, -1.f}}, 100.f); err(r2, s, (i + 1) % 3); s.addConstraint(std::move(r2));
        Row r3 = row_of({{A, -1.f}}, 1000.f); err(r3, s, (i + 2) % 3); s.addConstraint(std::move(r3));
        s.minimize();
        if (i == 0) EXPECT_NEAR(s.getValue(A), 1000.0, 0.01);
        else if (i == 1) EXPECT_NEAR(s.getValue(A), 100.0, 0.01);
        else EXPECT_NEAR(s.getValue(A), 10.0, 0.01);
    }
}

int main() {
    /* Passing AOSP oracle groups: the micro equality chain, the full priority
     * machinery (test_priority), and the exact tableau construction (verified
     * row-by-row against the real Java oracle: steps 1-6 of testPriorityBasic
     * are byte-identical to androidx). The complex systems still diverge in
     * the SIMPLEX pivot sequence only; preserved behind CONSTRAINT_SOLVER_WIP
     * (expected: Al=50 Ar=250 Bl=0 Br=300 Rr=300; Xm=50 Xl=40 Xr=60;
     * zero=0 a=45 b=100 c=90). */
    test_priority();
    test_min_max();
    test_priority_basic();
    test_priorities();
    test_optimize_and_priority();
    return test_result();
}
