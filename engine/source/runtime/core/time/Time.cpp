#include "Time.h"
#include "core/log/Log.h"
#include <chrono>

namespace NcmaEngine
{
	static Time* s_TimeInstance = nullptr;

	Time::Time()
	{
		s_TimeInstance = this;
	}

	void Time::Init()
	{
		m_Ticks = std::chrono::duration_cast<std::chrono::milliseconds>(
			std::chrono::high_resolution_clock::now().time_since_epoch()
		).count();
		m_LastTicks = m_Ticks;
		m_Initialized = true;
		CORE_LOG_INFO("Time system initialized");
	}

	void Time::OnUpdate(float deltaTime)
	{
		if (!m_Initialized)
		{
			Init();
		}

		// Clamp delta time
		m_DeltaTime = deltaTime;
		if (m_DeltaTime < m_MinDeltaTime) m_DeltaTime = m_MinDeltaTime;
		if (m_DeltaTime > m_MaxDeltaTime) m_DeltaTime = m_MaxDeltaTime;

		// Update time (scaled or unscaled depending on pause)
		if (!m_Paused)
		{
			m_Time += m_DeltaTime * m_TimeScale;
			m_FrameCount++;
		}

		// Get current ticks
		m_Ticks = std::chrono::duration_cast<std::chrono::milliseconds>(
			std::chrono::high_resolution_clock::now().time_since_epoch()
		).count();

		// Calculate FPS
		m_FrameTime = (float)(m_Ticks - m_LastTicks);
		m_LastTicks = m_Ticks;

		m_FPSAccumulator += m_DeltaTime;
		m_FrameCountForFPS++;

		m_FPSTimer += m_FrameTime;
		if (m_FPSTimer >= 1000.0f)  // Update FPS every second
		{
			m_FPS = (float)m_FrameCountForFPS * 1000.0f / m_FPSTimer;
			m_FrameCountForFPS = 0;
			m_FPSTimer = 0.0f;
		}
	}

	Time& GetTime()
	{
		return *s_TimeInstance;
	}
}
