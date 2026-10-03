#include "WindowModule.h"
#include "../../application/GenericWindow.h"
#include "../log/Log.h"
#include "../event/WindowCloseEvent.h"
#include "../event/WindowResizeEvent.h"
#include "../event/KeyEvent.h"
#include "../event/MouseEvent.h"

namespace NcmaEngine
{
	static WindowModule* s_Instance = nullptr;

	bool WindowModule::s_bGLFWInitialized = false;

	WindowModule::WindowModule()
	{
		s_Instance = this;
	}

	WindowModule::~WindowModule()
	{
		ShutdownModule();
		s_Instance = nullptr;
	}

	void WindowModule::StartupModule()
	{
		CORE_LOG_INFO("WindowModule starting...");

		if (!s_bGLFWInitialized)
		{
			if (!glfwInit())
			{
				CORE_LOG_ERROR("Failed to initialize GLFW!");
				return;
			}
			s_bGLFWInitialized = true;
			CORE_LOG_INFO("GLFW initialized");
		}

		m_bModuleLoaded = true;
	}

	void WindowModule::ShutdownModule()
	{
		CORE_LOG_INFO("WindowModule shutting down...");

		if (m_Window)
		{
			delete m_Window;
			m_Window = nullptr;
		}

		if (s_bGLFWInitialized)
		{
			glfwTerminate();
			s_bGLFWInitialized = false;
			CORE_LOG_INFO("GLFW terminated");
		}

		m_bModuleLoaded = false;
	}

	Window* WindowModule::CreateEngineWindow(const WindowProps& props)
	{
		if (m_Window)
		{
			CORE_LOG_WARN("Window already exists, destroying old window");
			delete m_Window;
		}

		m_Window = Window::Create(props);
		if (m_Window && m_EventCallback)
		{
			m_Window->SetEventCallback(m_EventCallback);
		}

		return m_Window;
	}

	void WindowModule::DestroyWindow()
	{
		if (m_Window)
		{
			delete m_Window;
			m_Window = nullptr;
		}
	}

	void WindowModule::SetEventCallback(const std::function<void(class Event&)>& callback)
	{
		m_EventCallback = callback;
		if (m_Window)
		{
			m_Window->SetEventCallback(callback);
		}
	}

	void WindowModule::SetWindowIcon(const char* iconPath)
	{
		if (m_Window)
		{
			m_Window->SetIcon(iconPath);
		}
	}

	WindowModule& GetWindowModule()
	{
		return *s_Instance;
	}
}