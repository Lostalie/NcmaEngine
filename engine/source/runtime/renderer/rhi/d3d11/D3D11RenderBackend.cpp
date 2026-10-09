#include "renderer/rhi/d3d11/D3D11RenderBackend.h"

#include <d3dcompiler.h>

#include <cstring>
#include <iomanip>
#include <limits>
#include <sstream>

namespace NcmaEngine::Rhi
{
    namespace
    {
        std::string HResultError(const char* operation, HRESULT result)
        {
            std::ostringstream stream;
            stream << operation << " failed (HRESULT 0x" << std::hex << std::uppercase
                   << static_cast<unsigned long>(result) << ')';
            return stream.str();
        }

        DXGI_FORMAT ColorFormat(TextureFormat format)
        {
            switch (format)
            {
            case TextureFormat::Rgba8Unorm: return DXGI_FORMAT_R8G8B8A8_UNORM;
            case TextureFormat::Rgba8Srgb: return DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
            case TextureFormat::Rgba16Float: return DXGI_FORMAT_R16G16B16A16_FLOAT;
            case TextureFormat::D24S8:
            case TextureFormat::D32Float: break;
            }
            return DXGI_FORMAT_UNKNOWN;
        }

        D3D11_TEXTURE_ADDRESS_MODE AddressMode(SamplerAddressMode mode)
        {
            switch (mode)
            {
            case SamplerAddressMode::Repeat: return D3D11_TEXTURE_ADDRESS_WRAP;
            case SamplerAddressMode::MirroredRepeat: return D3D11_TEXTURE_ADDRESS_MIRROR;
            case SamplerAddressMode::ClampToEdge: return D3D11_TEXTURE_ADDRESS_CLAMP;
            case SamplerAddressMode::ClampToBorder: return D3D11_TEXTURE_ADDRESS_BORDER;
            }
            return D3D11_TEXTURE_ADDRESS_WRAP;
        }

        D3D11_COMPARISON_FUNC Comparison(CompareOperation operation)
        {
            switch (operation)
            {
            case CompareOperation::Disabled:
            case CompareOperation::Always: return D3D11_COMPARISON_ALWAYS;
            case CompareOperation::Never: return D3D11_COMPARISON_NEVER;
            case CompareOperation::Less: return D3D11_COMPARISON_LESS;
            case CompareOperation::LessEqual: return D3D11_COMPARISON_LESS_EQUAL;
            case CompareOperation::Equal: return D3D11_COMPARISON_EQUAL;
            case CompareOperation::GreaterEqual: return D3D11_COMPARISON_GREATER_EQUAL;
            case CompareOperation::Greater: return D3D11_COMPARISON_GREATER;
            case CompareOperation::NotEqual: return D3D11_COMPARISON_NOT_EQUAL;
            }
            return D3D11_COMPARISON_ALWAYS;
        }

        D3D11_FILTER Filter(const SamplerDescription& description)
        {
            const bool comparison = description.Comparison != CompareOperation::Disabled;
            switch (description.Filter)
            {
            case SamplerFilter::Nearest:
                return comparison ? D3D11_FILTER_COMPARISON_MIN_MAG_MIP_POINT :
                    D3D11_FILTER_MIN_MAG_MIP_POINT;
            case SamplerFilter::Linear:
                return comparison ? D3D11_FILTER_COMPARISON_MIN_MAG_MIP_LINEAR :
                    D3D11_FILTER_MIN_MAG_MIP_LINEAR;
            case SamplerFilter::Anisotropic:
                return comparison ? D3D11_FILTER_COMPARISON_ANISOTROPIC : D3D11_FILTER_ANISOTROPIC;
            }
            return D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        }

        DXGI_FORMAT VertexAttributeFormat(VertexFormat format)
        {
            switch (format)
            {
            case VertexFormat::Float2: return DXGI_FORMAT_R32G32_FLOAT;
            case VertexFormat::Float3: return DXGI_FORMAT_R32G32B32_FLOAT;
            case VertexFormat::Float4: return DXGI_FORMAT_R32G32B32A32_FLOAT;
            case VertexFormat::Rgba8Unorm: return DXGI_FORMAT_R8G8B8A8_UNORM;
            }
            return DXGI_FORMAT_UNKNOWN;
        }

        bool CompileShader(
            std::string_view source, const std::string& entryPoint, const char* profile,
            Microsoft::WRL::ComPtr<ID3DBlob>& bytecode, std::string& error)
        {
            UINT flags = D3DCOMPILE_ENABLE_STRICTNESS;
#if defined(_DEBUG)
            flags |= D3DCOMPILE_DEBUG | D3DCOMPILE_SKIP_OPTIMIZATION;
#else
            flags |= D3DCOMPILE_OPTIMIZATION_LEVEL3;
#endif
            Microsoft::WRL::ComPtr<ID3DBlob> diagnostics;
            const HRESULT result = D3DCompile(
                source.data(), source.size(), nullptr, nullptr, D3D_COMPILE_STANDARD_FILE_INCLUDE,
                entryPoint.c_str(), profile, flags, 0, &bytecode, &diagnostics);
            if (FAILED(result))
            {
                error = diagnostics
                    ? std::string(static_cast<const char*>(diagnostics->GetBufferPointer()), diagnostics->GetBufferSize())
                    : HResultError("D3DCompile", result);
                return false;
            }
            return true;
        }
    }

