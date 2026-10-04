#include "ReferenceKernel.h"
#include <cmath>
#include <algorithm>
#include <cstring>
namespace NcmaEngine::Rendering {
namespace {
        struct PreviewVertex final
        {
            float Position[3];
            float Normal[3];
        };

        struct PreviewTransformConstants final
        {
            Matrix4 Model = Matrix4::Identity();
            Matrix4 ModelViewProjection = Matrix4::Identity();
            Matrix4 View = Matrix4::Identity();
        };

        struct PreviewPbrConstants final
        {
            std::array<float, 4> BaseColor{};
            std::array<float, 4> Material{};
            std::array<float, 4> LightDirection{};
            std::array<float, 4> LightColor{};
            std::array<float, 4> CameraPosition{};
            std::array<Matrix4, 4> LightViewProjections{
                Matrix4::Identity(), Matrix4::Identity(), Matrix4::Identity(), Matrix4::Identity()};
            std::array<float, 4> CascadeSplits{};
            std::array<float, 4> ShadowParameters{};
            std::array<float, 4> CascadeParameters{};
        };

        struct ShadowTransformConstants final
        {
            Matrix4 LightModelViewProjection = Matrix4::Identity();
        };

        struct ToneMapVertex final
        {
            float Position[2];
            float TextureCoordinate[2];
        };

        struct ToneMapConstants final
        {
            std::array<float, 4> ToneParameters{};
            std::array<float, 4> LightDirection{};
            std::array<float, 4> ProjectionParameters{};
        };

