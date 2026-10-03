#include "Renderer.h"
#include "api/OpenGLRenderAPI.h"
#include "core/log/Log.h"
#include "OpenGLLoader.h"

namespace NcmaEngine
{
	RenderAPI* Renderer::s_RenderAPI = nullptr;
	RenderAPIType Renderer::s_CurrentAPI = RenderAPIType::OpenGL;

	void Renderer::Init()
	{
		CORE_LOG_INFO("Initializing Renderer...");

		switch (s_CurrentAPI)
		{
		case RenderAPIType::OpenGL:
			s_RenderAPI = new OpenGLRenderAPI();
			break;
		case RenderAPIType::Direct3D12:
			CORE_LOG_ERROR("Direct3D 12 not implemented yet!");
			return;
		case RenderAPIType::Vulkan:
			CORE_LOG_ERROR("Vulkan not implemented yet!");
			return;
		default:
			CORE_LOG_ERROR("Unknown RenderAPI!");
			return;
		}

		s_RenderAPI->Init();
		CORE_LOG_INFO("Renderer initialized with OpenGL");
	}

	void Renderer::Shutdown()
	{
		if (s_RenderAPI)
		{
			s_RenderAPI->Shutdown();
			delete s_RenderAPI;
			s_RenderAPI = nullptr;
		}
	}

	void Renderer::BeginFrame()
	{
		Clear();
	}

	void Renderer::EndFrame()
	{
		// Present/swap buffers would happen here
	}

	void Renderer::Submit(VertexArray* vertexArray)
	{
		vertexArray->Bind();
		glDrawElements(GL_TRIANGLES, vertexArray->GetIndexBuffer()->GetCount(), GL_UNSIGNED_INT, nullptr);
	}

	void Renderer::SetAPI(RenderAPIType apiType)
	{
		s_CurrentAPI = apiType;
	}

	void Renderer::Clear()
	{
		s_RenderAPI->Clear();
	}

	void Renderer::SetClearColor(float r, float g, float b, float a)
	{
		s_RenderAPI->SetClearColor(r, g, b, a);
	}
}
