#pragma once
#include "StaticMeshKernel.h"
#include "contracts/NcmaScenePipeline.h"
#include "contracts/NcmaShaderPipeline.h"
#include "contracts/NcmaShaderStages.h"
namespace NcmaEngine::Rendering {
class ScenePipelineKernel final {
    Rhi::IRenderBackend& backend;
    Rhi::GraphicsPipelineHandle geometry,shadowPipeline,tone;
    Rhi::BufferHandle constants;
    Rhi::SamplerHandle sampler,shadowSampler;
    Rhi::TextureHandle white,normal;
public:
    Rhi::TextureHandle hdr,depth,shadow;
    uint32_t width=0,height=0,resolution=0;
    explicit ScenePipelineKernel(Rhi::IRenderBackend& b):backend(b){}
    ~ScenePipelineKernel();
    static std::string_view ShaderSource();
    struct Programs { Rhi::GraphicsPipelineHandle geometry,shadow,tone; };
    bool Initialize(const NcmaScenePipelineDescriptionV4&,std::string&,const NcmaShaderPairV1* = nullptr,const NcmaSceneShadersV1* = nullptr);
    bool PrepareShaders(const NcmaSceneShadersV1&,Programs&,std::string&);
    void ReleasePrograms(Programs& p) noexcept { backend.DestroyGraphicsPipeline(p.geometry);backend.DestroyGraphicsPipeline(p.shadow);backend.DestroyGraphicsPipeline(p.tone);p={}; }
    void PublishShaders(Programs& p) noexcept { Programs old{geometry,shadowPipeline,tone};geometry=p.geometry;shadowPipeline=p.shadow;tone=p.tone;p={};ReleasePrograms(old); }
    Rhi::GraphicsPipelineHandle PrepareTone(const NcmaShaderPairV1&,std::string&);
    void PublishTone(Rhi::GraphicsPipelineHandle candidate) noexcept { auto old=tone;tone=candidate;backend.DestroyGraphicsPipeline(old); }
    bool BeginShadow(std::string&);
    bool BeginGeometry(const NcmaSceneFrameV4&,std::string&);
    bool Draw(const StaticMesh&,const NcmaSceneDrawV4&,const NcmaMaterialDescriptionV3&,
        const std::array<Rhi::TextureHandle,6>&,const NcmaSceneFrameV4&,bool,bool,float,std::string&);
    bool Tone(const NcmaSceneFrameV4&,Rhi::TextureHandle,float,std::string&);
};
}
