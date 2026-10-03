#include "D3D12Shader.h"
#include "core/log/Log.h"

namespace NcmaEngine
{
	D3D12Shader::D3D12Shader(const std::string& vertexSrc, const std::string& fragmentSrc)
		: m_Name("Unnamed")
	{
		CORE_LOG_INFO("D3D12 Shader created (from source)");
	}

	D3D12Shader::D3D12Shader(const std::string& name, const std::string& vertexSrc, const std::string& fragmentSrc)
		: m_Name(name)
	{
		CORE_LOG_INFO("D3D12 Shader '{}' created", name);
	}

	D3D12Shader::~D3D12Shader()
	{
	}

	void D3D12Shader::Bind() const
	{
	}

	void D3D12Shader::Unbind() const
	{
	}

	void D3D12Shader::SetUniform1i(const std::string& name, int value)
	{
	}

	void D3D12Shader::SetUniform1f(const std::string& name, float value)
	{
	}

	void D3D12Shader::SetUniform2f(const std::string& name, float v0, float v1)
	{
	}

	void D3D12Shader::SetUniform3f(const std::string& name, float v0, float v1, float v2)
	{
	}

	void D3D12Shader::SetUniform4f(const std::string& name, float v0, float v1, float v2, float v3)
	{
	}

	void D3D12Shader::SetUniformMat4f(const std::string& name, const float* value)
	{
	}
}
