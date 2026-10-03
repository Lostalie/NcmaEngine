#include "D3D12VertexBuffer.h"
#include "D3D12RenderAPI.h"
#include "core/log/Log.h"

namespace NcmaEngine
{
	D3D12VertexBuffer::D3D12VertexBuffer(float* vertices, uint32_t size)
		: m_Size(size)
	{
		CORE_LOG_INFO("Created D3D12 VertexBuffer (size: {} bytes)", size);
	}

	D3D12VertexBuffer::D3D12VertexBuffer(uint32_t size)
		: m_Size(size)
	{
		CORE_LOG_INFO("Created D3D12 VertexBuffer (dynamic, size: {} bytes)", size);
	}

	D3D12VertexBuffer::~D3D12VertexBuffer()
	{
		m_VertexBuffer.Reset();
	}

	void D3D12VertexBuffer::Bind() const
	{
	}

	void D3D12VertexBuffer::Unbind() const
	{
	}

	void D3D12VertexBuffer::SetData(const void* data, uint32_t size)
	{
	}
}
