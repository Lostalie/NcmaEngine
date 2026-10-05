#pragma once
#include "contracts/NcmaSceneRender.h"
#include "renderer/rhi/RenderBackend.h"
namespace NcmaEngine::Rendering {
// GPU execution/resources only; no managed scene, asset catalog or pipeline policy.
class StaticMesh final {
    Rhi::IRenderBackend& backend;
public:
    Rhi::BufferHandle vertices, indices;
    uint32_t indexCount=0;
    uint32_t vertexStride=48;
    uint64_t bytes=0;
    explicit StaticMesh(Rhi::IRenderBackend& value):backend(value) {}
    ~StaticMesh();
    bool Initialize(const NcmaMeshDescriptionV1&,std::string&);
};
class StaticMeshKernel final {
    Rhi::IRenderBackend& backend;
    Rhi::BufferHandle constants;
    Rhi::GraphicsPipelineHandle pipeline;
public:
    explicit StaticMeshKernel(Rhi::IRenderBackend& value):backend(value) {}
    ~StaticMeshKernel();
    bool Initialize(std::string&);
    bool Draw(const StaticMesh&,const NcmaMeshDrawV1&,const float* viewport,std::string&);
};
}
