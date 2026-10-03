#include "renderer/rhi/RenderBackendRegistry.h"

#include <algorithm>
#include <stdexcept>

#if defined(_WIN32)
#include "renderer/rhi/d3d11/D3D11RenderBackend.h"
#endif

namespace NcmaEngine::Rhi
{
    namespace
    {
        class NullRenderBackend final : public IRenderBackend
        {
        public:
            BackendType GetType() const noexcept override { return BackendType::Null; }
            std::string_view GetName() const noexcept override { return "Null"; }
            const BackendCapabilities& GetCapabilities() const noexcept override { return m_Capabilities; }

            bool Initialize(const BackendCreateInfo&, std::string&) override
            {
                m_Initialized = true;
                return true;
            }

            void Shutdown() noexcept override { m_Initialized = false; }
            bool BeginFrame(std::string& error) override
            {
                if (!m_Initialized)
                {
                    error = "Null backend has not been initialized";
                    return false;
                }
                return true;
            }
            void EndFrame() override {}
            bool BeginRenderPass(const RenderPassDescription& description, std::string& error) override
            {
                if (!m_Initialized)
                {
                    error = "Null backend has not been initialized";
                    return false;
                }
                return Validate(description, error);
            }
            void EndRenderPass() override {}
            void Resize(std::uint32_t, std::uint32_t) override {}
            BufferHandle CreateBuffer(
                const BufferDescription& description, const void*, std::string& error) override
            {
                if (!m_Initialized || !Validate(description, error))
                {
                    if (!m_Initialized)
                        error = "Null backend has not been initialized";
                    return {};
                }
                return {m_NextBuffer++};
            }
            void DestroyBuffer(BufferHandle) noexcept override {}
            bool UpdateBuffer(
                BufferHandle buffer, const void* data, std::size_t size, std::size_t,
                std::string& error) override
            {
                if (!m_Initialized || !buffer || data == nullptr || size == 0)
                {
                    error = !m_Initialized ? "Null backend has not been initialized" :
                        "Buffer updates require a valid handle, data, and size";
                    return false;
                }
                error.clear();
                return true;
            }
            TextureHandle CreateTexture(
                const TextureDescription& description, std::string& error) override
            {
                if (!m_Initialized || !Validate(description, error))
                {
                    if (!m_Initialized)
                        error = "Null backend has not been initialized";
                    return {};
                }
                return {m_NextTexture++};
            }
            void DestroyTexture(TextureHandle) noexcept override {}
            SamplerHandle CreateSampler(
                const SamplerDescription& description, std::string& error) override
            {
                if (!m_Initialized || !Validate(description, error))
                {
                    if (!m_Initialized)
                        error = "Null backend has not been initialized";
                    return {};
                }
                return {m_NextSampler++};
            }
            void DestroySampler(SamplerHandle) noexcept override {}
            GraphicsPipelineHandle CreateGraphicsPipeline(
                const GraphicsPipelineDescription& description, std::string& error) override
            {
                if (!m_Initialized || !Validate(description, error))
                {
                    if (!m_Initialized)
                        error = "Null backend has not been initialized";
                    return {};
                }
                return {m_NextPipeline++};
            }
            void DestroyGraphicsPipeline(GraphicsPipelineHandle) noexcept override {}
            bool Draw(const DrawDescription& description, std::string& error) override
            {
                if (!m_Initialized)
                {
                    error = "Null backend has not been initialized";
                    return false;
                }
                return Validate(description, error);
            }

        private:
            BackendCapabilities m_Capabilities{};
            bool m_Initialized = false;
            std::uint64_t m_NextBuffer = 1;
            std::uint64_t m_NextTexture = 1;
            std::uint64_t m_NextSampler = 1;
            std::uint64_t m_NextPipeline = 1;
        };

        std::unique_ptr<IRenderBackend> CreateNullBackend()
        {
            return std::make_unique<NullRenderBackend>();
        }
    }

    RenderBackendRegistry::RenderBackendRegistry()
    {
        Register(BackendType::Null, &CreateNullBackend);
#if defined(_WIN32)
        Register(BackendType::Direct3D11, &CreateD3D11RenderBackend);
#endif
    }

    void RenderBackendRegistry::Register(BackendType type, BackendFactory factory)
    {
        if (factory == nullptr)
            throw std::invalid_argument("Render backend factory cannot be null");
        m_Factories[type] = factory;
    }

    bool RenderBackendRegistry::IsRegistered(BackendType type) const { return m_Factories.contains(type); }

    std::unique_ptr<IRenderBackend> RenderBackendRegistry::Create(BackendType type) const
    {
        const auto it = m_Factories.find(type);
        return it == m_Factories.end() ? nullptr : it->second();
    }

    std::vector<BackendType> RenderBackendRegistry::GetRegisteredBackends() const
    {
        std::vector<BackendType> result;
        result.reserve(m_Factories.size());
        for (const auto& [type, factory] : m_Factories)
        {
            (void)factory;
            result.push_back(type);
        }
        std::sort(result.begin(), result.end(), [](BackendType lhs, BackendType rhs) {
            return static_cast<int>(lhs) < static_cast<int>(rhs);
        });
        return result;
    }

    std::optional<BackendType> RenderBackendRegistry::Parse(std::string_view name)
    {
        if (name == "d3d11" || name == "dx11" || name == "Direct3D11")
            return BackendType::Direct3D11;
        if (name == "vulkan" || name == "vk" || name == "Vulkan")
            return BackendType::Vulkan;
        if (name == "null" || name == "Null")
            return BackendType::Null;
        return std::nullopt;
    }

    std::string_view RenderBackendRegistry::ToString(BackendType type)
    {
        switch (type)
        {
        case BackendType::Direct3D11: return "Direct3D11";
        case BackendType::Vulkan: return "Vulkan";
        case BackendType::Null: return "Null";
        }
        return "Unknown";
    }
}
