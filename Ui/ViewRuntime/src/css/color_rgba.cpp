#include <viewruntime/viewruntime.h>
#include <cstring>
#include <cstdlib>
#include <cctype>
#include <cmath>

static int hex_digit(char c) {
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;
}

static bool_t try_parse_hex_component(const char* s, int len, uint8_t* out) {
    if (len == 1) {
        int d = hex_digit(s[0]);
        if (d < 0) return FALSE;
        *out = (uint8_t)(d * 17);
        return TRUE;
    }
    if (len == 2) {
        int h = hex_digit(s[0]);
        int l = hex_digit(s[1]);
        if (h < 0 || l < 0) return FALSE;
        *out = (uint8_t)((h << 4) | l);
        return TRUE;
    }
    return FALSE;
}

static bool_t try_parse_hex(const char* s, color_rgba* out) {
    uint8_t parts[4] = {0, 0, 0, 255};
    size_t len = strlen(s);

    if (len == 3 || len == 4) {
        for (size_t i = 0; i < len; i++) {
            if (!try_parse_hex_component(s + i, 1, &parts[i]))
                return FALSE;
        }
    } else if (len == 6 || len == 8) {
        for (size_t i = 0; i < len / 2; i++) {
            if (!try_parse_hex_component(s + i * 2, 2, &parts[i]))
                return FALSE;
        }
    } else {
        return FALSE;
    }

    out->r = parts[0] / 255.0f;
    out->g = parts[1] / 255.0f;
    out->b = parts[2] / 255.0f;
    out->a = parts[3] / 255.0f;
    return TRUE;
}

static bool_t try_channel(const char* text, float* value) {
    *value = 0;
    size_t len = strlen(text);
    if (len == 0) return FALSE;

    if (text[len - 1] == '%') {
        char buf[32];
        if (len - 1 >= sizeof(buf)) return FALSE;
        memcpy(buf, text, len - 1);
        buf[len - 1] = '\0';
        char* end = nullptr;
        float pct = strtof(buf, &end);
        if (end == buf || !std::isfinite(pct)) return FALSE;
        *value = fminf(fmaxf(pct / 100.0f, 0.0f), 1.0f);
        return TRUE;
    }

    char* end = nullptr;
    float channel = strtof(text, &end);
    if (end == text || !std::isfinite(channel)) return FALSE;
    *value = fminf(fmaxf(channel / 255.0f, 0.0f), 1.0f);
    return TRUE;
}

static bool_t try_alpha(const char* text, float* value) {
    *value = 1.0f;
    size_t len = strlen(text);
    if (len == 0) return FALSE;

    if (text[len - 1] == '%') {
        char buf[32];
        if (len - 1 >= sizeof(buf)) return FALSE;
        memcpy(buf, text, len - 1);
        buf[len - 1] = '\0';
        char* end = nullptr;
        float pct = strtof(buf, &end);
        if (end == buf || !std::isfinite(pct)) return FALSE;
        *value = fminf(fmaxf(pct / 100.0f, 0.0f), 1.0f);
        return TRUE;
    }

    char* end = nullptr;
    float alpha = strtof(text, &end);
    if (end == text || !std::isfinite(alpha)) return FALSE;
    *value = fminf(fmaxf(alpha, 0.0f), 1.0f);
    return TRUE;
}

static bool_t try_parse_rgb(const char* s, bool_t has_alpha, color_rgba* out) {
    const char* p = s;
    float channels[4] = {0, 0, 0, 1};
    int count = 0;

    while (*p && count < (has_alpha ? 4 : 3)) {
        while (*p == ' ' || *p == '\t') ++p;
        if (*p == '\0') break;

        char buf[32];
        int i = 0;
        while (*p && *p != ',' && *p != ')' && i < (int)sizeof(buf) - 1) {
            buf[i++] = *p++;
        }
        buf[i] = '\0';

        while (i > 0 && buf[i - 1] == ' ') buf[--i] = '\0';

        float val;
        if (count < 3) {
            if (!try_channel(buf, &val)) return FALSE;
        } else {
            if (!try_alpha(buf, &val)) return FALSE;
        }
        channels[count++] = val;

        while (*p == ' ' || *p == '\t') ++p;
        if (*p == ',') ++p;
    }

    if (count != (has_alpha ? 4 : 3)) return FALSE;

    out->r = channels[0];
    out->g = channels[1];
    out->b = channels[2];
    out->a = channels[3];
    return TRUE;
}

static color_rgba from_bytes(uint8_t r, uint8_t g, uint8_t b, uint8_t a = 255) {
    color_rgba c;
    c.r = r / 255.0f;
    c.g = g / 255.0f;
    c.b = b / 255.0f;
    c.a = a / 255.0f;
    return c;
}

struct named_color_entry { const char* name; color_rgba color; };

static const named_color_entry named_colors[] = {
    {"transparent",  {0, 0, 0, 0}},
    {"black",        from_bytes(0, 0, 0)},
    {"white",        from_bytes(255, 255, 255)},
    {"red",          from_bytes(255, 0, 0)},
    {"green",        from_bytes(0, 128, 0)},
    {"blue",         from_bytes(0, 0, 255)},
    {"gray",         from_bytes(128, 128, 128)},
    {"grey",         from_bytes(128, 128, 128)},
    {"silver",       from_bytes(192, 192, 192)},
    {"navy",         from_bytes(0, 0, 128)},
    {"teal",         from_bytes(0, 128, 128)},
    {"purple",       from_bytes(128, 0, 128)},
    {"orange",       from_bytes(255, 165, 0)},
    {"yellow",       from_bytes(255, 255, 0)},
    {"whitesmoke",   from_bytes(245, 245, 245)},
    {"aliceblue",    from_bytes(240, 248, 255)},
};
static const int named_color_count = sizeof(named_colors) / sizeof(named_colors[0]);

static void to_lower_inplace(char* s) {
    for (; *s; ++s) {
        if (*s >= 'A' && *s <= 'Z') *s = *s + ('a' - 'A');
    }
}

API bool_t color_rgba_try_parse(const char* input, color_rgba* out) {
    if (!input || !out) return FALSE;
    *out = from_bytes(0, 0, 0, 0);

    while (*input == ' ' || *input == '\t') ++input;
    if (*input == '\0') return FALSE;

    char lower[128];
    size_t len = strlen(input);
    if (len >= sizeof(lower)) return FALSE;
    memcpy(lower, input, len + 1);
    to_lower_inplace(lower);

    for (int i = 0; i < named_color_count; i++) {
        if (strcmp(lower, named_colors[i].name) == 0) {
            *out = named_colors[i].color;
            return TRUE;
        }
    }

    if (lower[0] == '#') {
        return try_parse_hex(lower + 1, out);
    }

    if (strncmp(lower, "rgba(", 5) == 0 && lower[len - 1] == ')') {
        return try_parse_rgb(lower + 5, TRUE, out);
    }
    if (strncmp(lower, "rgb(", 4) == 0 && lower[len - 1] == ')') {
        return try_parse_rgb(lower + 4, FALSE, out);
    }

    return FALSE;
}
