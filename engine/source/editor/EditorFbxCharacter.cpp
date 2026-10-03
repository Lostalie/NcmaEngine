#include "EditorApplication.h"
#include "imgui.h"

#include <commdlg.h>
#include <algorithm>
#include <cmath>

namespace NcmaEngine::Editor
{
    void EditorApplication::ExecuteFbxEdit(const std::function<void(FbxPreviewState&)>& edit)
    {
        FbxPreviewState candidate = m_FbxPreview;
        edit(candidate);
        m_FbxUndo.push_back(m_FbxPreview);
        if (m_FbxUndo.size() > 32) m_FbxUndo.erase(m_FbxUndo.begin());
        m_FbxPreview = std::move(candidate); m_FbxRedo.clear(); m_FbxError.clear();
    }

    void EditorApplication::UndoFbxEdit(bool redo)
    {
        auto& source = redo ? m_FbxRedo : m_FbxUndo;
        auto& target = redo ? m_FbxUndo : m_FbxRedo;
        if (source.empty()) return;
        target.push_back(m_FbxPreview);
        m_FbxPreview = std::move(source.back()); source.pop_back(); m_FbxError.clear();
    }

    void EditorApplication::LoadFbxCharacter(const std::filesystem::path& path)
    {
        try
        {
            Assets::FbxImportOptions options;
            if (m_FbxPreview.Character && std::filesystem::equivalent(path, m_FbxPreview.Character->Source))
                options.AssetId = m_FbxPreview.Character->Id;
            auto character = Assets::FbxCharacterImporter::Import(path, options);
            ExecuteFbxEdit([character](FbxPreviewState& state) {
                state.Character = character;
                state.Player.emplace(character->Animations);
                if (character->Animations->GetClips().size() > 1)
                    state.Player->Play(character->Animations->GetClips()[1].Name, 0);
                state.Paused = true;
            });
            m_ShowFbxCharacter = true;
            AppendLog("Imported FBX character: " + character->InspectJson());
        }
        catch (const std::exception& error) { m_FbxError = error.what(); AppendLog(m_FbxError); }
    }

