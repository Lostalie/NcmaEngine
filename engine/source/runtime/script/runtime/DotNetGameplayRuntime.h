#pragma once

#include "script/runtime/ScriptRuntime.h"
#include "scene/ManagedSceneClient.h"
#include "script/runtime/GameplayTypes.h"
#include "interop/NcmaGameplayBridge.h"

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
        void Tick(double deltaSeconds) override; // IScriptRuntime adapter; delegates to the sole managed AdvanceFrame.
        void AdvanceFrame(double deltaSeconds);
        [[nodiscard]] bool SubmitInput(NcmaInputFrameV1 input, std::string& error);
        [[nodiscard]] bool ReadRenderFrame(std::vector<NcmaRenderObjectV1>& output, NcmaRenderHeaderV1& header, std::string& error);
        [[nodiscard]] bool Pause(std::string& error);
        [[nodiscard]] bool Resume(std::string& error);
        [[nodiscard]] bool Step(std::string& error);
        [[nodiscard]] const NcmaPlayStatusV5& GetPlayStatus() const noexcept;

        [[nodiscard]] bool Reload(std::string& error);
        // Bind an opaque C# session handle; no native world pointer crosses the ABI.
        [[nodiscard]] bool BindScene(ManagedSceneClient& world, std::string& error);
        void EndScene() noexcept;
        [[nodiscard]] const std::vector<BehaviourDescriptor>& GetTypes() const noexcept;
        [[nodiscard]] bool IsStarted() const noexcept;
        [[nodiscard]] int GetBehaviourCount() const noexcept;
        [[nodiscard]] std::int64_t GetTickCount() const noexcept;
        [[nodiscard]] const std::string& GetLastError() const noexcept;

    private:
        struct Implementation;
        bool Control(std::uint32_t command, std::string& error);
        std::unique_ptr<Implementation> m_Implementation;
    };
}
