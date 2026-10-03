#pragma once

#include "script/runtime/ScriptRuntime.h"
#include "script/runtime/GameplayTypes.h"
#include "scene/SceneWorld.h"

#include <filesystem>
#include <memory>

namespace NcmaEngine::Scripting
{
    // CPython headers and object ownership are private to the adapter implementation.
    // Main-thread only. The borrowed play world must outlive EndScene/Stop.
    class PythonGameplayRuntime final : public IScriptRuntime
    {
    public:
        PythonGameplayRuntime(std::filesystem::path projectRoot, std::filesystem::path nativeLibrary,
            std::filesystem::path scriptDirectory = {});
        ~PythonGameplayRuntime() override;
        PythonGameplayRuntime(const PythonGameplayRuntime&) = delete;
        PythonGameplayRuntime& operator=(const PythonGameplayRuntime&) = delete;
        const RuntimeDescriptor& GetDescriptor() const noexcept override;
        bool Start(std::string& error) override;
        void Stop() noexcept override;
        void Tick(double deltaSeconds) override;
        bool Reload(std::string& error);
        bool BindScene(SceneWorld& world, std::string& error);
        void EndScene() noexcept;
        const std::vector<BehaviourDescriptor>& GetTypes() const noexcept;
        bool IsStarted() const noexcept;
        int GetBehaviourCount() const noexcept;
        const std::string& GetLastError() const noexcept;
    private:
        struct Implementation;
        std::unique_ptr<Implementation> m_Implementation;
    };
}