        static_assert(sizeof(PreviewTransformConstants) % 16 == 0);
        static_assert(sizeof(PreviewPbrConstants) % 16 == 0);
        static_assert(sizeof(ShadowTransformConstants) == sizeof(Matrix4));
        static_assert(sizeof(ToneMapConstants) == 48);


}
ReferenceKernel::~ReferenceKernel() {
    for(auto h : {m_PreviewPipeline, m_ShadowPipeline, m_ToneMapPipeline}) m_Renderer->DestroyGraphicsPipeline(h);
    for(auto h : {m_PreviewVertexBuffer, m_PreviewIndexBuffer, m_PreviewTransformBuffer, m_GroundTransformBuffer, m_PreviewMaterialBuffer, m_ShadowTransformBuffer, m_ToneMapVertexBuffer, m_ToneMapConstantBuffer}) m_Renderer->DestroyBuffer(h);
    for(auto h : {m_PreviewShadowMap, m_PreviewHdrColor, m_PreviewDepth}) m_Renderer->DestroyTexture(h);
    for(auto h : {m_PreviewShadowSampler, m_PreviewLinearSampler}) m_Renderer->DestroySampler(h);
}
void ReferenceKernel::Configure(const NcmaReferenceSettingsV1& s) noexcept {
    m_PreviewMaterial.BaseColor[0]=s.base_red; m_PreviewMaterial.BaseColor[1]=s.base_green; m_PreviewMaterial.BaseColor[2]=s.base_blue;
    m_PreviewLightIntensity=s.light_intensity; m_PreviewAmbient=s.ambient;
    m_PreviewShadowSettings.Enabled=s.shadow_enabled!=0; m_PreviewShadowSettings.Filter=static_cast<ShadowFilter>(s.shadow_filter);
    m_PreviewShadowSettings.ConstantBias=s.constant_bias; m_PreviewShadowSettings.SlopeBias=s.slope_bias;
    m_PreviewShadowSettings.MaxDistance=s.max_distance; m_PreviewShadowSettings.CascadeLambda=s.cascade_lambda;
    m_PreviewShadowSettings.LightRadius=s.light_radius;
    m_PreviewContactShadowSettings.Enabled=s.contact_enabled!=0; m_PreviewContactShadowSettings.StepCount=s.contact_steps;
    m_PreviewContactShadowSettings.MaxDistance=s.contact_distance; m_PreviewContactShadowSettings.Thickness=s.contact_thickness;
    m_PreviewContactShadowSettings.Strength=s.contact_strength;
}
bool ReferenceKernel::Initialize(std::string& error) {
    m_PreviewMaterial.BaseColor[0]=0.32F; m_PreviewMaterial.BaseColor[1]=0.16F; m_PreviewMaterial.BaseColor[2]=0.82F;
    m_PreviewShadowSettings.Resolution=2048; m_PreviewShadowSettings.CascadeCount=4;
    m_PreviewShadowSettings.MaxDistance=40; m_PreviewShadowSettings.Filter=ShadowFilter::Pcss;
        constexpr std::array<PreviewVertex, 28> previewVertices{{
            {{-1.0F, -1.0F, -1.0F}, {0.0F, 0.0F, -1.0F}},
            {{-1.0F, 1.0F, -1.0F}, {0.0F, 0.0F, -1.0F}},
            {{1.0F, 1.0F, -1.0F}, {0.0F, 0.0F, -1.0F}},
            {{1.0F, -1.0F, -1.0F}, {0.0F, 0.0F, -1.0F}},
            {{-1.0F, -1.0F, 1.0F}, {0.0F, 0.0F, 1.0F}},
            {{1.0F, -1.0F, 1.0F}, {0.0F, 0.0F, 1.0F}},
            {{1.0F, 1.0F, 1.0F}, {0.0F, 0.0F, 1.0F}},
            {{-1.0F, 1.0F, 1.0F}, {0.0F, 0.0F, 1.0F}},
            {{-1.0F, -1.0F, 1.0F}, {-1.0F, 0.0F, 0.0F}},
            {{-1.0F, 1.0F, 1.0F}, {-1.0F, 0.0F, 0.0F}},
            {{-1.0F, 1.0F, -1.0F}, {-1.0F, 0.0F, 0.0F}},
            {{-1.0F, -1.0F, -1.0F}, {-1.0F, 0.0F, 0.0F}},
            {{1.0F, -1.0F, -1.0F}, {1.0F, 0.0F, 0.0F}},
            {{1.0F, 1.0F, -1.0F}, {1.0F, 0.0F, 0.0F}},
            {{1.0F, 1.0F, 1.0F}, {1.0F, 0.0F, 0.0F}},
            {{1.0F, -1.0F, 1.0F}, {1.0F, 0.0F, 0.0F}},
            {{-1.0F, 1.0F, -1.0F}, {0.0F, 1.0F, 0.0F}},
            {{-1.0F, 1.0F, 1.0F}, {0.0F, 1.0F, 0.0F}},
            {{1.0F, 1.0F, 1.0F}, {0.0F, 1.0F, 0.0F}},
            {{1.0F, 1.0F, -1.0F}, {0.0F, 1.0F, 0.0F}},
            {{-1.0F, -1.0F, 1.0F}, {0.0F, -1.0F, 0.0F}},
            {{-1.0F, -1.0F, -1.0F}, {0.0F, -1.0F, 0.0F}},
            {{1.0F, -1.0F, -1.0F}, {0.0F, -1.0F, 0.0F}},
            {{1.0F, -1.0F, 1.0F}, {0.0F, -1.0F, 0.0F}},
            {{-4.0F, -1.25F, -4.0F}, {0.0F, 1.0F, 0.0F}},
            {{-4.0F, -1.25F, 4.0F}, {0.0F, 1.0F, 0.0F}},
            {{4.0F, -1.25F, 4.0F}, {0.0F, 1.0F, 0.0F}},
            {{4.0F, -1.25F, -4.0F}, {0.0F, 1.0F, 0.0F}}
        }};
        constexpr std::array<std::uint16_t, 42> previewIndices{{
            0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7,
            8, 9, 10, 8, 10, 11, 12, 13, 14, 12, 14, 15,
            16, 17, 18, 16, 18, 19, 20, 21, 22, 20, 22, 23,
            24, 25, 26, 24, 26, 27
        }};
        Rhi::BufferDescription probeDescription{};
        probeDescription.Size = sizeof(previewVertices);
        probeDescription.Stride = sizeof(PreviewVertex);
        probeDescription.Usage = Rhi::BufferUsage::Vertex;
        probeDescription.Memory = Rhi::MemoryUsage::GpuOnly;
        probeDescription.DebugName = "Reference.RendererStartupProbe";
        m_PreviewVertexBuffer = m_Renderer->CreateBuffer(probeDescription, previewVertices.data(), error);
        if (!m_PreviewVertexBuffer)
        {
            return false;
        }
        Rhi::BufferDescription indexDescription{};
        indexDescription.Size = sizeof(previewIndices);
        indexDescription.Stride = sizeof(std::uint16_t);
        indexDescription.Usage = Rhi::BufferUsage::Index;
        indexDescription.Memory = Rhi::MemoryUsage::GpuOnly;
        indexDescription.DebugName = "Reference.PreviewCubeIndices";
        m_PreviewIndexBuffer = m_Renderer->CreateBuffer(indexDescription, previewIndices.data(), error);
        if (!m_PreviewIndexBuffer)
        {
            return false;
        }
        Rhi::BufferDescription constantDescription{};
        constantDescription.Size = sizeof(PreviewTransformConstants);
        constantDescription.Usage = Rhi::BufferUsage::Constant;
        constantDescription.Memory = Rhi::MemoryUsage::CpuToGpu;
        constantDescription.DebugName = "Reference.PreviewTransforms";
        m_PreviewTransformBuffer = m_Renderer->CreateBuffer(constantDescription, nullptr, error);
        if (!m_PreviewTransformBuffer)
        {
            return false;
        }
        constantDescription.DebugName = "Reference.GroundTransforms";
        m_GroundTransformBuffer = m_Renderer->CreateBuffer(constantDescription, nullptr, error);
        if (!m_GroundTransformBuffer)
        {
            return false;
        }
        constantDescription.Size = sizeof(PreviewPbrConstants);
        constantDescription.DebugName = "Reference.PreviewPbrMaterial";
        m_PreviewMaterialBuffer = m_Renderer->CreateBuffer(constantDescription, nullptr, error);
        if (!m_PreviewMaterialBuffer)
        {
            return false;
        }
        constantDescription.Size = sizeof(ShadowTransformConstants);
        constantDescription.DebugName = "Reference.ShadowTransforms";
        m_ShadowTransformBuffer = m_Renderer->CreateBuffer(constantDescription, nullptr, error);
        if (!m_ShadowTransformBuffer)
        {
            return false;
        }

        constexpr std::string_view previewShader = R"(
static const float PI = 3.14159265359;
cbuffer PreviewTransforms : register(b0)
{
    column_major float4x4 Model;
    column_major float4x4 ModelViewProjection;
    column_major float4x4 View;
};
cbuffer PreviewMaterial : register(b0)
{
    float4 BaseColor;
    float4 Material;
    float4 LightDirection;
    float4 LightColor;
    float4 CameraPosition;
    column_major float4x4 LightViewProjections[4];
    float4 CascadeSplits;
    float4 ShadowParameters;
    float4 CascadeParameters;
};
Texture2DArray<float> ShadowMap : register(t0);
SamplerComparisonState ShadowSampler : register(s0);
struct VertexInput { float3 Position : POSITION; float3 Normal : NORMAL; };
struct PixelInput
{
    float4 Position : SV_POSITION;
    float3 WorldPosition : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float ViewDepth : TEXCOORD2;
};
PixelInput VSMain(VertexInput input)
{
    PixelInput output;
    float4 worldPosition = mul(Model, float4(input.Position, 1.0));
    output.Position = mul(ModelViewProjection, float4(input.Position, 1.0));
    output.WorldPosition = worldPosition.xyz;
    output.Normal = normalize(mul((float3x3)Model, input.Normal));
    output.ViewDepth = mul(View, worldPosition).z;
    return output;
}

float DistributionGGX(float3 normal, float3 halfway, float roughness)
{
    float alpha = roughness * roughness;
    float alphaSquared = alpha * alpha;
    float normalDotHalfway = max(dot(normal, halfway), 0.0);
    float denominator = normalDotHalfway * normalDotHalfway * (alphaSquared - 1.0) + 1.0;
    return alphaSquared / max(PI * denominator * denominator, 0.000001);
}

float GeometrySchlickGGX(float normalDotDirection, float roughness)
{
    float radius = roughness + 1.0;
    float k = radius * radius / 8.0;
    return normalDotDirection / max(normalDotDirection * (1.0 - k) + k, 0.000001);
}

float GeometrySmith(float3 normal, float3 viewDirection, float3 lightDirection, float roughness)
{
    return GeometrySchlickGGX(max(dot(normal, viewDirection), 0.0), roughness) *
        GeometrySchlickGGX(max(dot(normal, lightDirection), 0.0), roughness);
}

float3 FresnelSchlick(float cosineTheta, float3 reflectanceAtNormal)
{
    return reflectanceAtNormal + (1.0 - reflectanceAtNormal) * pow(saturate(1.0 - cosineTheta), 5.0);
}

float ReadShadowDepth(float2 uv, uint cascadeIndex)
{
    uint width;
    uint height;
    uint layers;
    ShadowMap.GetDimensions(width, height, layers);
    int2 texel = clamp(int2(saturate(uv) * float2(width, height)), int2(0, 0), int2(width - 1, height - 1));
    return ShadowMap.Load(int4(texel, cascadeIndex, 0));
}

float FindAverageBlockerDepth(float2 shadowUv, float receiverDepth, uint cascadeIndex)
{
    float blockerDepth = 0.0;
    float blockerCount = 0.0;
    float searchRadius = clamp(Material.z * receiverDepth, ShadowParameters.w, ShadowParameters.w * 8.0);
    [unroll]
    for (int y = -2; y <= 2; ++y)
    {
        [unroll]
        for (int x = -2; x <= 2; ++x)
        {
            float depth = ReadShadowDepth(
                shadowUv + float2(x, y) * (searchRadius * 0.5), cascadeIndex);
            if (depth < receiverDepth)
            {
                blockerDepth += depth;
                blockerCount += 1.0;
            }
        }
    }
    return blockerCount > 0.0 ? blockerDepth / blockerCount : -1.0;
}

float EvaluateCascadeShadow(float3 worldPosition, float normalDotLight, uint cascadeIndex)
{
    float4 lightClip = mul(LightViewProjections[cascadeIndex], float4(worldPosition, 1.0));
    float3 projected = lightClip.xyz / lightClip.w;
    float2 shadowUv = float2(projected.x * 0.5 + 0.5, -projected.y * 0.5 + 0.5);
    if (projected.z <= 0.0 || projected.z >= 1.0 ||
        any(shadowUv < 0.0) || any(shadowUv > 1.0))
        return 1.0;
    float bias = ShadowParameters.x + ShadowParameters.y * 0.001 * (1.0 - normalDotLight);
    float visibility = 0.0;
    float sampleCount = 0.0;
    int filterMode = (int)CascadeParameters.w;
    int filterRadius = filterMode == 0 ? 1 : 2;
    float filterStep = ShadowParameters.w;
    if (filterMode == 2)
    {
        float receiverDepth = projected.z - bias;
        float averageBlockerDepth = FindAverageBlockerDepth(shadowUv, receiverDepth, cascadeIndex);
        if (averageBlockerDepth < 0.0)
            return 1.0;
        float penumbraRatio = max(receiverDepth - averageBlockerDepth, 0.0) /
            max(averageBlockerDepth, 0.0001);
        float filterRadiusUv = clamp(
            penumbraRatio * Material.z, ShadowParameters.w, ShadowParameters.w * 8.0);
        filterStep = filterRadiusUv * 0.5;
    }
    [unroll]
    for (int y = -2; y <= 2; ++y)
    {
        [unroll]
        for (int x = -2; x <= 2; ++x)
        {
            if (abs(x) > filterRadius || abs(y) > filterRadius)
                continue;
            visibility += ShadowMap.SampleCmpLevelZero(
                ShadowSampler,
                float3(shadowUv + float2(x, y) * filterStep, cascadeIndex),
                projected.z - bias);
            sampleCount += 1.0;
        }
    }
    return visibility / sampleCount;
}

float EvaluateShadow(float3 worldPosition, float normalDotLight, float viewDepth)
{
    if (ShadowParameters.z < 0.5)
        return 1.0;
    uint cascadeIndex = viewDepth > CascadeSplits.x ? 1 : 0;
    cascadeIndex = viewDepth > CascadeSplits.y ? 2 : cascadeIndex;
    cascadeIndex = viewDepth > CascadeSplits.z ? 3 : cascadeIndex;
    float visibility = EvaluateCascadeShadow(worldPosition, normalDotLight, cascadeIndex);
    if (cascadeIndex >= 3)
        return visibility;
    float cascadeNear = cascadeIndex == 0 ? CascadeParameters.y : CascadeSplits[cascadeIndex - 1];
    float cascadeFar = CascadeSplits[cascadeIndex];
    float blendStart = lerp(cascadeFar, cascadeNear, CascadeParameters.x);
    float blend = saturate((viewDepth - blendStart) / max(cascadeFar - blendStart, 0.0001));
    if (blend > 0.0)
    {
        float nextVisibility = EvaluateCascadeShadow(worldPosition, normalDotLight, cascadeIndex + 1);
        visibility = lerp(visibility, nextVisibility, blend);
    }
    return visibility;
}

float4 PSMain(PixelInput input) : SV_TARGET
{
    float3 normal = normalize(input.Normal);
    float3 viewDirection = normalize(CameraPosition.xyz - input.WorldPosition);
    float3 lightDirection = normalize(LightDirection.xyz);
    float3 halfway = normalize(viewDirection + lightDirection);
    float metallic = saturate(Material.x);
    float roughness = clamp(Material.y, 0.04, 1.0);
    float3 baseColor = max(BaseColor.rgb, 0.0);
    float3 reflectanceAtNormal = lerp(0.04.xxx, baseColor, metallic);
    float3 fresnel = FresnelSchlick(max(dot(halfway, viewDirection), 0.0), reflectanceAtNormal);
    float distribution = DistributionGGX(normal, halfway, roughness);
    float geometry = GeometrySmith(normal, viewDirection, lightDirection, roughness);
    float normalDotView = max(dot(normal, viewDirection), 0.0);
    float normalDotLight = max(dot(normal, lightDirection), 0.0);
    float3 specular = distribution * geometry * fresnel /
        max(4.0 * normalDotView * normalDotLight, 0.0001);
    float3 diffuseWeight = (1.0 - fresnel) * (1.0 - metallic);
    float3 radiance = LightColor.rgb * LightColor.a;
    float shadow = EvaluateShadow(input.WorldPosition, normalDotLight, input.ViewDepth);
    float3 directLighting =
        (diffuseWeight * baseColor / PI + specular) * radiance * normalDotLight * shadow;
    float3 linearHdr = baseColor * Material.w + directLighting;
    return float4(linearHdr, BaseColor.a);
}
)";
        Rhi::GraphicsPipelineDescription previewPipelineDescription{};
        previewPipelineDescription.VertexShaderSource = previewShader;
        previewPipelineDescription.PixelShaderSource = previewShader;
        previewPipelineDescription.VertexLayout = {
            {"POSITION", 0, Rhi::VertexFormat::Float3, 0},
            {"NORMAL", 0, Rhi::VertexFormat::Float3, sizeof(float) * 3}};
        previewPipelineDescription.Cull = Rhi::CullMode::None;
        previewPipelineDescription.DepthTest = true;
        previewPipelineDescription.DepthWrite = true;
        previewPipelineDescription.DebugName = "Reference.ViewportPreview";
        m_PreviewPipeline = m_Renderer->CreateGraphicsPipeline(previewPipelineDescription, error);
        if (!m_PreviewPipeline)
        {
            m_Renderer->DestroyBuffer(m_PreviewVertexBuffer);
            m_PreviewVertexBuffer = {};
            return false;
        }
        m_PreviewDraw.Pipeline = m_PreviewPipeline;
        m_PreviewDraw.VertexBuffer = m_PreviewVertexBuffer;
        m_PreviewDraw.IndexBuffer = m_PreviewIndexBuffer;
        m_PreviewDraw.VertexConstantBuffer = m_PreviewTransformBuffer;
        m_PreviewDraw.PixelConstantBuffer = m_PreviewMaterialBuffer;
        m_PreviewDraw.IndexCount = 36;
        m_PreviewDraw.IndexStride = sizeof(std::uint16_t);
        m_PreviewDraw.VertexStride = sizeof(PreviewVertex);
        m_GroundDraw = m_PreviewDraw;
        m_GroundDraw.VertexConstantBuffer = m_GroundTransformBuffer;
        m_GroundDraw.IndexCount = 6;
        m_GroundDraw.FirstIndex = 36;

