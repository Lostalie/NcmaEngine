#pragma once

#include "Core.h"

namespace NcmaEngine
{
	// 渲染API类型
	enum class RenderAPIType
	{
		None = 0,
		OpenGL,
		Direct3D12,
		Vulkan
	};

	// 渲染API抽象接口
	class RenderAPI
	{
	public:
		virtual ~RenderAPI() = default;

		// 初始化渲染API
		virtual void Init() = 0;
		// 关闭渲染API
		virtual void Shutdown() = 0;

		// 清理当前帧
		virtual void Clear() = 0;
		// 设置清除颜色
		virtual void SetClearColor(float r, float g, float b, float a) = 0;

		// 视口设置
		virtual void SetViewport(uint32_t x, uint32_t y, uint32_t width, uint32_t height) = 0;

		// 启用/禁用深度测试
		virtual void EnableDepthTest(bool enable) = 0;
		// 启用/禁用混合
		virtual void EnableBlending(bool enable) = 0;

		// 获取当前API类型
		inline RenderAPIType GetAPIType() const { return m_APIType; }

	protected:
		explicit RenderAPI(RenderAPIType apiType) : m_APIType(apiType) {}

		RenderAPIType m_APIType;
	};
}
