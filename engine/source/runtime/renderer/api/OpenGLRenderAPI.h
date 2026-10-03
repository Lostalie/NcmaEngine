#pragma once

#include "RenderAPI.h"
#include <GLFW/glfw3.h>

namespace NcmaEngine
{
	class OpenGLRenderAPI : public RenderAPI
	{
	public:
		OpenGLRenderAPI();
		virtual ~OpenGLRenderAPI();

		virtual void Init() override;
		virtual void Shutdown() override;

		virtual void Clear() override;
		virtual void SetClearColor(float r, float g, float b, float a) override;

		virtual void SetViewport(uint32_t x, uint32_t y, uint32_t width, uint32_t height) override;

		virtual void EnableDepthTest(bool enable) override;
		virtual void EnableBlending(bool enable) override;

	private:
		GLFWwindow* m_WindowHandle = nullptr;
	};
}
