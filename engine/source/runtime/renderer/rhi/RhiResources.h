#pragma once

#include <array>
#include <compare>
#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>
#include <span>

namespace NcmaEngine::Rhi
{
    inline constexpr std::size_t MaxPixelResources = 8;

    enum class BufferUsage : std::uint8_t { Vertex, Index, Constant, Storage };
    enum class MemoryUsage : std::uint8_t { GpuOnly, CpuToGpu, GpuToCpu };
    enum class TextureFormat : std::uint8_t { Rgba8Unorm, Rgba8Srgb, Rgba16Float, D24S8, D32Float, Rgba32Float, Rg32Float };
    enum class TextureUsage : std::uint8_t
    {
        None = 0,
        Sampled = 1U << 0U,
        RenderTarget = 1U << 1U,
        DepthStencil = 1U << 2U,
        Storage = 1U << 3U
    };
    enum class SamplerFilter : std::uint8_t { Nearest, Linear, Anisotropic };
    enum class SamplerAddressMode : std::uint8_t { Repeat, MirroredRepeat, ClampToEdge, ClampToBorder };
    enum class CompareOperation : std::uint8_t
    {
        Disabled,
        Never,
        Less,
        LessEqual,
        Equal,
        GreaterEqual,
        Greater,
        NotEqual,
        Always
    };
    enum class VertexFormat : std::uint8_t { Float2, Float3, Float4, Rgba8Unorm };
    enum class PrimitiveTopology : std::uint8_t { TriangleList, LineList };
    enum class CullMode : std::uint8_t { None, Front, Back };

    [[nodiscard]] constexpr TextureUsage operator|(TextureUsage lhs, TextureUsage rhs) noexcept
    {
        return static_cast<TextureUsage>(static_cast<std::uint8_t>(lhs) | static_cast<std::uint8_t>(rhs));
    }

    [[nodiscard]] constexpr bool HasTextureUsage(TextureUsage set, TextureUsage usage) noexcept
    {
        return (static_cast<std::uint8_t>(set) & static_cast<std::uint8_t>(usage)) != 0;
    }

    struct BufferHandle final
    {
        std::uint64_t Value = 0;
        [[nodiscard]] explicit operator bool() const noexcept { return Value != 0; }
        auto operator<=>(const BufferHandle&) const = default;
    };

    struct TextureHandle final
    {
        std::uint64_t Value = 0;
        [[nodiscard]] explicit operator bool() const noexcept { return Value != 0; }
        auto operator<=>(const TextureHandle&) const = default;
    };

    struct SamplerHandle final
    {
        std::uint64_t Value = 0;
        [[nodiscard]] explicit operator bool() const noexcept { return Value != 0; }
        auto operator<=>(const SamplerHandle&) const = default;
    };

    struct GraphicsPipelineHandle final
    {
        std::uint64_t Value = 0;
        [[nodiscard]] explicit operator bool() const noexcept { return Value != 0; }
        auto operator<=>(const GraphicsPipelineHandle&) const = default;
    };

    struct BufferDescription final
    {
        std::size_t Size = 0;
        std::uint32_t Stride = 0;
        BufferUsage Usage = BufferUsage::Vertex;
        MemoryUsage Memory = MemoryUsage::GpuOnly;
        std::string DebugName;
        bool RawGpuWritable = false;
    };

    struct TextureMipData final { const void* Pixels=nullptr; std::uint32_t RowPitch=0, Bytes=0; };
    struct TextureDescription final
    {
        std::uint32_t Width = 1;
        std::uint32_t Height = 1;
        std::uint32_t MipLevels = 1;
        std::uint32_t ArrayLayers = 1;
        TextureFormat Format = TextureFormat::Rgba8Unorm;
        TextureUsage Usage = TextureUsage::Sampled;
        std::string DebugName;
        // Optional borrowed immutable initial data, valid synchronously during CreateTexture only.
        std::vector<TextureMipData> InitialMips;
        // Immutable cube: six square faces, face-major declared mip chain initial data.
        bool Cube = false;
    };

    struct SamplerDescription final
    {
        SamplerFilter Filter = SamplerFilter::Linear;
        SamplerAddressMode AddressU = SamplerAddressMode::Repeat;
        SamplerAddressMode AddressV = SamplerAddressMode::Repeat;
        SamplerAddressMode AddressW = SamplerAddressMode::Repeat;
        CompareOperation Comparison = CompareOperation::Disabled;
        std::uint32_t MaxAnisotropy = 1;
        float MinLod = 0.0F;
        float MaxLod = 1000.0F;
        float MipLodBias = 0.0F;
        float BorderColor[4]{0.0F, 0.0F, 0.0F, 0.0F};
        std::string DebugName;
    };

    struct VertexAttribute final
    {
        std::string Semantic;
        std::uint32_t SemanticIndex = 0;
        VertexFormat Format = VertexFormat::Float3;
        std::uint32_t Offset = 0;
    };

    struct GraphicsPipelineDescription final
    {
        std::string_view VertexShaderSource;
        std::string_view PixelShaderSource;
        // Native-private compiled preparation. Borrowed synchronously; never serialized.
        std::span<const std::uint8_t> VertexBytecode;
        std::span<const std::uint8_t> PixelBytecode;
        std::string VertexEntryPoint = "VSMain";
        std::string PixelEntryPoint = "PSMain";
        std::vector<VertexAttribute> VertexLayout;
        PrimitiveTopology Topology = PrimitiveTopology::TriangleList;
        CullMode Cull = CullMode::Back;
        bool DepthTest = false;
        bool DepthWrite = false;
        bool AlphaBlend = false;
        std::string DebugName;
    };

    struct RenderPassDescription final
    {
        bool UseColorTarget = true;
        TextureHandle ColorTarget;
        TextureHandle DepthTarget;
        std::uint32_t DepthLayer = 0;
        bool ClearColor = false;
        bool ClearDepth = false;
        float ClearColorValue[4]{0.0F, 0.0F, 0.0F, 1.0F};
        float ClearDepthValue = 1.0F;
        std::string DebugName;
    };

    struct DrawDescription final
    {
        GraphicsPipelineHandle Pipeline;
        BufferHandle VertexBuffer;
        BufferHandle IndexBuffer;
        BufferHandle VertexConstantBuffer;
        BufferHandle PixelConstantBuffer;
        std::array<TextureHandle, MaxPixelResources> PixelTextures{};
        std::array<SamplerHandle, MaxPixelResources> PixelSamplers{};
        std::uint32_t VertexCount = 0;
        std::uint32_t IndexCount = 0;
        std::uint32_t FirstVertex = 0;
        std::uint32_t FirstIndex = 0;
        std::int32_t BaseVertex = 0;
        std::uint32_t VertexStride = 0;
        std::uint32_t VertexOffset = 0;
        std::uint32_t IndexStride = 0;
        float ViewportX = 0.0F;
        float ViewportY = 0.0F;
        float ViewportWidth = 0.0F;
        float ViewportHeight = 0.0F;
    };

    [[nodiscard]] bool Validate(const BufferDescription& description, std::string& error);
    [[nodiscard]] bool Validate(const TextureDescription& description, std::string& error);
    [[nodiscard]] bool Validate(const SamplerDescription& description, std::string& error);
    [[nodiscard]] bool Validate(const GraphicsPipelineDescription& description, std::string& error);
    [[nodiscard]] bool Validate(const RenderPassDescription& description, std::string& error);
    [[nodiscard]] bool Validate(const DrawDescription& description, std::string& error);
}
