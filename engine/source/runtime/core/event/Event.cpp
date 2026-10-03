#include "Event.h"
#include "WindowCloseEvent.h"
#include "WindowResizeEvent.h"
#include "KeyEvent.h"
#include "MouseEvent.h"

namespace NcmaEngine
{
	EventTypeID GEventTypeCounter = 0;

	// Define static type IDs for all event classes
	EventTypeID WindowCloseEvent::s_TypeID = ++GEventTypeCounter;
	EventTypeID WindowResizeEvent::s_TypeID = ++GEventTypeCounter;
	EventTypeID KeyEvent::s_TypeID = ++GEventTypeCounter;
	EventTypeID KeyPressedEvent::s_TypeID = ++GEventTypeCounter;
	EventTypeID KeyReleasedEvent::s_TypeID = ++GEventTypeCounter;
	EventTypeID MouseMoveEvent::s_TypeID = ++GEventTypeCounter;
	EventTypeID MouseButtonEvent::s_TypeID = ++GEventTypeCounter;
	EventTypeID MouseScrollEvent::s_TypeID = ++GEventTypeCounter;
}