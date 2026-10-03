#pragma once

#include "../shaders/Shader.h"
#include <vulkan/vulkan.h>
#include <string>
#include <unordered_map>

namespace NcmaEngine
{
	class VulkanShader : public Shader
	{
	public:
		VulkanShader(const std::string& vertexSrc, const std::string& fragmentSrc);
		VulkanShader(const std::string& name, const std::string& vertexSrc, const std::string& fragmentSrc);
		virtual ~VulkanShader();

		virtual void Bind() const override;
		virtual void Unbind() const override;

		virtual void SetUniform1i(const std::string& name, int value) override;
		virtual void SetUniform1f(const std::string& name, float value) override;
		virtual void SetUniform2f(const std::string& name, float v0, float v1) override;
		virtual void SetUniform3f(const std::string& name, float v0, float v1, float v2) override;
		virtual void SetUniform4f(const std::string& name, float v0, float v1, float v2, float v3) override;
		virtual void SetUniformMat4f(const std::string& name, const float* value) override;

		virtual const std::string& GetName() const override { return m_Name; }

		VkPipeline GetPipeline() const { return m_Pipeline; }
		VkPipelineLayout GetPipelineLayout() const { return m_PipelineLayout; }

	private:
		std::string m_Name;
		std::unordered_map<std::string, int> m_UniformCache;
		VkPipeline m_Pipeline = VK_NULL_HANDLE;
		VkPipelineLayout m_PipelineLayout = VK_NULL_HANDLE;
	};
}
