#include "VulkanVertexBuffer.h"
#include "core/log/Log.h"

namespace NcmaEngine
{
	VulkanVertexBuffer::VulkanVertexBuffer(float* vertices, uint32_t size)
		: m_Size(size)
	{
		CORE_LOG_INFO("Created Vulkan VertexBuffer (size: {} bytes)", size);
	}

	VulkanVertexBuffer::VulkanVertexBuffer(uint32_t size)
		: m_Size(size)
	{
		CORE_LOG_INFO("Created Vulkan VertexBuffer (dynamic, size: {} bytes)", size);
	}

	VulkanVertexBuffer::~VulkanVertexBuffer()
	{
		if (m_Buffer != VK_NULL_HANDLE)
			vkDestroyBuffer(VK_NULL_HANDLE, m_Buffer, nullptr);
		if (m_BufferMemory != VK_NULL_HANDLE)
			vkFreeMemory(VK_NULL_HANDLE, m_BufferMemory, nullptr);
	}

	void VulkanVertexBuffer::Bind() const
	{
	}

	void VulkanVertexBuffer::Unbind() const
	{
	}

	void VulkanVertexBuffer::SetData(const void* data, uint32_t size)
	{
	}
}
