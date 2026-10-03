#pragma once

#include "CoreMinimal.h"
#include <functional>
#include <string>
#include <cstdint>

namespace NcmaEngine
{
	// 事件类型ID
	using EventTypeID = uint32_t;

	// 事件基类
	class Event
	{
	public:
		virtual ~Event() = default;

		virtual EventTypeID GetTypeID() const = 0;
		virtual const char* GetName() const = 0;
		virtual std::string ToString() const { return GetName(); }

		inline bool IsHandled() const { return m_bHandled; }
		inline void SetHandled(bool handled) { m_bHandled = handled; }

	protected:
		bool m_bHandled = false;
	};

	// 事件类型ID计数器
	extern EventTypeID GEventTypeCounter;

	// 事件类型注册宏 - 在类内部使用
	#define DECLARE_EVENT_TYPE(ThisClass) \
		static EventTypeID StaticTypeID() { return s_TypeID; } \
		EventTypeID GetTypeID() const override { return StaticTypeID(); } \
		const char* GetName() const override { return #ThisClass; } \
		static EventTypeID s_TypeID;
}