#include "PhysicsModule.h"
#include "Box2D/Box2DWorld.h"
#include "Jolt/JoltPhysicsWorld.h"

namespace NcmaEngine
{
	PhysicsModule::~PhysicsModule()
	{
		if (m_bModuleLoaded)
		{
			ShutdownModule();
		}
	}

	void PhysicsModule::StartupModule()
	{
		if (m_bModuleLoaded)
			return;

		// 如果未指定物理类型，默认使用 Box2D
		if (m_PhysicsType == PhysicsType::None)
		{
			m_PhysicsType = PhysicsType::Box2D;
		}

		// 根据类型创建物理世界
		switch (m_PhysicsType)
		{
		case PhysicsType::Box2D:
			m_PhysicsWorld = std::make_unique<Box2DWorld>();
			break;
		case PhysicsType::Jolt:
			m_PhysicsWorld = std::make_unique<JoltPhysicsWorld>();
			break;
		default:
			break;
		}

		if (m_PhysicsWorld)
		{
			m_bModuleLoaded = true;
			m_bIsReady = true;
		}
	}

	void PhysicsModule::ShutdownModule()
	{
		if (!m_bModuleLoaded)
			return;

		m_PhysicsWorld.reset();
		m_bModuleLoaded = false;
		m_bIsReady = false;
	}

	void PhysicsModule::SetPhysicsType(PhysicsType type)
	{
		if (m_bModuleLoaded)
		{
			// 模块已加载，需要先关闭再切换
			ShutdownModule();
		}
		m_PhysicsType = type;
	}

	PhysicsModule& PhysicsModule::Get()
	{
		static PhysicsModule instance;
		return instance;
	}

	PhysicsModule& GetPhysicsModule()
	{
		return PhysicsModule::Get();
	}
}
