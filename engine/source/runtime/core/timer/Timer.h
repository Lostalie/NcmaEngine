#pragma once

#include "CoreMinimal.h"

namespace NcmaEngine
{
	class Timer
	{
	public:
		Timer();

		void Reset();
		float ElapsedSeconds();
		float ElapsedMilliseconds();

	private:
		std::chrono::high_resolution_clock::time_point m_StartTime;
	};
}