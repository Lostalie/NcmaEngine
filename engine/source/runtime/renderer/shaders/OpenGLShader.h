#pragma once

#include "Shader.h"
#include <string>
#include <unordered_map>

namespace NcmaEngine
{
	class OpenGLShader : public Shader
	{
	public:
		OpenGLShader(const std::string& vertexSrc, const std::string& fragmentSrc);
		OpenGLShader(const std::string& name, const std::string& vertexSrc, const std::string& fragmentSrc);
		virtual ~OpenGLShader();

		virtual void Bind() const override;
		virtual void Unbind() const override;

		virtual void SetUniform1i(const std::string& name, int value) override;
		virtual void SetUniform1f(const std::string& name, float value) override;
		virtual void SetUniform2f(const std::string& name, float v0, float v1) override;
		virtual void SetUniform3f(const std::string& name, float v0, float v1, float v2) override;
		virtual void SetUniform4f(const std::string& name, float v0, float v1, float v2, float v3) override;
		virtual void SetUniformMat4f(const std::string& name, const float* value) override;

		virtual const std::string& GetName() const override { return m_Name; }

	private:
		std::string m_Name;
		uint32_t m_RendererID;
		std::unordered_map<std::string, int> m_UniformLocationCache;

		void Compile(const std::string& vertexSrc, const std::string& fragmentSrc);
		uint32_t CompileShader(uint32_t type, const std::string& source);
		int GetUniformLocation(const std::string& name);
	};
}
