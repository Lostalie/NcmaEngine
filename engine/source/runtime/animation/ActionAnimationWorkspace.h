#pragma once

#include "animation/AnimationRuntime.h"

#include <cstdint>

namespace NcmaEngine::Animation
{
    // This action laboratory is a preview session, not a serialized scene component.
    // Editor controls, C# and MCP all execute this same reversible command path.
    enum class AnimationCommand : std::uint32_t
    {
        SetSpeed = 1, TriggerAction = 2, SetPaused = 3, Step = 4, Reset = 5, Undo = 6, Redo = 7
    };

    [[nodiscard]] std::shared_ptr<const AnimationLibrary> CreateActionDemoLibrary();

    class ActionAnimationWorkspace final
    {
    public:
        ActionAnimationWorkspace();
        void Execute(AnimationCommand command, double value = 0, const std::string& text = {});
        void Tick(double seconds); // Automatic preview update; does not add history entries.
        [[nodiscard]] std::string InspectJson() const;
        [[nodiscard]] const AnimationPlayer& Player() const noexcept { return m_State.Player; }
        [[nodiscard]] const Transform& ActorTransform() const noexcept { return m_State.Actor; }
        [[nodiscard]] bool Paused() const noexcept { return m_State.Paused; }
        [[nodiscard]] double Speed() const noexcept { return m_State.Speed; }
        [[nodiscard]] bool CanUndo() const noexcept { return !m_Undo.empty(); }
        [[nodiscard]] bool CanRedo() const noexcept { return !m_Redo.empty(); }

    private:
        struct State final
        {
            explicit State(std::shared_ptr<const AnimationLibrary> library) : Player(std::move(library)) {}
            AnimationPlayer Player;
            Transform Actor, RootDelta;
            double Speed = 0;
            bool Paused = true;
            bool ActionActive = false;
            bool HitWindow = false, ComboWindow = false, Invulnerable = false;
            std::vector<FiredNotify> Events;
        };
        static void Advance(State& state, double seconds);
        static void Apply(State& state, AnimationCommand command, double value, const std::string& text);
        State m_State;
        std::vector<State> m_Undo, m_Redo;
        std::uint64_t m_Revision = 0;
    };
}
