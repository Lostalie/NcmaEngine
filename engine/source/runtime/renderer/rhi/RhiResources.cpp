#include "renderer/rhi/RhiResources.h"

#include <algorithm>
#include <cmath>

namespace NcmaEngine::Rhi
{
    bool Validate(const BufferDescription& description, std::string& error)
    {
        if(description.RawGpuWritable && (description.Usage!=BufferUsage::Vertex || description.Memory!=MemoryUsage::GpuOnly || description.Size%4)) {
            error="GPU writable vertex buffers require GPU-only, raw-aligned vertex storage";return false;
        }
        if (description.Size == 0)
        {
            error = "Buffer size must be greater than zero";
            return false;
        }
        if (description.Usage == BufferUsage::Constant && description.Size % 16 != 0)
        {
            error = "Constant buffer size must be a multiple of 16 bytes";
            return false;
        }
        if ((description.Usage == BufferUsage::Vertex || description.Usage == BufferUsage::Storage) &&
            description.Stride == 0)
        {
            error = "Vertex and storage buffers require a non-zero stride";
            return false;
        }
        if (description.Usage == BufferUsage::Index &&
            description.Stride != sizeof(std::uint16_t) && description.Stride != sizeof(std::uint32_t))
        {
            error = "Index buffer stride must be 2 or 4 bytes";
            return false;
        }
        error.clear();
        return true;
    }

    bool Validate(const TextureDescription& description, std::string& error)
    {
        if (description.Width == 0 || description.Height == 0 ||
            description.MipLevels == 0 || description.ArrayLayers == 0)
        {
            error = "Texture dimensions, mip levels, and array layers must be non-zero";
            return false;
        }
        if (description.Usage == TextureUsage::None)
        {
            error = "Texture usage must not be empty";
            return false;
        }
        const std::uint32_t largestDimension = std::max(description.Width, description.Height);
        std::uint32_t maximumMipLevels = 1;
        for (std::uint32_t size = largestDimension; size > 1; size >>= 1U)
            ++maximumMipLevels;
        if (description.MipLevels > maximumMipLevels)
        {
            error = "Texture mip count exceeds its dimensions";
            return false;
        }
        const bool depthFormat = description.Format == TextureFormat::D24S8 ||
            description.Format == TextureFormat::D32Float;
        if(description.Cube && (description.ArrayLayers!=6 || description.Width!=description.Height ||
            description.Usage!=TextureUsage::Sampled || description.InitialMips.empty() || depthFormat)) {
            error="Immutable cube requires six square sampled faces";return false;
        }
        if(!description.InitialMips.empty()) {
            const uint32_t pixelBytes=description.Format==TextureFormat::Rgba32Float?16u:
                description.Format==TextureFormat::Rg32Float?8u:
                (description.Format==TextureFormat::Rgba8Unorm||description.Format==TextureFormat::Rgba8Srgb)?4u:0u;
            if((description.ArrayLayers!=1&&!description.Cube)||description.Usage!=TextureUsage::Sampled||!pixelBytes||
               description.InitialMips.size()!=static_cast<uint64_t>(description.MipLevels)*description.ArrayLayers){error="Immutable texture initial-data contract";return false;}
            for(uint32_t face=0;face<description.ArrayLayers;face++) {
                uint32_t width=description.Width,height=description.Height;
                for(uint32_t level=0;level<description.MipLevels;level++) {
                    const auto& mip=description.InitialMips[static_cast<size_t>(face)*description.MipLevels+level];
                    if(!mip.Pixels||static_cast<uint64_t>(width)*pixelBytes!=mip.RowPitch||static_cast<uint64_t>(mip.RowPitch)*height!=mip.Bytes){error="Immutable texture mip stride/bytes";return false;}
                    width=std::max(1u,width/2);height=std::max(1u,height/2);
                }
            }
        }
        const bool depthUsage = HasTextureUsage(description.Usage, TextureUsage::DepthStencil);
        if (depthFormat != depthUsage)
        {
            error = "Depth formats must use DepthStencil usage and color formats must not";
            return false;
        }
        if (depthFormat && (HasTextureUsage(description.Usage, TextureUsage::RenderTarget) ||
            HasTextureUsage(description.Usage, TextureUsage::Storage)))
        {
            error = "Depth textures cannot use color render-target or storage usage";
            return false;
        }
        if (description.Format == TextureFormat::Rgba8Srgb &&
            HasTextureUsage(description.Usage, TextureUsage::Storage))
        {
            error = "sRGB textures cannot use storage usage";
            return false;
        }
        error.clear();
        return true;
    }

