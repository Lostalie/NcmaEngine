#include "animation/ActionAnimationWorkspace.h"

#include <algorithm>
#include <cmath>
#include <iomanip>
#include <locale>
#include <sstream>
#include <stdexcept>

namespace NcmaEngine::Animation
{
    namespace
    {
        std::string JsonString(const std::string& value)
        {
            std::ostringstream out;
            out << '"';
            for (const unsigned char ch : value)
            {
                if (ch == '"' || ch == '\\') out << '\\' << static_cast<char>(ch);
                else if (ch < 32) out << "\\u" << std::hex << std::setw(4) << std::setfill('0') << static_cast<int>(ch) << std::dec;
                else out << static_cast<char>(ch);
            }
            out << '"';
            return out.str();
        }

        void WriteVector(std::ostream& out, const Vector3& value)
        {
            out << '[' << value.x() << ',' << value.y() << ',' << value.z() << ']';
        }

        void WriteTransform(std::ostream& out, const Transform& value)
        {
            out << "{\"position\":"; WriteVector(out, value.Position);
            out << ",\"rotation_xyzw\":[" << value.Rotation.x() << ',' << value.Rotation.y() << ','
                << value.Rotation.z() << ',' << value.Rotation.w() << "],\"scale\":";
            WriteVector(out, value.Scale); out << '}';
        }
    }

    ActionAnimationWorkspace::ActionAnimationWorkspace() : m_State(CreateActionDemoLibrary()) {}

    void ActionAnimationWorkspace::Advance(State& state, double seconds)
    {
        if (!std::isfinite(seconds) || seconds < 0 || seconds > 1)
            throw std::invalid_argument("Step seconds must be finite and within 0..1");
        state.Events.clear(); state.RootDelta = {};
        double elapsed = 0;
        // At most an action tail and a locomotion segment; overshoot is preserved.
        while (seconds > 0)
        {
            const double rate = state.ActionActive || state.Speed == 0 ? 1.0 : 0.5 + state.Speed * 0.5;
            const double segment = state.ActionActive ? std::min(seconds,
                std::max(0.0, state.Player.Clip().Duration - state.Player.Time()) / rate) : seconds;
            state.Player.Advance(segment * rate);
            state.Actor = Transform::Combine(state.Actor, state.Player.RootDelta());
            state.RootDelta = Transform::Combine(state.RootDelta, state.Player.RootDelta());
            for (auto event : state.Player.Events())
            {
                event.Offset = elapsed + event.Offset / rate;
                if (event.Name == "Hit.Start") state.HitWindow = true;
                if (event.Name == "Hit.End") state.HitWindow = false;
                if (event.Name == "Combo.Open") state.ComboWindow = true;
                if (event.Name == "Combo.Close") state.ComboWindow = false;
                if (event.Name == "Invulnerability.Start") state.Invulnerable = true;
                if (event.Name == "Invulnerability.End") state.Invulnerable = false;
                state.Events.push_back(std::move(event));
            }
            seconds = std::max(0.0, seconds - segment);
            elapsed += segment;
            if (state.ActionActive && state.Player.Finished())
            {
                state.ActionActive = false;
                state.HitWindow = state.ComboWindow = state.Invulnerable = false;
                state.Player.Play(state.Speed > 0.1 ? "Run" : "Idle", 0.15);
            }
            else break;
        }
    }

    void ActionAnimationWorkspace::Apply(State& state, AnimationCommand command, double value, const std::string& text)
    {
        if (!std::isfinite(value)) throw std::invalid_argument("Command value must be finite");
        switch (command)
        {
        case AnimationCommand::SetSpeed:
            if (value < 0 || value > 1) throw std::invalid_argument("Speed must be within 0..1");
            state.Speed = value;
            if (!state.ActionActive)
            {
                const std::string target = value > 0.1 ? "Run" : "Idle";
                if (state.Player.Clip().Name != target) state.Player.Play(target, 0.15);
            }
            break;
        case AnimationCommand::TriggerAction:
            if (text != "Attack" && text != "Dodge") throw std::invalid_argument("Action must be Attack or Dodge");
            if (state.ActionActive && !(text == "Attack" && state.ComboWindow))
                throw std::invalid_argument("Action locked; Attack can chain only inside Combo.Open/Close");
            state.Player.Play(text, text == "Attack" ? 0.08 : 0.05);
            state.ActionActive = true;
            state.HitWindow = state.ComboWindow = state.Invulnerable = false;
            break;
        case AnimationCommand::SetPaused:
            if (value != 0 && value != 1) throw std::invalid_argument("Paused must be 0 or 1");
            state.Paused = value == 1;
            break;
        case AnimationCommand::Step: Advance(state, value); return;
        case AnimationCommand::Reset: state = State(CreateActionDemoLibrary()); return;
        default: throw std::invalid_argument("Unknown animation command");
        }
        state.Events.clear(); state.RootDelta = {};
    }

