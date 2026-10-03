#include "OpenGLRenderAPI.h"
#include "application/windows/WindowsWindow.h"
#include "application/Application.h"
#include "core/log/Log.h"
#include "../OpenGLLoader.h"
#include <GLFW/glfw3.h>

namespace NcmaEngine
{
	OpenGLRenderAPI::OpenGLRenderAPI()
		: RenderAPI(RenderAPIType::OpenGL)
	{
	}

	OpenGLRenderAPI::~OpenGLRenderAPI()
	{
		Shutdown();
	}

	void OpenGLRenderAPI::Init()
	{
		CORE_LOG_INFO("Initializing OpenGL RenderAPI...");

		// Get window handle from application
		Application& app = Application::Get();
		Window& window = app.GetWindow();
		m_WindowHandle = ((WindowsWindow&)window).GetGLFWWindow();

		if (!m_WindowHandle)
		{
			CORE_LOG_ERROR("Failed to get GLFW window handle for OpenGL!");
			return;
		}

		// Make OpenGL context current
		glfwMakeContextCurrent(m_WindowHandle);

		// Initialize our OpenGL function loader
		if (!InitOpenGL((void* (*)(const char*))glfwGetProcAddress))
		{
			CORE_LOG_ERROR("Failed to initialize OpenGL function pointers!");
			return;
		}

		CORE_LOG_INFO("OpenGL RenderAPI initialized");
		CORE_LOG_INFO("  Vendor: {}", (const char*)glGetString(GL_VENDOR));
		CORE_LOG_INFO("  Renderer: {}", (char*)glGetString(GL_RENDERER));
		CORE_LOG_INFO("  Version: {}", (char*)glGetString(GL_VERSION));

		// Enable depth testing by default
		glEnable(GL_DEPTH_TEST);
		glDepthFunc(GL_LESS);

		// Enable blending for transparency
		glEnable(GL_BLEND);
		glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

		// Enable face culling
		glEnable(GL_CULL_FACE);
		glCullFace(GL_BACK);
	}

	void OpenGLRenderAPI::Shutdown()
	{
		CORE_LOG_INFO("OpenGL RenderAPI shutting down...");
		m_WindowHandle = nullptr;
	}

	void OpenGLRenderAPI::Clear()
	{
		glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
	}

	void OpenGLRenderAPI::SetClearColor(float r, float g, float b, float a)
	{
		glClearColor(r, g, b, a);
	}

	void OpenGLRenderAPI::SetViewport(uint32_t x, uint32_t y, uint32_t width, uint32_t height)
	{
		glViewport(x, y, width, height);
	}

	void OpenGLRenderAPI::EnableDepthTest(bool enable)
	{
		if (enable)
			glEnable(GL_DEPTH_TEST);
		else
			glDisable(GL_DEPTH_TEST);
	}

	void OpenGLRenderAPI::EnableBlending(bool enable)
	{
		if (enable)
			glEnable(GL_BLEND);
		else
			glDisable(GL_BLEND);
	}
}
