#include <cstddef>
#include <cstdint>
#include <memory>
#include <new>
#include "astcenc.h"

namespace {

struct Context {
    astcenc_context* value = nullptr;
    ~Context() { astcenc_context_free(value); }
};

astcenc_error get_context(Context& context, unsigned x, unsigned y, bool decode)
{
    if (context.value)
        return ASTCENC_SUCCESS;
    astcenc_config config{};
    auto status = astcenc_config_init(ASTCENC_PRF_LDR, x, y, 1,
        ASTCENC_PRE_FASTEST, decode ? ASTCENC_FLG_DECOMPRESS_ONLY : 0, &config);
    if (status != ASTCENC_SUCCESS)
        return status;
    return astcenc_context_alloc(&config, 1, &context.value);
}

std::size_t blocks(unsigned width, unsigned height, unsigned bx, unsigned by)
{
    return ((width + bx - 1) / bx) * std::size_t((height + by - 1) / by) * 16;
}

}

extern "C" int repack_astc(const uint8_t* source, std::size_t source_size,
    unsigned width, unsigned height, unsigned bx, unsigned by, unsigned footprint,
    uint8_t* target, std::size_t target_size)
{
    const bool compressed = bx || by;
    if (!source || !target || !width || !height ||
        (compressed && (bx < 4 || by < 4 || bx > 12 || by > 12)) ||
        footprint < 4 || footprint > 6 ||
        width > 4096 || height > 4096 ||
        source_size != (compressed ? blocks(width, height, bx, by) : std::size_t(width) * height * 4) ||
        target_size != blocks(width, height, footprint, footprint))
        return -1;

    static Context decoders[13][13];
    static Context encoders[3];
    auto& encoder = encoders[footprint - 4];
    auto status = get_context(encoder, footprint, footprint, false);
    if (status != ASTCENC_SUCCESS)
        return status;

    std::unique_ptr<uint8_t[]> pixels;
    void* slice = const_cast<uint8_t*>(source);
    astcenc_image image{width, height, 1, ASTCENC_TYPE_U8, &slice};
    const astcenc_swizzle rgba{ASTCENC_SWZ_R, ASTCENC_SWZ_G, ASTCENC_SWZ_B, ASTCENC_SWZ_A};
    if (compressed) {
        auto& decoder = decoders[bx][by];
        status = get_context(decoder, bx, by, true);
        if (status != ASTCENC_SUCCESS)
            return status;
        pixels.reset(new (std::nothrow) uint8_t[std::size_t(width) * height * 4]);
        if (!pixels)
            return -2;
        slice = pixels.get();
        status = astcenc_decompress_image(decoder.value, source, source_size, &image, &rgba, 0);
        astcenc_decompress_reset(decoder.value);
        if (status != ASTCENC_SUCCESS)
            return status;
    }
    status = astcenc_compress_image(encoder.value, &image, &rgba, target, target_size, 0);
    astcenc_compress_reset(encoder.value);
    return status;
}
