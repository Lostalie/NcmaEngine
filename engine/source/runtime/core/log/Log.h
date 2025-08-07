#pragma once


#include <memory>
#include "../Core.h"
#include "spdlog/spdlog.h"

namespace NcmaEngine
{
	class Log
	{
	public:
		static void Init();

		inline static std::shared_ptr<spdlog::logger>& GetCoreLogger() { return s_CoreLogger; }
		inline static std::shared_ptr<spdlog::logger>& GetEngineLogger() { return s_EngineLogger; }

	private:

		static std::shared_ptr<spdlog::logger> s_CoreLogger;
		static std::shared_ptr<spdlog::logger> s_EngineLogger;

	};
}


