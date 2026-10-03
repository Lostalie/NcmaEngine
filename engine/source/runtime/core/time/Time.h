#pragma once

#include "Core.h"

namespace NcmaEngine
{
	class Time
	{
	public:
		Time();
		~Time() = default;

		// 初始化
		void Init();

		// 每帧更新 (由主循环调用)
		void OnUpdate(float deltaTime);

		// 时间访问
		float GetDeltaTime() const { return m_DeltaTime * m_TimeScale; }
		float GetUnscaledDeltaTime() const { return m_DeltaTime; }
		float GetTime() const { return m_Time * m_TimeScale; }
		float GetUnscaledTime() const { return m_Time; }
		float GetTimeScale() const { return m_TimeScale; }
		float GetFPS() const { return m_FPS; }
		float GetFrameTime() const { return m_FrameTime; }

		// 时间缩放控制
		void SetTimeScale(float scale) { m_TimeScale = scale; }

		// 时间戳
		uint64_t GetFrameCount() const { return m_FrameCount; }
		uint64_t GetTicks() const { return m_Ticks; }

		// 是否暂停
		bool IsPaused() const { return m_Paused; }
		void Pause() { m_Paused = true; }
		void Resume() { m_Paused = false; }
		void TogglePause() { m_Paused = !m_Paused; }

	private:
		float m_DeltaTime = 0.0f;
		float m_Time = 0.0f;
		float m_TimeScale = 1.0f;
		float m_FPS = 0.0f;
		float m_FrameTime = 0.0f;
		float m_FPSAccumulator = 0.0f;
		float m_FPSTimer = 0.0f;
		int m_FrameCountForFPS = 0;

		uint64_t m_FrameCount = 0;
		uint64_t m_Ticks = 0;
		uint64_t m_LastTicks = 0;

		bool m_Paused = false;
		bool m_Initialized = false;

		float m_MinDeltaTime = 0.001f;
		float m_MaxDeltaTime = 0.1f;  // Cap at 100ms to prevent spiral of death
	};

	// 全局时间实例
	Time& GetTime();
}
