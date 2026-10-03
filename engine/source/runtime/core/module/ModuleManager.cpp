#include "ModuleManager.h"
#include "core/log/Log.h"

namespace NcmaEngine
{
	ModuleManager::~ModuleManager()
	{
		// 卸载所有模块
		for (auto& pair : m_LoadedModules)
		{
			CORE_LOG_INFO("Unloading module: {}", pair.first);
			pair.second->ShutdownModule();
			delete pair.second;
		}
		m_LoadedModules.clear();
	}

	ModuleManager& ModuleManager::Get()
	{
		static ModuleManager instance;
		return instance;
	}

	bool ModuleManager::LoadModule(const char* ModuleName)
	{
		if (IsModuleLoaded(ModuleName))
		{
			CORE_LOG_INFO("Module '{}' is already loaded", ModuleName);
			return true;
		}

		CORE_LOG_INFO("Loading module: {}", ModuleName);

		// 查找模块创建函数 - 这里使用简单的方式
		// 实际项目中会使用更复杂的模块发现机制
		IModuleInterface* Module = nullptr;

		if (Module)
		{
			Module->StartupModule();
			Module->m_bModuleLoaded = true;
			Module->m_bIsReady = true;
			m_LoadedModules[ModuleName] = Module;
			CORE_LOG_INFO("Module '{}' loaded successfully", ModuleName);
			return true;
		}

		CORE_LOG_WARN("Module '{}' not found", ModuleName);
		return false;
	}

	void ModuleManager::UnloadModule(const char* ModuleName)
	{
		auto it = m_LoadedModules.find(ModuleName);
		if (it != m_LoadedModules.end())
		{
			CORE_LOG_INFO("Unloading module: {}", ModuleName);
			it->second->ShutdownModule();
			delete it->second;
			m_LoadedModules.erase(it);
		}
	}

	IModuleInterface* ModuleManager::GetModule(const char* ModuleName)
	{
		auto it = m_LoadedModules.find(ModuleName);
		if (it != m_LoadedModules.end())
		{
			return it->second;
		}
		return nullptr;
	}

	bool ModuleManager::IsModuleLoaded(const char* ModuleName) const
	{
		return m_LoadedModules.find(ModuleName) != m_LoadedModules.end();
	}
}
