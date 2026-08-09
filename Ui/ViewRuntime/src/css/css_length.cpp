#include <viewruntime/viewruntime.h>
#include <cmath>
#include <cstring>
#include <cstdlib>
#include <cctype>
#include <string>
#include <string_view>

namespace {

enum class calc_value_kind_e { number, length };

struct calc_value_s {
    calc_value_kind_e kind = calc_value_kind_e::number;
    float number = 0.f;
    css_length length = css_length_zero();
};

static bool is_ascii_alpha(char value) {
    return (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z');
}

static bool ascii_iequals(std::string_view left, std::string_view right) {
    if (left.size() != right.size()) return false;
    for (size_t i = 0; i < left.size(); ++i) {
        const auto lower = [](char value) {
            return value >= 'A' && value <= 'Z' ? static_cast<char>(value - 'A' + 'a') : value;
        };
        if (lower(left[i]) != lower(right[i])) return false;
    }
    return true;
}

static css_length calc_linear_length(css_length value) {
    if (value.unit == CSS_UNIT_LINEAR) return value;

    css_length linear = css_length_zero();
    linear.unit = CSS_UNIT_LINEAR;
    switch (value.unit) {
        case CSS_UNIT_PX:      linear.value = value.value; break;
        case CSS_UNIT_PERCENT: linear.linear_percent = value.value; break;
        case CSS_UNIT_EM:      linear.linear_em = value.value; break;
        case CSS_UNIT_REM:     linear.linear_rem = value.value; break;
        case CSS_UNIT_VW:      linear.linear_vw = value.value; break;
        case CSS_UNIT_VH:      linear.linear_vh = value.value; break;
        default: break;
    }
    return linear;
}

static calc_value_s calc_length(css_length value) {
    return {calc_value_kind_e::length, 0.f, calc_linear_length(value)};
}

static bool calc_add(calc_value_s& left, const calc_value_s& right, float sign) {
    if (left.kind == calc_value_kind_e::number && right.kind == calc_value_kind_e::number) {
        left.number += sign * right.number;
        return std::isfinite(left.number);
    }
    // CSS permits a unitless zero wherever a length is expected. Preserve that
    // compatibility without treating arbitrary numbers as lengths.
    if (left.kind == calc_value_kind_e::number) {
        if (left.number != 0.f) return false;
        left = calc_length(css_length_zero());
    }
    if (right.kind == calc_value_kind_e::number) {
        return right.number == 0.f;
    }
    left.length.value += sign * right.length.value;
    left.length.linear_percent += sign * right.length.linear_percent;
    left.length.linear_em += sign * right.length.linear_em;
    left.length.linear_rem += sign * right.length.linear_rem;
    left.length.linear_vw += sign * right.length.linear_vw;
    left.length.linear_vh += sign * right.length.linear_vh;
    return std::isfinite(left.length.value) && std::isfinite(left.length.linear_percent) &&
           std::isfinite(left.length.linear_em) && std::isfinite(left.length.linear_rem) &&
           std::isfinite(left.length.linear_vw) && std::isfinite(left.length.linear_vh);
}

static bool calc_multiply(calc_value_s& left, const calc_value_s& right) {
    if (left.kind == calc_value_kind_e::length && right.kind == calc_value_kind_e::length) return false;
    if (left.kind == calc_value_kind_e::number && right.kind == calc_value_kind_e::number) {
        left.number *= right.number;
        return std::isfinite(left.number);
    }
    const float scale = left.kind == calc_value_kind_e::number ? left.number : right.number;
    const css_length length = left.kind == calc_value_kind_e::length ? left.length : right.length;
    left = calc_length(length);
    left.length.value *= scale;
    left.length.linear_percent *= scale;
    left.length.linear_em *= scale;
    left.length.linear_rem *= scale;
    left.length.linear_vw *= scale;
    left.length.linear_vh *= scale;
    return std::isfinite(left.length.value) && std::isfinite(left.length.linear_percent) &&
           std::isfinite(left.length.linear_em) && std::isfinite(left.length.linear_rem) &&
           std::isfinite(left.length.linear_vw) && std::isfinite(left.length.linear_vh);
}

static bool calc_divide(calc_value_s& left, const calc_value_s& right) {
    if (right.kind != calc_value_kind_e::number || right.number == 0.f || !std::isfinite(right.number)) return false;
    if (left.kind == calc_value_kind_e::number) {
        left.number /= right.number;
        return std::isfinite(left.number);
    }
    const float reciprocal = 1.f / right.number;
    left.length.value *= reciprocal;
    left.length.linear_percent *= reciprocal;
    left.length.linear_em *= reciprocal;
    left.length.linear_rem *= reciprocal;
    left.length.linear_vw *= reciprocal;
    left.length.linear_vh *= reciprocal;
    return std::isfinite(left.length.value) && std::isfinite(left.length.linear_percent) &&
           std::isfinite(left.length.linear_em) && std::isfinite(left.length.linear_rem) &&
           std::isfinite(left.length.linear_vw) && std::isfinite(left.length.linear_vh);
}

class calc_parser_s {
public:
    explicit calc_parser_s(std::string_view source) : source_(source) {}

    bool parse(css_length& output) {
        skip_whitespace();
        if (!consume_identifier("calc") || !consume('(')) return false;
        calc_value_s value{};
        if (!parse_sum(value)) return false;
        skip_whitespace();
        if (!consume(')')) return false;
        skip_whitespace();
        if (position_ != source_.size()) return false;
        if (value.kind == calc_value_kind_e::number) {
            if (value.number != 0.f) return false;
            output = css_length_zero();
        } else {
            output = value.length;
        }
        return true;
    }

private:
    bool parse_sum(calc_value_s& output) {
        if (!parse_product(output)) return false;
        for (;;) {
            const size_t before_whitespace = position_;
            skip_whitespace();
            const bool had_leading_whitespace = position_ != before_whitespace;
            if (position_ == source_.size() || (source_[position_] != '+' && source_[position_] != '-')) return true;
            const char operation = source_[position_++];
            const size_t after_operator = position_;
            skip_whitespace();
            // Binary + and - require whitespace on both sides in CSS calc().
            if (!had_leading_whitespace || position_ == after_operator) return false;
            calc_value_s right{};
            if (!parse_product(right) || !calc_add(output, right, operation == '+' ? 1.f : -1.f)) return false;
        }
    }

    bool parse_product(calc_value_s& output) {
        if (!parse_unary(output)) return false;
        for (;;) {
            const size_t before_whitespace = position_;
            skip_whitespace();
            if (position_ == source_.size() || (source_[position_] != '*' && source_[position_] != '/')) {
                // Addition/subtraction owns its preceding whitespace. Retain it
                // so parse_sum can enforce the CSS tokenization rule.
                position_ = before_whitespace;
                return true;
            }
            const char operation = source_[position_++];
            calc_value_s right{};
            if (!parse_unary(right)) return false;
            if (operation == '*' ? !calc_multiply(output, right) : !calc_divide(output, right)) return false;
        }
    }

    bool parse_unary(calc_value_s& output) {
        skip_whitespace();
        float sign = 1.f;
        if (consume('+')) sign = 1.f;
        else if (consume('-')) sign = -1.f;
        if (!parse_primary(output)) return false;
        if (output.kind == calc_value_kind_e::number) output.number *= sign;
        else {
            output.length.value *= sign;
            output.length.linear_percent *= sign;
            output.length.linear_em *= sign;
            output.length.linear_rem *= sign;
            output.length.linear_vw *= sign;
            output.length.linear_vh *= sign;
        }
        return true;
    }

    bool parse_primary(calc_value_s& output) {
        skip_whitespace();
        if (consume('(')) {
            if (!parse_sum(output)) return false;
            skip_whitespace();
            return consume(')');
        }

        const char* begin = source_.data() + position_;
        char* end = nullptr;
        const float number = std::strtof(begin, &end);
        if (end == begin || !std::isfinite(number)) return false;
        position_ += static_cast<size_t>(end - begin);

        const size_t unit_begin = position_;
        if (position_ < source_.size() && source_[position_] == '%') ++position_;
        else while (position_ < source_.size() && is_ascii_alpha(source_[position_])) ++position_;
        const auto unit = source_.substr(unit_begin, position_ - unit_begin);
        if (unit.empty()) {
            output = {calc_value_kind_e::number, number, css_length_zero()};
            return true;
        }

        css_length length = css_length_zero();
        length.value = number;
        if (unit == "px") length.unit = CSS_UNIT_PX;
        else if (unit == "%") length.unit = CSS_UNIT_PERCENT;
        else if (ascii_iequals(unit, "em")) length.unit = CSS_UNIT_EM;
        else if (ascii_iequals(unit, "rem")) length.unit = CSS_UNIT_REM;
        else if (ascii_iequals(unit, "vw")) length.unit = CSS_UNIT_VW;
        else if (ascii_iequals(unit, "vh")) length.unit = CSS_UNIT_VH;
        else return false;
        output = calc_length(length);
        return true;
    }

    bool consume(char expected) {
        if (position_ >= source_.size() || source_[position_] != expected) return false;
        ++position_;
        return true;
    }

    bool consume_identifier(std::string_view expected) {
        if (source_.substr(position_, expected.size()).size() != expected.size() ||
            !ascii_iequals(source_.substr(position_, expected.size()), expected)) return false;
        position_ += expected.size();
        return true;
    }

    void skip_whitespace() {
        while (position_ < source_.size() && std::isspace(static_cast<unsigned char>(source_[position_]))) ++position_;
    }

    std::string_view source_;
    size_t position_ = 0;
};

} // namespace

API css_length css_length_auto(void) {
    css_length l{};
    l.unit = CSS_UNIT_AUTO;
    return l;
}

API css_length css_length_zero(void) {
    css_length l{};
    l.unit = CSS_UNIT_PX;
    l.value = 0;
    return l;
}

API bool_t css_length_is_auto(css_length l) {
    return l.unit == CSS_UNIT_AUTO ? TRUE : FALSE;
}

API float css_length_resolve(css_length l, float reference,
    float font_size, float root_font_size, float viewport_width, float viewport_height) {
    switch (l.unit) {
        case CSS_UNIT_AUTO:    return NAN;
        case CSS_UNIT_PX:      return l.value;
        case CSS_UNIT_PERCENT: return reference * l.value / 100.0f;
        case CSS_UNIT_EM:      return font_size * l.value;
        case CSS_UNIT_REM:     return root_font_size * l.value;
        case CSS_UNIT_VW:      return viewport_width * l.value / 100.0f;
        case CSS_UNIT_VH:      return viewport_height * l.value / 100.0f;
        case CSS_UNIT_LINEAR:
            return l.value
                + (reference * l.linear_percent / 100.0f)
                + (font_size * l.linear_em)
                + (root_font_size * l.linear_rem)
                + (viewport_width * l.linear_vw / 100.0f)
                + (viewport_height * l.linear_vh / 100.0f);
    }
    return l.value;
}

API bool_t css_length_try_parse(const char* input, css_length* out) {
    if (!input || !out) return FALSE;
    *out = css_length_auto();

    std::string source(input);
    const auto first = source.find_first_not_of(" \t\r\n\f");
    if (first == std::string::npos) return FALSE;
    const auto last = source.find_last_not_of(" \t\r\n\f");
    source = source.substr(first, last - first + 1);
    const char* s = source.c_str();

    if (calc_parser_s parser(source); parser.parse(*out)) return TRUE;
    if (source.size() >= 5 && ascii_iequals(std::string_view(source).substr(0, 5), "calc(")) return FALSE;

    if (s[0] == 'a' && s[1] == 'u' && s[2] == 't' && s[3] == 'o' && s[4] == '\0') {
        *out = css_length_auto();
        return TRUE;
    }

    if (s[0] == '0' && s[1] == '\0') {
        *out = css_length_zero();
        return TRUE;
    }

    css_unit_t unit = CSS_UNIT_PX;
    size_t len = strlen(s);

    if (len >= 3 && s[len - 3] == 'r' && s[len - 2] == 'e' && s[len - 1] == 'm') {
        unit = CSS_UNIT_REM;
        len -= 3;
    } else if (len >= 2 && s[len - 2] == 'e' && s[len - 1] == 'm') {
        unit = CSS_UNIT_EM;
        len -= 2;
    } else if (len >= 2 && s[len - 2] == 'v' && s[len - 1] == 'w') {
        unit = CSS_UNIT_VW;
        len -= 2;
    } else if (len >= 2 && s[len - 2] == 'v' && s[len - 1] == 'h') {
        unit = CSS_UNIT_VH;
        len -= 2;
    } else if (len >= 2 && s[len - 2] == 'p' && s[len - 1] == 'x') {
        unit = CSS_UNIT_PX;
        len -= 2;
    } else if (len >= 1 && s[len - 1] == '%') {
        unit = CSS_UNIT_PERCENT;
        len -= 1;
    }

    char buf[64];
    if (len >= sizeof(buf)) return FALSE;
    memcpy(buf, s, len);
    buf[len] = '\0';

    char* end = nullptr;
    float number = strtof(buf, &end);
    if (end == buf || *end != '\0' || !std::isfinite(number)) return FALSE;

    out->value = number;
    out->unit = unit;
    return TRUE;
}