    bool D3D11RenderBackend::Initialize(const BackendCreateInfo& createInfo, std::string& error)
    {
        if (m_Initialized)
            return true;
        if (createInfo.NativeWindow == nullptr)
        {
            error = "D3D11 requires a Win32 window handle";
            return false;
        }

        m_CreateInfo = createInfo;
        DXGI_SWAP_CHAIN_DESC swapChainDescription{};
        swapChainDescription.BufferCount = 2;
        swapChainDescription.BufferDesc.Width = createInfo.Width;
        swapChainDescription.BufferDesc.Height = createInfo.Height;
        swapChainDescription.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        swapChainDescription.BufferDesc.RefreshRate.Numerator = 60;
        swapChainDescription.BufferDesc.RefreshRate.Denominator = 1;
        swapChainDescription.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        swapChainDescription.OutputWindow = static_cast<HWND>(createInfo.NativeWindow);
        swapChainDescription.SampleDesc.Count = 1;
        swapChainDescription.Windowed = TRUE;
        swapChainDescription.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;

        constexpr D3D_FEATURE_LEVEL requestedLevels[]{
            D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL_11_0
        };
        D3D_FEATURE_LEVEL createdLevel{};
        UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        if (createInfo.EnableValidation)
            flags |= D3D11_CREATE_DEVICE_DEBUG;

        HRESULT result = D3D11CreateDeviceAndSwapChain(
            nullptr,
            D3D_DRIVER_TYPE_HARDWARE,
            nullptr,
            flags,
            requestedLevels,
            static_cast<UINT>(std::size(requestedLevels)),
            D3D11_SDK_VERSION,
            &swapChainDescription,
            &m_SwapChain,
            &m_Device,
            &createdLevel,
            &m_DeviceContext);


        if (FAILED(result))
        {
            error = HResultError("D3D11CreateDeviceAndSwapChain", result);
            Shutdown();
            return false;
        }

        (void)createdLevel;
        if (!CreateRenderTarget(error))
        {
            Shutdown();
            return false;
        }
        m_Initialized = true;
        error.clear();
        return true;
    }

    void D3D11RenderBackend::Shutdown() noexcept
    {
        if (m_DeviceContext)
            m_DeviceContext->ClearState();
        m_Pipelines.clear();
        m_Samplers.clear();
        m_Textures.clear();
        m_Buffers.clear();
        ReleaseRenderTarget();
        m_SwapChain.Reset();
        m_DeviceContext.Reset();
        m_Device.Reset();
        m_Initialized = false;
    }

    bool D3D11RenderBackend::BeginFrame(std::string& error)
    {
        if (!m_Initialized || !m_RenderTargetView || (m_CreateInfo.EnableDefaultDepth && !m_DepthStencilView))
        {
            error = "D3D11 backend is not ready";
            return false;
        }
        ID3D11RenderTargetView* renderTargets[]{m_RenderTargetView.Get()};
        m_DeviceContext->OMSetRenderTargets(1, renderTargets, m_DepthStencilView.Get());
        m_DeviceContext->ClearRenderTargetView(m_RenderTargetView.Get(), m_ClearColor);
        if (m_DepthStencilView) m_DeviceContext->ClearDepthStencilView(m_DepthStencilView.Get(), D3D11_CLEAR_DEPTH, 1.0F, 0);
        error.clear();
        return true;
    }

    void D3D11RenderBackend::EndFrame()
    {
        if (m_SwapChain)
            (void)m_SwapChain->Present(m_CreateInfo.EnableVSync ? 1U : 0U, 0);
    }

