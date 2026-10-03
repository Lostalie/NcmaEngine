#pragma once

#include "IModuleInterface.h"
#include <unordered_map>
#include <string>
#include <vector>

namespace NcmaEngine
{
	// 模块描述符
	struct FModuleDescriptor
	{
		const char* Name;
		IModuleInterface* (*CreateModule)();
		std::vector<std::string> Dependencies;
	};

	// 模块管理器 - 参考UE的FModuleManager
	class ModuleManager
	{
	public:
		static ModuleManager& Get();

		// 加载模块
		bool LoadModule(const char* ModuleName);
		// 卸载模块
		void UnloadModule(const char* ModuleName);
		// 获取模块
		IModuleInterface* GetModule(const char* ModuleName);
		// 检查模块是否已加载
		bool IsModuleLoaded(const char* ModuleName) const;

		// 获取所有已加载模块
		const std::unordered_map<std::string, IModuleInterface*>& GetLoadedModules() const { return m_LoadedModules; }

	private:
		ModuleManager() = default;
		~ModuleManager();

		ModuleManager(const ModuleManager&) = delete;
		ModuleManager& operator=(const ModuleManager&) = delete;

		std::unordered_map<std::string, IModuleInterface*> m_LoadedModules;
	};

	// 模块注册辅助宏
	#define DECLARE_MODULE(ModuleClass, ModuleName) \
		namespace { \
			NcmaEngine::IModuleInterface* CreateModule##ModuleClass() { \
				return new ModuleClass(); \
			} \
		} \
		static NcmaEngine::FModuleDescriptor ModuleClass##ModuleDescriptor##Instance(#ModuleName, CreateModule##ModuleClass)
}
