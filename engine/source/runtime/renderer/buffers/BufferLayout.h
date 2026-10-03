#pragma once

#include "Core.h"
#include <vector>
#include <string>

namespace NcmaEngine
{
	// 顶点属性类型
	enum class ShaderDataType
	{
		None = 0,
		Float,
		Float2,
		Float3,
		Float4,
		Int,
		Int2,
		Int3,
		Int4,
		Bool,
		Mat3,
		Mat4
	};

	// 获取ShaderDataType的大小
	inline uint32_t GetShaderDataTypeSize(ShaderDataType type)
	{
		switch (type)
		{
		case ShaderDataType::Float:  return 4;
		case ShaderDataType::Float2:  return 4 * 2;
		case ShaderDataType::Float3:  return 4 * 3;
		case ShaderDataType::Float4:  return 4 * 4;
		case ShaderDataType::Int:     return 4;
		case ShaderDataType::Int2:    return 4 * 2;
		case ShaderDataType::Int3:    return 4 * 3;
		case ShaderDataType::Int4:    return 4 * 4;
		case ShaderDataType::Bool:    return 1;
		case ShaderDataType::Mat3:    return 4 * 3 * 3;
		case ShaderDataType::Mat4:    return 4 * 4 * 4;
		}
		return 0;
	}

	// 获取OpenGL类型
	inline uint32_t GetShaderDataTypeGLType(ShaderDataType type)
	{
		switch (type)
		{
		case ShaderDataType::Float:   return GL_FLOAT;
		case ShaderDataType::Float2:  return GL_FLOAT;
		case ShaderDataType::Float3:  return GL_FLOAT;
		case ShaderDataType::Float4:  return GL_FLOAT;
		case ShaderDataType::Int:     return GL_INT;
		case ShaderDataType::Int2:    return GL_INT;
		case ShaderDataType::Int3:    return GL_INT;
		case ShaderDataType::Int4:    return GL_INT;
		case ShaderDataType::Bool:    return GL_BOOL;
		case ShaderDataType::Mat3:    return GL_FLOAT;
		case ShaderDataType::Mat4:    return GL_FLOAT;
		}
		return 0;
	}

	// 顶点元素描述
	struct BufferElement
	{
		std::string Name;
		ShaderDataType Type;
		uint32_t Size;
		size_t Offset;

		BufferElement() : Name(""), Type(ShaderDataType::None), Size(0), Offset(0) {}

		BufferElement(const std::string& name, ShaderDataType type)
			: Name(name), Type(type), Size(GetShaderDataTypeSize(type)), Offset(0)
		{
		}

		uint32_t GetComponentCount() const
		{
			switch (Type)
			{
			case ShaderDataType::Float:   return 1;
			case ShaderDataType::Float2:  return 2;
			case ShaderDataType::Float3:  return 3;
			case ShaderDataType::Float4:  return 4;
			case ShaderDataType::Int:     return 1;
			case ShaderDataType::Int2:    return 2;
			case ShaderDataType::Int3:    return 3;
			case ShaderDataType::Int4:    return 4;
			case ShaderDataType::Bool:    return 1;
			case ShaderDataType::Mat3:    return 3 * 3;
			case ShaderDataType::Mat4:    return 4 * 4;
			}
			return 0;
		}
	};

	// 顶点缓冲区布局
	class BufferLayout
	{
	public:
		BufferLayout() {}
		BufferLayout(const std::initializer_list<BufferElement>& elements)
			: m_Elements(elements)
		{
			CalculateOffsetsAndStride();
		}

		const std::vector<BufferElement>& GetElements() const { return m_Elements; }
		uint32_t GetStride() const { return m_Stride; }

		std::vector<BufferElement>::iterator begin() { return m_Elements.begin(); }
		std::vector<BufferElement>::iterator end() { return m_Elements.end(); }
		std::vector<BufferElement>::const_iterator begin() const { return m_Elements.begin(); }
		std::vector<BufferElement>::const_iterator end() const { return m_Elements.end(); }

	private:
		void CalculateOffsetsAndStride()
		{
			size_t offset = 0;
			m_Stride = 0;
			for (auto& element : m_Elements)
			{
				element.Offset = offset;
				offset += element.Size;
				m_Stride += element.Size;
			}
		}

		std::vector<BufferElement> m_Elements;
		uint32_t m_Stride = 0;
	};
}