    void ActionAnimationWorkspace::Execute(AnimationCommand command, double value, const std::string& text)
    {
        if (command == AnimationCommand::Undo || command == AnimationCommand::Redo)
        {
            auto& source = command == AnimationCommand::Undo ? m_Undo : m_Redo;
            auto& destination = command == AnimationCommand::Undo ? m_Redo : m_Undo;
            if (source.empty()) throw std::invalid_argument("Animation history is empty");
            destination.push_back(m_State);
            m_State = std::move(source.back()); source.pop_back();
        }
        else
        {
            State candidate = m_State;
            Apply(candidate, command, value, text); // Failed validation never changes state/history.
            m_Undo.push_back(m_State);
            if (m_Undo.size() > 128) m_Undo.erase(m_Undo.begin());
            m_State = std::move(candidate); m_Redo.clear();
        }
        ++m_Revision;
    }

    void ActionAnimationWorkspace::Tick(double seconds)
    {
        if (!m_State.Paused) Advance(m_State, seconds);
    }

    std::string ActionAnimationWorkspace::InspectJson() const
    {
        const auto& player = m_State.Player;
        std::ostringstream out;
        out.imbue(std::locale::classic());
        out << std::setprecision(9) << std::boolalpha;
        out << "{\"schema_version\":1,\"session_kind\":\"action_animation_preview\",\"revision\":" << m_Revision
            << ",\"state\":" << JsonString(player.Clip().Name) << ",\"time\":" << player.Time()
            << ",\"duration\":" << player.Clip().Duration << ",\"blend_weight\":" << player.BlendWeight()
            << ",\"paused\":" << m_State.Paused << ",\"speed\":" << m_State.Speed
            << ",\"action_active\":" << m_State.ActionActive << ",\"hit_window\":" << m_State.HitWindow
            << ",\"combo_window\":" << m_State.ComboWindow << ",\"invulnerable\":" << m_State.Invulnerable
            << ",\"can_undo\":" << CanUndo() << ",\"can_redo\":" << CanRedo() << ",\"actor\":";
        WriteTransform(out, m_State.Actor);
        out << ",\"root_delta\":"; WriteTransform(out, m_State.RootDelta);
        out << ",\"events\":[";
        bool first = true;
        for (const auto& event : m_State.Events)
        {
            if (!first) out << ',';
            first = false;
            out << "{\"clip_uuid\":" << JsonString(event.ClipId.ToString()) << ",\"name\":" << JsonString(event.Name)
                << ",\"offset\":" << event.Offset << '}';
        }
        out << "],\"skeleton_uuid\":" << JsonString(player.Library().GetSkeleton().Id.ToString()) << ",\"bones\":[";
        const auto& bones = player.Library().GetSkeleton().Bones;
        for (std::size_t i = 0; i < bones.size(); ++i)
        {
            if (i) out << ',';
            out << "{\"name\":" << JsonString(bones[i].Name) << ",\"parent\":" << bones[i].Parent << ",\"position\":";
            WriteVector(out, player.ModelMatrices()[i].block<3, 1>(0, 3));
            out << ",\"local\":"; WriteTransform(out, player.LocalPose()[i]); out << '}';
        }
        out << "],\"clips\":["; first = true;
        for (const auto& clip : player.Library().GetClips())
        {
            if (!first) out << ',';
            first = false;
            out << "{\"uuid\":" << JsonString(clip.Id.ToString()) << ",\"name\":" << JsonString(clip.Name)
                << ",\"duration\":" << clip.Duration << ",\"loop\":" << clip.Loop
                << ",\"root_motion\":" << clip.ExtractRootMotion << '}';
        }
        out << "]}";
        return out.str();
    }
}
