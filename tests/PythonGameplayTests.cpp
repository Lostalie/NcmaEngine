#include "script/runtime/PythonGameplayRuntime.h"
#include "scene/SceneSerializer.h"

#include <algorithm>
#include <cmath>
#include <iostream>
#include <fstream>
#include <limits>
#include <stdexcept>

namespace
{
    void Check(bool value, const std::string& message) { if (!value) throw std::runtime_error(message); }
    void Set(NcmaEngine::BehaviourBinding& binding, const std::string& name, double value)
    {
        auto property = std::find_if(binding.Properties.begin(), binding.Properties.end(),
            [&](const auto& field) { return field.Name == name; });
        Check(property != binding.Properties.end(), "Missing Python Export: " + name);
        property->Value = value;
    }
}
int main(int argc, char** argv)
{
    try
    {
        Check(argc == 3, "Expected project root and native ABI library");
        using namespace NcmaEngine;
        SceneWorld authored("PythonGameplay");
        const auto first = authored.CreateNode("First"), second = authored.CreateNode("Second");
        const auto disabled = authored.CreateNode("Disabled"), unattached = authored.CreateNode("Unattached");
        SceneWorld play;
        Scripting::PythonGameplayRuntime runtime(argv[1], argv[2]);
        std::string error;
        Check(runtime.Start(error), error);
        Check(runtime.GetDescriptor().ScriptLanguage == Scripting::Language::Python, "Wrong runtime language");
        Check(runtime.GetTypes().size() == 1, "Expected one Python type");
        Check(runtime.GetBehaviourCount() == 0, "Discovery instantiated gameplay");
        const auto& type = runtime.GetTypes().front();
        Check(type.Properties.size() == 3, "Expected double, bool and int exports");
        auto fast = type.CreateBinding(), slow = type.CreateBinding(), off = type.CreateBinding();
        Check(fast.Language == BehaviourLanguage::Python, "Binding language missing");
        Set(fast, "degrees_per_second", 90); Set(fast, "multiplier", 2); Set(fast, "clockwise", 1);
        Set(slow, "degrees_per_second", 30); off.Enabled = false;
        authored.GetBehaviours(first).push_back(fast);
        authored.GetBehaviours(second).push_back(slow);
        authored.GetBehaviours(disabled).push_back(off);
        // A C# binding must not be interpreted as a Python type.
        authored.GetBehaviours(unattached).push_back({SceneUuid::New(), "Missing.CSharp.Type", true, {}});
        const auto firstId = authored.GetPersistentId(first), secondId = authored.GetPersistentId(second);
        SceneSnapshot snapshot;
        Check(SceneSerializer::Deserialize(SceneSerializer::Serialize(authored.CaptureSnapshot()), snapshot, error), error);
        Check(snapshot.Version == 3, "Expected language-tagged scene v3");
        Check(play.RestoreSnapshot(snapshot, error), error);
        Check(play.GetBehaviours(play.FindNode(firstId)).front() == fast, "Python binding scene round-trip failed");
        Check(runtime.BindScene(play, error), error);
        Check(runtime.GetBehaviourCount() == 3, "Per-node binding failed");
        runtime.Tick(0.5);
        Check(runtime.GetLastError().empty(), runtime.GetLastError());
        Check(std::abs(play.GetLocalTransform(play.FindNode(firstId)).Rotation.y() + std::sqrt(0.5F)) < 0.0001F,
            "Python did not update native Transform with scalar Exports");
        Check(std::abs(play.GetLocalTransform(play.FindNode(secondId)).Rotation.y() - std::sin(7.5F * 3.14159265F / 180)) < 0.0001F,
            "Python instances do not have independent Export values");
        Check(play.GetLocalTransform(play.FindNode(authored.GetPersistentId(disabled))).Rotation.isApprox(Quaternion::Identity()),
            "Disabled Python behaviour updated");
        Check(authored.GetLocalTransform(first).Rotation.isApprox(Quaternion::Identity()), "Authored scene was changed by play");
        runtime.EndScene();
        Check(play.Contains(play.FindNode(firstId)), "Borrowed native world was destroyed");
        Check(runtime.Reload(error), error);
        Check(runtime.BindScene(play, error), error);
        const auto rotation = play.GetLocalTransform(play.FindNode(firstId)).Rotation;
        runtime.Tick(0.25);
        Check(!play.GetLocalTransform(play.FindNode(firstId)).Rotation.isApprox(rotation), "Reloaded Python script did not tick");
        runtime.Tick(std::numeric_limits<double>::quiet_NaN());
        Check(!runtime.GetLastError().empty(), "Invalid dt was accepted");
        runtime.Tick(0);
        Check(runtime.GetLastError().empty(), "Host failed to recover after invalid dt");
        runtime.EndScene();
        play.GetBehaviours(play.FindNode(firstId)).front().TypeName = "missing.Script";
        Check(!runtime.BindScene(play, error) && error.find("Missing Python Behaviour") != std::string::npos,
            "Missing Python script was not reported");
        Check(runtime.GetBehaviourCount() == 0, "Failed bind retained instances");
        play.GetBehaviours(play.FindNode(firstId)).front() = fast;
        Check(runtime.BindScene(play, error), "Python host did not recover: " + error);
        runtime.Stop();
        Check(!runtime.IsStarted(), "Python host remained started");
        Check(runtime.Start(error) && runtime.BindScene(play, error), error);
        runtime.Stop();
        // Generated failure cases remain under ignored output, never edit project gameplay.
        const auto fixtures = std::filesystem::path(argv[1]) / "out" / "tests" / "python-host";
        std::filesystem::create_directories(fixtures);
        const auto source = fixtures / "probe.py";
        {
            std::ofstream script(source);
            script << "from ncma_gameplay import Behaviour\nclass Probe(Behaviour):\n"
                "    def on_enable(self): raise SystemExit('activation failed')\n";
            Check(script.good(), "Cannot write generated Python fixture");
        }
        SceneWorld errors("Exceptions");
        Scripting::PythonGameplayRuntime failing(argv[1], argv[2], fixtures);
        Check(failing.Start(error), error);
        const auto target = errors.CreateNode("Target");
        errors.GetBehaviours(target).push_back(failing.GetTypes().front().CreateBinding());
        Check(!failing.BindScene(errors, error) && error.find("SystemExit: activation failed") != std::string::npos,
            "Python SystemExit escaped host or was not reported");
        Check(failing.GetBehaviourCount() == 0, "Failed activation retained instances");
        {
            std::ofstream script(source);
            script << "from ncma_gameplay import Behaviour\nclass Probe(Behaviour):\n"
                "    def on_update(self, dt): raise SystemExit('tick failed')\n"
                "    def on_destroy(self): raise RuntimeError('cleanup failed')\n";
        }
        Check(failing.Reload(error) && failing.BindScene(errors, error), error);
        failing.Tick(0.1);
        Check(failing.GetLastError().find("SystemExit: tick failed") != std::string::npos, "Tick exception was not contained");
        failing.Stop();
        Check(!failing.IsStarted() && failing.GetBehaviourCount() == 0, "Cleanup failure left runtime started");
        Check(errors.Contains(target), "Cleanup destroyed the borrowed native world");
        {
            std::ofstream script(source);
            script << "from ncma_gameplay import Behaviour\nclass Probe(Behaviour):\n    pass\n";
        }
        Check(failing.Start(error) && failing.BindScene(errors, error), "Host failed to recover after cleanup error: " + error);
        failing.Stop();
        std::cout << "Python gameplay: discovery, per-node Exports, native ABI, play isolation, scene v3, reload and recovery passed\n";
        return 0;
    }
    catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
