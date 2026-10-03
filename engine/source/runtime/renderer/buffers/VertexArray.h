#pragma once

#include "Core.h"

namespace NcmaEngine
{
	class VertexArray
	{
	public:
		virtual ~VertexArray() = default;

		// 绑定顶点数组
		virtual void Bind() const = 0;
		// 解绑顶点数组
		virtual void Unbind() const = 0;

		// 添加顶点缓冲区并自动设置布局
		virtual void AddVertexBuffer(class VertexBuffer* vertexBuffer) = 0;
		// 设置索引缓冲区
		virtual void SetIndexBuffer(class IndexBuffer* indexBuffer) = 0;

		// 获取索引缓冲区
		virtual class IndexBuffer* GetIndexBuffer() const = 0;

		// 创建顶点数组
		static VertexArray* Create();

	private:
		friend class OpenGLVertexArray;
		uint32_t m_RendererID;
	};
}
