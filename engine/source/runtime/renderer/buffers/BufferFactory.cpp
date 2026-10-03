#include "VertexBuffer.h"
#include "IndexBuffer.h"
#include "VertexArray.h"
#include "OpenGLVertexBuffer.h"
#include "OpenGLIndexBuffer.h"
#include "OpenGLVertexArray.h"

namespace NcmaEngine
{
	VertexBuffer* VertexBuffer::Create(float* vertices, uint32_t size)
	{
		return new OpenGLVertexBuffer(vertices, size);
	}

	VertexBuffer* VertexBuffer::Create(uint32_t size)
	{
		return new OpenGLVertexBuffer(size);
	}

	IndexBuffer* IndexBuffer::Create(uint32_t* indices, uint32_t count)
	{
		return new OpenGLIndexBuffer(indices, count);
	}

	VertexArray* VertexArray::Create()
	{
		return new OpenGLVertexArray();
	}
}
