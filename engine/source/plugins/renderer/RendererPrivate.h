#pragma once
#include "../contracts/NcmaRenderer.h"
struct ID3D11Device;
struct ID3D11DeviceContext;
#if defined(NCMA_RENDERER_EXPORTS)
#define NCMA_RENDERER_INTERNAL __declspec(dllexport)
#else
#define NCMA_RENDERER_INTERNAL __declspec(dllimport)
#endif
// Native-only v1 service; GUI holds a borrow until DX11 backend shutdown, blocking destroy.
// Device/context never appear in a managed ABI table. Owner thread only.
extern "C" NCMA_RENDERER_INTERNAL uint32_t NCMA_CALL ncma_renderer_borrow_dx11_v1(uint64_t module, uint64_t renderer, ID3D11Device**, ID3D11DeviceContext**, NcmaErrorV1*) noexcept;
extern "C" NCMA_RENDERER_INTERNAL uint32_t NCMA_CALL ncma_renderer_release_dx11_v1(uint64_t module, uint64_t renderer, NcmaErrorV1*) noexcept;

extern "C" NCMA_RENDERER_INTERNAL uint32_t NCMA_CALL ncma_renderer_validate_gui_frame_v1(uint64_t module,uint64_t renderer,NcmaErrorV1*) noexcept;
