#pragma once

#include "../buffers/IndexBuffer.h"
#include <vulkan/vulkan.h>

namespace NcmaEngine
{
	class VulkanIndexBuffer : public IndexBuffer
	{
	public:
		VulkanIndexBuffer(uint32_t* indices, uint32_t count);
		virtual ~VulkanIndexBuffer();

		virtual void Bind() const override;
		virtual void Unbind() const override;
		virtual uint32_t GetCount() const override { return m_Count; }

		VkBuffer GetBuffer() const { return m_Buffer; }

	private:
		VkBuffer m_Buffer = VK_NULL_HANDLE;
		VkDeviceMemory m_BufferMemory = VK_NULL_HANDLE;
		uint32_t m_Count = 0;
	};
}
