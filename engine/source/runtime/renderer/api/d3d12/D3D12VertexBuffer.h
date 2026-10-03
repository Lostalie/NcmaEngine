#pragma once

#include "../buffers/VertexBuffer.h"
#include <d3d12.h>
#include <wrl/client.h>

using namespace Microsoft::WRL;

namespace NcmaEngine
{
	class D3D12VertexBuffer : public VertexBuffer
	{
	public:
		D3D12VertexBuffer(float* vertices, uint32_t size);
		D3D12VertexBuffer(uint32_t size);
		virtual ~D3D12VertexBuffer();

		virtual void Bind() const override;
		virtual void Unbind() const override;
		virtual void SetData(const void* data, uint32_t size) override;

		ID3D12Resource* GetResource() const { return m_VertexBuffer.Get(); }
		D3D12_VERTEX_BUFFER_VIEW GetView() const { return m_VertexBufferView; }

	private:
		ComPtr<ID3D12Resource> m_VertexBuffer;
		D3D12_VERTEX_BUFFER_VIEW m_VertexBufferView;
		uint32_t m_Size = 0;
	};
}
