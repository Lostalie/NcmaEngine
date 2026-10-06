#ifndef NCMA_TEXT_H
#define NCMA_TEXT_H
#include "NcmaPlugin.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Windows numerical text service, ABI1.0. DirectWrite shapes caller-supplied in-memory OpenType
   fonts; no filesystem/network/system-font discovery. C# owns UUID/licence/cache/UI policies.
   UTF8 text <=65536 bytes; font <=32MiB,4 fonts/64MiB;128 layouts/128MiB RGBA total.
   Prep/copy/release OFF FRAME only; owner thread; no GPU/World/ImGui/Python dependency.
   Layouts pin fonts. Explicit failure for missing glyphs. Copied RGBA8 white/straight-alpha
   coverage suitable for tinting; grayscale, not ClearType/colour emoji. Raster max4096x4096.
   Destroy layouts then fonts before shutdown; busy retains all resources. */
typedef struct NcmaTextRequestV1 {
    uint32_t struct_size,text_bytes;uint64_t font;
    const uint8_t* text;float size,width,height,scale;uint32_t wrap,right_to_left;
} NcmaTextRequestV1;
typedef struct NcmaTextMetricsV1 {uint32_t struct_size,width,height,byte_count;float content_width,content_height;uint32_t lines,reserved;} NcmaTextMetricsV1;
typedef uint32_t(NCMA_CALL *NcmaCreateFontV1)(uint64_t,const uint8_t*,uint32_t,uint64_t*,NcmaErrorV1*);
typedef uint32_t(NCMA_CALL *NcmaPrepareTextV1)(uint64_t,const NcmaTextRequestV1*,uint64_t*,NcmaTextMetricsV1*,NcmaErrorV1*);
typedef uint32_t(NCMA_CALL *NcmaCopyTextV1)(uint64_t,uint64_t,uint8_t*,uint32_t,NcmaErrorV1*);
typedef uint32_t(NCMA_CALL *NcmaReleaseTextV1)(uint64_t,uint64_t,NcmaErrorV1*);
typedef struct NcmaTextApiV1 {NcmaModuleApiV1 module;NcmaCreateFontV1 create_font;NcmaPrepareTextV1 prepare;NcmaCopyTextV1 copy;NcmaReleaseTextV1 release;} NcmaTextApiV1;
#ifdef __cplusplus
}
static_assert(sizeof(NcmaTextRequestV1)==48&&sizeof(NcmaTextMetricsV1)==32&&sizeof(NcmaTextApiV1)==88);
#endif
#endif
