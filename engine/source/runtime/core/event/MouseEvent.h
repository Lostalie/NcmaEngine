#pragma once

#include "Event.h"

namespace NcmaEngine
{
	class MouseMoveEvent : public Event
	{
	public:
		DECLARE_EVENT_TYPE(MouseMoveEvent)

		MouseMoveEvent(float x, float y)
			: m_PosX(x), m_PosY(y) {}

		inline float GetX() const { return m_PosX; }
		inline float GetY() const { return m_PosY; }

		std::string ToString() const override
		{
			return "MouseMoveEvent: " + std::to_string(m_PosX) + ", " + std::to_string(m_PosY);
		}

	private:
		float m_PosX;
		float m_PosY;
	};

	class MouseButtonEvent : public Event
	{
	public:
		DECLARE_EVENT_TYPE(MouseButtonEvent)

		MouseButtonEvent(int button, bool pressed, float x, float y)
			: m_Button(button), m_Pressed(pressed), m_PosX(x), m_PosY(y) {}

		inline int GetButton() const { return m_Button; }
		inline bool IsPressed() const { return m_Pressed; }
		inline float GetX() const { return m_PosX; }
		inline float GetY() const { return m_PosY; }

		std::string ToString() const override
		{
			return "MouseButtonEvent: " + std::to_string(m_Button) + " (" + (m_Pressed ? "pressed" : "released") + ")";
		}

	protected:
		int m_Button;
		bool m_Pressed;
		float m_PosX;
		float m_PosY;
	};

	class MouseScrollEvent : public Event
	{
	public:
		DECLARE_EVENT_TYPE(MouseScrollEvent)

		MouseScrollEvent(float xOffset, float yOffset)
			: m_XOffset(xOffset), m_YOffset(yOffset) {}

		inline float GetXOffset() const { return m_XOffset; }
		inline float GetYOffset() const { return m_YOffset; }

		std::string ToString() const override
		{
			return "MouseScrollEvent: " + std::to_string(m_XOffset) + ", " + std::to_string(m_YOffset);
		}

	private:
		float m_XOffset;
		float m_YOffset;
	};
}