        Rhi::TextureDescription shadowMapDescription{};
        shadowMapDescription.Width = m_PreviewShadowSettings.Resolution;
        shadowMapDescription.Height = m_PreviewShadowSettings.Resolution;
        shadowMapDescription.ArrayLayers = m_PreviewShadowSettings.CascadeCount;
        shadowMapDescription.Format = Rhi::TextureFormat::D32Float;
        shadowMapDescription.Usage = Rhi::TextureUsage::DepthStencil | Rhi::TextureUsage::Sampled;
        shadowMapDescription.DebugName = "Reference.DirectionalShadowMap";
        m_PreviewShadowMap = m_Renderer->CreateTexture(shadowMapDescription, error);
        if (!m_PreviewShadowMap)
        {
            return false;
        }

        Rhi::SamplerDescription shadowSamplerDescription{};
        shadowSamplerDescription.Filter = Rhi::SamplerFilter::Nearest;
        shadowSamplerDescription.AddressU = Rhi::SamplerAddressMode::ClampToBorder;
        shadowSamplerDescription.AddressV = Rhi::SamplerAddressMode::ClampToBorder;
        shadowSamplerDescription.AddressW = Rhi::SamplerAddressMode::ClampToBorder;
        shadowSamplerDescription.Comparison = Rhi::CompareOperation::LessEqual;
        shadowSamplerDescription.BorderColor[0] = 1.0F;
        shadowSamplerDescription.BorderColor[1] = 1.0F;
        shadowSamplerDescription.BorderColor[2] = 1.0F;
        shadowSamplerDescription.BorderColor[3] = 1.0F;
        shadowSamplerDescription.DebugName = "Reference.PcfShadowSampler";
        m_PreviewShadowSampler = m_Renderer->CreateSampler(shadowSamplerDescription, error);
        if (!m_PreviewShadowSampler)
        {
            return false;
        }

