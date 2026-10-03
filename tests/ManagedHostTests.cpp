#include "script/runtime/DotNetGameplayRuntime.h"

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
        runtime.Tick(0.5);
        Check(runtime.GetLastError().empty(), runtime.GetLastError());
        Check(std::abs(world.GetLocalTransform(fast).Rotation.y() + std::sqrt(0.5F)) < 0.0001F,
            "Float/bool/int Exports were not applied to the attached gameObject");
        Check(std::abs(world.GetLocalTransform(slow).Rotation.y() - std::sin(7.5F * 3.14159265F / 180)) < 0.0001F,
            "Per-gameObject instance values are not independent");
        Check(world.GetLocalTransform(disabled).Rotation.isApprox(Quaternion::Identity()), "Disabled script updated");
        Check(world.GetLocalTransform(unattached).Rotation.isApprox(Quaternion::Identity()), "Unattached gameObject changed");
        Check(runtime.GetTickCount() == 1, "Tick count mismatch");
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
        world.BeginGameplayPhase();
        runtime.Tick(0.1);
        Check(runtime.GetLastError().empty(), runtime.GetLastError());
        Check(world.GetLocalTransform(fastReloaded).Rotation.isApprox(beforePhase), "Update became visible before commit");
        world.CommitGameplayPhase();
        Check(!world.GetLocalTransform(fastReloaded).Rotation.isApprox(beforePhase), "C# phase did not commit");
        Check(world.GetLocalTransform(unattachedReloaded).Rotation.isApprox(Quaternion::Identity()), "Unattached object changed");
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
