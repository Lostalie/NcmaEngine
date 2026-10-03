#include "D3D12RenderAPI.h"
#include "application/windows/WindowsWindow.h"
#include "application/Application.h"
#include "core/log/Log.h"

#ifdef _DEBUG
#include <dxgidebug.h>
#endif

using namespace Microsoft::WRL;

namespace NcmaEngine
{
	D3D12RenderAPI::D3D12RenderAPI()
		: RenderAPI(RenderAPIType::Direct3D12)
	{
	}

	D3D12RenderAPI::~D3D12RenderAPI()
	{
		Shutdown();
	}

	void D3D12RenderAPI::Init()
	{
		CORE_LOG_INFO("Initializing Direct3D 12 RenderAPI...");

#ifdef _DEBUG
		// Enable debug layer
		ComPtr<ID3D12Debug> debugController;
		if (SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&debugController))))
		{
			debugController->EnableDebugLayer();
			CORE_LOG_INFO("D3D12 debug layer enabled");
		}
#endif

		CreateDevice();
		CreateCommandQueue();
		CreateFence();
		CreateSwapChain();
		CreateRenderTargetViews();
		CreateDepthStencilBuffer();

		// Create command list
		ThrowIfFailed(m_Device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, m_CommandAllocators[0].Get(), nullptr, IID_PPV_ARGS(&m_CommandList)));
		ThrowIfFailed(m_CommandList->Close());

		m_Viewport.Width = 1280.0f;
		m_Viewport.Height = 720.0f;
		m_Viewport.MinDepth = 0.0f;
		m_Viewport.MaxDepth = 1.0f;
		m_Viewport.TopLeftX = 0.0f;
		m_Viewport.TopLeftY = 0.0f;

		m_ScissorRect.left = 0;
		m_ScissorRect.top = 0;
		m_ScissorRect.right = 1280;
		m_ScissorRect.bottom = 720;

		CORE_LOG_INFO("Direct3D 12 RenderAPI initialized");
		CORE_LOG_INFO("  Device: D3D12");

#ifdef _DEBUG
		ComPtr<IDXGIDebug> dxgiDebug;
		if (SUCCEEDED(DXGIGetDebugInterface0(0, IID_PPV_ARGS(&dxgiDebug))))
		{
			CORE_LOG_INFO("DXGI debug interface available");
		}