        constexpr std::string_view shadowShader = R"(
cbuffer ShadowTransforms : register(b0)
{
    column_major float4x4 LightModelViewProjection;
};
struct VertexInput { float3 Position : POSITION; };
float4 VSMain(VertexInput input) : SV_POSITION
{
    return mul(LightModelViewProjection, float4(input.Position, 1.0));
}
void PSMain() {}
)";
        Rhi::GraphicsPipelineDescription shadowPipelineDescription{};
        shadowPipelineDescription.VertexShaderSource = shadowShader;
        shadowPipelineDescription.PixelShaderSource = shadowShader;
        shadowPipelineDescription.VertexLayout = {
            {"POSITION", 0, Rhi::VertexFormat::Float3, 0}};
        shadowPipelineDescription.Cull = Rhi::CullMode::Back;
        shadowPipelineDescription.DepthTest = true;
        shadowPipelineDescription.DepthWrite = true;
        shadowPipelineDescription.DebugName = "Reference.DirectionalShadowDepth";
        m_ShadowPipeline = m_Renderer->CreateGraphicsPipeline(shadowPipelineDescription, error);
        if (!m_ShadowPipeline)
        {
            return false;
        }
        m_ShadowDraw.Pipeline = m_ShadowPipeline;
        m_ShadowDraw.VertexBuffer = m_PreviewVertexBuffer;
        m_ShadowDraw.IndexBuffer = m_PreviewIndexBuffer;
        m_ShadowDraw.VertexConstantBuffer = m_ShadowTransformBuffer;
        m_ShadowDraw.IndexCount = 36;
        m_ShadowDraw.IndexStride = sizeof(std::uint16_t);
        m_ShadowDraw.VertexStride = sizeof(PreviewVertex);
        m_ShadowDraw.ViewportWidth = static_cast<float>(m_PreviewShadowSettings.Resolution);
        m_ShadowDraw.ViewportHeight = static_cast<float>(m_PreviewShadowSettings.Resolution);
        m_GroundShadowDraw = m_ShadowDraw;
        m_GroundShadowDraw.IndexCount = 6;
        m_GroundShadowDraw.FirstIndex = 36;
        m_PreviewDraw.PixelTextures[0] = m_PreviewShadowMap;
        m_PreviewDraw.PixelSamplers[0] = m_PreviewShadowSampler;
        m_GroundDraw.PixelTextures[0] = m_PreviewShadowMap;
        m_GroundDraw.PixelSamplers[0] = m_PreviewShadowSampler;

        constexpr std::array<ToneMapVertex, 3> toneMapVertices{{
            {{-1.0F, -1.0F}, {0.0F, 1.0F}},
            {{-1.0F, 3.0F}, {0.0F, -1.0F}},
            {{3.0F, -1.0F}, {2.0F, 1.0F}}
        }};
        Rhi::BufferDescription toneMapVertexDescription{};
        toneMapVertexDescription.Size = sizeof(toneMapVertices);
        toneMapVertexDescription.Stride = sizeof(ToneMapVertex);
        toneMapVertexDescription.Usage = Rhi::BufferUsage::Vertex;
        toneMapVertexDescription.Memory = Rhi::MemoryUsage::GpuOnly;
        toneMapVertexDescription.DebugName = "Reference.ToneMapTriangle";
        m_ToneMapVertexBuffer =
            m_Renderer->CreateBuffer(toneMapVertexDescription, toneMapVertices.data(), error);
        if (!m_ToneMapVertexBuffer)
        {
            return false;
        }
        Rhi::BufferDescription toneMapConstantDescription{};
        toneMapConstantDescription.Size = sizeof(ToneMapConstants);
        toneMapConstantDescription.Usage = Rhi::BufferUsage::Constant;
        toneMapConstantDescription.Memory = Rhi::MemoryUsage::CpuToGpu;
        toneMapConstantDescription.DebugName = "Reference.ToneMapConstants";
        m_ToneMapConstantBuffer =
            m_Renderer->CreateBuffer(toneMapConstantDescription, nullptr, error);
        if (!m_ToneMapConstantBuffer)
        {
            return false;
        }

        Rhi::SamplerDescription linearSamplerDescription{};
        linearSamplerDescription.Filter = Rhi::SamplerFilter::Linear;
        linearSamplerDescription.AddressU = Rhi::SamplerAddressMode::ClampToEdge;
        linearSamplerDescription.AddressV = Rhi::SamplerAddressMode::ClampToEdge;
        linearSamplerDescription.AddressW = Rhi::SamplerAddressMode::ClampToEdge;
        linearSamplerDescription.DebugName = "Reference.HdrLinearSampler";
        m_PreviewLinearSampler = m_Renderer->CreateSampler(linearSamplerDescription, error);
        if (!m_PreviewLinearSampler)
        {
            return false;
        }

        constexpr std::string_view toneMapShader = R"(
