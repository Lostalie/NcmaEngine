#include "EditorApplication.h"
#include "imgui.h"

#include <algorithm>
#include <array>
#include <exception>

namespace NcmaEngine::Editor
{
    void EditorApplication::RenderAnimationLab()
    {
        if (!m_ShowAnimationLab) return;
        ImGui::SetNextWindowSize({850, 600}, ImGuiCond_FirstUseEver);
        if (!ImGui::Begin("Action Animation Lab", &m_ShowAnimationLab))
        {
            ImGui::End();
            return;
        }
        using Animation::AnimationCommand;
        const auto command = [this](AnimationCommand operation, double value = 0, const std::string& text = {}) {
            try
            {
                m_AnimationPreview.Execute(operation, value, text);
                m_AnimationCommandError.clear();
            }
            catch (const std::exception& error) { m_AnimationCommandError = error.what(); }
        };
        ImGui::TextDisabled("Isolated procedural skeleton | same C++ commands as C# / MCP | no scene changes");
        if (ImGui::Button(m_AnimationPreview.Paused() ? "Play Preview" : "Pause Preview"))
            command(AnimationCommand::SetPaused, m_AnimationPreview.Paused() ? 0 : 1);
        ImGui::SameLine();
        if (ImGui::Button("Step 1/60")) command(AnimationCommand::Step, 1.0 / 60);
        ImGui::SameLine();
        if (ImGui::Button("Attack")) command(AnimationCommand::TriggerAction, 0, "Attack");
        ImGui::SameLine();
        if (ImGui::Button("Dodge")) command(AnimationCommand::TriggerAction, 0, "Dodge");
        ImGui::SameLine();
        if (ImGui::Button("Reset")) command(AnimationCommand::Reset);
        ImGui::SameLine();
        ImGui::BeginDisabled(!m_AnimationPreview.CanUndo());
        if (ImGui::Button("Undo")) command(AnimationCommand::Undo);
        ImGui::EndDisabled(); ImGui::SameLine();
        ImGui::BeginDisabled(!m_AnimationPreview.CanRedo());
        if (ImGui::Button("Redo")) command(AnimationCommand::Redo);
        ImGui::EndDisabled();
        ImGui::SetNextItemWidth(250);
        ImGui::SliderFloat("Locomotion speed", &m_AnimationSpeed, 0, 1);
        if (ImGui::IsItemDeactivatedAfterEdit()) command(AnimationCommand::SetSpeed, m_AnimationSpeed);
        if (!ImGui::IsItemActive()) m_AnimationSpeed = static_cast<float>(m_AnimationPreview.Speed());
        if (!m_AnimationCommandError.empty())
            ImGui::TextColored({1, 0.55F, 0.35F, 1}, "%s", m_AnimationCommandError.c_str());

        const auto& player = m_AnimationPreview.Player();
        ImGui::Text("State: %s   Time: %.3f / %.3f   Blend: %.0f%%", player.Clip().Name.c_str(),
            player.Time(), player.Clip().Duration, player.BlendWeight() * 100);
        const auto& actor = m_AnimationPreview.ActorTransform().Position;
        ImGui::Text("Accumulated root motion: (%.3f, %.3f, %.3f) m  | camera follows root", actor.x(), actor.y(), actor.z());
        ImGui::Separator();
        const ImVec2 top = ImGui::GetCursorScreenPos();
        const ImVec2 size{std::max(ImGui::GetContentRegionAvail().x, 1.0F), 280};
        ImGui::InvisibleButton("##SkeletonPreview", size);
        ImDrawList* draw = ImGui::GetWindowDrawList();
        draw->PushClipRect(top, {top.x + size.x, top.y + size.y}, true);
        draw->AddRectFilled(top, {top.x + size.x, top.y + size.y}, IM_COL32(15, 20, 29, 255), 6);
        const float scale = 120;
        const auto project = [&](const Vector3& position) {
            return ImVec2{top.x + size.x * 0.28F + (position.x() + position.z() * 0.45F) * scale,
                top.y + 248 - (position.y() - position.z() * 0.15F) * scale};
        };
        draw->AddLine({top.x + 12, top.y + 248}, {top.x + size.x * 0.52F, top.y + 248}, IM_COL32(65, 77, 95, 255));
        const auto& bones = player.Library().GetSkeleton().Bones;
        for (std::size_t i = 0; i < bones.size(); ++i)
        {
            const ImVec2 endpoint = project(player.ModelMatrices()[i].block<3, 1>(0, 3));
            if (bones[i].Parent >= 0)
                draw->AddLine(project(player.ModelMatrices()[static_cast<std::size_t>(bones[i].Parent)].block<3, 1>(0, 3)),
                    endpoint, IM_COL32(154, 135, 255, 255), 3);
            draw->AddCircleFilled(endpoint, 4, i == 0 ? IM_COL32(255, 140, 70, 255) : IM_COL32(220, 225, 245, 255));
        }
        const std::array<const char*, 4> states{"Idle", "Run", "Attack", "Dodge"};
        for (std::size_t i = 0; i < states.size(); ++i)
        {
            const ImVec2 a{top.x + size.x * 0.58F, top.y + 24 + static_cast<float>(i) * 52};
            const ImVec2 b{top.x + size.x - 20, a.y + 38};
            const bool active = player.Clip().Name == states[i];
            draw->AddRectFilled(a, b, active ? IM_COL32(83, 68, 145, 255) : IM_COL32(34, 41, 55, 255), 5);
            draw->AddText({a.x + 12, a.y + 10}, IM_COL32(225, 231, 246, 255), states[i]);
        }
        draw->AddText({top.x + 14, top.y + 12}, IM_COL32(155, 164, 183, 255), "Bone pose / no skinned mesh");
        draw->PopClipRect();
        ImGui::TextDisabled("State debug view; node graph authoring is a separate milestone.");
        if (ImGui::CollapsingHeader("Structured state / notify inspection"))
        {
            ImGui::BeginChild("AnimationJson", {0, 150}, ImGuiChildFlags_Borders, ImGuiWindowFlags_HorizontalScrollbar);
            ImGui::TextUnformatted(m_AnimationPreview.InspectJson().c_str());
            ImGui::EndChild();
        }
        ImGui::End();
    }
}
