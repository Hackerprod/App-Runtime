#pragma once
/* Faithful port of androidx.constraintlayout.core.LinearSystem (androidx-main),
 * the solver behind ConstraintLayout. Row/strength conventions verified
 * against the reference sources in .tmp/constraintlayout/.
 *
 * Row invariant: a stored row is `key_variable = constant + Σ coefs*others`
 * with parametric (non-basic) variables at 0 in BFS form. Strengths index the
 * goal strength vectors directly (0..8). */

#include <unordered_map>
#include <vector>

namespace viewruntime::android::constraint {

enum : int {
    ST_NONE = 0,
    ST_LOW = 1,
    ST_MEDIUM = 2,
    ST_HIGH = 3,
    ST_HIGHEST = 4,
    ST_EQUALITY = 5,
    ST_BARRIER = 6,
    ST_CENTERING = 7,
    ST_FIXED = 8
};
constexpr int MAX_STRENGTH = 9;

enum class VarType : int { UNRESTRICTED, SLACK, ERROR, UNKNOWN };

struct Variable {
    int id = 0;
    VarType type = VarType::UNKNOWN;
    float computed_value = 0.f;
    bool is_final = false;
    int definition_id = -1;      /* row index defining this variable */
    bool is_synonym = false;
    int synonym = 0;
    float synonym_delta = 0.f;
    int usage_in_row_count = 0;
    int strength = 0;
    bool in_goal = false;
    float strength_vector[MAX_STRENGTH] = {};
    float goal_strength_vector[MAX_STRENGTH] = {};
};

/* Sparse linear row. The tableau stores rows as key = constant + Σ coefs*others.
 * Coefficients are kept SORTED BY VARIABLE ID — ArrayLinkedVariables maintains
 * a sorted linked list (put() inserts at the sorted position), and every
 * iteration order (chooseSubject, substitution, pivoting) depends on it for
 * pixel-exact tie-breaking. */
struct Row {
    std::vector<std::pair<int, float>> coefs;
    float constant = 0.f;
    int key_variable = -1;
    bool is_simple_definition = false;

    float get(int v) const {
        for (const auto& kv : coefs) if (kv.first == v) return kv.second;
        return 0.f;
    }
    void put(int v, float c) {
        if (c == 0.f) {
            for (auto it = coefs.begin(); it != coefs.end(); ++it) {
                if (it->first == v) { coefs.erase(it); return; }
            }
            return;
        }
        for (auto it = coefs.begin(); it != coefs.end(); ++it) {
            if (it->first == v) { it->second = c; return; }
            if (it->first > v) { coefs.insert(it, {v, c}); return; }
        }
        coefs.emplace_back(v, c);
    }
    bool has(int v) const { return get(v) != 0.f; }
    bool is_empty() const {
        return key_variable == -1 && constant == 0.f && coefs.empty();
    }
    /* AOSP ArrayLinkedVariables.invert: flips only the coefficients; the
     * caller negates the constant separately. */
    void invert() {
        for (auto& kv : coefs) kv.second = -kv.second;
    }
    /* AOSP ArrayLinkedVariables.divideByAmount: divides only the coefficients;
     * the pivot divides the constant separately. */
    void divide(float amount) {
        for (auto& kv : coefs) kv.second /= amount;
    }
};

class ConstraintSystem {
public:
    ConstraintSystem();
    ~ConstraintSystem();

    int createVariable();
    int createSlackVariable();
    int createExtraVariable();
    int createErrorVariable(int strength);
    void addSingleError(Row& row, int sign, int strength);

    /* a = b + margin (strength ST_FIXED -> hard; otherwise soft w/ error) */
    void addEquality(int a, int b, float margin, int strength);
    void addEquality(int a, float value);
    /* a >= b + margin */
    void addGreaterThan(int a, int b, float margin, int strength);
    /* a <= b + margin */
    void addLowerThan(int a, int b, float margin, int strength);
    /* (1-bias)*(a-b-m1) = bias*(c-d-m2) */
    void addCentering(int a, int b, float m1, float bias, int c, int d, float m2, int strength);
    /* a = b + (c-d)*ratio */
    void addRatio(int a, int b, int c, int d, float ratio, int strength);

    /* Add a raw row (Σ coefs*vars + constant = 0). The row is consumed. */
    void addConstraint(Row row);

    void minimize();
    float getValue(int variable_id) const;
    int variableCount() const { return static_cast<int>(m_vars.size()) - 1; }
    void reset();
    void dump() const; /* debug */

private:
    void addRow(Row&& row);
    bool chooseSubject(Row& row);
    bool isNew(int var) const;
    void pivot(Row& row, int v);
    void updateFromSystem(Row& row);
    void updateReferencesWithNewDefinition(int var, Row& definition);
    void setFinalValue(int var, float value);
    void computeValues();
    void optimize(Row& goal);
    void enforceBFS();
    int goalGetPivotCandidate(const std::vector<bool>& avoid) const;
    bool goalIsNegative(const Variable& v) const;
    bool goalIsSmallerThan(const Variable& a, const Variable& b) const;

    std::vector<Variable> m_vars;      /* index == id; [0] unused */
    std::vector<Row*> m_rows;          /* tableau */
    std::vector<bool> m_already_tested;
    std::vector<int> m_goal;           /* error variables in the goal, sorted by id */
    Row m_temp_goal;
};

} // namespace viewruntime::android::constraint
