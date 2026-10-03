#pragma once

#include "Event.h"

namespace NcmaEngine
{
	class KeyEvent : public Event
	{
	public:
		DECLARE_EVENT_TYPE(KeyEvent)

		KeyEvent(int keyCode, int scanCode, bool pressed, bool repeated = false)
			: m_KeyCode(keyCode), m_ScanCode(scanCode), m_Pressed(pressed), m_Repeated(repeated) {}

		inline int GetKeyCode() const { return m_KeyCode; }
		inline int GetScanCode() const { return m_ScanCode; }
		inline bool IsPressed() const { return m_Pressed; }
		inline bool IsRepeated() const { return m_Repeated; }

		std::string ToString() const override
		{
			return "KeyEvent: " + std::to_string(m_KeyCode) + " (" + (m_Pressed ? "pressed" : "released") + ")";
		}

	protected:
		int m_KeyCode;
		int m_ScanCode;
		bool m_Pressed;
		bool m_Repeated;
	};

	class KeyPressedEvent : public KeyEvent
	{
	public:
		DECLARE_EVENT_TYPE(KeyPressedEvent)

		KeyPressedEvent(int keyCode, int scanCode, bool repeated = false)
			: KeyEvent(keyCode, scanCode, true, repeated) {}
	};

	class KeyReleasedEvent : public KeyEvent
	{
	public:
		DECLARE_EVENT_TYPE(KeyReleasedEvent)

		KeyReleasedEvent(int keyCode, int scanCode)
			: KeyEvent(keyCode, scanCode, false) {}
	};
}