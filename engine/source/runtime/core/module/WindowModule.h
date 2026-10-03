#pragma once

#include "IModuleInterface.h"
#include "application/WindowProps.h"
#include <functional>
#include <GLFW/glfw3.h>

namespace NcmaEngine
{
	class Window;

	// 窗口模块 - 封装GLFW窗口管理
	class WindowModule : public IModuleInterface
	{
	public:
		WindowModule();
		virtual ~WindowModule();

		// IModuleInterface
		void StartupModule() override;
		void ShutdownModule() override;

		// 创建窗口
		Window* CreateEngineWindow(const WindowProps& props);
		// 销毁窗口
		void DestroyWindow();
		// 获取当前窗口
		Window* GetWindow() const { return m_Window; }

		// 窗口事件回调
		void SetEventCallback(const std::function<void(class Event&)>& callback);

		// 设置窗口图标
		void SetWindowIcon(const char* iconPath);

	private:
		static bool s_bGLFWInitialized;

		Window* m_Window = nullptr;
		std::function<void(class Event&)> m_EventCallback;
	};

	// 获取窗口模块单例
	WindowModule& GetWindowModule();
}