#endif
	}

	void D3D12RenderAPI::Shutdown()
	{
		WaitForGPU();

		if (m_FenceEvent)
		{
			CloseHandle(m_FenceEvent);
			m_FenceEvent = nullptr;
		}

		for (uint32_t i = 0; i < s_FrameCount; i++)
		{
			m_RenderTargets[i].Reset();
		}
		m_DepthStencilBuffer.Reset();
		m_RTVHeap.Reset();
		m_DSVHeap.Reset();
		m_CommandList.Reset();
		m_CommandQueue.Reset();
		m_Device.Reset();
		m_Fence.Reset();
		m_SwapChain.Reset();
		m_Factory.Reset();

		CORE_LOG_INFO("Direct3D 12 RenderAPI shutdown complete");
	}

	void D3D12RenderAPI::CreateDevice()
	{
		UINT dxgiFactoryFlags = 0;

#ifdef _DEBUG
		dxgiFactoryFlags |= DXGI_CREATE_FACTORY_DEBUG;
#endif

		ThrowIfFailed(CreateDXGIFactory2(dxgiFactoryFlags, IID_PPV_ARGS(&m_Factory)));

		// Try to create device with hardware acceleration
		ComPtr<IDXGIAdapter1> adapter;
		for (uint32_t i = 0; m_Factory->EnumAdapters1(i, &adapter) != DXGI_ERROR_NOT_FOUND; i++)
		{
			DXGI_ADAPTER_DESC1 desc;
			adapter->GetDesc1(&desc);

			if (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE)
				continue;

			if (SUCCEEDED(D3D12CreateDevice(adapter.Get(), D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&m_Device))))
			{
				CORE_LOG_INFO("  Adapter: {}", NcmaEngine::WStringToString(desc.Description).c_str());
				break;
			}
		}

		if (!m_Device)
		{
			CORE_LOG_ERROR("Failed to create D3D12 device!");
			throw std::exception("Failed to create D3D12 device");
		}
	}

	void D3D12RenderAPI::CreateCommandQueue()
	{
		D3D12_COMMAND_QUEUE_DESC queueDesc = {};
		queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
		queueDesc.Flags = D3D12_COMMAND_QUEUE_FLAG_NONE;

		ThrowIfFailed(m_Device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&m_CommandQueue)));

		for (uint32_t i = 0; i < s_FrameCount; i++)
		{
			ThrowIfFailed(m_Device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&m_CommandAllocators[i])));
		}
	}

	void D3D12RenderAPI::CreateFence()
	{
		ThrowIfFailed(m_Device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&m_Fence)));
		m_FenceEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);
		if (!m_FenceEvent)
		{
			throw std::exception("Failed to create fence event");
		}
	}

	void D3D12RenderAPI::CreateSwapChain()
	{
		Application& app = Application::Get();
		Window& window = app.GetWindow();
		void* hwnd = ((WindowsWindow&)window).GetHWND();

		DXGI_SWAP_CHAIN_DESC1 swapChainDesc = {};
		swapChainDesc.Width = window.GetWidth();
		swapChainDesc.Height = window.GetHeight();
		swapChainDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
		swapChainDesc.SampleDesc.Count = 1;
		swapChainDesc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
		swapChainDesc.BufferCount = s_FrameCount;
		swapChainDesc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;

		ComPtr<IDXGISwapChain1> swapChain;
		ThrowIfFailed(m_Factory->CreateSwapChainForHwnd(m_CommandQueue.Get(), (HWND)hwnd, &swapChainDesc, nullptr, nullptr, &swapChain));
		ThrowIfFailed(swapChain.As(&m_SwapChain));

		m_Factory->MakeWindowAssociation((HWND)hwnd, DXGI_MWA_NO_ALT_ENTER);
		m_CurrentFrameIndex = m_SwapChain->GetCurrentBackBufferIndex();
	}

	void D3D12RenderAPI::CreateRenderTargetViews()
	{
		D3D12_DESCRIPTOR_HEAP_DESC rtvHeapDesc = {};
		rtvHeapDesc.NumDescriptors = s_FrameCount;
		rtvHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
		rtvHeapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE;

		ThrowIfFailed(m_Device->CreateDescriptorHeap(&rtvHeapDesc, IID_PPV_ARGS(&m_RTVHeap)));
		m_RTVDescriptorSize = m_Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);

		D3D12_CPU_DESCRIPTOR_HANDLE rtvHandle = m_RTVHeap->GetCPUDescriptorHandleForHeapStart();
		for (uint32_t i = 0; i < s_FrameCount; i++)
		{
			ThrowIfFailed(m_SwapChain->GetBuffer(i, IID_PPV_ARGS(&m_RenderTargets[i])));
			m_Device->CreateRenderTargetView(m_RenderTargets[i].Get(), nullptr, rtvHandle);
			rtvHandle.ptr += m_RTVDescriptorSize;
		}
	}

	void D3D12RenderAPI::CreateDepthStencilBuffer()
	{
		D3D12_DESCRIPTOR_HEAP_DESC dsvHeapDesc = {};
		dsvHeapDesc.NumDescriptors = 1;
		dsvHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_DSV;
		dsvHeapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE;

		ThrowIfFailed(m_Device->CreateDescriptorHeap(&dsvHeapDesc, IID_PPV_ARGS(&m_DSVHeap)));
		m_DSVDescriptorSize = m_Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_DSV);

		D3D12_RESOURCE_DESC depthStencilDesc = {};
		depthStencilDesc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
		depthStencilDesc.Width = 1280;
		depthStencilDesc.Height = 720;
		depthStencilDesc.DepthOrArraySize = 1;
		depthStencilDesc.MipLevels = 1;
		depthStencilDesc.Format = DXGI_FORMAT_D24_UNORM_S8_UINT;
		depthStencilDesc.SampleDesc.Count = 1;
		depthStencilDesc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
		depthStencilDesc.Flags = D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;

		D3D12_CLEAR_VALUE clearValue = {};
		clearValue.Format = DXGI_FORMAT_D24_UNORM_S8_UINT;
		clearValue.DepthStencil.Depth = 1.0f;
		clearValue.DepthStencil.Stencil = 0;

		D3D12_HEAP_PROPERTIES heapProps = {};
		heapProps.Type = D3D12_HEAP_TYPE_DEFAULT;
		heapProps.CPUPageProperty = D3D12_CPU_PAGE_PROPERTY_UNKNOWN;
		heapProps.MemoryPoolPreference = D3D12_MEMORY_POOL_L0;

		ThrowIfFailed(m_Device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &depthStencilDesc,
			D3D12_RESOURCE_STATE_COMMON, &clearValue, IID_PPV_ARGS(&m_DepthStencilBuffer)));

		D3D12_DEPTH_STENCIL_VIEW_DESC dsvDesc = {};
		dsvDesc.Format = DXGI_FORMAT_D24_UNORM_S8_UINT;
		dsvDesc.ViewDimension = D3D12_DSV_DIMENSION_TEXTURE2D;
		dsvDesc.Texture2D.MipSlice = 0;

		m_Device->CreateDepthStencilView(m_DepthStencilBuffer.Get(), &dsvDesc, m_DSVHeap->GetCPUDescriptorHandleForHeapStart());
	}

	void D3D12RenderAPI::UpdateRenderTargetViews()
	{
		m_CurrentFrameIndex = m_SwapChain->GetCurrentBackBufferIndex();
	}

	void D3D12RenderAPI::Clear()
	{
		// Reset command allocator and list
		ThrowIfFailed(m_CommandAllocators[m_CurrentFrameIndex]->Reset());
		ThrowIfFailed(m_CommandList->Reset(m_CommandAllocators[m_CurrentFrameIndex].Get(), nullptr));

		// Transition render target
		D3D12_RESOURCE_BARRIER barrier = {};
		barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
		barrier.Transition.pResource = m_RenderTargets[m_CurrentFrameIndex].Get();
		barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_PRESENT;
		barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_RENDER_TARGET;
		barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
		m_CommandList->ResourceBarrier(1, &barrier);

		// Set viewport and scissor
		m_CommandList->RSSetViewports(1, &m_Viewport);
		m_CommandList->RSSetScissorRects(1, &m_ScissorRect);

		// Clear render target
		D3D12_CPU_DESCRIPTOR_HANDLE rtvHandle = m_RTVHeap->GetCPUDescriptorHandleForHeapStart();
		rtvHandle.ptr += m_CurrentFrameIndex * m_RTVDescriptorSize;
		m_CommandList->ClearRenderTargetView(rtvHandle, m_ClearColor, 0, nullptr);

		// Clear depth stencil
		D3D12_CPU_DESCRIPTOR_HANDLE dsvHandle = m_DSVHeap->GetCPUDescriptorHandleForHeapStart();
		m_CommandList->ClearDepthStencilView(dsvHandle, D3D12_CLEAR_FLAG_DEPTH | D3D12_CLEAR_FLAG_STENCIL, 1.0f, 0, 0, nullptr);

		// Transition back to present
		barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_RENDER_TARGET;
		barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_PRESENT;
		m_CommandList->ResourceBarrier(1, &barrier);

		ThrowIfFailed(m_CommandList->Close());
	}

	void D3D12RenderAPI::SetClearColor(float r, float g, float b, float a)
	{
		m_ClearColor[0] = r;
		m_ClearColor[1] = g;
		m_ClearColor[2] = b;
		m_ClearColor[3] = a;
	}

	void D3D12RenderAPI::SetViewport(uint32_t x, uint32_t y, uint32_t width, uint32_t height)
	{
		m_Viewport.TopLeftX = static_cast<float>(x);
		m_Viewport.TopLeftY = static_cast<float>(y);
		m_Viewport.Width = static_cast<float>(width);
		m_Viewport.Height = static_cast<float>(height);
		m_Viewport.MinDepth = 0.0f;
		m_Viewport.MaxDepth = 1.0f;

		m_ScissorRect.left = x;
		m_ScissorRect.top = y;
		m_ScissorRect.right = x + width;
		m_ScissorRect.bottom = y + height;
	}

	void D3D12RenderAPI::EnableDepthTest(bool enable)
	{
		m_DepthTestEnabled = enable;
	}

	void D3D12RenderAPI::EnableBlending(bool enable)
	{
		m_BlendingEnabled = enable;
	}

	void D3D12RenderAPI::WaitForGPU()
	{
		m_CommandQueue->Signal(m_Fence.Get(), m_FenceValue);
		m_Fence->SetEventOnCompletion(m_FenceValue, m_FenceEvent);
		WaitForSingleObject(m_FenceEvent, INFINITE);
		m_FenceValue++;
	}

	void D3D12RenderAPI::MoveToNextFrame()
	{
		const uint64_t currentFenceValue = m_FenceValue;
		m_CommandQueue->Signal(m_Fence.Get(), currentFenceValue);
		m_FenceValue++;

		m_CommandList->Execute(m_CommandAllocators[m_CurrentFrameIndex].Get());
		m_SwapChain->Present(1, 0);

		UpdateRenderTargetViews();
	}
}