    bool D3D11RenderBackend::BeginRenderPass(
        const RenderPassDescription& description, std::string& error)
    {
        if (!m_Initialized || !Validate(description, error))
        {
            if (!m_Initialized)
                error = "D3D11 backend is not ready";
            return false;
        }

        ID3D11RenderTargetView* colorView =
            description.UseColorTarget ? m_RenderTargetView.Get() : nullptr;
        if (description.UseColorTarget && description.ColorTarget)
        {
            const auto color = m_Textures.find(description.ColorTarget.Value);
            if (color == m_Textures.end() || !color->second.RenderTargetView)
            {
                error = "Render pass references an unknown texture or a texture without RenderTarget usage";
                return false;
            }
            colorView = color->second.RenderTargetView.Get();
        }

        ID3D11DepthStencilView* depthView = nullptr;
        if (description.DepthTarget)
        {
            const auto depth = m_Textures.find(description.DepthTarget.Value);
            if (depth == m_Textures.end() || !depth->second.DepthStencilView)
            {
                error = "Render pass references an unknown texture or a texture without DepthStencil usage";
                return false;
            }
            if (description.DepthLayer >= depth->second.DepthStencilLayerViews.size())
            {
                error = "Render pass depth layer exceeds the target texture array size";
                return false;
            }
            depthView = depth->second.DepthStencilLayerViews[description.DepthLayer].Get();
        }

        ID3D11ShaderResourceView* noShaderResources[MaxPixelResources]{};
        m_DeviceContext->PSSetShaderResources(
            0, static_cast<UINT>(MaxPixelResources), noShaderResources);
        m_DeviceContext->OMSetRenderTargets(
            description.UseColorTarget ? 1U : 0U,
            description.UseColorTarget ? &colorView : nullptr, depthView);
        if (description.ClearColor && colorView != nullptr)
            m_DeviceContext->ClearRenderTargetView(colorView, description.ClearColorValue);
        if (description.ClearDepth && depthView != nullptr)
        {
            m_DeviceContext->ClearDepthStencilView(
                depthView, D3D11_CLEAR_DEPTH, description.ClearDepthValue, 0);
        }
        error.clear();
        return true;
    }

    void D3D11RenderBackend::EndRenderPass() {}

    void D3D11RenderBackend::Resize(std::uint32_t width, std::uint32_t height)
    {
        std::string error;
        (void)ResizeChecked(width,height,error);
    }
    bool D3D11RenderBackend::ResizeChecked(uint32_t width,uint32_t height,std::string& error)
    {
        if (!m_Initialized || width == 0 || height == 0 || !m_SwapChain) { error="Invalid resize."; return false; }
        m_DeviceContext->OMSetRenderTargets(0,nullptr,nullptr);
        ReleaseRenderTarget();
        const HRESULT result=m_SwapChain->ResizeBuffers(0,width,height,DXGI_FORMAT_UNKNOWN,0);
        if (FAILED(result)) { error=HResultError("ResizeBuffers",result); return false; }
        m_CreateInfo.Width=width; m_CreateInfo.Height=height;
        return CreateRenderTarget(error);
    }

    void D3D11RenderBackend::SetClearColor(float red, float green, float blue, float alpha) noexcept
    {
        m_ClearColor[0] = red;
        m_ClearColor[1] = green;
        m_ClearColor[2] = blue;
        m_ClearColor[3] = alpha;
    }

    BufferHandle D3D11RenderBackend::CreateBuffer(
        const BufferDescription& description, const void* initialData, std::string& error)
    {
        if (!m_Initialized)
        {
            error = "D3D11 backend is not ready";
            return {};
        }
        if (!Validate(description, error))
            return {};
        if (description.Usage == BufferUsage::Storage && description.Memory != MemoryUsage::GpuOnly)
        {
            error = "The D3D11 backend currently supports storage buffers only in GPU-only memory";
            return {};
        }
        if (description.Size > (std::numeric_limits<UINT>::max)())
        {
            error = "D3D11 buffer size exceeds the 32-bit API limit";
            return {};
        }

        D3D11_BUFFER_DESC nativeDescription{};
        nativeDescription.ByteWidth = static_cast<UINT>(description.Size);
        nativeDescription.StructureByteStride = description.Usage == BufferUsage::Storage
            ? description.Stride : 0;
        switch (description.Usage)
        {
        case BufferUsage::Vertex: nativeDescription.BindFlags = D3D11_BIND_VERTEX_BUFFER; break;
        case BufferUsage::Index: nativeDescription.BindFlags = D3D11_BIND_INDEX_BUFFER; break;
        case BufferUsage::Constant: nativeDescription.BindFlags = D3D11_BIND_CONSTANT_BUFFER; break;
        case BufferUsage::Storage:
            nativeDescription.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_UNORDERED_ACCESS;
            nativeDescription.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
            break;
        }
        switch (description.Memory)
        {
        case MemoryUsage::GpuOnly:
            nativeDescription.Usage = D3D11_USAGE_DEFAULT;
            break;
        case MemoryUsage::CpuToGpu:
            nativeDescription.Usage = D3D11_USAGE_DYNAMIC;
            nativeDescription.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
            break;
        case MemoryUsage::GpuToCpu:
            nativeDescription.Usage = D3D11_USAGE_STAGING;
            nativeDescription.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
            nativeDescription.BindFlags = 0;
            nativeDescription.MiscFlags = 0;
            nativeDescription.StructureByteStride = 0;
            break;
        }

        if(description.RawGpuWritable) {
            nativeDescription.BindFlags|=D3D11_BIND_UNORDERED_ACCESS;
            nativeDescription.MiscFlags|=D3D11_RESOURCE_MISC_BUFFER_ALLOW_RAW_VIEWS;
        }

        D3D11_SUBRESOURCE_DATA data{};
        data.pSysMem = initialData;
        Microsoft::WRL::ComPtr<ID3D11Buffer> nativeBuffer;
        const HRESULT result = m_Device->CreateBuffer(
            &nativeDescription, initialData != nullptr ? &data : nullptr, &nativeBuffer);
        if (FAILED(result))
        {
            error = HResultError("ID3D11Device::CreateBuffer", result);
            return {};
        }
        const BufferHandle handle{m_NextBuffer++};
        m_Buffers.emplace(handle.Value, BufferResource{std::move(nativeBuffer), description});
        error.clear();
        return handle;
    }

