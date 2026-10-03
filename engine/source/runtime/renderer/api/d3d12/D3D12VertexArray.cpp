#include "D3D12VertexArray.h"
#include "core/log/Log.h"

namespace NcmaEngine
{
	D3D12VertexArray::D3D12VertexArray()
	{
		CORE_LOG_INFO("Created D3D12 VertexArray");
	}

	D3D12VertexArray::~D3D12VertexArray()
	{
	}

	void D3D12VertexArray::Bind() const
	{
	}

	void D3D12VertexArray::Unbind() const
	{
	}

	void D3D12VertexArray::AddVertexBuffer(VertexBuffer* vertexBuffer)
	{
		m_VertexBuffers.push_back(vertexBuffer);
	}

	void D3D12VertexArray::SetIndexBuffer(IndexBuffer* indexBuffer)
	{
		m_IndexBuffer = indexBuffer;
	}
}
