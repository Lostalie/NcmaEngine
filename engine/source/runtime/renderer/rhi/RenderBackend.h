#pragma once

#include "renderer/rhi/RhiResources.h"

#include <cstdint>
#include <memory>
#include <string>
#include <string_view>

namespace NcmaEngine::Rhi
{
    enum class BackendType : std::uint8_t
    {
        Direct3D11,
        Vulkan,
        Null
    };

    enum class Feature : std::uint32_t
    {
        None = 0,
        ComputeShaders = 1U << 0U,
        TextureArrays = 1U << 1U,
        TimestampQueries = 1U << 2U,
        ShadowComparisonSamplers = 1U << 3U,
        BindlessResources = 1U << 4U
    };

    [[nodiscard]] constexpr Feature operator|(Feature lhs, Feature rhs)
    {
        return static_cast<Feature>(static_cast<std::uint32_t>(lhs) | static_cast<std::uint32_t>(rhs));
    }

    [[nodiscard]] constexpr bool HasFeature(Feature set, Feature feature)
    {
        return (static_cast<std::uint32_t>(set) & static_cast<std::uint32_t>(feature)) != 0;
    }

    struct BackendCapabilities final
    {
        Feature Features = Feature::None;
        std::uint32_t MaxTextureDimension2D = 0;
        std::uint32_t MaxColorAttachments = 0;
        bool SupportsPbrPipeline = false;
        bool SupportsSoftShadows = false;
    };

    struct BackendCreateInfo final
    {
        void* NativeWindow = nullptr;
        std::uint32_t Width = 1280;
        std::uint32_t Height = 720;
        bool EnableValidation = false;
        bool EnableVSync = true;
    };

    class IRenderBackend
    {
    public:
        virtual ~IRenderBackend() = default;
        [[nodiscard]] virtual BackendType GetType() const noexcept = 0;
        [[nodiscard]] virtual std::string_view GetName() const noexcept = 0;
        [[nodiscard]] virtual const BackendCapabilities& GetCapabilities() const noexcept = 0;
        virtual bool Initialize(const BackendCreateInfo& createInfo, std::string& error) = 0;
        virtual void Shutdown() noexcept = 0;
        virtual bool BeginFrame(std::string& error) = 0;
        virtual void EndFrame() = 0;
        virtual bool BeginRenderPass(const RenderPassDescription& description, std::string& error) = 0;
        virtual void EndRenderPass() = 0;
        virtual void Resize(std::uint32_t width, std::uint32_t height) = 0;
        [[nodiscard]] virtual BufferHandle CreateBuffer(
            const BufferDescription& description, const void* initialData, std::string& error) = 0;
        virtual void DestroyBuffer(BufferHandle buffer) noexcept = 0;
        virtual bool UpdateBuffer(
            BufferHandle buffer, const void* data, std::size_t size, std::size_t offset,
            std::string& error) = 0;
        [[nodiscard]] virtual TextureHandle CreateTexture(
            const TextureDescription& description, std::string& error) = 0;
        virtual void DestroyTexture(TextureHandle texture) noexcept = 0;
        [[nodiscard]] virtual SamplerHandle CreateSampler(
            const SamplerDescription& description, std::string& error) = 0;
        virtual void DestroySampler(SamplerHandle sampler) noexcept = 0;
        [[nodiscard]] virtual GraphicsPipelineHandle CreateGraphicsPipeline(
            const GraphicsPipelineDescription& description, std::string& error) = 0;
        virtual void DestroyGraphicsPipeline(GraphicsPipelineHandle pipeline) noexcept = 0;
        virtual bool Draw(const DrawDescription& description, std::string& error) = 0;
    };

    using BackendFactory = std::unique_ptr<IRenderBackend>(*)();
}
