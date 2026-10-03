#include "Log.h"

#include "spdlog/sinks/stdout_color_sinks.h"

namespace NcmaEngine
{
	std::shared_ptr<spdlog::logger> Log::s_CoreLogger;
	std::shared_ptr<spdlog::logger> Log::s_EngineLogger;
	
	void Log::Init()
	{
		if (s_CoreLogger && s_EngineLogger)
			return;
		spdlog::set_pattern("%^[%T] %n %v%$");

		s_CoreLogger = spdlog::get("Core");
		if (!s_CoreLogger)
			s_CoreLogger = spdlog::stdout_color_mt("Core");
		s_CoreLogger->set_level(spdlog::level::trace);

		s_EngineLogger = spdlog::get("Engine");
		if (!s_EngineLogger)
			s_EngineLogger = spdlog::stdout_color_mt("Engine");
		s_EngineLogger->set_level(spdlog::level::trace);
	}

	void Log::Shutdown()
	{
		// Release application-owned loggers before clearing spdlog's registry.
		// Some console sinks serialize destruction through the registry mutex.
		s_CoreLogger.reset();
		s_EngineLogger.reset();
		spdlog::shutdown();
	}

	bool Log::IsInitialized() noexcept
	{
		return s_CoreLogger != nullptr && s_EngineLogger != nullptr;
	}
}
