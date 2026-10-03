#pragma once

#include "CoreMinimal.h"

namespace NcmaEngine
{
	// 引擎模块接口 - 参考UE的IModuleInterface
	class IModuleInterface
	{
	public:
		virtual ~IModuleInterface() = default;

		// 模块启动时调用
		virtual void StartupModule() {}
		// 模块关闭时调用
		virtual void ShutdownModule() {}

		// 获取模块是否已加载
		bool IsModuleLoaded() const { return m_bModuleLoaded; }

		// 模块内部状态
		bool m_bModuleLoaded = false;
		bool m_bIsReady = false;
	};

	// 模块基类模板
	template<typename T>
	class TModuleBase : public IModuleInterface
	{
	public:
		TModuleBase() = default;
		virtual ~TModuleBase() { ShutdownModule(); }

		static T& Get() { return *s_Instance; }
		static bool IsAvailable() { return s_Instance != nullptr && s_Instance->IsModuleLoaded(); }

	protected:
		static T* s_Instance;
	};

	template<typename T>
	T* TModuleBase<T>::s_Instance = nullptr;
}
