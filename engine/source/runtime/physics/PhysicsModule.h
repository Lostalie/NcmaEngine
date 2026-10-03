#pragma once

#include "CoreMinimal.h"
#include "module/IModuleInterface.h"
#include "Interface/IPhysicsWorld.h"
#include <memory>

namespace NcmaEngine
{
	class PhysicsModule : public IModuleInterface
	{
	public:
		PhysicsModule() = default;
		virtual ~PhysicsModule();

		// IModuleInterface
		void StartupModule() override;
		void ShutdownModule() override;

		// 获取当前物理世界
		IPhysicsWorld* GetWorld() const { return m_PhysicsWorld.get(); }

		// 设置物理类型
		void SetPhysicsType(PhysicsType type);
		PhysicsType GetPhysicsType() const { return m_PhysicsType; }

		// 获取模块单例
		static PhysicsModule& Get();

	private:
		std::unique_ptr<IPhysicsWorld> m_PhysicsWorld;
		PhysicsType m_PhysicsType = PhysicsType::None;
	};

	// 获取物理模块单例
	PhysicsModule& GetPhysicsModule();
}
