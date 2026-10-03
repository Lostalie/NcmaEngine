#pragma once

#include "CoreMinimal.h"
#include "Layer.h"
#include "Interface/IPhysicsWorld.h"

namespace NcmaEngine
{
	// 物理层 - 负责每帧更新物理模拟
	class PhysicsLayer : public Layer
	{
	public:
		PhysicsLayer();
		virtual ~PhysicsLayer();

		// Layer 接口
		virtual void OnAttach() override;
		virtual void OnDetach() override;
		virtual void OnUpdate(float deltaTime) override;
		virtual void OnEvent(class Event& event) override;

		// 物理控制
		void SetPaused(bool paused);
		bool IsPaused() const { return m_bPaused; }

		// 获取当前物理世界
		IPhysicsWorld* GetPhysicsWorld() const;

	private:
		bool m_bPaused = false;
	};

	// 获取物理层单例
	PhysicsLayer& GetPhysicsLayer();
}
