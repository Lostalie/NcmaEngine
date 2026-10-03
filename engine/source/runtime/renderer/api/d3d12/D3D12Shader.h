#pragma once

#include "../shaders/Shader.h"
#include <d3d12.h>
#include <wrl/client.h>
#include <string>
#include <unordered_map>

using namespace Microsoft::WRL;

namespace NcmaEngine
{
	class D3D12Shader : public Shader
	{
	public:
		D3D12Shader(const std::string& vertexSrc, const std::string& fragmentSrc);
		D3D12Shader(const std::string& name, const std::string& vertexSrc, const std::string& fragmentSrc);
		virtual ~D3D12Shader();

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
		std::unordered_map<std::string, int> m_UniformCache;
	};
}
