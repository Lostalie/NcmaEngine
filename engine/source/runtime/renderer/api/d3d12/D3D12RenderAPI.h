#pragma once

#include "../RenderAPI.h"
#include <d3d12.h>
#include <dxgi1_6.h>
#include <wrl/client.h>

#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")

using namespace Microsoft::WRL;

namespace NcmaEngine
{
	class D3D12RenderAPI : public RenderAPI
	{
	public:
		D3D12RenderAPI();
		virtual ~D3D12RenderAPI();

		virtual void Init() override;
		virtual void Shutdown() override;

		virtual void Clear() override;
		virtual void SetClearColor(float r, float g, float b, float a) override;

		virtual void SetViewport(uint32_t x, uint32_t y, uint32_t width, uint32_t height) override;

		virtual void EnableDepthTest(bool enable) override;
		virtual void EnableBlending(bool enable) override;

		// D3D12 specific
		ID3D12Device* GetDevice() const { return m_Device.Get(); }
		ID3D12GraphicsCommandList* GetCommandList() const { return m_CommandList.Get(); }
		ID3D12CommandQueue* GetCommandQueue() const { return m_CommandQueue.Get(); }
		ID3D12Fence* GetFence() const { return m_Fence.Get(); }
		HANDLE GetFenceEvent() const { return m_FenceEvent; }
		uint64_t GetFenceValue() const { return m_FenceValue; }

		void WaitForGPU();
		void MoveToNextFrame();

	private:
		void CreateDevice();
		void CreateCommandQueue();
		void CreateFence();
		void CreateSwapChain();
		void CreateRenderTargetViews();
		void CreateDepthStencilBuffer();
		void UpdateRenderTargetViews();

		static const uint32_t s_FrameCount = 2;

		ComPtr<IDXGIFactory6> m_Factory;
		ComPtr<ID3D12Device> m_Device;
		ComPtr<ID3D12CommandQueue> m_CommandQueue;
		ComPtr<ID3D12GraphicsCommandList> m_CommandList;
		ComPtr<ID3D12CommandAllocator> m_CommandAllocators[s_FrameCount];
		ComPtr<IDXGISwapChain3> m_SwapChain;
		ComPtr<ID3D12DescriptorHeap> m_RTVHeap;
		ComPtr<ID3D12DescriptorHeap> m_DSVHeap;
		ComPtr<ID3D12Resource> m_RenderTargets[s_FrameCount];
		ComPtr<ID3D12Resource> m_DepthStencilBuffer;
		ComPtr<ID3D12Fence> m_Fence;

		D3D12_VIEWPORT m_Viewport;
		D3D12_RECT m_ScissorRect;

		uint32_t m_RTVDescriptorSize = 0;
		uint32_t m_DSVDescriptorSize = 0;
		uint32_t m_CurrentFrameIndex = 0;
		uint64_t m_FenceValue = 0;
		HANDLE m_FenceEvent = nullptr;

		float m_ClearColor[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
		bool m_DepthTestEnabled = true;
		bool m_BlendingEnabled = false;
	};
}
