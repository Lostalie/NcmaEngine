#pragma once


#include <memory>
#include <string_view>
#include "spdlog/spdlog.h"

namespace NcmaEngine
{
	class Log
	{
	public:
		static void Init();
		static void Shutdown();
		static bool IsInitialized() noexcept;

		inline static std::shared_ptr<spdlog::logger>& GetCoreLogger() { return s_CoreLogger; }
		inline static std::shared_ptr<spdlog::logger>& GetEngineLogger() { return s_EngineLogger; }

	private:

		static std::shared_ptr<spdlog::logger> s_CoreLogger;
		static std::shared_ptr<spdlog::logger> s_EngineLogger;

	};
}

// Log macros
#define CORE_LOG_TRACE(...)   ::NcmaEngine::Log::GetCoreLogger()->trace(__VA_ARGS__)
#define CORE_LOG_DEBUG(...)   ::NcmaEngine::Log::GetCoreLogger()->debug(__VA_ARGS__)
#define CORE_LOG_INFO(...)    ::NcmaEngine::Log::GetCoreLogger()->info(__VA_ARGS__)
#define CORE_LOG_WARN(...)    ::NcmaEngine::Log::GetCoreLogger()->warn(__VA_ARGS__)
#define CORE_LOG_ERROR(...)   ::NcmaEngine::Log::GetCoreLogger()->error(__VA_ARGS__)
#define CORE_LOG_CRITICAL(...) ::NcmaEngine::Log::GetCoreLogger()->critical(__VA_ARGS__)

#define ENGINE_LOG_TRACE(...)   ::NcmaEngine::Log::GetEngineLogger()->trace(__VA_ARGS__)
#define ENGINE_LOG_DEBUG(...)   ::NcmaEngine::Log::GetEngineLogger()->debug(__VA_ARGS__)
#define ENGINE_LOG_INFO(...)    ::NcmaEngine::Log::GetEngineLogger()->info(__VA_ARGS__)
#define ENGINE_LOG_WARN(...)    ::NcmaEngine::Log::GetEngineLogger()->warn(__VA_ARGS__)
#define ENGINE_LOG_ERROR(...)   ::NcmaEngine::Log::GetEngineLogger()->error(__VA_ARGS__)
#define ENGINE_LOG_CRITICAL(...) ::NcmaEngine::Log::GetEngineLogger()->critical(__VA_ARGS__)


