#pragma once
#include "../contracts/NcmaRenderer.h"
#include "../contracts/NcmaResourceRender.h"
struct ID3D11Device;
struct ID3D11DeviceContext;
struct ID3D11ShaderResourceView;
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
// Native-only GUI Image bridge. frame=0 resolves CPU draw data; nonzero requires an exact
// submitted target frame. retain=1 pins the target; release after render/discard. No GPU wait.
extern "C" NCMA_RENDERER_INTERNAL uint32_t NCMA_CALL ncma_renderer_gui_image_v1(uint64_t module,uint64_t renderer,NcmaGpuResourceV3 token,uint64_t frame,uint32_t retain,ID3D11ShaderResourceView**,NcmaErrorV1*) noexcept;
extern "C" NCMA_RENDERER_INTERNAL uint32_t NCMA_CALL ncma_renderer_release_gui_image_v1(uint64_t module,uint64_t renderer,NcmaGpuResourceV3 token,NcmaErrorV1*) noexcept;
// GUI1.6: token identifies a query7 presentation lease, NOT a scene target. Exact lease frame.
extern "C" NCMA_RENDERER_INTERNAL uint32_t NCMA_CALL ncma_renderer_gui_cached_image_v1(uint64_t,uint64_t,NcmaGpuResourceV3,uint64_t,uint32_t,ID3D11ShaderResourceView**,NcmaErrorV1*) noexcept;
extern "C" NCMA_RENDERER_INTERNAL uint32_t NCMA_CALL ncma_renderer_release_gui_cached_image_v1(uint64_t,uint64_t,NcmaGpuResourceV3,NcmaErrorV1*) noexcept;
