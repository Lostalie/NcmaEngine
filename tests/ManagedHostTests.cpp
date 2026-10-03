#include "script/runtime/DotNetGameplayRuntime.h"
#include "script/runtime/ManagedHost.h"
#include <array>
#include <future>

#include <algorithm>
#include <cmath>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <string>

void TestManagedSceneBridge();
void TestSceneDocumentFiles();

namespace
{
    void Check(bool condition, const std::string& message)
    {
        if (!condition) throw std::runtime_error(message);
    }
    void Set(NcmaEngine::BehaviourBinding& binding, const std::string& name, double value)
    {
        auto property = std::find_if(binding.Properties.begin(), binding.Properties.end(),
            [&](const auto& item) { return item.Name == name; });
        Check(property != binding.Properties.end(), "Missing Export: " + name);
        property->Value = value;
    }
}

int main(int argumentCount, char** arguments)
{
    if (argumentCount != 4) return 2;
    try
    {
        using namespace NcmaEngine;
        ManagedSceneClient world("GameplayTest");
        const auto fast = world.CreateObject("Fast");
        const auto slow = world.CreateObject("Slow");
        const auto disabled = world.CreateObject("Disabled");
        const auto unattached = world.CreateObject("No script");
        // World is declared first: it must outlive the runtime and its borrowed managed objects.
        Scripting::DotNetGameplayRuntime runtime(
            std::filesystem::path{arguments[1]}, std::filesystem::path{arguments[2]},
            std::filesystem::path{arguments[3]});
        std::string error;
        Check(runtime.Start(error), error);
        Check(runtime.GetTypes().size() == 1, "Expected one discovered type");
        Check(runtime.GetBehaviourCount() == 0, "Loading types must not instantiate gameplay");
        const auto& type = runtime.GetTypes().front();
        Check(type.Properties.size() == 3, "Expected float, bool, int Exports");
        auto fastBinding = type.CreateBinding();
        Set(fastBinding, "DegreesPerSecond", 90);
        Set(fastBinding, "Multiplier", 2);
        Set(fastBinding, "Clockwise", 0);
        auto slowBinding = type.CreateBinding();
        Set(slowBinding, "DegreesPerSecond", 30);
        auto disabledBinding = type.CreateBinding();
        disabledBinding.Enabled = false;
        world.AddBehaviour(fast, fastBinding);
        world.AddBehaviour(slow, slowBinding);
        world.AddBehaviour(disabled, disabledBinding);
        Check(runtime.BindScene(world, error), error);
        Check(runtime.GetBehaviourCount() == 3, "Expected three bound instances");
        using Advance = int(__cdecl*)(std::uint64_t, std::uint64_t, std::uint64_t, double, NcmaPlayStatusV5*, char*, int);
        using Control = int(__cdecl*)(std::uint64_t, std::uint64_t, std::uint64_t, std::uint32_t, NcmaPlayStatusV5*, char*, int);
        using Version = std::uint32_t(__cdecl*)();
        const auto advance = reinterpret_cast<Advance>(Scripting::ManagedHost::Resolve(L"AdvanceFrame"));
        const auto control = reinterpret_cast<Control>(Scripting::ManagedHost::Resolve(L"ControlPlay"));
        const auto version = reinterpret_cast<Version>(Scripting::ManagedHost::Resolve(L"GetBridgeVersion"));
        Check(version() == 5 && runtime.GetPlayStatus().Version == 5, "Gameplay bridge version/layout mismatch");
        const auto token = runtime.GetPlayStatus();
        const auto boundBefore = world.CaptureDocument();
        const auto rejectPlay = [&](std::uint64_t high, std::uint64_t low, double delta) {
            NcmaPlayStatusV5 output{}; std::array<char, 2048> message{};
            Check(advance(world.Handle(), high, low, delta, &output, message.data(), static_cast<int>(message.size())) < 0,
                "Invalid Play request accepted");
            Check(world.CaptureDocument() == boundBefore, "Rejected Play request changed document");
        };
        using End = int(__cdecl*)(std::uint64_t, std::uint64_t, std::uint64_t, char*, int);
        using Unload = int(__cdecl*)(char*, int);
        const auto end = reinterpret_cast<End>(Scripting::ManagedHost::Resolve(L"EndScene"));
        const auto unload = reinterpret_cast<Unload>(Scripting::ManagedHost::Resolve(L"UnloadGameplay"));
        std::array<char, 2048> rejected{};
        Check(end(world.Handle(), 0, 0, rejected.data(), static_cast<int>(rejected.size())) < 0 &&
            world.CaptureDocument() == boundBefore, "Stale Stop changed active Play");
        Check(unload(rejected.data(), static_cast<int>(rejected.size())) < 0 &&
            world.CaptureDocument() == boundBefore, "Unscoped unload stopped active Play");
        NcmaInputFrameV1 input; input.Sequence = 1; input.Focused = 1;
        Check(runtime.SubmitInput(input, error), error);
        input.Sequence = 1; Check(!runtime.SubmitInput(input, error), "Stale input sequence accepted");
        std::vector<NcmaRenderObjectV1> renderObjects; NcmaRenderHeaderV1 renderHeader;
        Check(runtime.ReadRenderFrame(renderObjects, renderHeader, error), error);
        Check(renderHeader.Version == 1 && renderObjects.size() == 4, "Copied render view missing spatial objects");
        using Reload = int(__cdecl*)(const char*, std::uint64_t, std::uint64_t, std::uint64_t, NcmaPlayStatusV5*, char*, int);
        const auto reload = reinterpret_cast<Reload>(Scripting::ManagedHost::Resolve(L"ReloadGameplay"));
        NcmaPlayStatusV5 pausedStatus{};
        Check(reload("out/managed/missing-candidate.dll", world.Handle(), token.SessionHigh, token.SessionLow, &pausedStatus,
            rejected.data(), static_cast<int>(rejected.size())) < 0 && pausedStatus.State == NcmaPlayState::Paused &&
            pausedStatus.SessionHigh == token.SessionHigh && world.CaptureDocument() == boundBefore,
            "Candidate failure did not retain the old paused session");
        Check(runtime.Resume(error), error);
        rejectPlay(0, 0, 1.0 / 60);
        rejectPlay(token.SessionHigh, token.SessionLow, -1);
        auto foreign = std::async(std::launch::async, [&] {
            NcmaPlayStatusV5 output{}; std::array<char, 2048> message{};
            return control(world.Handle(), token.SessionHigh, token.SessionLow, 0, &output,
                message.data(), static_cast<int>(message.size()));
        });
        Check(foreign.get() < 0 && world.CaptureDocument() == boundBefore, "Foreign Play control changed state");
        bool directWriteDenied = false;
        try { world.SetLocalTransform(fast, Transform{}); } catch (const std::exception&) { directWriteDenied = true; }
        Check(directWriteDenied && world.CaptureDocument() == boundBefore, "Native bypass modified active Play");
        bool legacyTickRemoved = false;
        try { (void)Scripting::ManagedHost::Resolve(L"Tick"); } catch (const std::exception&) { legacyTickRemoved = true; }
        Check(legacyTickRemoved, "Legacy unmanaged Tick is still callable");
        for (int i = 0; i < 30; ++i) runtime.AdvanceFrame(1.0 / 60.0);
        Check(runtime.GetLastError().empty(), runtime.GetLastError());
        Check(std::abs(world.GetLocalTransform(fast).Rotation.y() + std::sqrt(0.5F)) < 0.0001F,
            "Float/bool/int Exports were not applied to the attached gameObject");
        Check(std::abs(world.GetLocalTransform(slow).Rotation.y() - std::sin(7.5F * 3.14159265F / 180)) < 0.0001F,
            "Per-gameObject instance values are not independent");
        Check(world.GetLocalTransform(disabled).Rotation.isApprox(Quaternion::Identity()), "Disabled script updated");
        Check(world.GetLocalTransform(unattached).Rotation.isApprox(Quaternion::Identity()), "Unattached gameObject changed");
        Check(runtime.GetTickCount() == 30, "Tick count mismatch");
        runtime.EndScene();
        Check(world.Contains(fast), "Borrowed ManagedSceneClient was destroyed by managed code");

        const auto loaded = world.CaptureView();
        Check(world.RestoreDocument(world.CaptureDocument(), error), error);
        const auto unattachedReloaded = world.FindObject(loaded.Objects.back().PersistentId);
        Check(runtime.Reload(error), error);
        Check(runtime.GetBehaviourCount() == 0, "Reload must wait for explicit scene binding");
        Check(runtime.BindScene(world, error), error);
        const auto fastReloaded = world.FindObject(loaded.Objects.front().PersistentId);
        const auto beforeTick = world.GetLocalTransform(fastReloaded).Rotation;
        runtime.Tick(0.5);
        Check(runtime.GetLastError().empty(), runtime.GetLastError());
        Check(!world.GetLocalTransform(fastReloaded).Rotation.isApprox(beforeTick), "Reloaded script did not update");
        runtime.EndScene();

        auto missing = fastBinding;
        missing.TypeName = "Missing.Type";
        world.UpdateBehaviour(fastReloaded, missing);
        Check(!runtime.BindScene(world, error) && error.find("Missing") != std::string::npos,
            "Missing script must report an actionable error");
        Check(runtime.GetBehaviourCount() == 0, "Failed bind left live instances");
        world.UpdateBehaviour(fastReloaded, fastBinding);
        Check(runtime.BindScene(world, error), "Host must recover after failed bind: " + error);
        runtime.Stop();
        Check(!runtime.IsStarted(), "Host remained started after Stop");
        Check(world.Contains(fastReloaded), "Stop destroyed native scene");
        Check(runtime.Start(error), error);
        Check(runtime.BindScene(world, error), error);
        const auto beforePhase = world.GetLocalTransform(fastReloaded).Rotation;
        runtime.AdvanceFrame(0.1);
        Check(runtime.GetLastError().empty(), runtime.GetLastError());
        Check(runtime.GetPlayStatus().StepsExecuted == 6, "Frame must execute six fixed steps");
        Check(!world.GetLocalTransform(fastReloaded).Rotation.isApprox(beforePhase), "Managed fixed step did not commit");
        Check(runtime.Pause(error), error);
        const auto pausedTick = runtime.GetPlayStatus().Tick;
        const auto pausedRotation = world.GetLocalTransform(fastReloaded).Rotation;
        runtime.AdvanceFrame(2.0);
        Check(runtime.GetPlayStatus().Tick == pausedTick, "Paused frame accumulated simulation");
        Check(runtime.Step(error), error);
        Check(runtime.GetPlayStatus().Tick == pausedTick + 1, "Step must commit exactly one Tick");
        Check(!world.GetLocalTransform(fastReloaded).Rotation.isApprox(pausedRotation), "Step did not simulate");
        Check(runtime.Resume(error), error);
        runtime.AdvanceFrame(0.0);
        Check(runtime.GetPlayStatus().Tick == pausedTick + 1, "Resume paid paused-time debt");
        runtime.AdvanceFrame(1.0);
        Check(runtime.GetPlayStatus().StepsExecuted == 8 && runtime.GetPlayStatus().DroppedSeconds > 0.8,
            "Interactive catch-up must bound and report dropped time");
        Check(world.GetLocalTransform(unattachedReloaded).Rotation.isApprox(Quaternion::Identity()), "Unattached object changed");
        for (int reloadIndex = 0; reloadIndex < 12; ++reloadIndex)
        {
            const auto old = runtime.GetPlayStatus();
            Check(runtime.Reload(error), error);
            Check(runtime.GetPlayStatus().State == NcmaPlayState::Paused && runtime.GetPlayStatus().Tick == old.Tick &&
                (runtime.GetPlayStatus().SessionHigh != old.SessionHigh || runtime.GetPlayStatus().SessionLow != old.SessionLow),
                "Live reload must preserve committed World and rotate its session epoch");
            Check(runtime.Resume(error), error);
        }
        runtime.Stop();
        // An empty logic object can carry a Behaviour without a mandatory Transform.
        const auto logicObject = world.CreateObject("Non-spatial disabled behaviour", false);
        auto logicBinding = disabledBinding; logicBinding.Id = SceneUuid::New();
        world.AddBehaviour(logicObject, logicBinding);
        Check(runtime.Start(error), error);
        Check(runtime.BindScene(world, error), "Non-spatial Behaviour binding failed: " + error);
        Check(!world.HasTransform(logicObject), "Logic binding added a Transform");
        runtime.Stop();
        TestManagedSceneBridge();
        TestSceneDocumentFiles();
        std::cout << "Ncma gameplay: reflection, attachment, Exports, scene round-trip, reload, failure recovery passed\n";
        return 0;
    }
    catch (const std::exception& exception)
    {
        std::cerr << exception.what() << '\n';
        return 1;
    }
}
