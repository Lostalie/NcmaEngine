#include "D3D12IndexBuffer.h"
#include "D3D12RenderAPI.h"
#include "core/log/Log.h"

namespace NcmaEngine
{
	D3D12IndexBuffer::D3D12IndexBuffer(uint32_t* indices, uint32_t count)
		: m_Count(count)
	{
		CORE_LOG_INFO("Created D3D12 IndexBuffer (count: {})", count);
	}

	D3D12IndexBuffer::~D3D12IndexBuffer()
	{
		m_IndexBuffer.Reset();
	}

	void D3D12IndexBuffer::Bind() const
	{
	}

	void D3D12IndexBuffer::Unbind() const
	{
	}
}
