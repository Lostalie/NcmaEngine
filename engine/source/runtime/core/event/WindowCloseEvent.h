#pragma once

#include "Event.h"

namespace NcmaEngine
{
	class WindowCloseEvent : public Event
	{
	public:
		DECLARE_EVENT_TYPE(WindowCloseEvent)
	};
}