    bool Validate(const SamplerDescription& description, std::string& error)
    {
        if (description.MaxAnisotropy == 0 || description.MaxAnisotropy > 16)
        {
            error = "Sampler anisotropy must be in the range 1..16";
            return false;
        }
        if (description.Filter != SamplerFilter::Anisotropic && description.MaxAnisotropy != 1)
        {
            error = "Only anisotropic samplers may request anisotropy greater than one";
            return false;
        }
        if (!std::isfinite(description.MinLod) || !std::isfinite(description.MaxLod) ||
            !std::isfinite(description.MipLodBias) || description.MinLod > description.MaxLod)
        {
            error = "Sampler LOD values must be finite and MinLod must not exceed MaxLod";
            return false;
        }
        error.clear();
        return true;
    }

    bool Validate(const GraphicsPipelineDescription& description, std::string& error)
    {
        const bool compiled = !description.VertexBytecode.empty() || !description.PixelBytecode.empty();
        if (compiled ? description.VertexBytecode.empty() || description.PixelBytecode.empty() ||
            description.VertexBytecode.size() > 1048576 || description.PixelBytecode.size() > 1048576 ||
            !description.VertexShaderSource.empty() || !description.PixelShaderSource.empty() :
            description.VertexShaderSource.empty() || description.PixelShaderSource.empty() ||
            description.VertexEntryPoint.empty() || description.PixelEntryPoint.empty())
        {
            error = "Graphics pipelines require vertex and pixel shader source and entry points";
            return false;
        }
        // An empty layout is valid for a shader-generated full-screen triangle.
        // The backend verifies that no non-system vertex inputs are required.
        for (const VertexAttribute& attribute : description.VertexLayout)
        {
            if (attribute.Semantic.empty())
            {
                error = "Vertex attribute semantics must not be empty";
                return false;
            }
        }
        error.clear();
        return true;
    }

    bool Validate(const RenderPassDescription& description, std::string& error)
    {
        if (!description.UseColorTarget && description.ColorTarget)
        {
            error = "Depth-only render passes cannot specify a color target";
            return false;
        }
        if (!description.UseColorTarget && description.ClearColor)
        {
            error = "Depth-only render passes cannot clear color";
            return false;
        }
        if (!description.UseColorTarget && !description.DepthTarget)
        {
            error = "Depth-only render passes require a depth target";
            return false;
        }
        if (description.DepthLayer > 0 && !description.DepthTarget)
        {
            error = "A render-pass depth layer requires a depth target";
            return false;
        }
        if (!std::isfinite(description.ClearDepthValue) ||
            description.ClearDepthValue < 0.0F || description.ClearDepthValue > 1.0F)
        {
            error = "Render-pass clear depth must be finite and in the range 0..1";
            return false;
        }
        for (const float component : description.ClearColorValue)
        {
            if (!std::isfinite(component))
            {
                error = "Render-pass clear color must be finite";
                return false;
            }
        }
        error.clear();
        return true;
    }

    bool Validate(const DrawDescription& description, std::string& error)
    {
        if (!description.Pipeline || (description.VertexBuffer ? description.VertexStride == 0 :
            description.VertexStride != 0 || description.VertexOffset != 0 || description.IndexCount != 0) ||
            (description.VertexCount == 0 && description.IndexCount == 0))
        {
            error = "Draw commands require pipeline, vertex buffer, draw count, and vertex stride";
            return false;
        }
        for (std::size_t slot = 0; slot < MaxPixelResources; ++slot)
        {
            if (static_cast<bool>(description.PixelTextures[slot]) !=
                static_cast<bool>(description.PixelSamplers[slot]))
            {
                error = "Each sampled pixel texture requires a matching sampler in the same slot";
                return false;
            }
        }
        if (description.IndexCount > 0 && (!description.IndexBuffer ||
            (description.IndexStride != sizeof(std::uint16_t) && description.IndexStride != sizeof(std::uint32_t))))
        {
            error = "Indexed draws require an index buffer with a 2-byte or 4-byte index stride";
            return false;
        }
        if (!std::isfinite(description.ViewportX) || !std::isfinite(description.ViewportY) ||
            !std::isfinite(description.ViewportWidth) || !std::isfinite(description.ViewportHeight) ||
            description.ViewportWidth <= 0.0F || description.ViewportHeight <= 0.0F)
        {
            error = "Draw viewport must be finite and have positive dimensions";
            return false;
        }
        error.clear();
        return true;
    }
}
