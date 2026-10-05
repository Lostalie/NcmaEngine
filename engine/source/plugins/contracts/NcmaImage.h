#ifndef NCMA_IMAGE_H
#define NCMA_IMAGE_H
#include "NcmaImport.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Tool-only memory decode ABI1. PNG/JPEG <=16MiB encoded, <=4096 dimensions,
   RGBA<=64MiB. Profiled/oriented inputs rejected, no filenames/World/GPU.
   Short output returns required bytes; pixels copied only on complete success. */
typedef struct NcmaImageInfoV1 { uint32_t struct_size,width,height,row_pitch,bytes,container,reserved[2]; } NcmaImageInfoV1;
NCMA_IMPORT_API uint32_t NCMA_CALL ncma_image_decode_v1(uint32_t version,const uint8_t* encoded,uint32_t bytes,NcmaImageInfoV1* info,uint8_t* rgba,uint32_t capacity,NcmaErrorV1* error);
#ifdef __cplusplus
}
static_assert(sizeof(NcmaImageInfoV1)==32);
#endif
#endif
