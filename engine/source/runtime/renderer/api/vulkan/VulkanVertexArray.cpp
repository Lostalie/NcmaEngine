#include "VulkanVertexArray.h"
#include "core/log/Log.h"

namespace NcmaEngine
{
	VulkanVertexArray::VulkanVertexArray()
	{
		CORE_LOG_INFO("Created Vulkan VertexArray");
	}

	VulkanVertexArray::~VulkanVertexArray()
	{
	}

	void VulkanVertexArray::Bind() const
	{
	}

	void VulkanVertexArray::Unbind() const
	{
	}

	void VulkanVertexArray::AddVertexBuffer(VertexBuffer* vertexBuffer)
	{
		m_VertexBuffers.push_back(vertexBuffer);
	}

	void VulkanVertexArray::SetIndexBuffer(IndexBuffer* indexBuffer)
	{
		m_IndexBuffer = indexBuffer;
	}
}
