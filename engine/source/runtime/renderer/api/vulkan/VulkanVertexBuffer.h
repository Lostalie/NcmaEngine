#pragma once

#include "../buffers/VertexBuffer.h"
#include <vulkan/vulkan.h>

namespace NcmaEngine
{
	class VulkanVertexBuffer : public VertexBuffer
	{
	public:
		VulkanVertexBuffer(float* vertices, uint32_t size);
		VulkanVertexBuffer(uint32_t size);
		virtual ~VulkanVertexBuffer();

		virtual void Bind() const override;
		virtual void Unbind() const override;
		virtual void SetData(const void* data, uint32_t size) override;

		VkBuffer GetBuffer() const { return m_Buffer; }

	private:
		VkBuffer m_Buffer = VK_NULL_HANDLE;
		VkDeviceMemory m_BufferMemory = VK_NULL_HANDLE;
		uint32_t m_Size = 0;
	};
}
