#pragma once

#include "Event.h"

namespace NcmaEngine
{
	// 事件分发器
	class EventDispatcher
	{
	public:
		template<typename T>
		using EventFn = std::function<bool(T&)>;

		explicit EventDispatcher(Event& event)
			: m_Event(event) {}

		template<typename T>
		bool Dispatch(EventFn<T> func)
		{
			if (m_Event.GetTypeID() == T::StaticTypeID())
			{
				m_Event.SetHandled(func(static_cast<T&>(m_Event)));
				return true;
			}
			return false;
		}

	private:
		Event& m_Event;
	};
}