#include "VulkanIndexBuffer.h"
#include "core/log/Log.h"

namespace NcmaEngine
{
	VulkanIndexBuffer::VulkanIndexBuffer(uint32_t* indices, uint32_t count)
		: m_Count(count)
	{
		CORE_LOG_INFO("Created Vulkan IndexBuffer (count: {})", count);
	}

	VulkanIndexBuffer::~VulkanIndexBuffer()
	{
		if (m_Buffer != VK_NULL_HANDLE)
			vkDestroyBuffer(VK_NULL_HANDLE, m_Buffer, nullptr);
		if (m_BufferMemory != VK_NULL_HANDLE)
			vkFreeMemory(VK_NULL_HANDLE, m_BufferMemory, nullptr);
	}

	void VulkanIndexBuffer::Bind() const
	{
	}

	void VulkanIndexBuffer::Unbind() const
	{
	}
}
