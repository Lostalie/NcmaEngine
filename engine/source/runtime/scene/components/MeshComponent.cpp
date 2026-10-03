#include "MeshComponent.h"
#include "renderer/Renderer.h"
#include "renderer/buffers/BufferLayout.h"
#include "renderer/buffers/VertexBuffer.h"
#include "renderer/buffers/IndexBuffer.h"
#include "core/log/Log.h"

namespace NcmaEngine
{
	MeshComponent::MeshComponent()
	{
	}

	MeshComponent::~MeshComponent()
	{
	}

	void MeshComponent::SetGeometry(VertexArray* geometry)
	{
		m_Geometry = geometry;
	}

	void MeshComponent::SetMaterial(Material* material)
	{
		m_Material = material;
	}

	uint32_t MeshComponent::GetIndexCount() const
	{
		if (m_Geometry && m_Geometry->GetIndexBuffer())
			return m_Geometry->GetIndexBuffer()->GetCount();
		return 0;
	}

	StaticMesh::StaticMesh()
	{
	}

	StaticMesh::~StaticMesh()
	{
	}

	void StaticMesh::SetGeometry(VertexArray* geometry)
	{
		m_Geometry = geometry;
	}

	void StaticMesh::SetMaterial(Material* material)
	{
		m_Material = material;
	}

	void StaticMesh::Draw() const
	{
		if (m_Geometry && m_Material)
		{
			m_Material->Bind();
			Renderer::Submit(m_Geometry);
		}
	}

	VertexArray* MeshFactory::CreateQuad(float size)
	{
		// Vertices for a quad (position + normal + texcoord)
		float vertices[] = {
			// Position         // Normal       // TexCoord
			-size,  size, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 1.0f,
			 size,  size, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f,
			 size, -size, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 0.0f,
			-size, -size, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f
		};

		uint32_t indices[] = { 0, 1, 2, 2, 3, 0 };

		BufferLayout layout = {
			{ "a_Position", ShaderDataType::Float3 },
			{ "a_Normal", ShaderDataType::Float3 },
			{ "a_TexCoord", ShaderDataType::Float2 }
		};

		VertexBuffer* vb = VertexBuffer::Create(vertices, sizeof(vertices));
		vb->SetLayout(layout);

		IndexBuffer* ib = IndexBuffer::Create(indices, 6);

		VertexArray* va = VertexArray::Create();
		va->AddVertexBuffer(vb);
		va->SetIndexBuffer(ib);

		CORE_LOG_INFO("Created quad mesh");
		return va;
	}

