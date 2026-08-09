/* Faithful port of androidx.constraintlayout.core.LinearSystem + ArrayRow +
 * PriorityGoalRow (androidx-main), the solver behind ConstraintLayout. */

#include "constraint_solver.h"

#include <algorithm>
#include <cfloat>
#include <cmath>
#include <cstdint>
#include <cstring>

#include <cstdio>

namespace viewruntime::android::constraint {

constexpr float EPSILON = 1e-4f;

ConstraintSystem::ConstraintSystem() {
    m_vars.emplace_back(); /* index 0 unused */
}

ConstraintSystem::~ConstraintSystem() {
    for (Row* row : m_rows) delete row;
}

void ConstraintSystem::reset() {
    for (Row* row : m_rows) delete row;
    m_rows.clear();
    m_vars.clear();
    m_vars.emplace_back();
    m_goal.clear();
    m_already_tested.clear();
}

int ConstraintSystem::createVariable() {
    Variable v;
    v.id = static_cast<int>(m_vars.size());
    v.type = VarType::UNRESTRICTED;
    m_vars.push_back(v);
    return v.id;
}

int ConstraintSystem::createSlackVariable() {
    Variable v;
    v.id = static_cast<int>(m_vars.size());
    v.type = VarType::SLACK;
    m_vars.push_back(v);
    return v.id;
}

int ConstraintSystem::createExtraVariable() {
    return createSlackVariable();
}

int ConstraintSystem::createErrorVariable(int strength) {
    Variable v;
    v.id = static_cast<int>(m_vars.size());
    v.type = VarType::ERROR;
    v.strength = strength;
    if (strength >= 0 && strength < MAX_STRENGTH) {
        v.goal_strength_vector[strength] = 1.f;
    }
    v.in_goal = true;
    m_vars.push_back(v);
    /* goal keeps error variables sorted by id */
    m_goal.push_back(v.id);
    std::sort(m_goal.begin(), m_goal.end());
    return v.id;
}

void ConstraintSystem::addSingleError(Row& row, int sign, int strength) {
    const int error = createErrorVariable(strength);
    row.put(error, static_cast<float>(sign));
}

/* ArrayRow.isNew: a variable is "new" when it does not yet appear in any
 * stored row (AOSP usageInRowCount <= 1, where the count includes the row
 * being added). */
bool ConstraintSystem::isNew(int var) const {
    for (const Row* row : m_rows) {
        if (row && row->has(var)) return false;
    }
    return true;
}

/* ArrayRow.chooseSubjectInVariables: prefer unrestricted (max coefficient,
 * then new), else a negative restricted variable (most negative, then new).
 * AOSP picks the LARGEST unrestricted amount; this ordering drives which
 * variable becomes each row's key and must match for pixel-exact results. */
bool ConstraintSystem::chooseSubject(Row& row) {
    int unrestricted = -1, restricted = -1;
    float unrestricted_amount = 0.f, restricted_amount = 0.f;
    bool unrestricted_new = false, restricted_new = false;

    for (const auto& kv : row.coefs) {
        const int var = kv.first;
        const float amount = kv.second;
        const Variable& v = m_vars[var];
        if (v.type == VarType::UNRESTRICTED) {
            if (unrestricted == -1) {
                unrestricted = var;
                unrestricted_amount = amount;
                unrestricted_new = isNew(var);
            } else if (amount > unrestricted_amount) {
                unrestricted = var;
                unrestricted_amount = amount;
                unrestricted_new = isNew(var);
            } else if (!unrestricted_new && isNew(var)) {
                unrestricted = var;
                unrestricted_amount = amount;
                unrestricted_new = true;
            }
        } else if (unrestricted == -1) {
            if (amount < 0.f) {
                if (restricted == -1) {
                    restricted = var;
                    restricted_amount = amount;
                    restricted_new = isNew(var);
                } else if (amount < restricted_amount) {
                    restricted = var;
                    restricted_amount = amount;
                    restricted_new = isNew(var);
                } else if (!restricted_new && isNew(var)) {
                    restricted = var;
                    restricted_amount = amount;
                    restricted_new = true;
                }
            }
        }
    }

    const int candidate = unrestricted != -1 ? unrestricted : restricted;
    if (candidate == -1) return false; /* needs an extra variable */
    pivot(row, candidate);
    if (row.coefs.empty()) row.is_simple_definition = true;
    return true;
}

/* ArrayRow.pivot: swap the key variable with v. Row invariant afterwards:
 * v = constant + Σ coefs*others. */
void ConstraintSystem::pivot(Row& row, int v) {
    if (row.key_variable != -1) {
        row.put(row.key_variable, -1.f);
        m_vars[row.key_variable].definition_id = -1;
        row.key_variable = -1;
    }
    auto it = std::find_if(row.coefs.begin(), row.coefs.end(), [v](const auto& kv) { return kv.first == v; });
    const float coef = it == row.coefs.end() ? 0.f : it->second;
    if (it != row.coefs.end()) row.coefs.erase(it);
    const float amount = -coef;
    row.key_variable = v;
    if (amount == 1.f) return;
    row.constant /= amount;
    row.divide(amount);
}

/* Substitute every already-defined variable out of a fresh row
 * (ArrayRow.updateFromSystem). AOSP loops until no more substitutions happen:
 * replacing one defined variable can expose another one. */
void ConstraintSystem::updateFromSystem(Row& row) {
    bool done = false;    while (!done) {
        const int num = static_cast<int>(row.coefs.size());
        std::vector<std::pair<int, float>> snapshot(row.coefs.begin(), row.coefs.end());
        for (const auto& kv : snapshot) {
            const int var = kv.first;
            const float coef = kv.second;
            const Variable& v = m_vars[var];
            if (v.definition_id != -1 && v.definition_id < static_cast<int>(m_rows.size())) {
                const Row* def = m_rows[v.definition_id];
                if (def) {
                    for (const auto& dkv : def->coefs) {
                        if (dkv.first == var) continue;
                        row.put(dkv.first, row.get(dkv.first) + dkv.second * coef);
                    }
                    row.constant += def->constant * coef;
                    row.put(var, 0.f);
                }
            } else if (v.is_final) {
                row.constant += v.computed_value * coef;
                row.put(var, 0.f);
            } else if (v.is_synonym) {
                row.constant += m_vars[v.synonym].computed_value * coef + v.synonym_delta * coef;
                row.put(var, 0.f);
            }
        }
        if (static_cast<int>(row.coefs.size()) == num) done = true;
    }
}

/* After a row defining `var` is added, eliminate `var` from every other row
 * (Gaussian elimination), and from the goal when it is a goal variable
 * (PriorityGoalRow.updateFromRow). */
void ConstraintSystem::updateReferencesWithNewDefinition(int var, Row& definition) {
    /* goal substitution: replace `var` in the goal with its definition */
    if (m_vars[var].in_goal) {
        for (const auto& dkv : definition.coefs) {
            const int w = dkv.first;
            const float value = dkv.second;
            Variable& wv = m_vars[w];
            bool nonzero = false;
            for (int k = 0; k < MAX_STRENGTH; ++k) {
                float v = wv.goal_strength_vector[k] + m_vars[var].goal_strength_vector[k] * value;
                if (std::fabs(v) < EPSILON) v = 0.f;
                wv.goal_strength_vector[k] = v;
                if (v != 0.f) nonzero = true;
            }
            if (nonzero && !wv.in_goal) {
                wv.in_goal = true;
                m_goal.push_back(w);
                std::sort(m_goal.begin(), m_goal.end());
            }
        }
        m_vars[var].in_goal = false;
        std::fill(m_vars[var].goal_strength_vector, m_vars[var].goal_strength_vector + MAX_STRENGTH, 0.f);
        m_goal.erase(std::remove(m_goal.begin(), m_goal.end(), var), m_goal.end());
    }
    for (Row* row : m_rows) {
        if (!row || row == &definition) continue;
        auto it = std::find_if(row->coefs.begin(), row->coefs.end(), [var](const auto& kv) { return kv.first == var; });
        if (it == row->coefs.end()) continue;
        const float coef = it->second;
        row->coefs.erase(it);
        for (const auto& dkv : definition.coefs) {
            if (dkv.first == var) continue;
            row->put(dkv.first, row->get(dkv.first) + dkv.second * coef);
        }
        row->constant += definition.constant * coef;
        if (row->coefs.empty()) {
            row->is_simple_definition = true;
        }
    }
    m_vars[var].usage_in_row_count = 1; /* present only in its own row */
    for (const auto& dkv : definition.coefs) {
        m_vars[dkv.first].usage_in_row_count++;
    }
}

void ConstraintSystem::setFinalValue(int var, float value) {
    /* Remove a variable from the goal (AOSP updateFromFinalVariable). */
    auto remove_from_goal = [this](int id) {
        Variable& gv = m_vars[id];
        gv.in_goal = false;
        std::fill(gv.goal_strength_vector, gv.goal_strength_vector + MAX_STRENGTH, 0.f);
        m_goal.erase(std::remove(m_goal.begin(), m_goal.end(), id), m_goal.end());
    };

    /* Work queue of (var, value) to finalize. Iterative — never recursive:
     * a recursive cascade mutates m_rows while an outer loop holds stale
     * indices, which can delete the same row twice and corrupt the heap. */
    std::vector<int> pending;
    std::vector<float> pending_values;
    pending.push_back(var);
    pending_values.push_back(value);

    while (!pending.empty()) {
        const int v = pending.back();
        pending.pop_back();
        const float val = pending_values.back();
        pending_values.pop_back();

        Variable& vv = m_vars[v];
        if (vv.in_goal) {
            remove_from_goal(v);
        }
        vv.computed_value = val;
        vv.is_final = true;
        vv.definition_id = -1;

        /* The fast paths (addEquality with a final RHS) set the value directly
         * and skip the row machinery; a value finalized after other rows were
         * stored must be substituted into those rows retroactively. AOSP never
         * skips the row machinery — every equality is a row, and
         * updateFromSystem resolves finals at insert time — so this loop
         * restores that invariant. */
        for (Row* row : m_rows) {
            if (!row || row->key_variable == v) continue;
            auto it = std::find_if(row->coefs.begin(), row->coefs.end(),
                [v](const auto& kv) { return kv.first == v; });
            if (it == row->coefs.end()) continue;
            const float coef = it->second;
            row->coefs.erase(it);
            row->constant += val * coef;
            if (row->coefs.empty() && row->key_variable != -1) {
                row->is_simple_definition = true;
            }
        }

        /* Collect rows that just became simple definitions: mark their keys
         * final immediately (dedupe) and queue them for propagation. Erase in
         * place keeps the index valid. */
        for (size_t i = 0; i < m_rows.size();) {
            Row* r = m_rows[i];
            if (r->coefs.empty() && r->key_variable != -1 &&
                !m_vars[r->key_variable].is_final) {
                const int key = r->key_variable;
                const float constant = r->constant;
                Variable& kv = m_vars[key];
                if (kv.in_goal) {
                    remove_from_goal(key);
                }
                kv.computed_value = constant;
                kv.is_final = true;
                kv.definition_id = -1;
                pending.push_back(key);
                pending_values.push_back(constant);
                delete r;
                m_rows.erase(m_rows.begin() + static_cast<long>(i));
            } else {
                ++i;
            }
        }
    }
}

void ConstraintSystem::addConstraint(Row row) {
    bool added = false;
    if (!row.is_simple_definition) {
        updateFromSystem(row);
        if (row.is_empty()) return;

        if (row.constant < 0.f) {
            row.constant = -row.constant;
            row.invert();
        }

        if (!chooseSubject(row)) {
            /* no candidate: add an extra variable and try to eliminate it */
            const int extra = createExtraVariable();
            row.key_variable = extra;
            m_vars[extra].definition_id = -1;
            const int num_rows = static_cast<int>(m_rows.size());
            addRow(std::move(row));
            if (static_cast<int>(m_rows.size()) == num_rows + 1) {
                added = true;
                Row* stored = m_rows.back();
                /* mTempGoal.initFromRow(row); optimize(mTempGoal, true); */
                Row goal;
                for (const auto& kv : stored->coefs) {
                    const Variable& v = m_vars[kv.first];
                    for (int k = 0; k < MAX_STRENGTH; ++k) {
                        if (v.goal_strength_vector[k] != 0.f) {
                            goal.put(kv.first, goal.get(kv.first) + v.goal_strength_vector[k] * kv.second);
                        }
                    }
                }
                optimize(goal);
                if (m_vars[extra].definition_id == -1) {
                    /* the extra got eliminated: pivot it out if possible */
                    Row* r = nullptr;
                    for (Row* candidate : m_rows) {
                        if (candidate && candidate->key_variable == extra) { r = candidate; break; }
                    }
                    if (r) {
                        int pivot_candidate = -1;
                        float min_amount = FLT_MAX;
                        for (const auto& kv : r->coefs) {
                            const float amount = kv.second;
                            const Variable& v = m_vars[kv.first];
                            if (v.type != VarType::UNRESTRICTED && amount < min_amount) {
                                min_amount = amount;
                                pivot_candidate = kv.first;
                            }
                        }
                        if (pivot_candidate != -1) pivot(*r, pivot_candidate);
                        if (!r->is_simple_definition) {
                            m_vars[r->key_variable].definition_id = -1;
                            m_vars[r->key_variable].usage_in_row_count = 0;
                            m_rows.erase(std::remove(m_rows.begin(), m_rows.end(), r), m_rows.end());
                            delete r;
                        }
                    }
                }
            }
        }

        if (row.key_variable == -1) {
            return; /* resolved to nil */
        }
    }
    if (!added) {
        addRow(std::move(row));
    }
}

void ConstraintSystem::addRow(Row&& row) {
    if (row.is_simple_definition && row.key_variable != -1) {
        /* simple definition: resolve immediately (SIMPLIFY_SYNONYMS) */
        setFinalValue(row.key_variable, row.constant);
        return;
    }
    auto* stored = new Row(std::move(row));
    m_rows.push_back(stored);
    if (stored->key_variable != -1) {
        m_vars[stored->key_variable].definition_id = static_cast<int>(m_rows.size()) - 1;
        updateReferencesWithNewDefinition(stored->key_variable, *stored);
    }
    /* compact simple-definition rows left behind by substitution */
    for (size_t i = 0; i < m_rows.size();) {
        Row* r = m_rows[i];
        if (r->coefs.empty() && r->key_variable != -1) {
            setFinalValue(r->key_variable, r->constant);
            delete r;
            m_rows.erase(m_rows.begin() + static_cast<long>(i));
            for (size_t j = i; j < m_rows.size(); ++j) {
                if (m_rows[j]->key_variable != -1) {
                    m_vars[m_rows[j]->key_variable].definition_id = static_cast<int>(j);
                }
            }
        } else {
            ++i;
        }
    }
}

void ConstraintSystem::addEquality(int a, int b, float margin, int strength) {
    Variable& va = m_vars[a];
    if (strength == ST_FIXED && m_vars[b].is_final && va.definition_id == -1) {
        setFinalValue(a, m_vars[b].computed_value + margin);
        return;
    }
    Row row;
    /* createRowEquals(a, b, margin): a = b + margin */
    if (margin != 0.f) {
        const float m = std::fabs(margin);
        row.constant = m;
        if (margin < 0.f) {
            row.put(a, 1.f);
            row.put(b, -1.f);
        } else {
            row.put(a, -1.f);
            row.put(b, 1.f);
        }
    } else {
        row.put(a, -1.f);
        row.put(b, 1.f);
    }
    if (strength != ST_FIXED) {
        addSingleError(row, 1, strength);
        addSingleError(row, -1, strength);
    }
    addConstraint(std::move(row));
}

void ConstraintSystem::addEquality(int a, float value) {
    Variable& va = m_vars[a];
    if (va.definition_id == -1) {
        setFinalValue(a, value);
        return;
    }
    Row row;
    if (value < 0.f) {
        row.constant = -value;
        row.put(a, 1.f);
    } else {
        row.constant = value;
        row.put(a, -1.f);
    }
    addConstraint(std::move(row));
}

void ConstraintSystem::addGreaterThan(int a, int b, float margin, int strength) {
    Row row;
    const int slack = createSlackVariable();
    m_vars[slack].strength = 0;
    /* createRowGreaterThan(a, b, slack, margin): a >= b + margin */
    if (margin != 0.f) {
        const float m = std::fabs(margin);
        row.constant = m;
        if (margin < 0.f) {
            row.put(a, 1.f);
            row.put(b, -1.f);
            row.put(slack, -1.f);
        } else {
            row.put(a, -1.f);
            row.put(b, 1.f);
            row.put(slack, 1.f);
        }
    } else {
        row.put(a, -1.f);
        row.put(b, 1.f);
        row.put(slack, 1.f);
    }
    if (strength != ST_FIXED) {
        const float slack_value = row.get(slack);
        addSingleError(row, static_cast<int>(-1.f * slack_value), strength);
    }
    addConstraint(std::move(row));
}

void ConstraintSystem::addLowerThan(int a, int b, float margin, int strength) {
    Row row;
    const int slack = createSlackVariable();
    m_vars[slack].strength = 0;
    /* createRowLowerThan(a, b, slack, margin): a <= b + margin */
    if (margin != 0.f) {
        const float m = std::fabs(margin);
        row.constant = m;
        if (margin < 0.f) {
            row.put(a, 1.f);
            row.put(b, -1.f);
            row.put(slack, 1.f);
        } else {
            row.put(a, -1.f);
            row.put(b, 1.f);
            row.put(slack, -1.f);
        }
    } else {
        row.put(a, -1.f);
        row.put(b, 1.f);
        row.put(slack, -1.f);
    }
    if (strength != ST_FIXED) {
        const float slack_value = row.get(slack);
        addSingleError(row, static_cast<int>(-1.f * slack_value), strength);
    }
    addConstraint(std::move(row));
}

void ConstraintSystem::addCentering(int a, int b, float m1, float bias,
                                    int c, int d, float m2, int strength) {
    Row row;
    /* ArrayRow.createRowCentering */
    if (b == c) {
        /* B - A == D - B: 0 = A + D - 2*B */
        row.put(a, 1.f);
        row.put(d, 1.f);
        row.put(b, -2.f);
    } else if (bias == 0.5f) {
        row.put(a, 1.f);
        row.put(b, -1.f);
        row.put(c, -1.f);
        row.put(d, 1.f);
        if (m1 > 0.f || m2 > 0.f) row.constant = -m1 + m2;
    } else if (bias <= 0.f) {
        row.put(a, -1.f);
        row.put(b, 1.f);
        row.constant = m1;
    } else if (bias >= 1.f) {
        row.put(d, -1.f);
        row.put(c, 1.f);
        row.constant = -m2;
    } else {
        row.put(a, 1.f * (1.f - bias));
        row.put(b, -1.f * (1.f - bias));
        row.put(c, -1.f * bias);
        row.put(d, 1.f * bias);
        if (m1 > 0.f || m2 > 0.f) {
            row.constant = -m1 * (1.f - bias) + m2 * bias;
        }
    }
    if (strength != ST_FIXED) {
        addSingleError(row, 1, strength);
        addSingleError(row, -1, strength);
    }
    addConstraint(std::move(row));
}

void ConstraintSystem::addRatio(int a, int b, int c, int d, float ratio, int strength) {
    Row row;
    /* createRowDimensionRatio: a = b + (c-d)*ratio */
    row.put(a, -1.f);
    row.put(b, 1.f);
    row.put(c, ratio);
    row.put(d, -ratio);
    if (strength != ST_FIXED) {
        addSingleError(row, 1, strength);
        addSingleError(row, -1, strength);
    }
    addConstraint(std::move(row));
}

float ConstraintSystem::getValue(int variable_id) const {
    if (variable_id < 0 || variable_id >= static_cast<int>(m_vars.size())) return 0.f;
    const Variable& v = m_vars[variable_id];
    if (v.is_final) return v.computed_value;
    if (v.is_synonym) return m_vars[v.synonym].computed_value + v.synonym_delta;
    return v.computed_value;
}

void ConstraintSystem::dump() const {
    for (size_t i = 0; i < m_rows.size(); ++i) {
        const Row* r = m_rows[i];
        if (!r) { std::printf("row %zu: null\n", i); continue; }
        std::printf("row %zu: v%d = %.3f", i, r->key_variable, r->constant);
        for (const auto& kv : r->coefs) std::printf(" %+.3f*v%d", kv.second, kv.first);
        std::printf("\n");
    }
    for (size_t v = 1; v < m_vars.size(); ++v) {
        const Variable& var = m_vars[v];
        if (var.is_final) std::printf("final v%zu = %.3f\n", v, var.computed_value);
    }
    std::printf("goal: ");
    for (const int g : m_goal) {
        std::printf("v%d[", g);
        for (int k = MAX_STRENGTH - 1; k >= 0; --k) std::printf("%.0f", m_vars[g].goal_strength_vector[k]);
        std::printf("] ");
    }
    std::printf("\n");
}

/* ── Goal / Simplex ────────────────────────────────────────────────── */

bool ConstraintSystem::goalIsNegative(const Variable& v) const {
    for (int i = MAX_STRENGTH - 1; i >= 0; --i) {
        const float value = v.goal_strength_vector[i];
        if (value > 0.f) return false;
        if (value < 0.f) return true;
    }
    return false;
}

bool ConstraintSystem::goalIsSmallerThan(const Variable& a, const Variable& b) const {
    for (int i = MAX_STRENGTH - 1; i >= 0; --i) {
        const float value = a.goal_strength_vector[i];
        const float compared = b.goal_strength_vector[i];
        if (value == compared) continue;
        return value < compared;
    }
    return false;
}

int ConstraintSystem::goalGetPivotCandidate(const std::vector<bool>& avoid) const {
    int pivot = -1;
    for (size_t i = 0; i < m_goal.size(); ++i) {
        const Variable& variable = m_vars[m_goal[i]];
        if (variable.id >= 0 && variable.id < static_cast<int>(avoid.size()) && avoid[variable.id]) {
            continue;
        }
        if (pivot == -1) {
            if (goalIsNegative(variable)) pivot = static_cast<int>(i);
        } else if (goalIsSmallerThan(variable, m_vars[m_goal[pivot]])) {
            pivot = static_cast<int>(i);
        }
    }
    return pivot == -1 ? -1 : m_goal[pivot];
}

/* LinearSystem.optimize: Simplex iteration over the goal. */
void ConstraintSystem::optimize(Row& goal) {
    (void)goal; /* the goal lives in m_goal; the parameter is vestigial */
    m_already_tested.assign(m_vars.size(), false);
    bool done = false;
    int tries = 0;
    while (!done) {
        ++tries;
        if (tries >= 2 * static_cast<int>(m_vars.size())) return;

        const int pivot_candidate = goalGetPivotCandidate(m_already_tested);
        if (pivot_candidate == -1) {
            done = true;
            continue;
        }
        if (pivot_candidate < static_cast<int>(m_already_tested.size())) {
            if (m_already_tested[pivot_candidate]) return;
            m_already_tested[pivot_candidate] = true;
        }

        float min = FLT_MAX;
        int pivot_row = -1;
        for (int i = 0; i < static_cast<int>(m_rows.size()); ++i) {
            Row* current = m_rows[i];
            if (!current || current->is_simple_definition) continue;
            if (m_vars[current->key_variable].type == VarType::UNRESTRICTED) continue;
            if (!current->has(pivot_candidate)) continue;
            const float a_j = current->get(pivot_candidate);
            if (a_j < 0.f) {
                const float value = -current->constant / a_j;
                if (value < min) {
                    min = value;
                    pivot_row = i;
                }
            }
        }
        if (pivot_row > -1) {
            Row* pivot_equation = m_rows[pivot_row];
            m_vars[pivot_equation->key_variable].definition_id = -1;
            pivot(*pivot_equation, pivot_candidate);
            m_vars[pivot_equation->key_variable].definition_id = pivot_row;
            updateReferencesWithNewDefinition(pivot_equation->key_variable, *pivot_equation);
        }
    }
}

/* LinearSystem.enforceBFS: pivot restricted rows with negative constants. */
void ConstraintSystem::enforceBFS() {
    bool infeasible = false;
    for (const Row* row : m_rows) {
        if (!row) continue;
        if (m_vars[row->key_variable].type == VarType::UNRESTRICTED) continue;
        if (row->constant < 0.f) { infeasible = true; break; }
    }
    if (!infeasible) return;

    bool done = false;
    int tries = 0;
    while (!done) {
        ++tries;
        float min = FLT_MAX;
        int strength = 0;
        int pivot_row = -1;
        int pivot_column = -1;

        for (int i = 0; i < static_cast<int>(m_rows.size()); ++i) {
            Row* current = m_rows[i];
            if (!current || current->is_simple_definition) continue;
            if (m_vars[current->key_variable].type == VarType::UNRESTRICTED) continue;
            if (current->constant < 0.f) {
                for (const auto& kv : current->coefs) {
                    const float a_j = kv.second;
                    if (a_j <= 0.f) continue;
                    const Variable& candidate = m_vars[kv.first];
                    for (int k = 0; k < MAX_STRENGTH; ++k) {
                        const float value = candidate.strength_vector[k] / a_j;
                        if ((value < min && k == strength) || k > strength) {
                            min = value;
                            pivot_row = i;
                            pivot_column = kv.first;
                            strength = k;
                        }
                    }
                }
            }
        }

        if (pivot_row != -1) {
            Row* pivot_equation = m_rows[pivot_row];
            m_vars[pivot_equation->key_variable].definition_id = -1;
            pivot(*pivot_equation, pivot_column);
            m_vars[pivot_equation->key_variable].definition_id = pivot_row;
            updateReferencesWithNewDefinition(pivot_equation->key_variable, *pivot_equation);
        } else {
            done = true;
        }
        if (tries > static_cast<int>(m_vars.size()) / 2) done = true;
    }
}

void ConstraintSystem::computeValues() {
    for (const Row* row : m_rows) {
        if (!row || row->key_variable == -1) continue;
        m_vars[row->key_variable].computed_value = row->constant;
        m_vars[row->key_variable].is_final = true;
    }
}

void ConstraintSystem::minimize() {
    if (m_goal.empty()) {
        computeValues();
        return;
    }
    bool fully_solved = true;
    for (const Row* row : m_rows) {
        if (!row || !row->is_simple_definition) { fully_solved = false; break; }
    }
    if (!fully_solved) {
        enforceBFS();
        Row goal;
        for (const int var : m_goal) {
            for (int k = 0; k < MAX_STRENGTH; ++k) {
                if (m_vars[var].goal_strength_vector[k] != 0.f) {
                    goal.put(var, goal.get(var) + m_vars[var].goal_strength_vector[k]);
                }
            }
        }
        optimize(goal);
    }
    computeValues();
}

} // namespace viewruntime::android::constraint