cbuffer ToneMapConstants : register(b0)
{
    float4 ToneParameters;
    float4 ContactLightDirection;
    float4 ProjectionParameters;
};
Texture2D<float4> HdrColor : register(t0);
Texture2D<float> ViewDepth : register(t1);
SamplerState LinearSampler : register(s0);
struct VertexInput { float2 Position : POSITION; float2 TextureCoordinate : TEXCOORD0; };
struct PixelInput { float4 Position : SV_POSITION; float2 TextureCoordinate : TEXCOORD0; };
PixelInput VSMain(VertexInput input)
{
    PixelInput output;
    output.Position = float4(input.Position, 0.0, 1.0);
    output.TextureCoordinate = input.TextureCoordinate;
    return output;
}
float3 AcesFilm(float3 color)
{
    return saturate((color * (2.51 * color + 0.03)) /
        (color * (2.43 * color + 0.59) + 0.14));
}

float DeviceDepthToViewDepth(float deviceDepth)
{
    float nearPlane = ProjectionParameters.z;
    float farPlane = ProjectionParameters.w;
    return nearPlane * farPlane /
        max(farPlane - deviceDepth * (farPlane - nearPlane), 0.0001);
}

float2 ProjectViewPosition(float3 viewPosition)
{
    float2 ndc = viewPosition.xy * ProjectionParameters.xy / viewPosition.z;
    return float2(ndc.x * 0.5 + 0.5, -ndc.y * 0.5 + 0.5);
}

float EvaluateContactShadow(float2 uv, float deviceDepth)
{
    float strength = ToneParameters.y;
    if (strength <= 0.0 || deviceDepth >= 0.99999)
        return 1.0;

    float viewDepth = DeviceDepthToViewDepth(deviceDepth);
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float3 rayPosition = float3(
        ndc.x * viewDepth / ProjectionParameters.x,
        ndc.y * viewDepth / ProjectionParameters.y,
        viewDepth);
    float3 rayDirection = normalize(ContactLightDirection.xyz);
    int stepCount = clamp((int)ContactLightDirection.w, 4, 32);
    float stepLength = ToneParameters.z / stepCount;
    float thickness = ToneParameters.w;
    rayPosition += rayDirection * max(stepLength, thickness);

    uint depthWidth;
    uint depthHeight;
    ViewDepth.GetDimensions(depthWidth, depthHeight);
    [loop]
    for (int stepIndex = 0; stepIndex < 32; ++stepIndex)
    {
        if (stepIndex >= stepCount || rayPosition.z <= ProjectionParameters.z)
            break;
        float2 rayUv = ProjectViewPosition(rayPosition);
        if (any(rayUv <= 0.0) || any(rayUv >= 1.0))
            break;
        int2 texel = clamp(
            int2(rayUv * float2(depthWidth, depthHeight)),
            int2(0, 0), int2(depthWidth - 1, depthHeight - 1));
        float sceneDeviceDepth = ViewDepth.Load(int3(texel, 0));
        float sceneViewDepth = DeviceDepthToViewDepth(sceneDeviceDepth);
        float separation = rayPosition.z - sceneViewDepth;
        if (separation > 0.0 && separation < thickness)
        {
            float distanceFade = 1.0 - (float)stepIndex / stepCount;
            return 1.0 - strength * distanceFade;
        }
        rayPosition += rayDirection * stepLength;
    }
    return 1.0;
}

