#include <viewruntime/viewruntime.h>
#include <algorithm>

API bool_t rectf_contains(rectf r, pointf p) {
    return (p.x >= r.x && p.x <= r.x + r.width &&
            p.y >= r.y && p.y <= r.y + r.height) ? TRUE : FALSE;
}

API rectf rectf_deflate(rectf r, thicknessf t) {
    rectf out;
    out.x = r.x + t.left;
    out.y = r.y + t.top;
    out.width = std::max(0.0f, r.width - (t.left + t.right));
    out.height = std::max(0.0f, r.height - (t.top + t.bottom));
    return out;
}

API rectf rectf_inflate(rectf r, float v) {
    rectf out;
    out.x = r.x - v;
    out.y = r.y - v;
    out.width = r.width + v * 2;
    out.height = r.height + v * 2;
    return out;
}

API rectf rectf_offset(rectf r, float dx, float dy) {
    rectf out;
    out.x = r.x + dx;
    out.y = r.y + dy;
    out.width = r.width;
    out.height = r.height;
    return out;
}

API rectf rectf_from_edges(float left, float top, float right, float bottom) {
    rectf out;
    out.x = left;
    out.y = top;
    out.width = std::max(0.0f, right - left);
    out.height = std::max(0.0f, bottom - top);
    return out;
}

API float thicknessf_horizontal(thicknessf t) {
    return t.left + t.right;
}

API float thicknessf_vertical(thicknessf t) {
    return t.top + t.bottom;
}
