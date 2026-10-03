#pragma once

#include "Core.h"
#include "entity\Entity.h"
#include "renderer/buffers/VertexArray.h"
#include "material/Material.h"
#include <memory>

namespace NcmaEngine
{
	// Mesh component - holds geometry data for rendering
	class MeshComponent : public Component
	{
	public:
		MeshComponent();
		~MeshComponent();

		// Geometry
		void SetGeometry(VertexArray* geometry);
		VertexArray* GetGeometry() const { return m_Geometry; }

		// Material
		void SetMaterial(Material* material);
		Material* GetMaterial() const { return m_Material; }

		// Index count for drawing
		uint32_t GetIndexCount() const;

	private:
		VertexArray* m_Geometry = nullptr;
		Material* m_Material = nullptr;
	};

	// Static mesh - pre-built geometry
	class StaticMesh
	{
	public:
		StaticMesh();
		~StaticMesh();

		void SetGeometry(VertexArray* geometry);
		VertexArray* GetGeometry() const { return m_Geometry; }

		void SetMaterial(Material* material);
		Material* GetMaterial() const { return m_Material; }

		void Draw() const;

	private:
		VertexArray* m_Geometry = nullptr;
		Material* m_Material = nullptr;
	};

	// Mesh factory for creating basic shapes
	class MeshFactory
	{
	public:
		// Create common shapes
		static VertexArray* CreateQuad(float size = 1.0f);
		static VertexArray* CreateCube(float size = 1.0f);
		static VertexArray* CreateSphere(float radius = 0.5f, uint32_t segments = 32, uint32_t rings = 16);
		static VertexArray* CreatePlane(float width = 1.0f, float height = 1.0f);
		static VertexArray* CreateTorus(float outerRadius = 0.5f, float innerRadius = 0.2f, uint32_t segments = 32, uint32_t rings = 16);
	};
}