float4 PSMain(PixelInput input) : SV_TARGET
{
    float4 hdr = HdrColor.Sample(LinearSampler, input.TextureCoordinate);
    uint depthWidth;
    uint depthHeight;
    ViewDepth.GetDimensions(depthWidth, depthHeight);
    int2 depthTexel = clamp(
        int2(saturate(input.TextureCoordinate) * float2(depthWidth, depthHeight)),
        int2(0, 0), int2(depthWidth - 1, depthHeight - 1));
    float deviceDepth = ViewDepth.Load(int3(depthTexel, 0));
    float contactShadow = EvaluateContactShadow(input.TextureCoordinate, deviceDepth);
    float3 displayColor = AcesFilm(max(hdr.rgb, 0.0) * ToneParameters.x * contactShadow);
    displayColor = pow(displayColor, 1.0 / 2.2);
    return float4(displayColor, hdr.a);
}
)";
        Rhi::GraphicsPipelineDescription toneMapPipelineDescription{};
        toneMapPipelineDescription.VertexShaderSource = toneMapShader;
        toneMapPipelineDescription.PixelShaderSource = toneMapShader;
        toneMapPipelineDescription.VertexLayout = {
            {"POSITION", 0, Rhi::VertexFormat::Float2, 0},
            {"TEXCOORD", 0, Rhi::VertexFormat::Float2, sizeof(float) * 2}};
        toneMapPipelineDescription.Cull = Rhi::CullMode::None;
        toneMapPipelineDescription.DepthTest = false;
        toneMapPipelineDescription.DepthWrite = false;
        toneMapPipelineDescription.DebugName = "Reference.ViewportToneMap";
        m_ToneMapPipeline = m_Renderer->CreateGraphicsPipeline(toneMapPipelineDescription, error);
        if (!m_ToneMapPipeline)
        {
            return false;
        }
        m_ToneMapDraw.Pipeline = m_ToneMapPipeline;
        m_ToneMapDraw.VertexBuffer = m_ToneMapVertexBuffer;
        m_ToneMapDraw.PixelConstantBuffer = m_ToneMapConstantBuffer;
        m_ToneMapDraw.PixelSamplers[0] = m_PreviewLinearSampler;
        m_ToneMapDraw.PixelSamplers[1] = m_PreviewLinearSampler;
        m_ToneMapDraw.VertexCount = static_cast<std::uint32_t>(toneMapVertices.size());
        m_ToneMapDraw.VertexStride = sizeof(ToneMapVertex);


    return true;
}
    bool ReferenceKernel::EnsurePreviewTargets(
        std::uint32_t width, std::uint32_t height, std::string& error)
    {
        if (m_PreviewHdrColor && m_PreviewDepth &&
            width == m_PreviewTargetWidth && height == m_PreviewTargetHeight)
            return true;

        Rhi::TextureDescription colorDescription{};
        colorDescription.Width = width;
        colorDescription.Height = height;
        colorDescription.Format = Rhi::TextureFormat::Rgba16Float;
        colorDescription.Usage = Rhi::TextureUsage::Sampled | Rhi::TextureUsage::RenderTarget;
        colorDescription.DebugName = "Reference.ViewportHdrColor";
        const Rhi::TextureHandle newColor = m_Renderer->CreateTexture(colorDescription, error);
        if (!newColor)
            return false;

        Rhi::TextureDescription depthDescription{};
        depthDescription.Width = width;
        depthDescription.Height = height;
        depthDescription.Format = Rhi::TextureFormat::D32Float;
        depthDescription.Usage = Rhi::TextureUsage::DepthStencil | Rhi::TextureUsage::Sampled;
        depthDescription.DebugName = "Reference.ViewportDepth";
        const Rhi::TextureHandle newDepth = m_Renderer->CreateTexture(depthDescription, error);
        if (!newDepth)
        {
            m_Renderer->DestroyTexture(newColor);
            return false;
        }

        m_Renderer->DestroyTexture(m_PreviewDepth);
        m_Renderer->DestroyTexture(m_PreviewHdrColor);
        m_PreviewHdrColor = newColor;
        m_PreviewDepth = newDepth;
        m_PreviewTargetWidth = width;
        m_PreviewTargetHeight = height;
        return true;
    }

