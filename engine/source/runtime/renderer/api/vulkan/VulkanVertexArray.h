#pragma once

#include "../buffers/VertexArray.h"
#include <vulkan/vulkan.h>

namespace NcmaEngine
{
	class VulkanVertexArray : public VertexArray
	{
	public:
		VulkanVertexArray();
		virtual ~VulkanVertexArray();

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
