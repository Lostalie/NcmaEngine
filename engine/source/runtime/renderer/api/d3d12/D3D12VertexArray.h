#pragma once

#include "../buffers/VertexArray.h"
#include <d3d12.h>
#include <wrl/client.h>

using namespace Microsoft::WRL;

namespace NcmaEngine
{
	class D3D12VertexArray : public VertexArray
	{
	public:
		D3D12VertexArray();
		virtual ~D3D12VertexArray();

		virtual void Bind() const override;
		virtual void Unbind() const override;
		virtual void AddVertexBuffer(VertexBuffer* vertexBuffer) override;
		virtual void SetIndexBuffer(IndexBuffer* indexBuffer) override;
		virtual IndexBuffer* GetIndexBuffer() const override { return m_IndexBuffer; }

	private:
		std::vector<VertexBuffer*> m_VertexBuffers;
		IndexBuffer* m_IndexBuffer = nullptr;
	};
}
