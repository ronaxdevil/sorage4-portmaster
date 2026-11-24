#include "astcenc.h"
#include <ETC1/etc1.h>
#include <stdlib.h>

// Encode RGBA8 (w x h) -> ETC1 (GL_ETC1_RGB8_OES). out precisa ((w+3)/4)*((h+3)/4)*8 bytes.
// Padroniza p/ multiplo de 4 (clamp na borda). alpha ignorado (ETC1 = RGB). retorna 0 ok.
extern "C" int sor4_etc1_encode(const unsigned char* rgba, int w, int h, unsigned char* out) {
    if (!rgba || !out || w <= 0 || h <= 0) return -1;
    const size_t pixels = static_cast<size_t>(w) * h;
    auto* rgb = static_cast<unsigned char*>(malloc(pixels * 3));
    if (!rgb) return -2;
    for (size_t i = 0; i < pixels; ++i) {
        rgb[i * 3] = rgba[i * 4];
        rgb[i * 3 + 1] = rgba[i * 4 + 1];
        rgb[i * 3 + 2] = rgba[i * 4 + 2];
    }
    const int status = etc1_encode_image(rgb, w, h, 3, static_cast<unsigned>(w) * 3, out);
    free(rgb);
    return status;
}

// decode ASTC -> RGBA8. retorna 0 ok. outRGBA precisa w*h*4 bytes.
extern "C" int sor4_astc_decode(const unsigned char* data, unsigned long len,
                                int w, int h, int bx, int by, unsigned char* outRGBA) {
    astcenc_config config;
    astcenc_error st = astcenc_config_init(ASTCENC_PRF_LDR, bx, by, 1,
                          ASTCENC_PRE_FAST, ASTCENC_FLG_DECOMPRESS_ONLY, &config);
    if (st != ASTCENC_SUCCESS) return -1;
    astcenc_context* ctx = 0;
    st = astcenc_context_alloc(&config, 1, &ctx);
    if (st != ASTCENC_SUCCESS) return -2;
    astcenc_image image;
    image.dim_x = w; image.dim_y = h; image.dim_z = 1;
    image.data_type = ASTCENC_TYPE_U8;
    void* slice = outRGBA;
    image.data = &slice;
    astcenc_swizzle swz = { ASTCENC_SWZ_R, ASTCENC_SWZ_G, ASTCENC_SWZ_B, ASTCENC_SWZ_A };
    st = astcenc_decompress_image(ctx, data, len, &image, &swz, 0);
    astcenc_context_free(ctx);
    return st == ASTCENC_SUCCESS ? 0 : -3;
}
