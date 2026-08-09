#include "android_types.h"

#include <algorithm>
#include <cstring>

#define STB_IMAGE_IMPLEMENTATION
#define STBI_ONLY_PNG
#define STBI_ONLY_JPEG
#define STBI_NO_LINEAR
#include "../third_party/stb_image.h"

/* Decode a raw image file (PNG/JPEG) into straight ARGB8888 pixels owned by
 * the session cache. Returns false when the bytes are not decodable or the
 * fetch callback is missing — never invents a fallback. */

namespace viewruntime::android {

bool decode_and_cache_image(android_ui_s* ui, const std::string& source) {
    if (!ui->fetch_file || source.empty()) return false;
    auto cached = ui->decoded_images.find(source);
    if (cached != ui->decoded_images.end()) return true;

    const uint8_t* bytes = nullptr;
    int32_t size = 0;
    if (!ui->fetch_file(source.c_str(), &bytes, &size, ui->bridge_data) ||
        !bytes || size <= 0) {
        return false;
    }

    int w = 0, h = 0, comp = 0;
    /* stb_image returns 8-bit RGBA here; the backend expects straight
     * ARGB8888 (A first in memory), so swap channels while copying. */
    stbi_uc* rgba = stbi_load_from_memory(bytes, size, &w, &h, &comp, 4);
    if (!rgba || w <= 0 || h <= 0) return false;

    android_ui_s::DecodedImage img;
    img.width = w;
    img.height = h;
    img.argb.resize(static_cast<size_t>(w) * h * 4);
    const size_t count = static_cast<size_t>(w) * h;
    for (size_t i = 0; i < count; ++i) {
        img.argb[i * 4 + 0] = rgba[i * 4 + 3]; /* A */
        img.argb[i * 4 + 1] = rgba[i * 4 + 0]; /* R */
        img.argb[i * 4 + 2] = rgba[i * 4 + 1]; /* G */
        img.argb[i * 4 + 3] = rgba[i * 4 + 2]; /* B */
    }
    stbi_image_free(rgba);

    auto [it, inserted] = ui->decoded_images.emplace(source, std::move(img));
    (void)inserted;
    return true;
}

bool image_dimensions_from_cache(const android_ui_s* ui,
                                 const std::string& source,
                                 float* out_w, float* out_h) {
    const auto it = ui->decoded_images.find(source);
    if (it == ui->decoded_images.end()) return false;
    *out_w = static_cast<float>(it->second.width);
    *out_h = static_cast<float>(it->second.height);
    return true;
}

const android_ui_s::DecodedImage* find_decoded_image(const android_ui_s* ui,
                                                     const std::string& source) {
    const auto it = ui->decoded_images.find(source);
    return it == ui->decoded_images.end() ? nullptr : &it->second;
}

} // namespace viewruntime::android
