#pragma once

#include "Core.h"

namespace NcmaEngine
{
	class IndexBuffer
	{
	public:
		virtual ~IndexBuffer() = default;

		// 绑定缓冲区
		virtual void Bind() const = 0;
		// 解绑缓冲区
		virtual void Unbind() const = 0;

		// 获取索引数量
		virtual uint32_t GetCount() const = 0;

		// 创建索引缓冲区
		static IndexBuffer* Create(uint32_t* indices, uint32_t count);
	};
}