	VertexArray* MeshFactory::CreateCube(float size)
	{
		float s = size * 0.5f;

		float vertices[] = {
			// Front face
			-s, -s,  s,  0.0f, 0.0f, 1.0f, 0.0f, 0.0f,
			 s, -s,  s,  0.0f, 0.0f, 1.0f, 1.0f, 0.0f,
			 s,  s,  s,  0.0f, 0.0f, 1.0f, 1.0f, 1.0f,
			-s,  s,  s,  0.0f, 0.0f, 1.0f, 0.0f, 1.0f,
			// Back face
			 s, -s, -s,  0.0f, 0.0f, -1.0f, 0.0f, 0.0f,
			-s, -s, -s,  0.0f, 0.0f, -1.0f, 1.0f, 0.0f,
			-s,  s, -s,  0.0f, 0.0f, -1.0f, 1.0f, 1.0f,
			 s,  s, -s,  0.0f, 0.0f, -1.0f, 0.0f, 1.0f,
			// Top face
			-s,  s,  s,  0.0f, 1.0f, 0.0f, 0.0f, 0.0f,
			 s,  s,  s,  0.0f, 1.0f, 0.0f, 1.0f, 0.0f,
			 s,  s, -s,  0.0f, 1.0f, 0.0f, 1.0f, 1.0f,
			-s,  s, -s,  0.0f, 1.0f, 0.0f, 0.0f, 1.0f,
			// Bottom face
			-s, -s, -s,  0.0f, -1.0f, 0.0f, 0.0f, 0.0f,
			 s, -s, -s,  0.0f, -1.0f, 0.0f, 1.0f, 0.0f,
			 s, -s,  s,  0.0f, -1.0f, 0.0f, 1.0f, 1.0f,
			-s, -s,  s,  0.0f, -1.0f, 0.0f, 0.0f, 1.0f,
			// Right face
			 s, -s,  s,  1.0f, 0.0f, 0.0f, 0.0f, 0.0f,
			 s, -s, -s,  1.0f, 0.0f, 0.0f, 1.0f, 0.0f,
			 s,  s, -s,  1.0f, 0.0f, 0.0f, 1.0f, 1.0f,
			 s,  s,  s,  1.0f, 0.0f, 0.0f, 0.0f, 1.0f,
			// Left face
			-s, -s, -s,  -1.0f, 0.0f, 0.0f, 0.0f, 0.0f,
			-s, -s,  s,  -1.0f, 0.0f, 0.0f, 1.0f, 0.0f,
			-s,  s,  s,  -1.0f, 0.0f, 0.0f, 1.0f, 1.0f,
			-s,  s, -s,  -1.0f, 0.0f, 0.0f, 0.0f, 1.0f
		};

		uint32_t indices[] = {
			0, 1, 2, 2, 3, 0,       // Front
			4, 5, 6, 6, 7, 4,       // Back
			8, 9, 10, 10, 11, 8,   // Top
			12, 13, 14, 14, 15, 12, // Bottom
			16, 17, 18, 18, 19, 16, // Right
			20, 21, 22, 22, 23, 20  // Left
		};

		BufferLayout layout = {
			{ "a_Position", ShaderDataType::Float3 },
			{ "a_Normal", ShaderDataType::Float3 },
			{ "a_TexCoord", ShaderDataType::Float2 }
		};

		VertexBuffer* vb = VertexBuffer::Create(vertices, sizeof(vertices));
		vb->SetLayout(layout);

		IndexBuffer* ib = IndexBuffer::Create(indices, 36);

		VertexArray* va = VertexArray::Create();
		va->AddVertexBuffer(vb);
		va->SetIndexBuffer(ib);

		CORE_LOG_INFO("Created cube mesh");
		return va;
	}

	VertexArray* MeshFactory::CreateSphere(float radius, uint32_t segments, uint32_t rings)
	{
		std::vector<float> vertices;
		std::vector<uint32_t> indices;

		for (uint32_t ring = 0; ring <= rings; ring++)
		{
			float theta = ring * 3.14159265f / rings;
			float sinTheta = std::sin(theta);
			float cosTheta = std::cos(theta);

			for (uint32_t seg = 0; seg <= segments; seg++)
			{
				float phi = seg * 2.0f * 3.14159265f / segments;
				float sinPhi = std::sin(phi);
				float cosPhi = std::cos(phi);

				float x = cosPhi * sinTheta;
				float y = cosTheta;
				float z = sinPhi * sinTheta;

				// Position
				vertices.push_back(x * radius);
				vertices.push_back(y * radius);
				vertices.push_back(z * radius);
				// Normal
				vertices.push_back(x);
				vertices.push_back(y);
				vertices.push_back(z);
				// TexCoord
				vertices.push_back((float)seg / segments);
				vertices.push_back((float)ring / rings);
			}
		}

		for (uint32_t ring = 0; ring < rings; ring++)
		{
			for (uint32_t seg = 0; seg < segments; seg++)
			{
				uint32_t current = ring * (segments + 1) + seg;
				uint32_t next = current + segments + 1;

				indices.push_back(current);
				indices.push_back(next);
				indices.push_back(current + 1);

				indices.push_back(current + 1);
				indices.push_back(next);
				indices.push_back(next + 1);
			}
		}

		BufferLayout layout = {
			{ "a_Position", ShaderDataType::Float3 },
			{ "a_Normal", ShaderDataType::Float3 },
			{ "a_TexCoord", ShaderDataType::Float2 }
		};

		VertexBuffer* vb = VertexBuffer::Create(vertices.data(), vertices.size() * sizeof(float));
		vb->SetLayout(layout);

		IndexBuffer* ib = IndexBuffer::Create(indices.data(), indices.size());

		VertexArray* va = VertexArray::Create();
		va->AddVertexBuffer(vb);
		va->SetIndexBuffer(ib);

		CORE_LOG_INFO("Created sphere mesh ({} vertices, {} indices)", vertices.size() / 8, indices.size());
		return va;
	}

