#pragma once
#include "renderer/rhi/RenderBackend.h"
#include "renderer/pipeline/PbrPipelineSettings.h"
#include "foundation/MathTypes.h"
#include <array>
#include "../contracts/NcmaRenderer.h"
namespace NcmaEngine::Rendering {
// GPU resources and numeric kernels only; no scene, CLR, graph policy, input or GUI.
class ReferenceKernel final {
public:
    explicit ReferenceKernel(Rhi::IRenderBackend& renderer) : m_Renderer(&renderer) {}
    ~ReferenceKernel();
    bool Initialize(std::string& error);
    void Configure(const NcmaReferenceSettingsV1& settings) noexcept;
    bool Prepare(uint32_t width, uint32_t height, const float* columnMajorModel, float exposure, float metallic, float roughness, std::string& error);
    bool Shadow(std::string& error);
    bool Geometry(std::string& error);
    bool ToneMap(float exposureOverride, float x, float y, float width, float height, std::string& error);
private:
    bool EnsurePreviewTargets(uint32_t width, uint32_t height, std::string& error);
    Rhi::IRenderBackend* m_Renderer;
    Rhi::BufferHandle m_PreviewVertexBuffer, m_PreviewIndexBuffer, m_PreviewTransformBuffer, m_GroundTransformBuffer, m_PreviewMaterialBuffer;
    Rhi::GraphicsPipelineHandle m_PreviewPipeline, m_ShadowPipeline, m_ToneMapPipeline;
    Rhi::DrawDescription m_PreviewDraw, m_GroundDraw, m_ShadowDraw, m_GroundShadowDraw, m_ToneMapDraw;
    Rhi::TextureHandle m_PreviewShadowMap, m_PreviewHdrColor, m_PreviewDepth;
    Rhi::SamplerHandle m_PreviewShadowSampler, m_PreviewLinearSampler;
    Rhi::BufferHandle m_ShadowTransformBuffer, m_ToneMapVertexBuffer, m_ToneMapConstantBuffer;
    uint32_t m_PreviewTargetWidth = 0, m_PreviewTargetHeight = 0;
    Matrix4 m_PreviewCubeModel = Matrix4::Identity(), m_PreviewGroundModel = Matrix4::Identity();
    std::array<Matrix4, 4> m_CascadeLightViewProjections{};
    std::array<float, 4> m_CascadeSplits{};
    PbrMaterialParameters m_PreviewMaterial;
    DirectionalShadowSettings m_PreviewShadowSettings;
    ContactShadowSettings m_PreviewContactShadowSettings;
    std::array<float, 12> m_ToneData{};
    float m_PreviewLightIntensity = 4, m_PreviewExposure = 1, m_PreviewAmbient = 0.035F;
};
}
