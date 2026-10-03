#pragma once

#include "../buffers/IndexBuffer.h"
#include <d3d12.h>
#include <wrl/client.h>

using namespace Microsoft::WRL;

namespace NcmaEngine
{
	class D3D12IndexBuffer : public IndexBuffer
	{
	public:
		D3D12IndexBuffer(uint32_t* indices, uint32_t count);
		virtual ~D3D12IndexBuffer();

		virtual void Bind() const override;
		virtual void Unbind() const override;
		virtual uint32_t GetCount() const override { return m_Count; }

		ID3D12Resource* GetResource() const { return m_IndexBuffer.Get(); }
		D3D12_INDEX_BUFFER_VIEW GetView() const { return m_IndexBufferView; }

	private:
		ComPtr<ID3D12Resource> m_IndexBuffer;
		D3D12_INDEX_BUFFER_VIEW m_IndexBufferView;
		uint32_t m_Count = 0;
	};
}
