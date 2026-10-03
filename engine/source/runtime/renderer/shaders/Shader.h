#pragma once

#include "Core.h"
#include <string>
#include <unordered_map>

namespace NcmaEngine
{
	class Shader
	{
	public:
		virtual ~Shader() = default;

		// 绑定着色器程序
		virtual void Bind() const = 0;
		// 解绑着色器程序
		virtual void Unbind() const = 0;

		// 设置 uniform 变量
		virtual void SetUniform1i(const std::string& name, int value) = 0;
		virtual void SetUniform1f(const std::string& name, float value) = 0;
		virtual void SetUniform2f(const std::string& name, float v0, float v1) = 0;
		virtual void SetUniform3f(const std::string& name, float v0, float v1, float v2) = 0;
		virtual void SetUniform4f(const std::string& name, float v0, float v1, float v2, float v3) = 0;
		virtual void SetUniformMat4f(const std::string& name, const float* value) = 0;

		// 获取着色器名称
		virtual const std::string& GetName() const = 0;

		// 创建着色器（从源码）
		static Shader* Create(const std::string& vertexSrc, const std::string& fragmentSrc);
		static Shader* Create(const std::string& name, const std::string& vertexSrc, const std::string& fragmentSrc);
		// 创建着色器（从文件）
		static Shader* CreateFromFile(const std::string& vertexPath, const std::string& fragmentPath);

	private:
		friend class OpenGLShader;
		uint32_t m_RendererID;
		std::string m_Name;
	};
}
