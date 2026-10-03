#pragma once

#include "script/runtime/ScriptRuntime.h"
#include "scene/SceneWorld.h"
#include "script/runtime/GameplayTypes.h"

#include <filesystem>
#include <memory>

namespace NcmaEngine::Scripting
{
    class DotNetGameplayRuntime final : public IScriptRuntime
    {
    public:
        DotNetGameplayRuntime(
            std::filesystem::path runtimeConfig,
            std::filesystem::path hostAssembly,
            std::filesystem::path gameplayAssembly);
        ~DotNetGameplayRuntime() override;

        DotNetGameplayRuntime(const DotNetGameplayRuntime&) = delete;
        DotNetGameplayRuntime& operator=(const DotNetGameplayRuntime&) = delete;

        [[nodiscard]] const RuntimeDescriptor& GetDescriptor() const noexcept override;
        bool Start(std::string& error) override;
        void Stop() noexcept override;
        void Tick(double deltaSeconds) override;

        [[nodiscard]] bool Reload(std::string& error);
        // The world must outlive EndScene/Stop. Only bind a disposable play-session copy.
        [[nodiscard]] bool BindScene(SceneWorld& world, std::string& error);
        void EndScene() noexcept;
        [[nodiscard]] const std::vector<BehaviourDescriptor>& GetTypes() const noexcept;
        [[nodiscard]] bool IsStarted() const noexcept;
        [[nodiscard]] int GetBehaviourCount() const noexcept;
        [[nodiscard]] std::int64_t GetTickCount() const noexcept;
        [[nodiscard]] const std::string& GetLastError() const noexcept;

    private:
        struct Implementation;
        std::unique_ptr<Implementation> m_Implementation;
    };
}
