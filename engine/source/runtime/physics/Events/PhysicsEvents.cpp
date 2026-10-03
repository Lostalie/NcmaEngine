#include "CollisionEvent.h"
#include "TriggerEvent.h"

namespace NcmaEngine
{
	// Define static type IDs for physics event classes
	EventTypeID CollisionEvent::s_TypeID = ++GEventTypeCounter;
	EventTypeID TriggerEvent::s_TypeID = ++GEventTypeCounter;
}
