#include "VulkanShader.h"
#include "core/log/Log.h"

namespace NcmaEngine
{
	VulkanShader::VulkanShader(const std::string& vertexSrc, const std::string& fragmentSrc)
		: m_Name("Unnamed")
	{
		CORE_LOG_INFO("Vulkan Shader created (from source)");
	}

	VulkanShader::VulkanShader(const std::string& name, const std::string& vertexSrc, const std::string& fragmentSrc)
		: m_Name(name)
	{
		CORE_LOG_INFO("Vulkan Shader '{}' created", name);
	}

	VulkanShader::~VulkanShader()
	{
		if (m_Pipeline != VK_NULL_HANDLE)
			vkDestroyPipeline(VK_NULL_HANDLE, m_Pipeline, nullptr);
		if (m_PipelineLayout != VK_NULL_HANDLE)
			vkDestroyPipelineLayout(VK_NULL_HANDLE, m_PipelineLayout, nullptr);
	}

	void VulkanShader::Bind() const
	{
	}

	void VulkanShader::Unbind() const
	{
	}

	void VulkanShader::SetUniform1i(const std::string& name, int value)
	{
	}

	void VulkanShader::SetUniform1f(const std::string& name, float value)
	{
	}

	void VulkanShader::SetUniform2f(const std::string& name, float v0, float v1)
	{
	}

	void VulkanShader::SetUniform3f(const std::string& name, float v0, float v1, float v2)
	{
	}

	void VulkanShader::SetUniform4f(const std::string& name, float v0, float v1, float v2, float v3)
	{
	}

	void VulkanShader::SetUniformMat4f(const std::string& name, const float* value)
	{
	}
}
