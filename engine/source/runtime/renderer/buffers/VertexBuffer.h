#pragma once

#include "Core.h"

namespace NcmaEngine
{
	class VertexBuffer
	{
	public:
		virtual ~VertexBuffer() = default;

		// 绑定缓冲区
		virtual void Bind() const = 0;
		// 解绑缓冲区
		virtual void Unbind() const = 0;

		// 设置顶点数据
		virtual void SetData(const void* data, uint32_t size) = 0;

		// 获取布局
		virtual const class BufferLayout& GetLayout() const = 0;
		// 设置布局
		virtual void SetLayout(const class BufferLayout& layout) = 0;

		// 创建顶点缓冲区
		static VertexBuffer* Create(float* vertices, uint32_t size);
		static VertexBuffer* Create(uint32_t size);
	};
}
