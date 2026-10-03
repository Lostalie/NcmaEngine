#include "script/runtime/DotNetGameplayRuntime.h"
#include "script/runtime/PythonGameplayRuntime.h"
#include "scene/SceneSerializer.h"

#include <algorithm>
#include <cmath>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <string>

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
    if (argumentCount != 5) return 2;
    try
    {
        using namespace NcmaEngine;
        SceneWorld world("GameplayTest");
        const auto fast = world.CreateNode("Fast");
        const auto slow = world.CreateNode("Slow");
        const auto disabled = world.CreateNode("Disabled");
        const auto unattached = world.CreateNode("No script");
        // World is declared first: it must outlive the runtime and its borrowed managed nodes.
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
        world.GetBehaviours(fast).push_back(fastBinding);
        world.GetBehaviours(slow).push_back(slowBinding);
        world.GetBehaviours(disabled).push_back(disabledBinding);
        Check(runtime.BindScene(world, error), error);
        Check(runtime.GetBehaviourCount() == 3, "Expected three bound instances");
        runtime.Tick(0.5);
        Check(runtime.GetLastError().empty(), runtime.GetLastError());
        Check(std::abs(world.GetLocalTransform(fast).Rotation.y() + std::sqrt(0.5F)) < 0.0001F,
            "Float/bool/int Exports were not applied to the attached node");
        Check(std::abs(world.GetLocalTransform(slow).Rotation.y() - std::sin(7.5F * 3.14159265F / 180)) < 0.0001F,
            "Per-node instance values are not independent");
        Check(world.GetLocalTransform(disabled).Rotation.isApprox(Quaternion::Identity()), "Disabled script updated");
        Check(world.GetLocalTransform(unattached).Rotation.isApprox(Quaternion::Identity()), "Unattached node changed");
        Check(runtime.GetTickCount() == 1, "Tick count mismatch");
        runtime.EndScene();
        Check(world.Contains(fast), "Borrowed SceneWorld was destroyed by managed code");

        SceneSnapshot loaded;
        Check(SceneSerializer::Deserialize(SceneSerializer::Serialize(world.CaptureSnapshot()), loaded, error), error);
        Check(world.RestoreSnapshot(loaded, error), error);
        Check(runtime.Reload(error), error);
        Check(runtime.GetBehaviourCount() == 0, "Reload must wait for explicit scene binding");
        Check(runtime.BindScene(world, error), error);
        const auto fastReloaded = world.FindNode(loaded.Nodes.front().PersistentId);
        const auto beforeTick = world.GetLocalTransform(fastReloaded).Rotation;
        runtime.Tick(0.5);
        Check(runtime.GetLastError().empty(), runtime.GetLastError());
        Check(!world.GetLocalTransform(fastReloaded).Rotation.isApprox(beforeTick), "Reloaded script did not update");
        runtime.EndScene();

        world.GetBehaviours(fastReloaded).front().TypeName = "Missing.Type";
        Check(!runtime.BindScene(world, error) && error.find("Missing") != std::string::npos,
            "Missing script must report an actionable error");
        Check(runtime.GetBehaviourCount() == 0, "Failed bind left live instances");
        world.GetBehaviours(fastReloaded).front().TypeName = fastBinding.TypeName;
        Check(runtime.BindScene(world, error), "Host must recover after failed bind: " + error);
        runtime.Stop();
        Check(!runtime.IsStarted(), "Host remained started after Stop");
        Check(world.Contains(fastReloaded), "Stop destroyed native scene");
        Check(runtime.Start(error), error);
        Check(runtime.BindScene(world, error), error);
        runtime.Stop();
        Scripting::PythonGameplayRuntime python(arguments[4],
            std::filesystem::path(arguments[4]) / "out" / "managed" / "NcmaNative.dll");
        Check(python.Start(error), error);
        world.GetBehaviours(unattached).push_back(python.GetTypes().front().CreateBinding());
        Check(runtime.Start(error) && runtime.BindScene(world, error) && python.BindScene(world, error), error);
        Check(runtime.GetBehaviourCount() == 3 && python.GetBehaviourCount() == 1,
            "Language filters did not isolate the two hosts");
        const auto csharpBefore = world.GetLocalTransform(fastReloaded).Rotation;
        const auto pythonBefore = world.GetLocalTransform(unattached).Rotation;
        runtime.Tick(0.1); python.Tick(0.1);
        Check(runtime.GetLastError().empty() && python.GetLastError().empty(), "Mixed-language tick failed");
        Check(!world.GetLocalTransform(fastReloaded).Rotation.isApprox(csharpBefore) &&
            !world.GetLocalTransform(unattached).Rotation.isApprox(pythonBefore),
            "Both gameplay languages must update the same native scene independently");
        python.Stop(); runtime.Stop();
        std::cout << "Ncma gameplay: reflection, attachment, Exports, scene round-trip, reload, failure recovery passed\n";
        return 0;
    }
    catch (const std::exception& exception)
    {
        std::cerr << exception.what() << '\n';
        return 1;
    }
}