    void D3D11RenderBackend::DestroyBuffer(BufferHandle buffer) noexcept
    {
        m_Buffers.erase(buffer.Value);
    }

    bool D3D11RenderBackend::UpdateBuffer(
        BufferHandle buffer, const void* data, std::size_t size, std::size_t offset,
        std::string& error)
    {
        if (!m_Initialized || data == nullptr || size == 0)
        {
            error = !m_Initialized ? "D3D11 backend is not ready" :
                "Buffer updates require non-null data and a non-zero size";
            return false;
        }
        const auto resource = m_Buffers.find(buffer.Value);
        if (resource == m_Buffers.end())
        {
            error = "Buffer update references an unknown handle";
            return false;
        }
        if (resource->second.Description.Memory != MemoryUsage::CpuToGpu)
        {
            error = "D3D11 dynamic updates require CpuToGpu buffer memory";
            return false;
        }
        if (offset != 0 || size > resource->second.Description.Size)
        {
            error = "D3D11 discard updates must begin at zero and fit within the buffer";
            return false;
        }

        D3D11_MAPPED_SUBRESOURCE mapped{};
        const HRESULT result = m_DeviceContext->Map(
            resource->second.Buffer.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped);
        if (FAILED(result))
        {
            error = HResultError("ID3D11DeviceContext::Map", result);
            return false;
        }
        std::memcpy(mapped.pData, data, size);
        m_DeviceContext->Unmap(resource->second.Buffer.Get(), 0);
        error.clear();
        return true;
    }