	VertexArray* MeshFactory::CreatePlane(float width, float height)
	{
		float w = width * 0.5f;
		float h = height * 0.5f;

		float vertices[] = {
			// Position         // Normal       // TexCoord
			-w, 0.0f, -h,  0.0f, 1.0f, 0.0f, 0.0f, 0.0f,
			 w, 0.0f, -h,  0.0f, 1.0f, 0.0f, 1.0f, 0.0f,
			 w, 0.0f,  h,  0.0f, 1.0f, 0.0f, 1.0f, 1.0f,
			-w, 0.0f,  h,  0.0f, 1.0f, 0.0f, 0.0f, 1.0f
		};

		uint32_t indices[] = { 0, 1, 2, 2, 3, 0 };

		BufferLayout layout = {
			{ "a_Position", ShaderDataType::Float3 },
			{ "a_Normal", ShaderDataType::Float3 },
			{ "a_TexCoord", ShaderDataType::Float2 }
		};

		VertexBuffer* vb = VertexBuffer::Create(vertices, sizeof(vertices));
		vb->SetLayout(layout);

		IndexBuffer* ib = IndexBuffer::Create(indices, 6);

		VertexArray* va = VertexArray::Create();
		va->AddVertexBuffer(vb);
		va->SetIndexBuffer(ib);

		CORE_LOG_INFO("Created plane mesh");
		return va;
	}

	VertexArray* MeshFactory::CreateTorus(float outerRadius, float innerRadius, uint32_t segments, uint32_t rings)
	{
		std::vector<float> vertices;
		std::vector<uint32_t> indices;

		for (uint32_t ring = 0; ring <= rings; ring++)
		{
			float theta = ring * 2.0f * 3.14159265f / rings;
			float cosTheta = std::cos(theta);
			float sinTheta = std::sin(theta);

			for (uint32_t seg = 0; seg <= segments; seg++)
			{
				float phi = seg * 2.0f * 3.14159265f / segments;
				float cosPhi = std::cos(phi);
				float sinPhi = std::sin(phi);

				float x = (outerRadius + innerRadius * cosPhi) * cosTheta;
				float y = innerRadius * sinPhi;
				float z = (outerRadius + innerRadius * cosPhi) * sinTheta;

				// Position
				vertices.push_back(x);
				vertices.push_back(y);
				vertices.push_back(z);
				// Normal
				vertices.push_back(cosPhi * cosTheta);
				vertices.push_back(sinPhi);
				vertices.push_back(cosPhi * sinTheta);
				// TexCoord
				vertices.push_back((float)seg / segments);
				vertices.push_back((float)ring / rings);
			}
		}

		for (uint32_t ring = 0; ring < rings; ring++)
		{
			for (uint32_t seg = 0; seg < segments; seg++)
			{
				uint32_t current = ring * (segments + 1) + seg;
				uint32_t next = current + segments + 1;

				indices.push_back(current);
				indices.push_back(current + 1);
				indices.push_back(next);

				indices.push_back(current + 1);
				indices.push_back(next + 1);
				indices.push_back(next);
			}
		}

		BufferLayout layout = {
			{ "a_Position", ShaderDataType::Float3 },
			{ "a_Normal", ShaderDataType::Float3 },
			{ "a_TexCoord", ShaderDataType::Float2 }
		};

		VertexBuffer* vb = VertexBuffer::Create(vertices.data(), vertices.size() * sizeof(float));
		vb->SetLayout(layout);

		IndexBuffer* ib = IndexBuffer::Create(indices.data(), indices.size());

		VertexArray* va = VertexArray::Create();
		va->AddVertexBuffer(vb);
		va->SetIndexBuffer(ib);

		CORE_LOG_INFO("Created torus mesh");
		return va;
	}
}
