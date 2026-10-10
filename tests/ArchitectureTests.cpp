#include "ai/AgentCapabilityRegistry.h"
#include "renderer/pipeline/PbrPipelineSettings.h"
#include "renderer/rendergraph/RenderGraph.h"
#include "renderer/rhi/RenderBackendRegistry.h"
#include "renderer/rhi/vulkan/VulkanRuntimeProbe.h"
#include "physics/runtime/PhysicsWorld2D.h"
#include "physics/runtime/PhysicsWorld3D.h"
#include "script/runtime/ScriptRuntimeRegistry.h"

#include <cassert>
#include <cmath>
#include <iostream>
#if defined(_MSC_VER)
#include <crtdbg.h>
#endif

namespace
{

    bool Near(float lhs, float rhs) { return std::abs(lhs - rhs) < 0.0001F; }
}


int main()
{
#if defined(_MSC_VER)
    _CrtSetReportMode(_CRT_ASSERT, _CRTDBG_MODE_FILE);
    _CrtSetReportFile(_CRT_ASSERT, _CRTDBG_FILE_STDERR);
#endif
    using namespace NcmaEngine;

    Rhi::RenderBackendRegistry renderBackends;
    assert(renderBackends.IsRegistered(Rhi::BackendType::Null));
    assert(Rhi::RenderBackendRegistry::Parse("dx11") == Rhi::BackendType::Direct3D11);
    assert(Rhi::RenderBackendRegistry::Parse("vk") == Rhi::BackendType::Vulkan);
    assert(!Rhi::ProbeVulkanRuntime().Diagnostic.empty());
    auto nullBackend = renderBackends.Create(Rhi::BackendType::Null);
    std::string error;
    assert(nullBackend->Initialize({}, error));
    assert(nullBackend->BeginFrame(error));
    Rhi::RenderPassDescription backBufferPass;
    backBufferPass.ClearColor = true;
    assert(nullBackend->BeginRenderPass(backBufferPass, error));
    nullBackend->EndRenderPass();
    Rhi::BufferDescription vertexBuffer;
    vertexBuffer.Size = 3 * 3 * sizeof(float);
    vertexBuffer.Stride = 3 * sizeof(float);
    vertexBuffer.Usage = Rhi::BufferUsage::Vertex;
    const auto buffer = nullBackend->CreateBuffer(vertexBuffer, nullptr, error);
    assert(buffer);

    Rhi::BufferDescription invalidConstantBuffer;
    invalidConstantBuffer.Size = 15;
    invalidConstantBuffer.Usage = Rhi::BufferUsage::Constant;
    assert(!Rhi::Validate(invalidConstantBuffer, error));
    Rhi::TextureDescription depthTexture;
    depthTexture.Format = Rhi::TextureFormat::D32Float;
    depthTexture.Usage = Rhi::TextureUsage::DepthStencil | Rhi::TextureUsage::Sampled;
    assert(Rhi::Validate(depthTexture, error));
    const auto texture = nullBackend->CreateTexture(depthTexture, error);
    assert(texture);
    Rhi::RenderPassDescription depthOnlyPass;
    depthOnlyPass.UseColorTarget = false;
    depthOnlyPass.DepthTarget = texture;
    depthOnlyPass.ClearDepth = true;
    assert(nullBackend->BeginRenderPass(depthOnlyPass, error));
    nullBackend->EndRenderPass();
    depthOnlyPass.ClearColor = true;
    assert(!Rhi::Validate(depthOnlyPass, error));
    Rhi::RenderPassDescription invalidLayerPass;
    invalidLayerPass.DepthLayer = 1;
    assert(!Rhi::Validate(invalidLayerPass, error));
    nullBackend->DestroyTexture(texture);

    Rhi::TextureDescription invalidMipTexture;
    invalidMipTexture.Width = 4;
    invalidMipTexture.Height = 4;
    invalidMipTexture.MipLevels = 4;
    assert(!Rhi::Validate(invalidMipTexture, error));
    std::array<float,16> cubeFace{};
    Rhi::TextureDescription cube;
    cube.Width=cube.Height=2;cube.Cube=true;cube.ArrayLayers=6;cube.Format=Rhi::TextureFormat::Rgba32Float;
    cube.InitialMips.assign(6,{cubeFace.data(),32,64});
    assert(Rhi::Validate(cube,error));
    cube.ArrayLayers=5;assert(!Rhi::Validate(cube,error));cube.ArrayLayers=6;
    cube.Height=1;assert(!Rhi::Validate(cube,error));cube.Height=2;
    cube.InitialMips.back().RowPitch=16;assert(!Rhi::Validate(cube,error));cube.InitialMips.back().RowPitch=32;
    cube.InitialMips.pop_back();assert(!Rhi::Validate(cube,error));

    Rhi::SamplerDescription shadowSampler;
    shadowSampler.AddressU = Rhi::SamplerAddressMode::ClampToBorder;
    shadowSampler.AddressV = Rhi::SamplerAddressMode::ClampToBorder;
    shadowSampler.Comparison = Rhi::CompareOperation::LessEqual;
    const auto sampler = nullBackend->CreateSampler(shadowSampler, error);
    assert(sampler);
    nullBackend->DestroySampler(sampler);

    constexpr std::string_view shader =
        "float4 VSMain(float2 p : POSITION) : SV_POSITION { return float4(p, 0, 1); } "
        "float4 PSMain() : SV_TARGET { return float4(1, 0, 1, 1); }";
    Rhi::GraphicsPipelineDescription pipelineDescription;
    pipelineDescription.VertexShaderSource = shader;
    pipelineDescription.PixelShaderSource = shader;
    pipelineDescription.VertexLayout.push_back({"POSITION", 0, Rhi::VertexFormat::Float2, 0});
    const auto pipeline = nullBackend->CreateGraphicsPipeline(pipelineDescription, error);
    assert(pipeline);
    Rhi::DrawDescription draw;
    draw.Pipeline = pipeline;
    draw.VertexBuffer = buffer;
    draw.VertexCount = 3;
    draw.VertexStride = vertexBuffer.Stride;
    draw.ViewportWidth = 1280.0F;
    draw.ViewportHeight = 720.0F;
    assert(nullBackend->Draw(draw, error));

    Rhi::BufferDescription dynamicConstant;
    dynamicConstant.Size = 64;
    dynamicConstant.Usage = Rhi::BufferUsage::Constant;
    dynamicConstant.Memory = Rhi::MemoryUsage::CpuToGpu;
    const auto constantBuffer = nullBackend->CreateBuffer(dynamicConstant, nullptr, error);
    assert(constantBuffer);
    std::array<float, 16> identity{};
    identity[0] = identity[5] = identity[10] = identity[15] = 1.0F;
    assert(nullBackend->UpdateBuffer(constantBuffer, identity.data(), sizeof(identity), 0, error));
    assert(!nullBackend->UpdateBuffer({}, identity.data(), sizeof(identity), 0, error));
    nullBackend->DestroyBuffer(constantBuffer);

    Rhi::DrawDescription invalidIndexedDraw = draw;
    invalidIndexedDraw.VertexCount = 0;
    invalidIndexedDraw.IndexCount = 3;
    invalidIndexedDraw.IndexBuffer = buffer;
    invalidIndexedDraw.IndexStride = 1;
    assert(!Rhi::Validate(invalidIndexedDraw, error));
    Rhi::DrawDescription invalidSampledDraw = draw;
    invalidSampledDraw.PixelTextures[1] = {1};
    assert(!Rhi::Validate(invalidSampledDraw, error));
    invalidSampledDraw = draw; invalidSampledDraw.PixelSamplers[7] = {1};
    assert(!Rhi::Validate(invalidSampledDraw, error));
    invalidSampledDraw.AllowSamplerOnlyBindings = true;
    assert(Rhi::Validate(invalidSampledDraw, error));
    invalidSampledDraw.PixelTextures[8] = {1};
    assert(!Rhi::Validate(invalidSampledDraw, error)); // opt-in never permits texture without sampler.
    nullBackend->DestroyGraphicsPipeline(pipeline);
    nullBackend->DestroyBuffer(buffer);

    Rhi::SamplerDescription invalidSampler;
    invalidSampler.Filter = Rhi::SamplerFilter::Linear;
    invalidSampler.MaxAnisotropy = 4;
    assert(!Rhi::Validate(invalidSampler, error));

    Scripting::ScriptRuntimeRegistry scripts;
    assert(scripts.GetGameplayLanguage() == Scripting::Language::CSharp);

    AI::AgentCapabilityRegistry agentCapabilities;
    agentCapabilities.Register({
        "scene.list_objects",
        "List scene objects without changing editor state",
        R"({"type":"object","properties":{}})",
        R"({"type":"array"})",
        AI::MutationRisk::ReadOnly,
        true
    });
    const std::string manifest = agentCapabilities.ExportManifestJson();
    assert(manifest.find("scene.list_objects") != std::string::npos);
    assert(manifest.find("read_only") != std::string::npos);

    Rendering::PbrMaterialParameters material;
    material.Roughness = 0.0F;
    material.Sanitize();
    assert(Near(material.Roughness, 0.04F));
    assert(Rendering::PcfKernelRadius(Rendering::ShadowFilter::Pcf3x3) == 1);
    assert(Rendering::PcfSampleCount(Rendering::ShadowFilter::Pcf3x3) == 9);
    assert(Rendering::PcfKernelRadius(Rendering::ShadowFilter::Pcf5x5) == 2);
    assert(Rendering::PcfSampleCount(Rendering::ShadowFilter::Pcf5x5) == 25);
    assert(Rendering::ShadowSampleCount(Rendering::ShadowFilter::Pcss) == 50);
    Rendering::DirectionalShadowSettings shadowSettings;
    shadowSettings.LightRadius = 1.0F;
    shadowSettings.Sanitize();
    assert(Near(shadowSettings.LightRadius, 0.25F));
    Rendering::ContactShadowSettings contactSettings;
    contactSettings.StepCount = 100;
    contactSettings.Thickness = -1.0F;
    contactSettings.Strength = 2.0F;
    contactSettings.Sanitize();
    assert(contactSettings.StepCount == 32);
    assert(Near(contactSettings.Thickness, 0.001F));
    assert(Near(contactSettings.Strength, 1.0F));

    Rendering::RenderGraph renderGraph;
    std::vector<std::string> executedPasses;
    renderGraph.AddPass({"Geometry", {}, {"HdrColor"}}, [&](std::string&) {
        executedPasses.emplace_back("Geometry");
        return true;
    });
    renderGraph.AddPass({"ToneMap", {"HdrColor"}, {"Swapchain"}}, [&](std::string&) {
        executedPasses.emplace_back("ToneMap");
        return true;
    });
    assert(renderGraph.Compile(error));
    assert(renderGraph.Execute(error));
    assert((executedPasses == std::vector<std::string>{"Geometry", "ToneMap"}));

    Physics::PhysicsWorld2D physics2D;
    const auto body2D = physics2D.CreateBox(Vector2(0.0F, 4.0F), Vector2(0.5F, 0.5F), true);
    const float initial2DY = physics2D.GetPosition(body2D).y();
    for (int step = 0; step < 30; ++step)
        physics2D.Step(1.0F / 60.0F);
    assert(physics2D.GetPosition(body2D).y() < initial2DY);

    Physics::PhysicsWorld3D physics3D;
    const auto body3D = physics3D.CreateBox(Vector3(0.0F, 4.0F, 0.0F), Vector3(0.5F, 0.5F, 0.5F), true);
    const float initial3DY = physics3D.GetPosition(body3D).y();
    for (int step = 0; step < 30; ++step)
        physics3D.Step(1.0F / 60.0F);
    assert(physics3D.GetPosition(body3D).y() < initial3DY);

    std::cout << "Ncma architecture tests passed\n";
    return 0;
}
