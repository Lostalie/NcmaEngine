#pragma once

#include "Event.h"

namespace NcmaEngine
{
	class WindowResizeEvent : public Event
	{
	public:
		DECLARE_EVENT_TYPE(WindowResizeEvent)

		WindowResizeEvent(uint32_t width, uint32_t height)
			: m_Width(width), m_Height(height) {}

		inline uint32_t GetWidth() const { return m_Width; }
		inline uint32_t GetHeight() const { return m_Height; }

		std::string ToString() const override
		{
			return "WindowResizeEvent: " + std::to_string(m_Width) + "x" + std::to_string(m_Height);
		}

	private:
		uint32_t m_Width;
		uint32_t m_Height;
	};
}