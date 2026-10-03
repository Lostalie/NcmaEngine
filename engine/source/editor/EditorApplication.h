#pragma once

#include "animation/ActionAnimationWorkspace.h"
#include "assets/FbxCharacterImporter.h"
#include "renderer/pipeline/PbrPipelineSettings.h"
#include "renderer/rendergraph/RenderGraph.h"
#include "renderer/rhi/RenderBackendRegistry.h"
#include "scene/SceneCommandStack.h"
#include "scene/SceneSerializer.h"
#include "scene/SceneWorld.h"
#include "script/runtime/DotNetGameplayRuntime.h"

#define GLFW_INCLUDE_NONE
#include <GLFW/glfw3.h>
#include <Windows.h>

#include <cstdint>
#include <array>
#include <filesystem>
#include <functional>
#include <memory>
#include <optional>
#include <string>
#include <vector>

namespace NcmaEngine::Editor
{
    class EditorApplication final
    {
    public:
        explicit EditorApplication(
            HINSTANCE instance, Rhi::BackendType backendType = Rhi::BackendType::Direct3D11,
            bool smokeTest = false, bool gameplaySmokeTest = false, bool fbxSmokeTest = false);
        ~EditorApplication();

        int Run();

    private:
        struct FbxPreviewState final
        {
            std::shared_ptr<const Assets::ImportedCharacter> Character;
            std::optional<Animation::AnimationPlayer> Player;
            bool Paused = true;
        };
        bool Initialize();
        bool CreateEditorWindow();
        bool ApplyWindowIcons();
        bool InitializeRenderer();
        bool EnsurePreviewTargets(std::uint32_t width, std::uint32_t height, std::string& error);
        bool InitializeImGui();
        void Shutdown() noexcept;
        void CreateSampleScene();
        void BeginFrame();
        void RenderEditor();
        void EndFrame();
        void DrawViewportPreview();

        void RenderMenuBar();
        void RenderToolbar(const RECT& workArea);
        void RenderSceneObjects(const RECT& workArea);
        void RenderViewport(const RECT& workArea);
        void RenderInspector(const RECT& workArea);
        void RenderBottomPanel(const RECT& workArea);
        void RenderAnimationLab();
        void RenderFbxCharacter();
        void LoadFbxCharacter(const std::filesystem::path& path);
        void ExecuteFbxEdit(const std::function<void(FbxPreviewState&)>& edit);
        void UndoFbxEdit(bool redo = false);
        void RenderStatusBar(const RECT& workArea);
        void RenderSceneObject(GameObjectId gameObject);

        void SelectObject(GameObjectId gameObject);
        void CreateSceneObject();
        void DeleteSelectedObject();
        void NewScene();
        void OpenScene();
        void SaveScene();
        void UndoSceneEdit();
        void RedoSceneEdit();
        void ReloadGameplay();
        void StopGameplay();
        void TogglePlay();
        void RenderBehaviourInspector();
        void ExecuteSceneMutation(const std::string& label, const std::function<void()>& mutation);
        [[nodiscard]] SceneCommandStack::State CaptureEditorState() const;
        void RestoreSelection(const std::optional<SceneUuid>& selection);
        void BeginInspectorEdit();
        void CommitInspectorEdit(const std::string& label);
        void AppendLog(std::string message);

        HINSTANCE m_Instance = nullptr;
        GLFWwindow* m_Window = nullptr;
        HICON m_LargeWindowIcon = nullptr;
        HICON m_SmallWindowIcon = nullptr;
        std::uint32_t m_ClientWidth = 1440;
        std::uint32_t m_ClientHeight = 900;
        bool m_Running = true;
        bool m_Minimized = false;
        bool m_ImGuiInitialized = false;
        bool m_GlfwInitialized = false;
        bool m_ScenePlaying = false;
        bool m_ShowAssets = true;
        bool m_ShowAnimationLab = false;
        float m_AnimationSpeed = 0;
        std::string m_AnimationCommandError;
        Animation::ActionAnimationWorkspace m_AnimationPreview;
        bool m_ShowFbxCharacter = false;
        bool m_FbxSmokeTest = false;
        float m_FbxYaw = 0.5F;
        FbxPreviewState m_FbxPreview;
        std::vector<FbxPreviewState> m_FbxUndo, m_FbxRedo;
        std::vector<std::vector<Vector3>> m_FbxSkinScratch;
        std::string m_FbxError;
        bool m_SmokeTest = false;
        bool m_GameplaySmokeTest = false;
        std::uint32_t m_RenderedFrames = 0;

        Rhi::BackendType m_BackendType = Rhi::BackendType::Direct3D11;
        Rhi::RenderBackendRegistry m_BackendRegistry;
        std::unique_ptr<Rhi::IRenderBackend> m_Renderer;
        Rhi::BufferHandle m_PreviewVertexBuffer;
        Rhi::BufferHandle m_PreviewIndexBuffer;
        Rhi::BufferHandle m_PreviewTransformBuffer;
        Rhi::BufferHandle m_GroundTransformBuffer;
        Rhi::BufferHandle m_PreviewMaterialBuffer;
        Rhi::GraphicsPipelineHandle m_PreviewPipeline;
        Rhi::DrawDescription m_PreviewDraw;
        Rhi::DrawDescription m_GroundDraw;
        Rhi::TextureHandle m_PreviewShadowMap;
        Rhi::SamplerHandle m_PreviewShadowSampler;
        Rhi::BufferHandle m_ShadowTransformBuffer;
        Rhi::GraphicsPipelineHandle m_ShadowPipeline;
        Rhi::DrawDescription m_ShadowDraw;
        Rhi::DrawDescription m_GroundShadowDraw;
        Rhi::TextureHandle m_PreviewHdrColor;
        Rhi::TextureHandle m_PreviewDepth;
        Rhi::SamplerHandle m_PreviewLinearSampler;
        Rhi::BufferHandle m_ToneMapVertexBuffer;
        Rhi::BufferHandle m_ToneMapConstantBuffer;
        Rhi::GraphicsPipelineHandle m_ToneMapPipeline;
        Rhi::DrawDescription m_ToneMapDraw;
        Rendering::RenderGraph m_PreviewRenderGraph;
        std::uint32_t m_PreviewTargetWidth = 0;
        std::uint32_t m_PreviewTargetHeight = 0;
        Matrix4 m_PreviewCubeModel;
        Matrix4 m_PreviewGroundModel;
        std::array<Matrix4, 4> m_CascadeLightViewProjections;
        std::array<float, 4> m_CascadeSplits{};
        float m_PreviewRotation = 0.0F;
        Rendering::PbrMaterialParameters m_PreviewMaterial;
        Rendering::DirectionalShadowSettings m_PreviewShadowSettings;
        Rendering::ContactShadowSettings m_PreviewContactShadowSettings;
        float m_PreviewLightIntensity = 4.0F;
        float m_PreviewExposure = 1.0F;
        float m_PreviewAmbient = 0.035F;
        SceneWorld m_Scene{"EditorScene"};
        GameObjectId m_SelectedObject = InvalidGameObjectId;
        SceneCommandStack m_SceneHistory;
        std::optional<SceneCommandStack::State> m_InspectorEditBefore;
        std::filesystem::path m_ScenePath;
        std::filesystem::path m_ProjectRoot;
        std::unique_ptr<SceneWorld> m_PlayScene;
        bool m_ScenePaused = false;
        std::unique_ptr<Scripting::DotNetGameplayRuntime> m_GameplayRuntime;
        std::vector<std::string> m_Logs;
    };
}