bool ReferenceKernel::Prepare(uint32_t targetWidth,uint32_t targetHeight,const float* model,float exposure,float metallic,float roughness,std::string& error) {
    if(!EnsurePreviewTargets(targetWidth,targetHeight,error)) return false;
    m_PreviewCubeModel=Eigen::Map<const Matrix4>(model); m_PreviewExposure=exposure;
    m_PreviewMaterial.Metallic=metallic; m_PreviewMaterial.Roughness=roughness;
    const float aspect=static_cast<float>(targetWidth)/static_cast<float>(targetHeight);
        m_PreviewGroundModel = Matrix4::Identity();
        Matrix4 view = Matrix4::Identity();
        view(2, 3) = 5.0F;
        const float verticalScale = 1.0F / std::tan(52.0F * 3.1415926535F / 360.0F);
        constexpr float nearPlane = 0.1F;
        constexpr float farPlane = 100.0F;
        Matrix4 projection = Matrix4::Zero();
        projection(0, 0) = verticalScale / aspect;
        projection(1, 1) = verticalScale;
        projection(2, 2) = farPlane / (farPlane - nearPlane);
        projection(2, 3) = (-nearPlane * farPlane) / (farPlane - nearPlane);
        projection(3, 2) = 1.0F;
        PreviewTransformConstants transforms;
        transforms.Model = m_PreviewCubeModel;
        transforms.ModelViewProjection = projection * view * m_PreviewCubeModel;
        transforms.View = view;
        if (!m_Renderer->UpdateBuffer(
            m_PreviewTransformBuffer, &transforms, sizeof(transforms), 0, error))
        {
            return false;
        }
        transforms.Model = m_PreviewGroundModel;
        transforms.ModelViewProjection = projection * view * m_PreviewGroundModel;
        if (!m_Renderer->UpdateBuffer(
            m_GroundTransformBuffer, &transforms, sizeof(transforms), 0, error))
        {
            return false;
        }
        m_PreviewShadowSettings.Sanitize();
        const Vector3 lightDirection(-0.45F, 0.78F, -0.55F);
        const Vector3 lightForward = -lightDirection.normalized();
        const Vector3 up = std::abs(lightForward.dot(Vector3::UnitY())) > 0.98F
            ? Vector3::UnitZ() : Vector3::UnitY();
        const Vector3 lightRight = up.cross(lightForward).normalized();
        const Vector3 lightUp = lightForward.cross(lightRight);
        const Vector3 cameraPosition(0.0F, 0.0F, -5.0F);
        float cascadeNear = nearPlane;
        const float shadowDistance = std::min(m_PreviewShadowSettings.MaxDistance, farPlane);
        for (std::uint32_t cascade = 0;
             cascade < m_PreviewShadowSettings.CascadeCount; ++cascade)
        {
            const float fraction =
                static_cast<float>(cascade + 1) /
                static_cast<float>(m_PreviewShadowSettings.CascadeCount);
            const float logarithmic = nearPlane * std::pow(shadowDistance / nearPlane, fraction);
            const float linear = nearPlane + (shadowDistance - nearPlane) * fraction;
            const float cascadeFar = std::lerp(
                linear, logarithmic, m_PreviewShadowSettings.CascadeLambda);
            m_CascadeSplits[cascade] = cascadeFar;

            std::array<Vector3, 8> corners{};
            std::size_t cornerIndex = 0;
            for (const float distance : {cascadeNear, cascadeFar})
            {
                const float halfHeight = distance / verticalScale;
                const float halfWidth = halfHeight * aspect;
                for (const float y : {-1.0F, 1.0F})
                {
                    for (const float x : {-1.0F, 1.0F})
                    {
                        corners[cornerIndex++] = cameraPosition +
                            Vector3(x * halfWidth, y * halfHeight, distance);
                    }
                }
            }

            Vector3 cascadeCenter = Vector3::Zero();
            for (const Vector3& corner : corners)
                cascadeCenter += corner;
            cascadeCenter /= static_cast<float>(corners.size());
            float radius = 0.0F;
            for (const Vector3& corner : corners)
                radius = std::max(radius, (corner - cascadeCenter).norm());
            radius = std::ceil(radius * 16.0F) / 16.0F;

            const Vector3 lightPosition = cascadeCenter - lightForward * (radius + 20.0F);
            Matrix4 lightView = Matrix4::Identity();
            lightView.block<1, 3>(0, 0) = lightRight.transpose();
            lightView.block<1, 3>(1, 0) = lightUp.transpose();
            lightView.block<1, 3>(2, 0) = lightForward.transpose();
            lightView(0, 3) = -lightRight.dot(lightPosition);
            lightView(1, 3) = -lightUp.dot(lightPosition);
            lightView(2, 3) = -lightForward.dot(lightPosition);

            const Vector4 centerLight = lightView * Vector4(
                cascadeCenter.x(), cascadeCenter.y(), cascadeCenter.z(), 1.0F);
            const float texelSize = (2.0F * radius) /
                static_cast<float>(m_PreviewShadowSettings.Resolution);
            const float snappedX = std::floor(centerLight.x() / texelSize) * texelSize;
            const float snappedY = std::floor(centerLight.y() / texelSize) * texelSize;
            constexpr float depthPadding = 30.0F;
            const float left = snappedX - radius;
            const float right = snappedX + radius;
            const float bottom = snappedY - radius;
            const float top = snappedY + radius;
            const float lightNear = std::max(0.0F, centerLight.z() - radius - depthPadding);
            const float lightFar = centerLight.z() + radius + depthPadding;
            Matrix4 lightProjection = Matrix4::Zero();
            lightProjection(0, 0) = 2.0F / (right - left);
            lightProjection(1, 1) = 2.0F / (top - bottom);
            lightProjection(2, 2) = 1.0F / (lightFar - lightNear);
            lightProjection(0, 3) = -(right + left) / (right - left);
            lightProjection(1, 3) = -(top + bottom) / (top - bottom);
            lightProjection(2, 3) = -lightNear / (lightFar - lightNear);
            lightProjection(3, 3) = 1.0F;
            m_CascadeLightViewProjections[cascade] = lightProjection * lightView;
            cascadeNear = cascadeFar;
        }
        m_PreviewMaterial.Sanitize();
        m_PreviewShadowSettings.Sanitize();
        PreviewPbrConstants material;
        std::copy_n(m_PreviewMaterial.BaseColor, 4, material.BaseColor.begin());
        material.Material = {
            m_PreviewMaterial.Metallic, m_PreviewMaterial.Roughness,
            m_PreviewShadowSettings.LightRadius, m_PreviewAmbient};
        material.LightDirection = {
            lightDirection.x(), lightDirection.y(), lightDirection.z(), 0.0F};
        material.LightColor = {1.0F, 0.93F, 0.82F, m_PreviewLightIntensity};
        material.CameraPosition = {
            cameraPosition.x(), cameraPosition.y(), cameraPosition.z(), 1.0F};
        material.LightViewProjections = m_CascadeLightViewProjections;
        material.CascadeSplits = m_CascadeSplits;
        material.ShadowParameters = {
            m_PreviewShadowSettings.ConstantBias,
            m_PreviewShadowSettings.SlopeBias,
            m_PreviewShadowSettings.Enabled ? 1.0F : 0.0F,
            1.0F / static_cast<float>(m_PreviewShadowSettings.Resolution)};
        material.CascadeParameters = {
            0.10F, nearPlane,
            static_cast<float>(m_PreviewShadowSettings.CascadeCount),
            static_cast<float>(m_PreviewShadowSettings.Filter)};
        if (!m_Renderer->UpdateBuffer(
            m_PreviewMaterialBuffer, &material, sizeof(material), 0, error))
        {
            return false;
        }
        m_PreviewContactShadowSettings.Sanitize();
        ToneMapConstants toneMapConstants;
        toneMapConstants.ToneParameters = {
            m_PreviewExposure,
            m_PreviewContactShadowSettings.Enabled ? m_PreviewContactShadowSettings.Strength : 0.0F,
            m_PreviewContactShadowSettings.MaxDistance,
            m_PreviewContactShadowSettings.Thickness};
        toneMapConstants.LightDirection = {
            lightDirection.x(), lightDirection.y(), lightDirection.z(),
            static_cast<float>(m_PreviewContactShadowSettings.StepCount)};
        toneMapConstants.ProjectionParameters = {
            projection(0, 0), projection(1, 1), nearPlane, farPlane};
        std::memcpy(m_ToneData.data(), &toneMapConstants, sizeof(toneMapConstants));
        if (!m_Renderer->UpdateBuffer(
            m_ToneMapConstantBuffer, &toneMapConstants, sizeof(toneMapConstants), 0, error))
        {
            return false;
        }
        m_PreviewDraw.ViewportX = 0.0F;
        m_PreviewDraw.ViewportY = 0.0F;
        m_PreviewDraw.ViewportWidth = static_cast<float>(targetWidth);
        m_PreviewDraw.ViewportHeight = static_cast<float>(targetHeight);
        m_GroundDraw.ViewportWidth = m_PreviewDraw.ViewportWidth;
        m_GroundDraw.ViewportHeight = m_PreviewDraw.ViewportHeight;
        m_ToneMapDraw.PixelTextures[0] = m_PreviewHdrColor;
        m_ToneMapDraw.PixelTextures[1] = m_PreviewDepth;

    return true;
}
bool ReferenceKernel::Shadow(std::string& error) {
                if (!m_PreviewShadowSettings.Enabled)
                    return true;
                for (std::uint32_t cascade = 0;
                     cascade < m_PreviewShadowSettings.CascadeCount; ++cascade)
                {
                    Rhi::RenderPassDescription pass{};
                    pass.UseColorTarget = false;
                    pass.DepthTarget = m_PreviewShadowMap;
                    pass.DepthLayer = cascade;
                    pass.ClearDepth = true;
                    pass.DebugName = "Reference.DirectionalShadow";
                    if (!m_Renderer->BeginRenderPass(pass, error))
                        return false;
                    ShadowTransformConstants shadowTransforms;
                    shadowTransforms.LightModelViewProjection =
                        m_CascadeLightViewProjections[cascade] * m_PreviewCubeModel;
                    if (!m_Renderer->UpdateBuffer(
                        m_ShadowTransformBuffer, &shadowTransforms,
                        sizeof(shadowTransforms), 0, error) ||
                        !m_Renderer->Draw(m_ShadowDraw, error))
                    {
                        m_Renderer->EndRenderPass();
                        return false;
                    }
                    shadowTransforms.LightModelViewProjection =
                        m_CascadeLightViewProjections[cascade] * m_PreviewGroundModel;
                    if (!m_Renderer->UpdateBuffer(
                        m_ShadowTransformBuffer, &shadowTransforms,
                        sizeof(shadowTransforms), 0, error) ||
                        !m_Renderer->Draw(m_GroundShadowDraw, error))
                    {
                        m_Renderer->EndRenderPass();
                        return false;
                    }
                    m_Renderer->EndRenderPass();
                }
                return true;

}
bool ReferenceKernel::Geometry(std::string& error) {
                Rhi::RenderPassDescription pass{};
                pass.ColorTarget = m_PreviewHdrColor;
                pass.DepthTarget = m_PreviewDepth;
                pass.ClearColor = true;
                pass.ClearDepth = true;
                pass.ClearColorValue[0] = 0.012F;
                pass.ClearColorValue[1] = 0.016F;
                pass.ClearColorValue[2] = 0.026F;
                pass.DebugName = "Reference.PbrGeometry";
                if (!m_Renderer->BeginRenderPass(pass, error))
                    return false;
                const bool result = m_Renderer->Draw(m_PreviewDraw, error) &&
                    m_Renderer->Draw(m_GroundDraw, error);
                m_Renderer->EndRenderPass();
                return result;

}
bool ReferenceKernel::ToneMap(float exposureOverride, float x,float y,float width,float height,std::string& error) {
    ToneMapConstants constants;
    std::memcpy(&constants,m_ToneData.data(),sizeof(constants));
    if(exposureOverride!=0) constants.ToneParameters[0]=exposureOverride;
    if(!m_Renderer->UpdateBuffer(m_ToneMapConstantBuffer,&constants,sizeof(constants),0,error)) return false;
    m_ToneMapDraw.ViewportX=x; m_ToneMapDraw.ViewportY=y;
    m_ToneMapDraw.ViewportWidth=width; m_ToneMapDraw.ViewportHeight=height;

                Rhi::RenderPassDescription pass{};
                pass.DebugName = "Reference.ToneMapping";
                if (!m_Renderer->BeginRenderPass(pass, error))
                    return false;
                const bool result = m_Renderer->Draw(m_ToneMapDraw, error);
                m_Renderer->EndRenderPass();
                return result;

}
}
