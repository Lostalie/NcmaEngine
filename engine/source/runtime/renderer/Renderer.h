#pragma once

#include "Core.h"
#include "api/RenderAPI.h"
#include "buffers/VertexArray.h"
#include "shaders/Shader.h"

namespace NcmaEngine
{
	class Renderer
	{
	public:
		// 初始化渲染器
		static void Init();
		// 关闭渲染器
		static void Shutdown();

		// 开始新帧
		static void BeginFrame();
		// 结束帧
		static void EndFrame();

		// 提交渲染命令
		static void Submit(VertexArray* vertexArray);

		// 设置渲染 API
		static void SetAPI(RenderAPIType apiType);
		static RenderAPI* GetAPI() { return s_RenderAPI; }

		// 清除缓冲区
		static void Clear();
		static void SetClearColor(float r, float g, float b, float a);

	private:
		static RenderAPI* s_RenderAPI;
		static RenderAPIType s_CurrentAPI;
	};
}
