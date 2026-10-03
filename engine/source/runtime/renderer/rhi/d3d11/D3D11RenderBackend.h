#pragma once

#include "renderer/rhi/RenderBackend.h"

#if !defined(_WIN32)
#error D3D11RenderBackend is only available on Windows.
#endif

#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>

#include <unordered_map>
#include <vector>

namespace NcmaEngine::Rhi
{
    class D3D11RenderBackend final : public IRenderBackend
    {
    public:
        BackendType GetType() const noexcept override { return BackendType::Direct3D11; }
        std::string_view GetName() const noexcept override { return "Direct3D 11"; }
        const BackendCapabilities& GetCapabilities() const noexcept override { return m_Capabilities; }

        bool Initialize(const BackendCreateInfo& createInfo, std::string& error) override;
        void Shutdown() noexcept override;
        bool BeginFrame(std::string& error) override;
        void EndFrame() override;
        bool BeginRenderPass(const RenderPassDescription& description, std::string& error) override;
        void EndRenderPass() override;
        void Resize(std::uint32_t width, std::uint32_t height) override;
        [[nodiscard]] BufferHandle CreateBuffer(
            const BufferDescription& description, const void* initialData, std::string& error) override;
        void DestroyBuffer(BufferHandle buffer) noexcept override;
        bool UpdateBuffer(
            BufferHandle buffer, const void* data, std::size_t size, std::size_t offset,
            std::string& error) override;
        [[nodiscard]] TextureHandle CreateTexture(
            const TextureDescription& description, std::string& error) override;
        void DestroyTexture(TextureHandle texture) noexcept override;
        [[nodiscard]] SamplerHandle CreateSampler(
            const SamplerDescription& description, std::string& error) override;
        void DestroySampler(SamplerHandle sampler) noexcept override;
        [[nodiscard]] GraphicsPipelineHandle CreateGraphicsPipeline(
            const GraphicsPipelineDescription& description, std::string& error) override;
        void DestroyGraphicsPipeline(GraphicsPipelineHandle pipeline) noexcept override;
        bool Draw(const DrawDescription& description, std::string& error) override;

        [[nodiscard]] ID3D11Device* GetDevice() const noexcept { return m_Device.Get(); }
        [[nodiscard]] ID3D11DeviceContext* GetDeviceContext() const noexcept { return m_DeviceContext.Get(); }
        void SetClearColor(float red, float green, float blue, float alpha) noexcept;

    private:
        struct TextureResource final
        {
            Microsoft::WRL::ComPtr<ID3D11Texture2D> Texture;
            Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> ShaderResourceView;
            Microsoft::WRL::ComPtr<ID3D11RenderTargetView> RenderTargetView;
            Microsoft::WRL::ComPtr<ID3D11DepthStencilView> DepthStencilView;
            std::vector<Microsoft::WRL::ComPtr<ID3D11DepthStencilView>> DepthStencilLayerViews;
            Microsoft::WRL::ComPtr<ID3D11UnorderedAccessView> UnorderedAccessView;
        };

        struct GraphicsPipelineResource final
        {
            Microsoft::WRL::ComPtr<ID3D11VertexShader> VertexShader;
            Microsoft::WRL::ComPtr<ID3D11PixelShader> PixelShader;
            Microsoft::WRL::ComPtr<ID3D11InputLayout> InputLayout;
            Microsoft::WRL::ComPtr<ID3D11RasterizerState> RasterizerState;
            Microsoft::WRL::ComPtr<ID3D11DepthStencilState> DepthStencilState;
            Microsoft::WRL::ComPtr<ID3D11BlendState> BlendState;
            D3D11_PRIMITIVE_TOPOLOGY Topology = D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST;
        };

        bool CreateRenderTarget(std::string& error);
        void ReleaseRenderTarget() noexcept;

        BackendCapabilities m_Capabilities{
            Feature::ComputeShaders | Feature::TextureArrays | Feature::TimestampQueries |
                Feature::ShadowComparisonSamplers,
            16384,
            8,
            true,
            true
        };
        BackendCreateInfo m_CreateInfo{};
        Microsoft::WRL::ComPtr<ID3D11Device> m_Device;
        Microsoft::WRL::ComPtr<ID3D11DeviceContext> m_DeviceContext;
        Microsoft::WRL::ComPtr<IDXGISwapChain> m_SwapChain;
        Microsoft::WRL::ComPtr<ID3D11RenderTargetView> m_RenderTargetView;
        Microsoft::WRL::ComPtr<ID3D11Texture2D> m_DepthStencilTexture;
        Microsoft::WRL::ComPtr<ID3D11DepthStencilView> m_DepthStencilView;
        struct BufferResource final
        {
            Microsoft::WRL::ComPtr<ID3D11Buffer> Buffer;
            BufferDescription Description;
        };

        std::unordered_map<std::uint64_t, BufferResource> m_Buffers;
        std::unordered_map<std::uint64_t, TextureResource> m_Textures;
        std::unordered_map<std::uint64_t, Microsoft::WRL::ComPtr<ID3D11SamplerState>> m_Samplers;
        std::unordered_map<std::uint64_t, GraphicsPipelineResource> m_Pipelines;
        std::uint64_t m_NextBuffer = 1;
        std::uint64_t m_NextTexture = 1;
        std::uint64_t m_NextSampler = 1;
        std::uint64_t m_NextPipeline = 1;
        float m_ClearColor[4]{0.035F, 0.039F, 0.052F, 1.0F};
        bool m_Initialized = false;
    };

    [[nodiscard]] std::unique_ptr<IRenderBackend> CreateD3D11RenderBackend();
}
