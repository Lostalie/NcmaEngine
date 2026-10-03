#include "Shader.h"
#include "OpenGLShader.h"
#include "core/log/Log.h"
#include <fstream>
#include <sstream>

namespace NcmaEngine
{
	static std::string ReadFile(const std::string& filepath)
	{
		std::ifstream file(filepath);
		if (!file.is_open())
		{
			CORE_LOG_ERROR("Failed to open shader file: {}", filepath);
			return "";
		}

		std::stringstream ss;
		ss << file.rdbuf();
		file.close();
		return ss.str();
	}

	Shader* Shader::Create(const std::string& vertexSrc, const std::string& fragmentSrc)
	{
		return new OpenGLShader(vertexSrc, fragmentSrc);
	}

	Shader* Shader::Create(const std::string& name, const std::string& vertexSrc, const std::string& fragmentSrc)
	{
		return new OpenGLShader(name, vertexSrc, fragmentSrc);
	}

	Shader* Shader::CreateFromFile(const std::string& vertexPath, const std::string& fragmentPath)
	{
		std::string vertexSrc = ReadFile(vertexPath);
		std::string fragmentSrc = ReadFile(fragmentPath);

		if (vertexSrc.empty() || fragmentSrc.empty())
		{
			CORE_LOG_ERROR("Failed to load shader files!");
			return nullptr;
		}

		// Extract name from file path
		size_t lastSlash = vertexPath.find_last_of("/\\");
		std::string name = (lastSlash != std::string::npos) ? vertexPath.substr(lastSlash + 1) : vertexPath;

		return new OpenGLShader(name, vertexSrc, fragmentSrc);
	}
}
