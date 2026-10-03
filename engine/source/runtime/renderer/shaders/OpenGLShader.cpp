#include "OpenGLShader.h"
#include "core/log/Log.h"
#include "../OpenGLLoader.h"
#include <fstream>

namespace NcmaEngine
{
	OpenGLShader::OpenGLShader(const std::string& vertexSrc, const std::string& fragmentSrc)
		: m_Name("Unnamed")
	{
		Compile(vertexSrc, fragmentSrc);
	}

	OpenGLShader::OpenGLShader(const std::string& name, const std::string& vertexSrc, const std::string& fragmentSrc)
		: m_Name(name)
	{
		Compile(vertexSrc, fragmentSrc);
	}

	OpenGLShader::~OpenGLShader()
	{
		glDeleteProgram(m_RendererID);
	}

	void OpenGLShader::Compile(const std::string& vertexSrc, const std::string& fragmentSrc)
	{
		m_RendererID = glCreateProgram();

		uint32_t vs = CompileShader(GL_VERTEX_SHADER, vertexSrc);
		uint32_t fs = CompileShader(GL_FRAGMENT_SHADER, fragmentSrc);

		glAttachShader(m_RendererID, vs);
		glAttachShader(m_RendererID, fs);
		glLinkProgram(m_RendererID);

		GLint linkStatus;
		glGetProgramiv(m_RendererID, GL_LINK_STATUS, &linkStatus);
		if (linkStatus == GL_FALSE)
		{
			GLint infoLogLength;
			glGetProgramiv(m_RendererID, GL_INFO_LOG_LENGTH, &infoLogLength);
			std::string infoLog(infoLogLength, ' ');
			glGetProgramInfoLog(m_RendererID, infoLogLength, nullptr, &infoLog[0]);
			CORE_LOG_ERROR("Shader program linking failed: {}", infoLog);
		}

		glDeleteShader(vs);
		glDeleteShader(fs);

		CORE_LOG_INFO("OpenGL Shader compiled successfully (ID: {})", m_RendererID);
	}

	uint32_t OpenGLShader::CompileShader(uint32_t type, const std::string& source)
	{
		uint32_t shader = glCreateShader(type);
		const char* src = source.c_str();
		glShaderSource(shader, 1, &src, nullptr);
		glCompileShader(shader);

		GLint compileStatus;
		glGetShaderiv(shader, GL_COMPILE_STATUS, &compileStatus);
		if (compileStatus == GL_FALSE)
		{
			GLint infoLogLength;
			glGetShaderiv(shader, GL_INFO_LOG_LENGTH, &infoLogLength);
			std::string infoLog(infoLogLength, ' ');
			glGetShaderInfoLog(shader, infoLogLength, nullptr, &infoLog[0]);

			std::string shaderType = (type == GL_VERTEX_SHADER) ? "vertex" : "fragment";
			CORE_LOG_ERROR("{} shader compilation failed: {}", shaderType, infoLog);

			glDeleteShader(shader);
			return 0;
		}

		return shader;
	}

	void OpenGLShader::Bind() const
	{
		glUseProgram(m_RendererID);
	}

	void OpenGLShader::Unbind() const
	{
		glUseProgram(0);
	}

	int OpenGLShader::GetUniformLocation(const std::string& name)
	{
		if (m_UniformLocationCache.find(name) != m_UniformLocationCache.end())
			return m_UniformLocationCache[name];

		int location = glGetUniformLocation(m_RendererID, name.c_str());
		m_UniformLocationCache[name] = location;
		return location;
	}

	void OpenGLShader::SetUniform1i(const std::string& name, int value)
	{
		glUniform1i(GetUniformLocation(name), value);
	}

	void OpenGLShader::SetUniform1f(const std::string& name, float value)
	{
		glUniform1f(GetUniformLocation(name), value);
	}

	void OpenGLShader::SetUniform2f(const std::string& name, float v0, float v1)
	{
		glUniform2f(GetUniformLocation(name), v0, v1);
	}

	void OpenGLShader::SetUniform3f(const std::string& name, float v0, float v1, float v2)
	{
		glUniform3f(GetUniformLocation(name), v0, v1, v2);
	}

	void OpenGLShader::SetUniform4f(const std::string& name, float v0, float v1, float v2, float v3)
	{
		glUniform4f(GetUniformLocation(name), v0, v1, v2, v3);
	}

	void OpenGLShader::SetUniformMat4f(const std::string& name, const float* value)
	{
		glUniformMatrix4fv(GetUniformLocation(name), 1, GL_FALSE, value);
	}
}