    void EditorApplication::RenderFbxCharacter()
    {
        if (!m_ShowFbxCharacter) return;
        ImGui::SetNextWindowSize({900, 650}, ImGuiCond_FirstUseEver);
        if (!ImGui::Begin("FBX Character Import", &m_ShowFbxCharacter)) { ImGui::End(); return; }
        if (ImGui::Button("Open FBX..."))
        {
            std::array<wchar_t, 32768> path{};
            OPENFILENAMEW dialog{};
            dialog.lStructSize = sizeof(dialog);
            dialog.hwndOwner = GetActiveWindow();
            dialog.lpstrFilter = L"FBX characters (*.fbx)\0*.fbx\0\0";
            dialog.lpstrFile = path.data(); dialog.nMaxFile = static_cast<DWORD>(path.size());
            dialog.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
            if (GetOpenFileNameW(&dialog)) LoadFbxCharacter(std::filesystem::path(path.data()));
            else if (CommDlgExtendedError()) m_FbxError = "Unable to open FBX file picker";
        }
        ImGui::SameLine();
        if (ImGui::Button("Load Test Character"))
            LoadFbxCharacter(m_ProjectRoot / "tests" / "assets" / "fbx" / "blender_279_sausage_7400_binary.fbx");
        ImGui::SameLine(); ImGui::BeginDisabled(m_FbxUndo.empty());
        if (ImGui::Button("Undo")) UndoFbxEdit();
        ImGui::EndDisabled(); ImGui::SameLine(); ImGui::BeginDisabled(m_FbxRedo.empty());
        if (ImGui::Button("Redo")) UndoFbxEdit(true);
        ImGui::EndDisabled();
        if (!m_FbxError.empty()) ImGui::TextWrapped("%s", m_FbxError.c_str());
        if (!m_FbxPreview.Character) { ImGui::TextDisabled("Choose a rigged FBX character with linear skinning."); ImGui::End(); return; }
        const auto character = m_FbxPreview.Character;
        const auto& clips = character->Animations->GetClips();
        if (ImGui::Button(m_FbxPreview.Paused ? "Play" : "Pause"))
            ExecuteFbxEdit([](FbxPreviewState& state) { state.Paused = !state.Paused; });
        ImGui::SameLine();
        if (ImGui::Button("Step 1/60")) ExecuteFbxEdit([](FbxPreviewState& state) { state.Player->Advance(1.0 / 60); });
        ImGui::SameLine(); ImGui::SetNextItemWidth(280);
        if (ImGui::BeginCombo("Animation", m_FbxPreview.Player->Clip().Name.c_str()))
        {
            for (const auto& clip : clips)
                if (ImGui::Selectable(clip.Name.c_str(), m_FbxPreview.Player->Clip().Name == clip.Name))
                    ExecuteFbxEdit([&clip](FbxPreviewState& state) { state.Player->Play(clip.Name, 0); });
            ImGui::EndCombo();
        }
        std::size_t triangles = 0;
        for (const auto& mesh : character->Meshes) triangles += mesh.Indices.size() / 3;
        ImGui::Text("FBX %u %s | %zu bones/helpers | %zu meshes | %zu triangles | %.0f Hz",
            character->FbxVersion, character->Binary ? "binary" : "ASCII", character->Animations->GetSkeleton().Bones.size(),
            character->Meshes.size(), triangles, character->SampleRate);
        ImGui::Text("Time %.3f / %.3f s | CPU skinning wireframe", m_FbxPreview.Player->Time(), m_FbxPreview.Player->Clip().Duration);
        ImGui::SetNextItemWidth(250); ImGui::SliderFloat("Orbit", &m_FbxYaw, -3.14159F, 3.14159F);
        const ImVec2 origin = ImGui::GetCursorScreenPos();
        const ImVec2 size{std::max(1.0F, ImGui::GetContentRegionAvail().x), 340};
        ImGui::InvisibleButton("##FbxWireframe", size);
        auto* draw = ImGui::GetWindowDrawList();
        draw->PushClipRect(origin, {origin.x + size.x, origin.y + size.y}, true);
        draw->AddRectFilled(origin, {origin.x + size.x, origin.y + size.y}, IM_COL32(16, 21, 30, 255), 6);
        const Vector3 center = (character->BoundsMin + character->BoundsMax) * 0.5F;
        const float radius = std::max((character->BoundsMax - character->BoundsMin).norm() * 0.5F, 0.01F);
        const float scale = std::min(size.x, size.y) * 0.44F / radius;
        const auto project = [&](const Vector3& position) {
            const Vector3 p = position - center;
            return ImVec2{origin.x + size.x * 0.5F + (std::cos(m_FbxYaw) * p.x() + std::sin(m_FbxYaw) * p.z()) * scale,
                origin.y + size.y * 0.5F - p.y() * scale};
        };
        m_FbxSkinScratch.resize(character->Meshes.size());
        std::size_t remaining = 10000;
        for (std::size_t i = 0; i < character->Meshes.size(); ++i)
        {
            const auto& mesh = character->Meshes[i];
            auto& positions = m_FbxSkinScratch[i]; positions.resize(mesh.Vertices.size());
            Assets::SkinPositions(mesh, m_FbxPreview.Player->ModelMatrices(), positions);
            const auto count = std::min(remaining, mesh.Indices.size() / 3);
            for (std::size_t triangle = 0; triangle < count; ++triangle)
            {
                const auto offset = triangle * 3;
                for (std::size_t edge = 0; edge < 3; ++edge)
                    draw->AddLine(project(positions[mesh.Indices[offset + edge]]),
                        project(positions[mesh.Indices[offset + (edge + 1) % 3]]), IM_COL32(91, 135, 187, 160));
            }
            remaining -= count;
        }
        const auto& bones = character->Animations->GetSkeleton().Bones;
        const auto& matrices = m_FbxPreview.Player->ModelMatrices();
        for (std::size_t i = 0; i < bones.size(); ++i)
        {
            const auto point = project(matrices[i].block<3, 1>(0, 3));
            if (bones[i].Parent >= 0) draw->AddLine(project(matrices[static_cast<std::size_t>(bones[i].Parent)].block<3, 1>(0, 3)),
                point, IM_COL32(244, 168, 81, 255), 2);
            draw->AddCircleFilled(point, 3, IM_COL32(255, 214, 122, 255));
        }
        draw->PopClipRect();
        if (triangles > 10000) ImGui::TextDisabled("Wireframe displays the first 10,000 triangles; all meshes were imported.");
        for (const auto& warning : character->Warnings) ImGui::TextWrapped("Import note: %s", warning.c_str());
        if (ImGui::CollapsingHeader("Import report")) ImGui::TextWrapped("%s", character->InspectJson().c_str());
        ImGui::End();
    }
}
