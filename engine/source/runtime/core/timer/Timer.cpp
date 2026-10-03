#include "Timer.h"

namespace NcmaEngine
{
	Timer::Timer()
	{
		Reset();
	}

	void Timer::Reset()
	{
		m_StartTime = std::chrono::high_resolution_clock::now();
	}

	float Timer::ElapsedSeconds()
	{
		return ElapsedMilliseconds() * 0.001f;
	}

	float Timer::ElapsedMilliseconds()
	{
		auto currentTime = std::chrono::high_resolution_clock::now();
		auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(currentTime - m_StartTime);
		return static_cast<float>(duration.count());
	}
}