    bool D3D11RenderBackend::CaptureRgba8(TextureHandle texture,void* output,uint32_t capacity,std::string& error)
    {
        const auto it=m_Textures.find(texture.Value);if(it==m_Textures.end()||!output){error="Invalid capture texture";return false;}
        D3D11_TEXTURE2D_DESC d{};it->second.Texture->GetDesc(&d);
        if(d.Format!=DXGI_FORMAT_R8G8B8A8_UNORM||capacity!=static_cast<uint64_t>(d.Width)*d.Height*4){error="Capture dimensions/format";return false;}
        d.BindFlags=0;d.MiscFlags=0;d.Usage=D3D11_USAGE_STAGING;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        Microsoft::WRL::ComPtr<ID3D11Texture2D> staging;
        if(FAILED(m_Device->CreateTexture2D(&d,nullptr,&staging))){error="Capture staging allocation";return false;}
        m_DeviceContext->CopyResource(staging.Get(),it->second.Texture.Get());D3D11_MAPPED_SUBRESOURCE mapped{};
        if(FAILED(m_DeviceContext->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped))){error="Capture map";return false;}
        for(uint32_t y=0;y<d.Height;++y)std::memcpy(static_cast<uint8_t*>(output)+static_cast<size_t>(y)*d.Width*4,static_cast<const uint8_t*>(mapped.pData)+static_cast<size_t>(y)*mapped.RowPitch,d.Width*4);
        m_DeviceContext->Unmap(staging.Get(),0);return true;
    }
    TextureHandle D3D11RenderBackend::CreateTexture(
        const TextureDescription& description, std::string& error)
    {
        if (!m_Initialized)
        {
            error = "D3D11 backend is not ready";
            return {};
        }
        if (!Validate(description, error))
            return {};

        const bool sampled = HasTextureUsage(description.Usage, TextureUsage::Sampled);
        const bool depth = HasTextureUsage(description.Usage, TextureUsage::DepthStencil);
        D3D11_TEXTURE2D_DESC nativeDescription{};
        nativeDescription.Width = description.Width;
        nativeDescription.Height = description.Height;
        nativeDescription.MipLevels = description.MipLevels;
        nativeDescription.ArraySize = description.ArrayLayers;
        nativeDescription.SampleDesc.Count = 1;
        nativeDescription.Usage = D3D11_USAGE_DEFAULT;

        if (depth)
        {
            nativeDescription.Format = description.Format == TextureFormat::D24S8
                ? (sampled ? DXGI_FORMAT_R24G8_TYPELESS : DXGI_FORMAT_D24_UNORM_S8_UINT)
                : (sampled ? DXGI_FORMAT_R32_TYPELESS : DXGI_FORMAT_D32_FLOAT);
            nativeDescription.BindFlags |= D3D11_BIND_DEPTH_STENCIL;
        }
        else
        {
            nativeDescription.Format = ColorFormat(description.Format);
        }
        if (sampled)
            nativeDescription.BindFlags |= D3D11_BIND_SHADER_RESOURCE;
        if (HasTextureUsage(description.Usage, TextureUsage::RenderTarget))
            nativeDescription.BindFlags |= D3D11_BIND_RENDER_TARGET;
        if (HasTextureUsage(description.Usage, TextureUsage::Storage))
            nativeDescription.BindFlags |= D3D11_BIND_UNORDERED_ACCESS;

        std::vector<D3D11_SUBRESOURCE_DATA> initial;
        for(const auto& mip:description.InitialMips)initial.push_back({mip.Pixels,mip.RowPitch,mip.Bytes});
        if(!initial.empty())nativeDescription.Usage=D3D11_USAGE_IMMUTABLE;
        TextureResource resource;
        HRESULT result = m_Device->CreateTexture2D(&nativeDescription, initial.empty()?nullptr:initial.data(), &resource.Texture);
        if (FAILED(result))
        {
            error = HResultError("ID3D11Device::CreateTexture2D", result);
            return {};
        }

        if (sampled)
        {
            D3D11_SHADER_RESOURCE_VIEW_DESC view{};
            view.Format = depth
                ? (description.Format == TextureFormat::D24S8 ? DXGI_FORMAT_R24_UNORM_X8_TYPELESS : DXGI_FORMAT_R32_FLOAT)
                : nativeDescription.Format;
            if (description.ArrayLayers > 1)
            {
                view.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2DARRAY;
                view.Texture2DArray.MipLevels = description.MipLevels;
                view.Texture2DArray.ArraySize = description.ArrayLayers;
            }
            else
            {
                view.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
                view.Texture2D.MipLevels = description.MipLevels;
            }
            result = m_Device->CreateShaderResourceView(resource.Texture.Get(), &view, &resource.ShaderResourceView);
            if (FAILED(result))
            {
                error = HResultError("ID3D11Device::CreateShaderResourceView", result);
                return {};
            }
        }
        if (depth)
        {
            const DXGI_FORMAT depthViewFormat = description.Format == TextureFormat::D24S8
                ? DXGI_FORMAT_D24_UNORM_S8_UINT : DXGI_FORMAT_D32_FLOAT;
            resource.DepthStencilLayerViews.reserve(description.ArrayLayers);
            for (std::uint32_t layer = 0; layer < description.ArrayLayers; ++layer)
            {
                D3D11_DEPTH_STENCIL_VIEW_DESC layerView{};
                layerView.Format = depthViewFormat;
                if (description.ArrayLayers > 1)
                {
                    layerView.ViewDimension = D3D11_DSV_DIMENSION_TEXTURE2DARRAY;
                    layerView.Texture2DArray.FirstArraySlice = layer;
                    layerView.Texture2DArray.ArraySize = 1;
                }
                else
                {
                    layerView.ViewDimension = D3D11_DSV_DIMENSION_TEXTURE2D;
                }

                Microsoft::WRL::ComPtr<ID3D11DepthStencilView> layerDepthView;
                result = m_Device->CreateDepthStencilView(
                    resource.Texture.Get(), &layerView, &layerDepthView);
                if (FAILED(result))
                {
                    error = HResultError("ID3D11Device::CreateDepthStencilView(layer)", result);
                    return {};
                }
                resource.DepthStencilLayerViews.push_back(std::move(layerDepthView));
            }
            resource.DepthStencilView = resource.DepthStencilLayerViews.front();
        }
        if (HasTextureUsage(description.Usage, TextureUsage::RenderTarget))
        {
            result = m_Device->CreateRenderTargetView(resource.Texture.Get(), nullptr, &resource.RenderTargetView);
            if (FAILED(result))
            {
                error = HResultError("ID3D11Device::CreateRenderTargetView", result);
                return {};
            }
        }
        if (HasTextureUsage(description.Usage, TextureUsage::Storage))
        {
            result = m_Device->CreateUnorderedAccessView(resource.Texture.Get(), nullptr, &resource.UnorderedAccessView);
            if (FAILED(result))
            {
                error = HResultError("ID3D11Device::CreateUnorderedAccessView", result);
                return {};
            }
        }

        const TextureHandle handle{m_NextTexture++};
        m_Textures.emplace(handle.Value, std::move(resource));
        error.clear();
        return handle;
    }

    void D3D11RenderBackend::DestroyTexture(TextureHandle texture) noexcept
    {
        m_Textures.erase(texture.Value);
    }

    SamplerHandle D3D11RenderBackend::CreateSampler(
        const SamplerDescription& description, std::string& error)
    {
        if (!m_Initialized)
        {
            error = "D3D11 backend is not ready";
            return {};
        }
        if (!Validate(description, error))
            return {};

        D3D11_SAMPLER_DESC nativeDescription{};
        nativeDescription.Filter = Filter(description);
        nativeDescription.AddressU = AddressMode(description.AddressU);
        nativeDescription.AddressV = AddressMode(description.AddressV);
        nativeDescription.AddressW = AddressMode(description.AddressW);
        nativeDescription.MipLODBias = description.MipLodBias;
        nativeDescription.MaxAnisotropy = description.MaxAnisotropy;
        nativeDescription.ComparisonFunc = Comparison(description.Comparison);
        nativeDescription.MinLOD = description.MinLod;
        nativeDescription.MaxLOD = description.MaxLod;
        for (std::size_t index = 0; index < std::size(nativeDescription.BorderColor); ++index)
            nativeDescription.BorderColor[index] = description.BorderColor[index];

        Microsoft::WRL::ComPtr<ID3D11SamplerState> sampler;
        const HRESULT result = m_Device->CreateSamplerState(&nativeDescription, &sampler);
        if (FAILED(result))
        {
            error = HResultError("ID3D11Device::CreateSamplerState", result);
            return {};
        }
        const SamplerHandle handle{m_NextSampler++};
        m_Samplers.emplace(handle.Value, std::move(sampler));
        error.clear();
        return handle;
    }

    void D3D11RenderBackend::DestroySampler(SamplerHandle sampler) noexcept
    {
        m_Samplers.erase(sampler.Value);
    }

    GraphicsPipelineHandle D3D11RenderBackend::CreateGraphicsPipeline(
        const GraphicsPipelineDescription& description, std::string& error)
    {
        if (!m_Initialized)
        {
            error = "D3D11 backend is not ready";
            return {};
        }
        if (!Validate(description, error))
            return {};

        Microsoft::WRL::ComPtr<ID3DBlob> vertexBytecode;
        Microsoft::WRL::ComPtr<ID3DBlob> pixelBytecode;
        if (!description.VertexBytecode.empty()) {
            HRESULT copied = D3DCreateBlob(description.VertexBytecode.size(), &vertexBytecode);
            if (SUCCEEDED(copied)) copied = D3DCreateBlob(description.PixelBytecode.size(), &pixelBytecode);
            if (FAILED(copied)) { error = HResultError("Shader preparation bytecode copy", copied); return {}; }
            std::memcpy(vertexBytecode->GetBufferPointer(), description.VertexBytecode.data(), description.VertexBytecode.size());
            std::memcpy(pixelBytecode->GetBufferPointer(), description.PixelBytecode.data(), description.PixelBytecode.size());
        } else if (!CompileShader(description.VertexShaderSource, description.VertexEntryPoint, "vs_5_0", vertexBytecode, error) ||
            !CompileShader(description.PixelShaderSource, description.PixelEntryPoint, "ps_5_0", pixelBytecode, error))
            return {};

        GraphicsPipelineResource resource;
        HRESULT result = m_Device->CreateVertexShader(
            vertexBytecode->GetBufferPointer(), vertexBytecode->GetBufferSize(), nullptr, &resource.VertexShader);
        if (FAILED(result))
        {
            error = HResultError("ID3D11Device::CreateVertexShader", result);
            return {};
        }
        result = m_Device->CreatePixelShader(
            pixelBytecode->GetBufferPointer(), pixelBytecode->GetBufferSize(), nullptr, &resource.PixelShader);
        if (FAILED(result))
        {
            error = HResultError("ID3D11Device::CreatePixelShader", result);
            return {};
        }

        std::vector<D3D11_INPUT_ELEMENT_DESC> nativeLayout;
        nativeLayout.reserve(description.VertexLayout.size());
        for (const VertexAttribute& attribute : description.VertexLayout)
        {
            nativeLayout.push_back({
                attribute.Semantic.c_str(), attribute.SemanticIndex, VertexAttributeFormat(attribute.Format),
                0, attribute.Offset, D3D11_INPUT_PER_VERTEX_DATA, 0});
        }
        if (!nativeLayout.empty()) result = m_Device->CreateInputLayout(
            nativeLayout.data(), static_cast<UINT>(nativeLayout.size()),
            vertexBytecode->GetBufferPointer(), vertexBytecode->GetBufferSize(), &resource.InputLayout);
        else {
            Microsoft::WRL::ComPtr<ID3D11ShaderReflection> reflection;
            result = D3DReflect(vertexBytecode->GetBufferPointer(), vertexBytecode->GetBufferSize(), __uuidof(ID3D11ShaderReflection), &reflection);
            if (SUCCEEDED(result)) {
                D3D11_SHADER_DESC reflected{}; result = reflection->GetDesc(&reflected);
                for (UINT i = 0; SUCCEEDED(result) && i < reflected.InputParameters; ++i) {
                    D3D11_SIGNATURE_PARAMETER_DESC parameter{}; result = reflection->GetInputParameterDesc(i, &parameter);
                    if (SUCCEEDED(result) && parameter.SystemValueType != D3D_NAME_VERTEX_ID && parameter.SystemValueType != D3D_NAME_INSTANCE_ID) result = E_INVALIDARG;
                }
            }
        }
        if (FAILED(result))
        {
            error = HResultError("ID3D11Device::CreateInputLayout", result);
            return {};
        }

        D3D11_RASTERIZER_DESC rasterizer{};
        rasterizer.FillMode = D3D11_FILL_SOLID;
        rasterizer.CullMode = description.Cull == CullMode::None ? D3D11_CULL_NONE :
            (description.Cull == CullMode::Front ? D3D11_CULL_FRONT : D3D11_CULL_BACK);
        rasterizer.DepthClipEnable = TRUE;
        result = m_Device->CreateRasterizerState(&rasterizer, &resource.RasterizerState);
        if (FAILED(result))
        {
            error = HResultError("ID3D11Device::CreateRasterizerState", result);
            return {};
        }

        D3D11_DEPTH_STENCIL_DESC depthStencil{};
        depthStencil.DepthEnable = description.DepthTest;
        depthStencil.DepthWriteMask = description.DepthWrite ? D3D11_DEPTH_WRITE_MASK_ALL : D3D11_DEPTH_WRITE_MASK_ZERO;
        depthStencil.DepthFunc = D3D11_COMPARISON_LESS_EQUAL;
        result = m_Device->CreateDepthStencilState(&depthStencil, &resource.DepthStencilState);
        if (FAILED(result))
        {
            error = HResultError("ID3D11Device::CreateDepthStencilState", result);
            return {};
        }

        D3D11_BLEND_DESC blend{};
        blend.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
        blend.RenderTarget[0].BlendEnable = description.AlphaBlend;
        blend.RenderTarget[0].SrcBlend = D3D11_BLEND_SRC_ALPHA;
        blend.RenderTarget[0].DestBlend = D3D11_BLEND_INV_SRC_ALPHA;
        blend.RenderTarget[0].BlendOp = D3D11_BLEND_OP_ADD;
        blend.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ONE;
        blend.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_INV_SRC_ALPHA;
        blend.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
        result = m_Device->CreateBlendState(&blend, &resource.BlendState);
        if (FAILED(result))
        {
            error = HResultError("ID3D11Device::CreateBlendState", result);
            return {};
        }
        resource.Topology = description.Topology == PrimitiveTopology::TriangleList
            ? D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST : D3D11_PRIMITIVE_TOPOLOGY_LINELIST;

        const GraphicsPipelineHandle handle{m_NextPipeline++};
        m_Pipelines.emplace(handle.Value, std::move(resource));
        error.clear();
        return handle;
    }

    void D3D11RenderBackend::DestroyGraphicsPipeline(GraphicsPipelineHandle pipeline) noexcept
    {
        m_Pipelines.erase(pipeline.Value);
    }

    bool D3D11RenderBackend::Draw(const DrawDescription& description, std::string& error)
    {
        if (!m_Initialized || !Validate(description, error))
        {
            if (!m_Initialized)
                error = "D3D11 backend is not ready";
            return false;
        }
        const auto pipeline = m_Pipelines.find(description.Pipeline.Value);
        const auto vertexBuffer = m_Buffers.find(description.VertexBuffer.Value);
        if (pipeline == m_Pipelines.end() || (pipeline->second.InputLayout ? vertexBuffer == m_Buffers.end() : static_cast<bool>(description.VertexBuffer)))
        {
            error = "Draw command references an unknown pipeline or vertex buffer";
            return false;
        }

        const D3D11_VIEWPORT viewport{
            description.ViewportX, description.ViewportY, description.ViewportWidth,
            description.ViewportHeight, 0.0F, 1.0F};
        m_DeviceContext->RSSetViewports(1, &viewport);
        m_DeviceContext->IASetInputLayout(pipeline->second.InputLayout.Get());
        m_DeviceContext->IASetPrimitiveTopology(pipeline->second.Topology);
        const UINT stride = description.VertexStride;
        const UINT offset = description.VertexOffset;
        ID3D11Buffer* nativeVertexBuffer = vertexBuffer == m_Buffers.end() ? nullptr : vertexBuffer->second.Buffer.Get();
        m_DeviceContext->IASetVertexBuffers(0, 1, &nativeVertexBuffer, &stride, &offset);
        if (description.IndexCount > 0)
        {
            const auto indexBuffer = m_Buffers.find(description.IndexBuffer.Value);
            if (indexBuffer == m_Buffers.end() ||
                indexBuffer->second.Description.Usage != BufferUsage::Index)
            {
                error = "Indexed draw references an unknown or non-index buffer";
                return false;
            }
            m_DeviceContext->IASetIndexBuffer(
                indexBuffer->second.Buffer.Get(),
                description.IndexStride == sizeof(std::uint16_t) ? DXGI_FORMAT_R16_UINT : DXGI_FORMAT_R32_UINT,
                0);
        }
        ID3D11Buffer* vertexConstantBuffer = nullptr;
        if (description.VertexConstantBuffer)
        {
            const auto constantBuffer = m_Buffers.find(description.VertexConstantBuffer.Value);
            if (constantBuffer == m_Buffers.end() ||
                constantBuffer->second.Description.Usage != BufferUsage::Constant)
            {
                error = "Draw references an unknown or non-constant vertex buffer";
                return false;
            }
            vertexConstantBuffer = constantBuffer->second.Buffer.Get();
        }
        m_DeviceContext->VSSetConstantBuffers(0, 1, &vertexConstantBuffer);
        ID3D11Buffer* pixelConstantBuffer = nullptr;
        if (description.PixelConstantBuffer)
        {
            const auto constantBuffer = m_Buffers.find(description.PixelConstantBuffer.Value);
            if (constantBuffer == m_Buffers.end() ||
                constantBuffer->second.Description.Usage != BufferUsage::Constant)
            {
                error = "Draw references an unknown or non-constant pixel buffer";
                return false;
            }
            pixelConstantBuffer = constantBuffer->second.Buffer.Get();
        }
        m_DeviceContext->PSSetConstantBuffers(0, 1, &pixelConstantBuffer);
        ID3D11ShaderResourceView* pixelTextures[MaxPixelResources]{};
        ID3D11SamplerState* pixelSamplers[MaxPixelResources]{};
        for (std::size_t slot = 0; slot < MaxPixelResources; ++slot)
        {
            if (!description.PixelTextures[slot])
                continue;
            const auto texture = m_Textures.find(description.PixelTextures[slot].Value);
            const auto sampler = m_Samplers.find(description.PixelSamplers[slot].Value);
            if (texture == m_Textures.end() || !texture->second.ShaderResourceView ||
                sampler == m_Samplers.end())
            {
                error = "Draw references an unknown sampled texture or sampler";
                return false;
            }
            pixelTextures[slot] = texture->second.ShaderResourceView.Get();
            pixelSamplers[slot] = sampler->second.Get();
        }
        m_DeviceContext->PSSetShaderResources(
            0, static_cast<UINT>(MaxPixelResources), pixelTextures);
        m_DeviceContext->PSSetSamplers(
            0, static_cast<UINT>(MaxPixelResources), pixelSamplers);
        m_DeviceContext->VSSetShader(pipeline->second.VertexShader.Get(), nullptr, 0);
        m_DeviceContext->PSSetShader(pipeline->second.PixelShader.Get(), nullptr, 0);
        m_DeviceContext->RSSetState(pipeline->second.RasterizerState.Get());
        m_DeviceContext->OMSetDepthStencilState(pipeline->second.DepthStencilState.Get(), 0);
        const float blendFactor[4]{0.0F, 0.0F, 0.0F, 0.0F};
        m_DeviceContext->OMSetBlendState(pipeline->second.BlendState.Get(), blendFactor, 0xFFFFFFFFU);
        if (description.IndexCount > 0)
            m_DeviceContext->DrawIndexed(description.IndexCount, description.FirstIndex, description.BaseVertex);
        else
            m_DeviceContext->Draw(description.VertexCount, description.FirstVertex);
        error.clear();
        return true;
    }

    bool D3D11RenderBackend::CreateRenderTarget(std::string& error)
    {
        Microsoft::WRL::ComPtr<ID3D11Texture2D> backBuffer;
        HRESULT result = m_SwapChain->GetBuffer(0, IID_PPV_ARGS(&backBuffer));
        if (FAILED(result))
        {
            error = HResultError("IDXGISwapChain::GetBuffer", result);
            return false;
        }
        result = m_Device->CreateRenderTargetView(backBuffer.Get(), nullptr, &m_RenderTargetView);
        if (FAILED(result))
        {
            error = HResultError("ID3D11Device::CreateRenderTargetView", result);
            return false;
        }
        if (!m_CreateInfo.EnableDefaultDepth) return true;
        D3D11_TEXTURE2D_DESC depthDescription{};
        depthDescription.Width = m_CreateInfo.Width;
        depthDescription.Height = m_CreateInfo.Height;
        depthDescription.MipLevels = 1;
        depthDescription.ArraySize = 1;
        depthDescription.Format = DXGI_FORMAT_D32_FLOAT;
        depthDescription.SampleDesc.Count = 1;
        depthDescription.Usage = D3D11_USAGE_DEFAULT;
        depthDescription.BindFlags = D3D11_BIND_DEPTH_STENCIL;
        result = m_Device->CreateTexture2D(&depthDescription, nullptr, &m_DepthStencilTexture);
        if (FAILED(result))
        {
            error = HResultError("ID3D11Device::CreateTexture2D(back-buffer depth)", result);
            return false;
        }
        result = m_Device->CreateDepthStencilView(m_DepthStencilTexture.Get(), nullptr, &m_DepthStencilView);
        if (FAILED(result))
        {
            error = HResultError("ID3D11Device::CreateDepthStencilView(back-buffer depth)", result);
            return false;
        }
        return true;
    }

    void D3D11RenderBackend::ReleaseRenderTarget() noexcept
    {
        if (m_DeviceContext)
            m_DeviceContext->OMSetRenderTargets(0, nullptr, nullptr);
        m_DepthStencilView.Reset();
        m_DepthStencilTexture.Reset();
        m_RenderTargetView.Reset();
    }

    std::unique_ptr<IRenderBackend> CreateD3D11RenderBackend()
    {
        return std::make_unique<D3D11RenderBackend>();
    }
}
