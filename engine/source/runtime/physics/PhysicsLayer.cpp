#include "PhysicsLayer.h"
#include "PhysicsModule.h"

namespace NcmaEngine
{
	PhysicsLayer::PhysicsLayer()
		: Layer("PhysicsLayer")
	{
	}

	PhysicsLayer::~PhysicsLayer()
	{
	}

	void PhysicsLayer::OnAttach()
	{
		// 物理模块会在 Engine 启动时自动初始化
	}

	void PhysicsLayer::OnDetach()
	{
	}

	void PhysicsLayer::OnUpdate(float deltaTime)
	{
		// 获取物理世界并步进
		IPhysicsWorld* world = GetPhysicsWorld();
		if (world && !m_bPaused)
		{
			world->Step(deltaTime);
		}
	}

	void PhysicsLayer::OnEvent(Event& event)
	{
		// 目前物理层不处理事件，留给子类扩展
	}

	void PhysicsLayer::SetPaused(bool paused)
	{
		m_bPaused = paused;
		IPhysicsWorld* world = GetPhysicsWorld();
		if (world)
		{
			world->SetPaused(paused);
		}
	}

	IPhysicsWorld* PhysicsLayer::GetPhysicsWorld() const
	{
		PhysicsModule* module = &PhysicsModule::Get();
		if (module->IsModuleLoaded())
		{
			return module->GetWorld();
		}
		return nullptr;
	}

	PhysicsLayer& GetPhysicsLayer()
	{
		static PhysicsLayer instance;
		return instance;
	}
}
