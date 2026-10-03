#include "EditorApplication.h"
#include "EditorResources.h"

#include "core/log/Log.h"
#include "foundation/MathTypes.h"
#include "renderer/rhi/d3d11/D3D11RenderBackend.h"
#include "renderer/rhi/vulkan/VulkanRuntimeProbe.h"

#include "imgui.h"
#include "imgui_impl_dx11.h"
#include "imgui_impl_glfw.h"

#define GLFW_EXPOSE_NATIVE_WIN32
#include <GLFW/glfw3native.h>

#include <algorithm>
#include <array>
#include <cstdio>
#include <filesystem>
#include <format>
#include <string_view>

namespace NcmaEngine::Editor
{
    namespace
    {
        struct PreviewVertex final
        {
            float Position[3];
            float Normal[3];
        };

        struct PreviewTransformConstants final
        {
            Matrix4 Model = Matrix4::Identity();
            Matrix4 ModelViewProjection = Matrix4::Identity();
            Matrix4 View = Matrix4::Identity();
        };

        struct PreviewPbrConstants final
        {
            std::array<float, 4> BaseColor{};
            std::array<float, 4> Material{};
            std::array<float, 4> LightDirection{};
            std::array<float, 4> LightColor{};
            std::array<float, 4> CameraPosition{};
            std::array<Matrix4, 4> LightViewProjections{
                Matrix4::Identity(), Matrix4::Identity(), Matrix4::Identity(), Matrix4::Identity()};
            std::array<float, 4> CascadeSplits{};
            std::array<float, 4> ShadowParameters{};
            std::array<float, 4> CascadeParameters{};
        };

        struct ShadowTransformConstants final
        {
            Matrix4 LightModelViewProjection = Matrix4::Identity();
        };

        struct ToneMapVertex final
        {
            float Position[2];
            float TextureCoordinate[2];
        };

        struct ToneMapConstants final
        {
            std::array<float, 4> ToneParameters{};
            std::array<float, 4> LightDirection{};
            std::array<float, 4> ProjectionParameters{};
        };

        static_assert(sizeof(PreviewTransformConstants) % 16 == 0);
        static_assert(sizeof(PreviewPbrConstants) % 16 == 0);
        static_assert(sizeof(ShadowTransformConstants) == sizeof(Matrix4));
        static_assert(sizeof(ToneMapConstants) == 48);

        ImVec4 Color(float red, float green, float blue, float alpha = 1.0F)
        {
            return {red / 255.0F, green / 255.0F, blue / 255.0F, alpha};
        }

        void ApplyEditorStyle()
        {
            ImGuiStyle& style = ImGui::GetStyle();
            style.WindowPadding = {12.0F, 10.0F};
            style.FramePadding = {9.0F, 5.0F};
            style.ItemSpacing = {8.0F, 7.0F};
            style.WindowRounding = 5.0F;
            style.ChildRounding = 4.0F;
            style.FrameRounding = 4.0F;
            style.PopupRounding = 4.0F;
            style.ScrollbarRounding = 8.0F;
            style.GrabRounding = 4.0F;
            style.WindowBorderSize = 1.0F;
            style.ChildBorderSize = 1.0F;

            auto& colors = style.Colors;
            colors[ImGuiCol_Text] = Color(224, 228, 238);
            colors[ImGuiCol_TextDisabled] = Color(123, 130, 148);
            colors[ImGuiCol_WindowBg] = Color(24, 27, 35);
            colors[ImGuiCol_ChildBg] = Color(28, 32, 41);
            colors[ImGuiCol_PopupBg] = Color(29, 33, 43);
            colors[ImGuiCol_Border] = Color(55, 61, 76);
            colors[ImGuiCol_FrameBg] = Color(37, 42, 54);
            colors[ImGuiCol_FrameBgHovered] = Color(48, 55, 70);
            colors[ImGuiCol_FrameBgActive] = Color(58, 66, 84);
            colors[ImGuiCol_TitleBg] = Color(25, 28, 36);
            colors[ImGuiCol_TitleBgActive] = Color(31, 35, 45);
            colors[ImGuiCol_MenuBarBg] = Color(20, 23, 30);
            colors[ImGuiCol_Button] = Color(44, 50, 64);
            colors[ImGuiCol_ButtonHovered] = Color(82, 92, 122);
            colors[ImGuiCol_ButtonActive] = Color(102, 91, 186);
            colors[ImGuiCol_Header] = Color(66, 61, 118);
            colors[ImGuiCol_HeaderHovered] = Color(82, 75, 146);
            colors[ImGuiCol_HeaderActive] = Color(104, 91, 194);
            colors[ImGuiCol_Separator] = Color(54, 60, 75);
            colors[ImGuiCol_Tab] = Color(32, 36, 46);
            colors[ImGuiCol_TabHovered] = Color(74, 69, 129);
            colors[ImGuiCol_TabSelected] = Color(58, 54, 104);
            colors[ImGuiCol_CheckMark] = Color(149, 132, 255);
            colors[ImGuiCol_SliderGrab] = Color(129, 111, 231);
            colors[ImGuiCol_SliderGrabActive] = Color(162, 146, 255);
        }

        void SetFixedWindow(const char* name, float x, float y, float width, float height)
        {
            ImGui::SetNextWindowPos({x, y});
            ImGui::SetNextWindowSize({std::max(width, 1.0F), std::max(height, 1.0F)});
            ImGui::Begin(name, nullptr,
                ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoCollapse |
                ImGuiWindowFlags_NoBringToFrontOnFocus);
        }

    }

    EditorApplication::EditorApplication(HINSTANCE instance, Rhi::BackendType backendType, bool smokeTest, bool gameplaySmokeTest, bool fbxSmokeTest)
        : m_Instance(instance), m_FbxSmokeTest(fbxSmokeTest), m_SmokeTest(smokeTest), m_GameplaySmokeTest(gameplaySmokeTest),
          m_BackendType(backendType)
    {
        std::array<wchar_t, 32768> modulePath{};
        const DWORD pathLength = GetModuleFileNameW(
            nullptr, modulePath.data(), static_cast<DWORD>(modulePath.size()));
        if (pathLength > 0 && pathLength < modulePath.size())
            m_ProjectRoot = std::filesystem::path(modulePath.data()).parent_path().parent_path().parent_path();
        else
            m_ProjectRoot = std::filesystem::current_path();
        m_ScenePath = m_ProjectRoot / "assets" / "scenes" / "EditorScene.ncmascene";
        m_PreviewMaterial.BaseColor[0] = 0.32F;
        m_PreviewMaterial.BaseColor[1] = 0.16F;
        m_PreviewMaterial.BaseColor[2] = 0.82F;
        m_PreviewMaterial.Metallic = 0.35F;
        m_PreviewMaterial.Roughness = 0.28F;
        m_PreviewCubeModel.setIdentity();
        m_PreviewGroundModel.setIdentity();
        for (Matrix4& cascadeMatrix : m_CascadeLightViewProjections)
            cascadeMatrix.setIdentity();
        m_PreviewShadowSettings.Resolution = 2048;
        m_PreviewShadowSettings.CascadeCount = 4;
        m_PreviewShadowSettings.MaxDistance = 40.0F;
        m_PreviewShadowSettings.Filter = Rendering::ShadowFilter::Pcss;
        m_ShowAnimationLab = m_SmokeTest;
    }
    EditorApplication::~EditorApplication() { Shutdown(); }

    int EditorApplication::Run()
    {
        if (!Initialize())
            return 1;

        while (m_Running)
        {
            glfwPollEvents();
            if (glfwWindowShouldClose(m_Window))
                m_Running = false;
            if (!m_Running)
                break;
            if (m_Minimized && !m_SmokeTest)
            {
                CancelInspectorEdit();
                glfwWaitEventsTimeout(0.016);
                continue;
            }
            BeginFrame();
            RenderEditor();
            EndFrame();
            ++m_RenderedFrames;
            if (m_SmokeTest && m_RenderedFrames >= 3)
                m_Running = false;
        }
        if (m_GameplaySmokeTest)
        {
            const auto& runtimeError = m_GameplayRuntime->GetLastError();
            if (!m_PlayScene || !m_ScenePlaying || !runtimeError.empty()) return 2;
            const auto playObject = m_PlayScene->FindObject(m_Scene.GetPersistentId(m_SelectedObject));
            if (m_PlayScene->GetLocalTransform(playObject).Rotation.isApprox(Quaternion::Identity())) return 3;
            ReloadGameplay();
            const int count = m_GameplayRuntime->GetBehaviourCount();
            if (!m_ScenePlaying || count != 1) return 4;
            TogglePlay();
            if (!m_Scene.GetLocalTransform(m_SelectedObject).Rotation.isApprox(Quaternion::Identity())) return 5;
            UndoSceneEdit();
            if (m_Scene.GetObjectName(m_SelectedObject) != "Character" || m_Scene.GetBehaviours(m_SelectedObject).size() != 1) return 6;
            RedoSceneEdit();
            if (m_Scene.GetObjectName(m_SelectedObject) != "Character command" || m_Scene.GetBehaviours(m_SelectedObject).size() != 1) return 7;
            UndoSceneEdit(); UndoSceneEdit();
            if (!m_Scene.GetBehaviours(m_SelectedObject).empty()) return 11;
            RedoSceneEdit(); RedoSceneEdit();
            if (m_Scene.GetObjectName(m_SelectedObject) != "Character command" || m_Scene.GetBehaviours(m_SelectedObject).size() != 1) return 12;
        }
        if (m_FbxSmokeTest)
        {
            if (!m_FbxPreview.Player || m_FbxPreview.Player->Time() <= 0 || !m_FbxError.empty()) return 8;
            UndoFbxEdit(); UndoFbxEdit();
            if (m_FbxPreview.Character) return 9;
            UndoFbxEdit(true); UndoFbxEdit(true);
            if (!m_FbxPreview.Character || m_FbxPreview.Paused) return 10;
        }
        return 0;
    }

    bool EditorApplication::Initialize()
    {
        Log::Init();
        if (!CreateEditorWindow() || !InitializeRenderer() || !InitializeImGui())
            return false;
        CreateSampleScene();
        if (m_FbxSmokeTest)
        {
            LoadFbxCharacter(m_ProjectRoot / "tests" / "assets" / "fbx" / "blender_279_sausage_7400_binary.fbx");
            if (!m_FbxPreview.Character) return false;
            ExecuteFbxEdit([](FbxPreviewState& state) { state.Paused = false; });
        }
        m_GameplayRuntime = std::make_unique<Scripting::DotNetGameplayRuntime>(
            m_ProjectRoot / "out" / "managed" / "Ncma.Managed.Host.runtimeconfig.json",
            m_ProjectRoot / "out" / "managed" / "Ncma.Managed.Host.dll",
            m_ProjectRoot / "out" / "managed" / "Ncma.Gameplay.Sample.dll");
        if (!m_SmokeTest || m_GameplaySmokeTest)
        {
            std::string gameplayError;
            if (m_GameplayRuntime->Start(gameplayError))
                AppendLog(std::format("C# gameplay ready: {} Behaviour types", m_GameplayRuntime->GetTypes().size()));
            else
                AppendLog("C# gameplay unavailable: " + gameplayError);
        }
        if (m_GameplaySmokeTest && (!m_GameplayRuntime->IsStarted() || m_GameplayRuntime->GetTypes().empty())) return false;
        m_Scene.EnableEditing();
        m_Scene.SelectEditorObject(m_Scene.GetPersistentId(m_SelectedObject));
        if (m_SmokeTest)
        {
            const auto before = m_Scene.CaptureDocument(); const auto revision = m_Scene.GetDocumentRevision();
            BeginInspectorEdit("Smoke transform drag");
            for (int i = 0; i < 10; ++i)
            {
                m_InspectorDraft->LocalTransform.Position.x() = static_cast<float>(i);
                m_Scene.PreviewTransform(m_InspectorDraft->Token, m_InspectorDraft->Object, m_InspectorDraft->LocalTransform);
            }
            if (m_Scene.CaptureDocument() != before || m_Scene.GetDocumentRevision() != revision) return false;
            CommitInspectorEdit("Smoke transform drag");
            if (m_Scene.GetEditorState().UndoCount != 1) return false;
            UndoSceneEdit();
            if (m_Scene.CaptureDocument() != before) return false;
            const auto objects = m_Scene.GetObjects();
            const auto otherId = m_Scene.GetPersistentId(objects.back());
            BeginInspectorEdit("Smoke selection commit");
            m_Scene.PreviewName(m_InspectorDraft->Token, m_InspectorDraft->Object, "Selection preview");
            SelectObject(objects.back());
            if (!m_Scene.Contains(m_SelectedObject) || m_Scene.GetEditorState().Selection != otherId) return false;
            UndoSceneEdit();
            if (m_Scene.CaptureDocument() != before || !m_Scene.Contains(m_SelectedObject)) return false;
        }
        if (m_GameplaySmokeTest)
        {
            ExecuteSceneMutation("Attach Behaviour", [&] {
                m_Scene.SetEditorBindings(m_Scene.GetPersistentId(m_SelectedObject), {m_GameplayRuntime->GetTypes().front().CreateBinding()}, "Attach Behaviour");
            });
            m_Scene.RenameEditorObject(m_Scene.GetPersistentId(m_SelectedObject), "Character command");
            RefreshEditorSelection();
            TogglePlay();
            if (!m_ScenePlaying) return false;
        }
        if (m_SmokeTest && !m_GameplaySmokeTest)
        {
            BeginInspectorEdit("Escape smoke");
            m_InspectorDraft->Name = "Escape discarded";
            m_Scene.PreviewName(m_InspectorDraft->Token, m_InspectorDraft->Object, m_InspectorDraft->Name);
        }
        AppendLog(std::format("NcmaEditor initialized with {}", m_Renderer->GetName()));
        AppendLog("C# gameplay; Python is reserved for independent modules and tools");
        return true;
    }

    bool EditorApplication::CreateEditorWindow()
    {
        glfwSetErrorCallback([](int code, const char* description) {
            std::fprintf(stderr, "GLFW error %d: %s\n", code, description);
        });
        if (glfwInit() != GLFW_TRUE)
            return false;
        m_GlfwInitialized = true;
        glfwWindowHint(GLFW_CLIENT_API, GLFW_NO_API);
        glfwWindowHint(GLFW_VISIBLE, m_SmokeTest ? GLFW_FALSE : GLFW_TRUE);
        glfwWindowHint(GLFW_RESIZABLE, GLFW_TRUE);
        m_Window = glfwCreateWindow(
            static_cast<int>(m_ClientWidth), static_cast<int>(m_ClientHeight),
            "NcmaEngine Editor - GLFW / RHI", nullptr, nullptr);
        if (m_Window == nullptr)
            return false;
        if (!ApplyWindowIcons())
            return false;
        glfwSetWindowUserPointer(m_Window, this);
        glfwSetFramebufferSizeCallback(m_Window, [](GLFWwindow* window, int width, int height) {
            auto* application = static_cast<EditorApplication*>(glfwGetWindowUserPointer(window));
            application->m_Minimized = width == 0 || height == 0;
            if (!application->m_Minimized)
            {
                application->m_ClientWidth = static_cast<std::uint32_t>(width);
                application->m_ClientHeight = static_cast<std::uint32_t>(height);
                if (application->m_Renderer)
                    application->m_Renderer->Resize(application->m_ClientWidth, application->m_ClientHeight);
            }
        });
        if (!m_SmokeTest)
            glfwShowWindow(m_Window);
        return true;
    }

    bool EditorApplication::ApplyWindowIcons()
    {
        const HWND nativeWindow = glfwGetWin32Window(m_Window);
        m_LargeWindowIcon = static_cast<HICON>(LoadImageW(
            m_Instance, MAKEINTRESOURCEW(IDI_NCMA_EDITOR), IMAGE_ICON,
            GetSystemMetrics(SM_CXICON), GetSystemMetrics(SM_CYICON), LR_DEFAULTCOLOR));
        m_SmallWindowIcon = static_cast<HICON>(LoadImageW(
            m_Instance, MAKEINTRESOURCEW(IDI_NCMA_EDITOR), IMAGE_ICON,
            GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_DEFAULTCOLOR));
        if (m_LargeWindowIcon == nullptr || m_SmallWindowIcon == nullptr)
        {
            MessageBoxW(nativeWindow,
                L"NcmaEditor could not load its embedded window icon.",
                L"NcmaEditor resource error", MB_OK | MB_ICONERROR);
            return false;
        }
        SendMessageW(nativeWindow, WM_SETICON, ICON_BIG, reinterpret_cast<LPARAM>(m_LargeWindowIcon));
        SendMessageW(nativeWindow, WM_SETICON, ICON_SMALL, reinterpret_cast<LPARAM>(m_SmallWindowIcon));
        return true;
    }

    bool EditorApplication::InitializeRenderer()
    {
        m_Renderer = m_BackendRegistry.Create(m_BackendType);
        if (!m_Renderer)
        {
            std::string error = std::format(
                "The requested {} renderer is not compiled into NcmaEngine.",
                Rhi::RenderBackendRegistry::ToString(m_BackendType));
            if (m_BackendType == Rhi::BackendType::Vulkan)
                error += "\n\n" + Rhi::ProbeVulkanRuntime().Diagnostic;
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor renderer unavailable", MB_OK | MB_ICONERROR);
            return false;
        }

        Rhi::BackendCreateInfo createInfo;
        createInfo.NativeWindow = glfwGetWin32Window(m_Window);
        createInfo.Width = m_ClientWidth;
        createInfo.Height = m_ClientHeight;
        createInfo.EnableValidation = true;
        createInfo.EnableVSync = true;
        std::string error;
        if (!m_Renderer->Initialize(createInfo, error))
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor renderer error", MB_OK | MB_ICONERROR);
            return false;
        }

        constexpr std::array<PreviewVertex, 28> previewVertices{{
            {{-1.0F, -1.0F, -1.0F}, {0.0F, 0.0F, -1.0F}},
            {{-1.0F, 1.0F, -1.0F}, {0.0F, 0.0F, -1.0F}},
            {{1.0F, 1.0F, -1.0F}, {0.0F, 0.0F, -1.0F}},
            {{1.0F, -1.0F, -1.0F}, {0.0F, 0.0F, -1.0F}},
            {{-1.0F, -1.0F, 1.0F}, {0.0F, 0.0F, 1.0F}},
            {{1.0F, -1.0F, 1.0F}, {0.0F, 0.0F, 1.0F}},
            {{1.0F, 1.0F, 1.0F}, {0.0F, 0.0F, 1.0F}},
            {{-1.0F, 1.0F, 1.0F}, {0.0F, 0.0F, 1.0F}},
            {{-1.0F, -1.0F, 1.0F}, {-1.0F, 0.0F, 0.0F}},
            {{-1.0F, 1.0F, 1.0F}, {-1.0F, 0.0F, 0.0F}},
            {{-1.0F, 1.0F, -1.0F}, {-1.0F, 0.0F, 0.0F}},
            {{-1.0F, -1.0F, -1.0F}, {-1.0F, 0.0F, 0.0F}},
            {{1.0F, -1.0F, -1.0F}, {1.0F, 0.0F, 0.0F}},
            {{1.0F, 1.0F, -1.0F}, {1.0F, 0.0F, 0.0F}},
            {{1.0F, 1.0F, 1.0F}, {1.0F, 0.0F, 0.0F}},
            {{1.0F, -1.0F, 1.0F}, {1.0F, 0.0F, 0.0F}},
            {{-1.0F, 1.0F, -1.0F}, {0.0F, 1.0F, 0.0F}},
            {{-1.0F, 1.0F, 1.0F}, {0.0F, 1.0F, 0.0F}},
            {{1.0F, 1.0F, 1.0F}, {0.0F, 1.0F, 0.0F}},
            {{1.0F, 1.0F, -1.0F}, {0.0F, 1.0F, 0.0F}},
            {{-1.0F, -1.0F, 1.0F}, {0.0F, -1.0F, 0.0F}},
            {{-1.0F, -1.0F, -1.0F}, {0.0F, -1.0F, 0.0F}},
            {{1.0F, -1.0F, -1.0F}, {0.0F, -1.0F, 0.0F}},
            {{1.0F, -1.0F, 1.0F}, {0.0F, -1.0F, 0.0F}},
            {{-4.0F, -1.25F, -4.0F}, {0.0F, 1.0F, 0.0F}},
            {{-4.0F, -1.25F, 4.0F}, {0.0F, 1.0F, 0.0F}},
            {{4.0F, -1.25F, 4.0F}, {0.0F, 1.0F, 0.0F}},
            {{4.0F, -1.25F, -4.0F}, {0.0F, 1.0F, 0.0F}}
        }};
        constexpr std::array<std::uint16_t, 42> previewIndices{{
            0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7,
            8, 9, 10, 8, 10, 11, 12, 13, 14, 12, 14, 15,
            16, 17, 18, 16, 18, 19, 20, 21, 22, 20, 22, 23,
            24, 25, 26, 24, 26, 27
        }};
        Rhi::BufferDescription probeDescription{};
        probeDescription.Size = sizeof(previewVertices);
        probeDescription.Stride = sizeof(PreviewVertex);
        probeDescription.Usage = Rhi::BufferUsage::Vertex;
        probeDescription.Memory = Rhi::MemoryUsage::GpuOnly;
        probeDescription.DebugName = "Editor.RendererStartupProbe";
        m_PreviewVertexBuffer = m_Renderer->CreateBuffer(probeDescription, previewVertices.data(), error);
        if (!m_PreviewVertexBuffer)
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor resource error", MB_OK | MB_ICONERROR);
            return false;
        }
        Rhi::BufferDescription indexDescription{};
        indexDescription.Size = sizeof(previewIndices);
        indexDescription.Stride = sizeof(std::uint16_t);
        indexDescription.Usage = Rhi::BufferUsage::Index;
        indexDescription.Memory = Rhi::MemoryUsage::GpuOnly;
        indexDescription.DebugName = "Editor.PreviewCubeIndices";
        m_PreviewIndexBuffer = m_Renderer->CreateBuffer(indexDescription, previewIndices.data(), error);
        if (!m_PreviewIndexBuffer)
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor resource error", MB_OK | MB_ICONERROR);
            return false;
        }
        Rhi::BufferDescription constantDescription{};
        constantDescription.Size = sizeof(PreviewTransformConstants);
        constantDescription.Usage = Rhi::BufferUsage::Constant;
        constantDescription.Memory = Rhi::MemoryUsage::CpuToGpu;
        constantDescription.DebugName = "Editor.PreviewTransforms";
        m_PreviewTransformBuffer = m_Renderer->CreateBuffer(constantDescription, nullptr, error);
        if (!m_PreviewTransformBuffer)
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor resource error", MB_OK | MB_ICONERROR);
            return false;
        }
        constantDescription.DebugName = "Editor.GroundTransforms";
        m_GroundTransformBuffer = m_Renderer->CreateBuffer(constantDescription, nullptr, error);
        if (!m_GroundTransformBuffer)
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor resource error", MB_OK | MB_ICONERROR);
            return false;
        }
        constantDescription.Size = sizeof(PreviewPbrConstants);
        constantDescription.DebugName = "Editor.PreviewPbrMaterial";
        m_PreviewMaterialBuffer = m_Renderer->CreateBuffer(constantDescription, nullptr, error);
        if (!m_PreviewMaterialBuffer)
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor resource error", MB_OK | MB_ICONERROR);
            return false;
        }
        constantDescription.Size = sizeof(ShadowTransformConstants);
        constantDescription.DebugName = "Editor.ShadowTransforms";
        m_ShadowTransformBuffer = m_Renderer->CreateBuffer(constantDescription, nullptr, error);
        if (!m_ShadowTransformBuffer)
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor resource error", MB_OK | MB_ICONERROR);
            return false;
        }

        constexpr std::string_view previewShader = R"(
static const float PI = 3.14159265359;
cbuffer PreviewTransforms : register(b0)
{
    column_major float4x4 Model;
    column_major float4x4 ModelViewProjection;
    column_major float4x4 View;
};
cbuffer PreviewMaterial : register(b0)
{
    float4 BaseColor;
    float4 Material;
    float4 LightDirection;
    float4 LightColor;
    float4 CameraPosition;
    column_major float4x4 LightViewProjections[4];
    float4 CascadeSplits;
    float4 ShadowParameters;
    float4 CascadeParameters;
};
Texture2DArray<float> ShadowMap : register(t0);
SamplerComparisonState ShadowSampler : register(s0);
struct VertexInput { float3 Position : POSITION; float3 Normal : NORMAL; };
struct PixelInput
{
    float4 Position : SV_POSITION;
    float3 WorldPosition : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float ViewDepth : TEXCOORD2;
};
PixelInput VSMain(VertexInput input)
{
    PixelInput output;
    float4 worldPosition = mul(Model, float4(input.Position, 1.0));
    output.Position = mul(ModelViewProjection, float4(input.Position, 1.0));
    output.WorldPosition = worldPosition.xyz;
    output.Normal = normalize(mul((float3x3)Model, input.Normal));
    output.ViewDepth = mul(View, worldPosition).z;
    return output;
}

float DistributionGGX(float3 normal, float3 halfway, float roughness)
{
    float alpha = roughness * roughness;
    float alphaSquared = alpha * alpha;
    float normalDotHalfway = max(dot(normal, halfway), 0.0);
    float denominator = normalDotHalfway * normalDotHalfway * (alphaSquared - 1.0) + 1.0;
    return alphaSquared / max(PI * denominator * denominator, 0.000001);
}

float GeometrySchlickGGX(float normalDotDirection, float roughness)
{
    float radius = roughness + 1.0;
    float k = radius * radius / 8.0;
    return normalDotDirection / max(normalDotDirection * (1.0 - k) + k, 0.000001);
}

float GeometrySmith(float3 normal, float3 viewDirection, float3 lightDirection, float roughness)
{
    return GeometrySchlickGGX(max(dot(normal, viewDirection), 0.0), roughness) *
        GeometrySchlickGGX(max(dot(normal, lightDirection), 0.0), roughness);
}

float3 FresnelSchlick(float cosineTheta, float3 reflectanceAtNormal)
{
    return reflectanceAtNormal + (1.0 - reflectanceAtNormal) * pow(saturate(1.0 - cosineTheta), 5.0);
}

float ReadShadowDepth(float2 uv, uint cascadeIndex)
{
    uint width;
    uint height;
    uint layers;
    ShadowMap.GetDimensions(width, height, layers);
    int2 texel = clamp(int2(saturate(uv) * float2(width, height)), int2(0, 0), int2(width - 1, height - 1));
    return ShadowMap.Load(int4(texel, cascadeIndex, 0));
}

float FindAverageBlockerDepth(float2 shadowUv, float receiverDepth, uint cascadeIndex)
{
    float blockerDepth = 0.0;
    float blockerCount = 0.0;
    float searchRadius = clamp(Material.z * receiverDepth, ShadowParameters.w, ShadowParameters.w * 8.0);
    [unroll]
    for (int y = -2; y <= 2; ++y)
    {
        [unroll]
        for (int x = -2; x <= 2; ++x)
        {
            float depth = ReadShadowDepth(
                shadowUv + float2(x, y) * (searchRadius * 0.5), cascadeIndex);
            if (depth < receiverDepth)
            {
                blockerDepth += depth;
                blockerCount += 1.0;
            }
        }
    }
    return blockerCount > 0.0 ? blockerDepth / blockerCount : -1.0;
}

float EvaluateCascadeShadow(float3 worldPosition, float normalDotLight, uint cascadeIndex)
{
    float4 lightClip = mul(LightViewProjections[cascadeIndex], float4(worldPosition, 1.0));
    float3 projected = lightClip.xyz / lightClip.w;
    float2 shadowUv = float2(projected.x * 0.5 + 0.5, -projected.y * 0.5 + 0.5);
    if (projected.z <= 0.0 || projected.z >= 1.0 ||
        any(shadowUv < 0.0) || any(shadowUv > 1.0))
        return 1.0;
    float bias = ShadowParameters.x + ShadowParameters.y * 0.001 * (1.0 - normalDotLight);
    float visibility = 0.0;
    float sampleCount = 0.0;
    int filterMode = (int)CascadeParameters.w;
    int filterRadius = filterMode == 0 ? 1 : 2;
    float filterStep = ShadowParameters.w;
    if (filterMode == 2)
    {
        float receiverDepth = projected.z - bias;
        float averageBlockerDepth = FindAverageBlockerDepth(shadowUv, receiverDepth, cascadeIndex);
        if (averageBlockerDepth < 0.0)
            return 1.0;
        float penumbraRatio = max(receiverDepth - averageBlockerDepth, 0.0) /
            max(averageBlockerDepth, 0.0001);
        float filterRadiusUv = clamp(
            penumbraRatio * Material.z, ShadowParameters.w, ShadowParameters.w * 8.0);
        filterStep = filterRadiusUv * 0.5;
    }
    [unroll]
    for (int y = -2; y <= 2; ++y)
    {
        [unroll]
        for (int x = -2; x <= 2; ++x)
        {
            if (abs(x) > filterRadius || abs(y) > filterRadius)
                continue;
            visibility += ShadowMap.SampleCmpLevelZero(
                ShadowSampler,
                float3(shadowUv + float2(x, y) * filterStep, cascadeIndex),
                projected.z - bias);
            sampleCount += 1.0;
        }
    }
    return visibility / sampleCount;
}

float EvaluateShadow(float3 worldPosition, float normalDotLight, float viewDepth)
{
    if (ShadowParameters.z < 0.5)
        return 1.0;
    uint cascadeIndex = viewDepth > CascadeSplits.x ? 1 : 0;
    cascadeIndex = viewDepth > CascadeSplits.y ? 2 : cascadeIndex;
    cascadeIndex = viewDepth > CascadeSplits.z ? 3 : cascadeIndex;
    float visibility = EvaluateCascadeShadow(worldPosition, normalDotLight, cascadeIndex);
    if (cascadeIndex >= 3)
        return visibility;
    float cascadeNear = cascadeIndex == 0 ? CascadeParameters.y : CascadeSplits[cascadeIndex - 1];
    float cascadeFar = CascadeSplits[cascadeIndex];
    float blendStart = lerp(cascadeFar, cascadeNear, CascadeParameters.x);
    float blend = saturate((viewDepth - blendStart) / max(cascadeFar - blendStart, 0.0001));
    if (blend > 0.0)
    {
        float nextVisibility = EvaluateCascadeShadow(worldPosition, normalDotLight, cascadeIndex + 1);
        visibility = lerp(visibility, nextVisibility, blend);
    }
    return visibility;
}

float4 PSMain(PixelInput input) : SV_TARGET
{
    float3 normal = normalize(input.Normal);
    float3 viewDirection = normalize(CameraPosition.xyz - input.WorldPosition);
    float3 lightDirection = normalize(LightDirection.xyz);
    float3 halfway = normalize(viewDirection + lightDirection);
    float metallic = saturate(Material.x);
    float roughness = clamp(Material.y, 0.04, 1.0);
    float3 baseColor = max(BaseColor.rgb, 0.0);
    float3 reflectanceAtNormal = lerp(0.04.xxx, baseColor, metallic);
    float3 fresnel = FresnelSchlick(max(dot(halfway, viewDirection), 0.0), reflectanceAtNormal);
    float distribution = DistributionGGX(normal, halfway, roughness);
    float geometry = GeometrySmith(normal, viewDirection, lightDirection, roughness);
    float normalDotView = max(dot(normal, viewDirection), 0.0);
    float normalDotLight = max(dot(normal, lightDirection), 0.0);
    float3 specular = distribution * geometry * fresnel /
        max(4.0 * normalDotView * normalDotLight, 0.0001);
    float3 diffuseWeight = (1.0 - fresnel) * (1.0 - metallic);
    float3 radiance = LightColor.rgb * LightColor.a;
    float shadow = EvaluateShadow(input.WorldPosition, normalDotLight, input.ViewDepth);
    float3 directLighting =
        (diffuseWeight * baseColor / PI + specular) * radiance * normalDotLight * shadow;
    float3 linearHdr = baseColor * Material.w + directLighting;
    return float4(linearHdr, BaseColor.a);
}
)";
        Rhi::GraphicsPipelineDescription previewPipelineDescription{};
        previewPipelineDescription.VertexShaderSource = previewShader;
        previewPipelineDescription.PixelShaderSource = previewShader;
        previewPipelineDescription.VertexLayout = {
            {"POSITION", 0, Rhi::VertexFormat::Float3, 0},
            {"NORMAL", 0, Rhi::VertexFormat::Float3, sizeof(float) * 3}};
        previewPipelineDescription.Cull = Rhi::CullMode::None;
        previewPipelineDescription.DepthTest = true;
        previewPipelineDescription.DepthWrite = true;
        previewPipelineDescription.DebugName = "Editor.ViewportPreview";
        m_PreviewPipeline = m_Renderer->CreateGraphicsPipeline(previewPipelineDescription, error);
        if (!m_PreviewPipeline)
        {
            m_Renderer->DestroyBuffer(m_PreviewVertexBuffer);
            m_PreviewVertexBuffer = {};
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor pipeline error", MB_OK | MB_ICONERROR);
            return false;
        }
        m_PreviewDraw.Pipeline = m_PreviewPipeline;
        m_PreviewDraw.VertexBuffer = m_PreviewVertexBuffer;
        m_PreviewDraw.IndexBuffer = m_PreviewIndexBuffer;
        m_PreviewDraw.VertexConstantBuffer = m_PreviewTransformBuffer;
        m_PreviewDraw.PixelConstantBuffer = m_PreviewMaterialBuffer;
        m_PreviewDraw.IndexCount = 36;
        m_PreviewDraw.IndexStride = sizeof(std::uint16_t);
        m_PreviewDraw.VertexStride = sizeof(PreviewVertex);
        m_GroundDraw = m_PreviewDraw;
        m_GroundDraw.VertexConstantBuffer = m_GroundTransformBuffer;
        m_GroundDraw.IndexCount = 6;
        m_GroundDraw.FirstIndex = 36;

        Rhi::TextureDescription shadowMapDescription{};
        shadowMapDescription.Width = m_PreviewShadowSettings.Resolution;
        shadowMapDescription.Height = m_PreviewShadowSettings.Resolution;
        shadowMapDescription.ArrayLayers = m_PreviewShadowSettings.CascadeCount;
        shadowMapDescription.Format = Rhi::TextureFormat::D32Float;
        shadowMapDescription.Usage = Rhi::TextureUsage::DepthStencil | Rhi::TextureUsage::Sampled;
        shadowMapDescription.DebugName = "Editor.DirectionalShadowMap";
        m_PreviewShadowMap = m_Renderer->CreateTexture(shadowMapDescription, error);
        if (!m_PreviewShadowMap)
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor resource error", MB_OK | MB_ICONERROR);
            return false;
        }

        Rhi::SamplerDescription shadowSamplerDescription{};
        shadowSamplerDescription.Filter = Rhi::SamplerFilter::Nearest;
        shadowSamplerDescription.AddressU = Rhi::SamplerAddressMode::ClampToBorder;
        shadowSamplerDescription.AddressV = Rhi::SamplerAddressMode::ClampToBorder;
        shadowSamplerDescription.AddressW = Rhi::SamplerAddressMode::ClampToBorder;
        shadowSamplerDescription.Comparison = Rhi::CompareOperation::LessEqual;
        shadowSamplerDescription.BorderColor[0] = 1.0F;
        shadowSamplerDescription.BorderColor[1] = 1.0F;
        shadowSamplerDescription.BorderColor[2] = 1.0F;
        shadowSamplerDescription.BorderColor[3] = 1.0F;
        shadowSamplerDescription.DebugName = "Editor.PcfShadowSampler";
        m_PreviewShadowSampler = m_Renderer->CreateSampler(shadowSamplerDescription, error);
        if (!m_PreviewShadowSampler)
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor resource error", MB_OK | MB_ICONERROR);
            return false;
        }

        constexpr std::string_view shadowShader = R"(
cbuffer ShadowTransforms : register(b0)
{
    column_major float4x4 LightModelViewProjection;
};
struct VertexInput { float3 Position : POSITION; };
float4 VSMain(VertexInput input) : SV_POSITION
{
    return mul(LightModelViewProjection, float4(input.Position, 1.0));
}
void PSMain() {}
)";
        Rhi::GraphicsPipelineDescription shadowPipelineDescription{};
        shadowPipelineDescription.VertexShaderSource = shadowShader;
        shadowPipelineDescription.PixelShaderSource = shadowShader;
        shadowPipelineDescription.VertexLayout = {
            {"POSITION", 0, Rhi::VertexFormat::Float3, 0}};
        shadowPipelineDescription.Cull = Rhi::CullMode::Back;
        shadowPipelineDescription.DepthTest = true;
        shadowPipelineDescription.DepthWrite = true;
        shadowPipelineDescription.DebugName = "Editor.DirectionalShadowDepth";
        m_ShadowPipeline = m_Renderer->CreateGraphicsPipeline(shadowPipelineDescription, error);
        if (!m_ShadowPipeline)
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor pipeline error", MB_OK | MB_ICONERROR);
            return false;
        }
        m_ShadowDraw.Pipeline = m_ShadowPipeline;
        m_ShadowDraw.VertexBuffer = m_PreviewVertexBuffer;
        m_ShadowDraw.IndexBuffer = m_PreviewIndexBuffer;
        m_ShadowDraw.VertexConstantBuffer = m_ShadowTransformBuffer;
        m_ShadowDraw.IndexCount = 36;
        m_ShadowDraw.IndexStride = sizeof(std::uint16_t);
        m_ShadowDraw.VertexStride = sizeof(PreviewVertex);
        m_ShadowDraw.ViewportWidth = static_cast<float>(m_PreviewShadowSettings.Resolution);
        m_ShadowDraw.ViewportHeight = static_cast<float>(m_PreviewShadowSettings.Resolution);
        m_GroundShadowDraw = m_ShadowDraw;
        m_GroundShadowDraw.IndexCount = 6;
        m_GroundShadowDraw.FirstIndex = 36;
        m_PreviewDraw.PixelTextures[0] = m_PreviewShadowMap;
        m_PreviewDraw.PixelSamplers[0] = m_PreviewShadowSampler;
        m_GroundDraw.PixelTextures[0] = m_PreviewShadowMap;
        m_GroundDraw.PixelSamplers[0] = m_PreviewShadowSampler;

        constexpr std::array<ToneMapVertex, 3> toneMapVertices{{
            {{-1.0F, -1.0F}, {0.0F, 1.0F}},
            {{-1.0F, 3.0F}, {0.0F, -1.0F}},
            {{3.0F, -1.0F}, {2.0F, 1.0F}}
        }};
        Rhi::BufferDescription toneMapVertexDescription{};
        toneMapVertexDescription.Size = sizeof(toneMapVertices);
        toneMapVertexDescription.Stride = sizeof(ToneMapVertex);
        toneMapVertexDescription.Usage = Rhi::BufferUsage::Vertex;
        toneMapVertexDescription.Memory = Rhi::MemoryUsage::GpuOnly;
        toneMapVertexDescription.DebugName = "Editor.ToneMapTriangle";
        m_ToneMapVertexBuffer =
            m_Renderer->CreateBuffer(toneMapVertexDescription, toneMapVertices.data(), error);
        if (!m_ToneMapVertexBuffer)
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor resource error", MB_OK | MB_ICONERROR);
            return false;
        }
        Rhi::BufferDescription toneMapConstantDescription{};
        toneMapConstantDescription.Size = sizeof(ToneMapConstants);
        toneMapConstantDescription.Usage = Rhi::BufferUsage::Constant;
        toneMapConstantDescription.Memory = Rhi::MemoryUsage::CpuToGpu;
        toneMapConstantDescription.DebugName = "Editor.ToneMapConstants";
        m_ToneMapConstantBuffer =
            m_Renderer->CreateBuffer(toneMapConstantDescription, nullptr, error);
        if (!m_ToneMapConstantBuffer)
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor resource error", MB_OK | MB_ICONERROR);
            return false;
        }

        Rhi::SamplerDescription linearSamplerDescription{};
        linearSamplerDescription.Filter = Rhi::SamplerFilter::Linear;
        linearSamplerDescription.AddressU = Rhi::SamplerAddressMode::ClampToEdge;
        linearSamplerDescription.AddressV = Rhi::SamplerAddressMode::ClampToEdge;
        linearSamplerDescription.AddressW = Rhi::SamplerAddressMode::ClampToEdge;
        linearSamplerDescription.DebugName = "Editor.HdrLinearSampler";
        m_PreviewLinearSampler = m_Renderer->CreateSampler(linearSamplerDescription, error);
        if (!m_PreviewLinearSampler)
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor resource error", MB_OK | MB_ICONERROR);
            return false;
        }

        constexpr std::string_view toneMapShader = R"(
cbuffer ToneMapConstants : register(b0)
{
    float4 ToneParameters;
    float4 ContactLightDirection;
    float4 ProjectionParameters;
};
Texture2D<float4> HdrColor : register(t0);
Texture2D<float> ViewDepth : register(t1);
SamplerState LinearSampler : register(s0);
struct VertexInput { float2 Position : POSITION; float2 TextureCoordinate : TEXCOORD0; };
struct PixelInput { float4 Position : SV_POSITION; float2 TextureCoordinate : TEXCOORD0; };
PixelInput VSMain(VertexInput input)
{
    PixelInput output;
    output.Position = float4(input.Position, 0.0, 1.0);
    output.TextureCoordinate = input.TextureCoordinate;
    return output;
}
float3 AcesFilm(float3 color)
{
    return saturate((color * (2.51 * color + 0.03)) /
        (color * (2.43 * color + 0.59) + 0.14));
}

float DeviceDepthToViewDepth(float deviceDepth)
{
    float nearPlane = ProjectionParameters.z;
    float farPlane = ProjectionParameters.w;
    return nearPlane * farPlane /
        max(farPlane - deviceDepth * (farPlane - nearPlane), 0.0001);
}

float2 ProjectViewPosition(float3 viewPosition)
{
    float2 ndc = viewPosition.xy * ProjectionParameters.xy / viewPosition.z;
    return float2(ndc.x * 0.5 + 0.5, -ndc.y * 0.5 + 0.5);
}

float EvaluateContactShadow(float2 uv, float deviceDepth)
{
    float strength = ToneParameters.y;
    if (strength <= 0.0 || deviceDepth >= 0.99999)
        return 1.0;

    float viewDepth = DeviceDepthToViewDepth(deviceDepth);
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float3 rayPosition = float3(
        ndc.x * viewDepth / ProjectionParameters.x,
        ndc.y * viewDepth / ProjectionParameters.y,
        viewDepth);
    float3 rayDirection = normalize(ContactLightDirection.xyz);
    int stepCount = clamp((int)ContactLightDirection.w, 4, 32);
    float stepLength = ToneParameters.z / stepCount;
    float thickness = ToneParameters.w;
    rayPosition += rayDirection * max(stepLength, thickness);

    uint depthWidth;
    uint depthHeight;
    ViewDepth.GetDimensions(depthWidth, depthHeight);
    [loop]
    for (int stepIndex = 0; stepIndex < 32; ++stepIndex)
    {
        if (stepIndex >= stepCount || rayPosition.z <= ProjectionParameters.z)
            break;
        float2 rayUv = ProjectViewPosition(rayPosition);
        if (any(rayUv <= 0.0) || any(rayUv >= 1.0))
            break;
        int2 texel = clamp(
            int2(rayUv * float2(depthWidth, depthHeight)),
            int2(0, 0), int2(depthWidth - 1, depthHeight - 1));
        float sceneDeviceDepth = ViewDepth.Load(int3(texel, 0));
        float sceneViewDepth = DeviceDepthToViewDepth(sceneDeviceDepth);
        float separation = rayPosition.z - sceneViewDepth;
        if (separation > 0.0 && separation < thickness)
        {
            float distanceFade = 1.0 - (float)stepIndex / stepCount;
            return 1.0 - strength * distanceFade;
        }
        rayPosition += rayDirection * stepLength;
    }
    return 1.0;
}

float4 PSMain(PixelInput input) : SV_TARGET
{
    float4 hdr = HdrColor.Sample(LinearSampler, input.TextureCoordinate);
    uint depthWidth;
    uint depthHeight;
    ViewDepth.GetDimensions(depthWidth, depthHeight);
    int2 depthTexel = clamp(
        int2(saturate(input.TextureCoordinate) * float2(depthWidth, depthHeight)),
        int2(0, 0), int2(depthWidth - 1, depthHeight - 1));
    float deviceDepth = ViewDepth.Load(int3(depthTexel, 0));
    float contactShadow = EvaluateContactShadow(input.TextureCoordinate, deviceDepth);
    float3 displayColor = AcesFilm(max(hdr.rgb, 0.0) * ToneParameters.x * contactShadow);
    displayColor = pow(displayColor, 1.0 / 2.2);
    return float4(displayColor, hdr.a);
}
)";
        Rhi::GraphicsPipelineDescription toneMapPipelineDescription{};
        toneMapPipelineDescription.VertexShaderSource = toneMapShader;
        toneMapPipelineDescription.PixelShaderSource = toneMapShader;
        toneMapPipelineDescription.VertexLayout = {
            {"POSITION", 0, Rhi::VertexFormat::Float2, 0},
            {"TEXCOORD", 0, Rhi::VertexFormat::Float2, sizeof(float) * 2}};
        toneMapPipelineDescription.Cull = Rhi::CullMode::None;
        toneMapPipelineDescription.DepthTest = false;
        toneMapPipelineDescription.DepthWrite = false;
        toneMapPipelineDescription.DebugName = "Editor.ViewportToneMap";
        m_ToneMapPipeline = m_Renderer->CreateGraphicsPipeline(toneMapPipelineDescription, error);
        if (!m_ToneMapPipeline)
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor pipeline error", MB_OK | MB_ICONERROR);
            return false;
        }
        m_ToneMapDraw.Pipeline = m_ToneMapPipeline;
        m_ToneMapDraw.VertexBuffer = m_ToneMapVertexBuffer;
        m_ToneMapDraw.PixelConstantBuffer = m_ToneMapConstantBuffer;
        m_ToneMapDraw.PixelSamplers[0] = m_PreviewLinearSampler;
        m_ToneMapDraw.PixelSamplers[1] = m_PreviewLinearSampler;
        m_ToneMapDraw.VertexCount = static_cast<std::uint32_t>(toneMapVertices.size());
        m_ToneMapDraw.VertexStride = sizeof(ToneMapVertex);

        m_PreviewRenderGraph.AddPass(
            {"Directional Shadow", {}, {"Shadow.DirectionalDepth"}},
            [this](std::string& passError) {
                if (!m_PreviewShadowSettings.Enabled)
                    return true;
                for (std::uint32_t cascade = 0;
                     cascade < m_PreviewShadowSettings.CascadeCount; ++cascade)
                {
                    Rhi::RenderPassDescription pass{};
                    pass.UseColorTarget = false;
                    pass.DepthTarget = m_PreviewShadowMap;
                    pass.DepthLayer = cascade;
                    pass.ClearDepth = true;
                    pass.DebugName = "Editor.DirectionalShadow";
                    if (!m_Renderer->BeginRenderPass(pass, passError))
                        return false;
                    ShadowTransformConstants shadowTransforms;
                    shadowTransforms.LightModelViewProjection =
                        m_CascadeLightViewProjections[cascade] * m_PreviewCubeModel;
                    if (!m_Renderer->UpdateBuffer(
                        m_ShadowTransformBuffer, &shadowTransforms,
                        sizeof(shadowTransforms), 0, passError) ||
                        !m_Renderer->Draw(m_ShadowDraw, passError))
                    {
                        m_Renderer->EndRenderPass();
                        return false;
                    }
                    shadowTransforms.LightModelViewProjection =
                        m_CascadeLightViewProjections[cascade] * m_PreviewGroundModel;
                    if (!m_Renderer->UpdateBuffer(
                        m_ShadowTransformBuffer, &shadowTransforms,
                        sizeof(shadowTransforms), 0, passError) ||
                        !m_Renderer->Draw(m_GroundShadowDraw, passError))
                    {
                        m_Renderer->EndRenderPass();
                        return false;
                    }
                    m_Renderer->EndRenderPass();
                }
                return true;
            });
        m_PreviewRenderGraph.AddPass(
            {"PBR Geometry", {"Shadow.DirectionalDepth"}, {"Viewport.HdrColor", "Viewport.Depth"}},
            [this](std::string& passError) {
                Rhi::RenderPassDescription pass{};
                pass.ColorTarget = m_PreviewHdrColor;
                pass.DepthTarget = m_PreviewDepth;
                pass.ClearColor = true;
                pass.ClearDepth = true;
                pass.ClearColorValue[0] = 0.012F;
                pass.ClearColorValue[1] = 0.016F;
                pass.ClearColorValue[2] = 0.026F;
                pass.DebugName = "Editor.PbrGeometry";
                if (!m_Renderer->BeginRenderPass(pass, passError))
                    return false;
                const bool result = m_Renderer->Draw(m_PreviewDraw, passError) &&
                    m_Renderer->Draw(m_GroundDraw, passError);
                m_Renderer->EndRenderPass();
                return result;
            });
        m_PreviewRenderGraph.AddPass(
            {"Contact Shadows + Tone Mapping", {"Viewport.HdrColor", "Viewport.Depth"}, {"Swapchain.Color"}},
            [this](std::string& passError) {
                Rhi::RenderPassDescription pass{};
                pass.DebugName = "Editor.ToneMapping";
                if (!m_Renderer->BeginRenderPass(pass, passError))
                    return false;
                const bool result = m_Renderer->Draw(m_ToneMapDraw, passError);
                m_Renderer->EndRenderPass();
                return result;
            });
        if (!m_PreviewRenderGraph.Compile(error))
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor render graph error", MB_OK | MB_ICONERROR);
            return false;
        }

        Rhi::TextureDescription colorTextureDescription{};
        colorTextureDescription.Width = 16;
        colorTextureDescription.Height = 16;
        colorTextureDescription.Format = Rhi::TextureFormat::Rgba16Float;
        colorTextureDescription.Usage = Rhi::TextureUsage::Sampled | Rhi::TextureUsage::RenderTarget;
        colorTextureDescription.DebugName = "Editor.HdrColorProbe";
        const Rhi::TextureHandle colorTexture = m_Renderer->CreateTexture(colorTextureDescription, error);
        if (!colorTexture)
        {
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor resource error", MB_OK | MB_ICONERROR);
            return false;
        }

        Rhi::TextureDescription shadowTextureDescription{};
        shadowTextureDescription.Width = 16;
        shadowTextureDescription.Height = 16;
        shadowTextureDescription.ArrayLayers = 4;
        shadowTextureDescription.Format = Rhi::TextureFormat::D32Float;
        shadowTextureDescription.Usage = Rhi::TextureUsage::DepthStencil | Rhi::TextureUsage::Sampled;
        shadowTextureDescription.DebugName = "Editor.CascadedShadowProbe";
        const Rhi::TextureHandle shadowTexture = m_Renderer->CreateTexture(shadowTextureDescription, error);
        if (!shadowTexture)
        {
            m_Renderer->DestroyTexture(colorTexture);
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor resource error", MB_OK | MB_ICONERROR);
            return false;
        }

        Rhi::SamplerDescription shadowProbeSamplerDescription{};
        shadowProbeSamplerDescription.Filter = Rhi::SamplerFilter::Linear;
        shadowProbeSamplerDescription.AddressU = Rhi::SamplerAddressMode::ClampToBorder;
        shadowProbeSamplerDescription.AddressV = Rhi::SamplerAddressMode::ClampToBorder;
        shadowProbeSamplerDescription.AddressW = Rhi::SamplerAddressMode::ClampToBorder;
        shadowProbeSamplerDescription.Comparison = Rhi::CompareOperation::LessEqual;
        shadowProbeSamplerDescription.BorderColor[0] = 1.0F;
        shadowProbeSamplerDescription.BorderColor[1] = 1.0F;
        shadowProbeSamplerDescription.BorderColor[2] = 1.0F;
        shadowProbeSamplerDescription.BorderColor[3] = 1.0F;
        shadowProbeSamplerDescription.DebugName = "Editor.PcfShadowProbe";
        const Rhi::SamplerHandle shadowSampler =
            m_Renderer->CreateSampler(shadowProbeSamplerDescription, error);
        if (!shadowSampler)
        {
            m_Renderer->DestroyTexture(shadowTexture);
            m_Renderer->DestroyTexture(colorTexture);
            MessageBoxA(glfwGetWin32Window(m_Window), error.c_str(), "NcmaEditor resource error", MB_OK | MB_ICONERROR);
            return false;
        }
        m_Renderer->DestroySampler(shadowSampler);
        m_Renderer->DestroyTexture(shadowTexture);
        m_Renderer->DestroyTexture(colorTexture);
        return true;
    }

    bool EditorApplication::EnsurePreviewTargets(
        std::uint32_t width, std::uint32_t height, std::string& error)
    {
        if (m_PreviewHdrColor && m_PreviewDepth &&
            width == m_PreviewTargetWidth && height == m_PreviewTargetHeight)
            return true;

        Rhi::TextureDescription colorDescription{};
        colorDescription.Width = width;
        colorDescription.Height = height;
        colorDescription.Format = Rhi::TextureFormat::Rgba16Float;
        colorDescription.Usage = Rhi::TextureUsage::Sampled | Rhi::TextureUsage::RenderTarget;
        colorDescription.DebugName = "Editor.ViewportHdrColor";
        const Rhi::TextureHandle newColor = m_Renderer->CreateTexture(colorDescription, error);
        if (!newColor)
            return false;

        Rhi::TextureDescription depthDescription{};
        depthDescription.Width = width;
        depthDescription.Height = height;
        depthDescription.Format = Rhi::TextureFormat::D32Float;
        depthDescription.Usage = Rhi::TextureUsage::DepthStencil | Rhi::TextureUsage::Sampled;
        depthDescription.DebugName = "Editor.ViewportDepth";
        const Rhi::TextureHandle newDepth = m_Renderer->CreateTexture(depthDescription, error);
        if (!newDepth)
        {
            m_Renderer->DestroyTexture(newColor);
            return false;
        }

        m_Renderer->DestroyTexture(m_PreviewDepth);
        m_Renderer->DestroyTexture(m_PreviewHdrColor);
        m_PreviewHdrColor = newColor;
        m_PreviewDepth = newDepth;
        m_PreviewTargetWidth = width;
        m_PreviewTargetHeight = height;
        return true;
    }

    bool EditorApplication::InitializeImGui()
    {
        auto* d3d11 = dynamic_cast<Rhi::D3D11RenderBackend*>(m_Renderer.get());
        if (d3d11 == nullptr)
        {
            MessageBoxA(glfwGetWin32Window(m_Window),
                "The editor ImGui renderer currently requires the D3D11 backend.",
                "NcmaEditor UI backend unavailable", MB_OK | MB_ICONERROR);
            return false;
        }
        IMGUI_CHECKVERSION();
        ImGui::CreateContext();
        ImGuiIO& io = ImGui::GetIO();
        io.IniFilename = nullptr;
        io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;
        ApplyEditorStyle();

        if (!ImGui_ImplGlfw_InitForOther(m_Window, true) ||
            !ImGui_ImplDX11_Init(d3d11->GetDevice(), d3d11->GetDeviceContext()))
            return false;
        m_ImGuiInitialized = true;
        return true;
    }

    void EditorApplication::Shutdown() noexcept
    {
        if (m_GameplayRuntime) m_GameplayRuntime->Stop();
        m_PlayScene.reset();
        try { CancelInspectorEdit(); if (m_ScenePlaying) m_Scene.FreezeEditing(false); } catch (...) { }
        m_ScenePlaying = false;
        if (m_ImGuiInitialized)
        {
            ImGui_ImplDX11_Shutdown();
            ImGui_ImplGlfw_Shutdown();
            ImGui::DestroyContext();
            m_ImGuiInitialized = false;
        }
        if (m_Renderer)
        {
            m_PreviewRenderGraph.Reset();
            m_Renderer->DestroyGraphicsPipeline(m_ShadowPipeline);
            m_Renderer->DestroyBuffer(m_ShadowTransformBuffer);
            m_Renderer->DestroySampler(m_PreviewShadowSampler);
            m_Renderer->DestroyTexture(m_PreviewShadowMap);
            m_Renderer->DestroyTexture(m_PreviewDepth);
            m_Renderer->DestroyTexture(m_PreviewHdrColor);
            m_Renderer->DestroyGraphicsPipeline(m_ToneMapPipeline);
            m_Renderer->DestroySampler(m_PreviewLinearSampler);
            m_Renderer->DestroyBuffer(m_ToneMapConstantBuffer);
            m_Renderer->DestroyBuffer(m_ToneMapVertexBuffer);
            m_Renderer->DestroyGraphicsPipeline(m_PreviewPipeline);
            m_Renderer->DestroyBuffer(m_PreviewMaterialBuffer);
            m_Renderer->DestroyBuffer(m_GroundTransformBuffer);
            m_Renderer->DestroyBuffer(m_PreviewTransformBuffer);
            m_Renderer->DestroyBuffer(m_PreviewIndexBuffer);
            m_Renderer->DestroyBuffer(m_PreviewVertexBuffer);
            m_PreviewPipeline = {};
            m_ShadowPipeline = {};
            m_ShadowTransformBuffer = {};
            m_PreviewShadowSampler = {};
            m_PreviewShadowMap = {};
            m_PreviewDepth = {};
            m_PreviewHdrColor = {};
            m_ToneMapPipeline = {};
            m_PreviewLinearSampler = {};
            m_ToneMapConstantBuffer = {};
            m_ToneMapVertexBuffer = {};
            m_PreviewMaterialBuffer = {};
            m_GroundTransformBuffer = {};
            m_PreviewTransformBuffer = {};
            m_PreviewIndexBuffer = {};
            m_PreviewVertexBuffer = {};
            m_Renderer->Shutdown();
            m_Renderer.reset();
        }
        if (m_Window != nullptr)
        {
            glfwDestroyWindow(m_Window);
            m_Window = nullptr;
        }
        if (m_SmallWindowIcon != nullptr)
        {
            DestroyIcon(m_SmallWindowIcon);
            m_SmallWindowIcon = nullptr;
        }
        if (m_LargeWindowIcon != nullptr)
        {
            DestroyIcon(m_LargeWindowIcon);
            m_LargeWindowIcon = nullptr;
        }
        if (m_GlfwInitialized)
        {
            glfwTerminate();
            m_GlfwInitialized = false;
        }
        Log::Shutdown();
    }

    void EditorApplication::CreateSampleScene()
    {
        const GameObjectId light = m_Scene.CreateObject("Directional Light");
        const GameObjectId camera = m_Scene.CreateObject("Main Camera");
        const GameObjectId character = m_Scene.CreateObject("Character");
        (void)m_Scene.CreateObject("HUD");

        Transform lightTransform, cameraTransform;
        lightTransform.Rotation = Quaternion(0.90F, -0.25F, 0.35F, 0.0F).normalized();
        cameraTransform.Position = {0.0F, 2.4F, -6.0F};
        m_Scene.SetLocalTransform(light, lightTransform);
        m_Scene.SetLocalTransform(camera, cameraTransform);
        m_SelectedObject = character;
    }

    void EditorApplication::BeginFrame()
    {
        std::string error;
        if (!m_Renderer->BeginFrame(error))
            AppendLog(error);
        ImGui_ImplDX11_NewFrame();
        ImGui_ImplGlfw_NewFrame();
        const bool escapeSmoke = m_SmokeTest && !m_GameplaySmokeTest && m_RenderedFrames == 0;
        const auto escapeRevision = escapeSmoke ? m_Scene.GetDocumentRevision() : 0;
        const auto escapeUndoCount = escapeSmoke ? m_Scene.GetEditorState().UndoCount : 0;
        if (escapeSmoke) { ImGui::GetIO().AddFocusEvent(true); ImGui::GetIO().AddKeyEvent(ImGuiKey_Escape, true); }
        else if (m_SmokeTest) ImGui::GetIO().AddKeyEvent(ImGuiKey_Escape, false);
        ImGui::NewFrame();
        m_CancelledInspectorThisFrame = false;
        if (m_InspectorDraft && (ImGui::IsKeyPressed(ImGuiKey_Escape, false) ||
            (!m_SmokeTest && glfwGetWindowAttrib(m_Window, GLFW_FOCUSED) == GLFW_FALSE)))
        {
            CancelInspectorEdit();
            m_CancelledInspectorThisFrame = true;
        }
        if (escapeSmoke && (m_InspectorDraft || m_Scene.GetDocumentRevision() != escapeRevision ||
            m_Scene.GetEditorState().UndoCount != escapeUndoCount || m_Scene.GetObjectName(m_SelectedObject) == "Escape discarded"))
            throw std::runtime_error("Escape must cancel the Inspector draft without changing the document or history");
        m_AnimationPreview.Tick(std::clamp(static_cast<double>(ImGui::GetIO().DeltaTime), 0.0, 0.1));
        if (m_FbxPreview.Player && !m_FbxPreview.Paused)
        {
            try { m_FbxPreview.Player->Advance(m_FbxSmokeTest ? 1.0 / 60 :
                std::clamp(static_cast<double>(ImGui::GetIO().DeltaTime), 0.0, std::min(0.1, m_FbxPreview.Player->Clip().Duration * 64))); }
            catch (const std::exception& error) { m_FbxPreview.Paused = true; m_FbxError = error.what(); }
        }
        const bool updateGameplay = m_ScenePlaying && !m_ScenePaused && m_PlayScene;
        if (updateGameplay) m_PlayScene->BeginGameplayPhase();
        if (m_ScenePlaying && !m_ScenePaused && m_GameplayRuntime && m_GameplayRuntime->IsStarted())
        {
            m_GameplayRuntime->Tick((m_GameplaySmokeTest) ? 1.0 / 60.0 : ImGui::GetIO().DeltaTime);
            if (!m_GameplayRuntime->GetLastError().empty())
            {
                AppendLog("C# gameplay paused: " + m_GameplayRuntime->GetLastError());
                m_ScenePaused = true;
            }
        }
        if (updateGameplay)
        {
            if (m_ScenePaused) m_PlayScene->AbortGameplayPhase();
            else m_PlayScene->CommitGameplayPhase();
        }
    }

    void EditorApplication::RenderEditor()
    {
        if (m_SmokeTest && m_RenderedFrames == 1) m_ShowAnimationLab = false;
        RenderMenuBar();
        int width = 0;
        int height = 0;
        glfwGetFramebufferSize(m_Window, &width, &height);
        const float menuHeight = ImGui::GetFrameHeight();
        RECT workArea{0, static_cast<LONG>(menuHeight), width, height};
        RenderToolbar(workArea);
        RenderSceneObjects(workArea);
        RenderViewport(workArea);
        RenderInspector(workArea);
        RenderBottomPanel(workArea);
        RenderStatusBar(workArea);
        RenderAnimationLab();
        RenderFbxCharacter();
    }

    void EditorApplication::EndFrame()
    {
        ImGui::Render();
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
        DrawViewportPreview();
        m_Renderer->EndFrame();
    }

    void EditorApplication::DrawViewportPreview()
    {
        // This direct-to-swapchain preview predates composited viewport textures.
        // Suspend it while the floating animation lab is open so it cannot cover the lab.
        if (m_ShowAnimationLab || m_ShowFbxCharacter) return;
        if (!m_PreviewPipeline || !m_PreviewVertexBuffer || !m_PreviewIndexBuffer ||
            !m_PreviewTransformBuffer || !m_GroundTransformBuffer || !m_PreviewMaterialBuffer ||
            !m_ShadowPipeline || !m_PreviewShadowMap || !m_PreviewShadowSampler ||
            m_PreviewDraw.ViewportWidth <= 0.0F || m_PreviewDraw.ViewportHeight <= 0.0F)
            return;
        std::string error;
        const std::uint32_t targetWidth =
            std::max(1U, static_cast<std::uint32_t>(m_PreviewDraw.ViewportWidth));
        const std::uint32_t targetHeight =
            std::max(1U, static_cast<std::uint32_t>(m_PreviewDraw.ViewportHeight));
        if (!EnsurePreviewTargets(targetWidth, targetHeight, error))
        {
            AppendLog(error);
            return;
        }
        m_PreviewRotation += m_SmokeTest ? 0.04F : ImGui::GetIO().DeltaTime * 0.65F;
        const float aspect = static_cast<float>(targetWidth) / static_cast<float>(targetHeight);
        m_PreviewCubeModel = Matrix4::Identity();
        m_PreviewCubeModel.block<3, 3>(0, 0) =
            (Eigen::AngleAxisf(m_PreviewRotation, Vector3::UnitY()) *
             Eigen::AngleAxisf(-0.35F, Vector3::UnitX())).toRotationMatrix();
        // The reference mesh represents the selected scripted gameObject until mesh components land.
        if (m_Scene.Contains(m_SelectedObject) && m_Scene.HasTransform(m_SelectedObject))
        {
            const ManagedSceneClient& previewScene = m_PlayScene ? *m_PlayScene : m_Scene;
            const GameObjectId previewObject = previewScene.FindObject(m_Scene.GetPersistentId(m_SelectedObject));
            if (previewScene.Contains(previewObject))
                m_PreviewCubeModel = (m_InspectorDraft && m_InspectorDraft->HasTransform ? m_InspectorDraft->LocalTransform : previewScene.GetWorldTransform(previewObject)).ToMatrix();
        }
        m_PreviewGroundModel = Matrix4::Identity();
        Matrix4 view = Matrix4::Identity();
        view(2, 3) = 5.0F;
        const float verticalScale = 1.0F / std::tan(52.0F * 3.1415926535F / 360.0F);
        constexpr float nearPlane = 0.1F;
        constexpr float farPlane = 100.0F;
        Matrix4 projection = Matrix4::Zero();
        projection(0, 0) = verticalScale / aspect;
        projection(1, 1) = verticalScale;
        projection(2, 2) = farPlane / (farPlane - nearPlane);
        projection(2, 3) = (-nearPlane * farPlane) / (farPlane - nearPlane);
        projection(3, 2) = 1.0F;
        PreviewTransformConstants transforms;
        transforms.Model = m_PreviewCubeModel;
        transforms.ModelViewProjection = projection * view * m_PreviewCubeModel;
        transforms.View = view;
        if (!m_Renderer->UpdateBuffer(
            m_PreviewTransformBuffer, &transforms, sizeof(transforms), 0, error))
        {
            AppendLog(error);
            return;
        }
        transforms.Model = m_PreviewGroundModel;
        transforms.ModelViewProjection = projection * view * m_PreviewGroundModel;
        if (!m_Renderer->UpdateBuffer(
            m_GroundTransformBuffer, &transforms, sizeof(transforms), 0, error))
        {
            AppendLog(error);
            return;
        }
        m_PreviewShadowSettings.Sanitize();
        const Vector3 lightDirection(-0.45F, 0.78F, -0.55F);
        const Vector3 lightForward = -lightDirection.normalized();
        const Vector3 up = std::abs(lightForward.dot(Vector3::UnitY())) > 0.98F
            ? Vector3::UnitZ() : Vector3::UnitY();
        const Vector3 lightRight = up.cross(lightForward).normalized();
        const Vector3 lightUp = lightForward.cross(lightRight);
        const Vector3 cameraPosition(0.0F, 0.0F, -5.0F);
        float cascadeNear = nearPlane;
        const float shadowDistance = std::min(m_PreviewShadowSettings.MaxDistance, farPlane);
        for (std::uint32_t cascade = 0;
             cascade < m_PreviewShadowSettings.CascadeCount; ++cascade)
        {
            const float fraction =
                static_cast<float>(cascade + 1) /
                static_cast<float>(m_PreviewShadowSettings.CascadeCount);
            const float logarithmic = nearPlane * std::pow(shadowDistance / nearPlane, fraction);
            const float linear = nearPlane + (shadowDistance - nearPlane) * fraction;
            const float cascadeFar = std::lerp(
                linear, logarithmic, m_PreviewShadowSettings.CascadeLambda);
            m_CascadeSplits[cascade] = cascadeFar;

            std::array<Vector3, 8> corners{};
            std::size_t cornerIndex = 0;
            for (const float distance : {cascadeNear, cascadeFar})
            {
                const float halfHeight = distance / verticalScale;
                const float halfWidth = halfHeight * aspect;
                for (const float y : {-1.0F, 1.0F})
                {
                    for (const float x : {-1.0F, 1.0F})
                    {
                        corners[cornerIndex++] = cameraPosition +
                            Vector3(x * halfWidth, y * halfHeight, distance);
                    }
                }
            }

            Vector3 cascadeCenter = Vector3::Zero();
            for (const Vector3& corner : corners)
                cascadeCenter += corner;
            cascadeCenter /= static_cast<float>(corners.size());
            float radius = 0.0F;
            for (const Vector3& corner : corners)
                radius = std::max(radius, (corner - cascadeCenter).norm());
            radius = std::ceil(radius * 16.0F) / 16.0F;

            const Vector3 lightPosition = cascadeCenter - lightForward * (radius + 20.0F);
            Matrix4 lightView = Matrix4::Identity();
            lightView.block<1, 3>(0, 0) = lightRight.transpose();
            lightView.block<1, 3>(1, 0) = lightUp.transpose();
            lightView.block<1, 3>(2, 0) = lightForward.transpose();
            lightView(0, 3) = -lightRight.dot(lightPosition);
            lightView(1, 3) = -lightUp.dot(lightPosition);
            lightView(2, 3) = -lightForward.dot(lightPosition);

            const Vector4 centerLight = lightView * Vector4(
                cascadeCenter.x(), cascadeCenter.y(), cascadeCenter.z(), 1.0F);
            const float texelSize = (2.0F * radius) /
                static_cast<float>(m_PreviewShadowSettings.Resolution);
            const float snappedX = std::floor(centerLight.x() / texelSize) * texelSize;
            const float snappedY = std::floor(centerLight.y() / texelSize) * texelSize;
            constexpr float depthPadding = 30.0F;
            const float left = snappedX - radius;
            const float right = snappedX + radius;
            const float bottom = snappedY - radius;
            const float top = snappedY + radius;
            const float lightNear = std::max(0.0F, centerLight.z() - radius - depthPadding);
            const float lightFar = centerLight.z() + radius + depthPadding;
            Matrix4 lightProjection = Matrix4::Zero();
            lightProjection(0, 0) = 2.0F / (right - left);
            lightProjection(1, 1) = 2.0F / (top - bottom);
            lightProjection(2, 2) = 1.0F / (lightFar - lightNear);
            lightProjection(0, 3) = -(right + left) / (right - left);
            lightProjection(1, 3) = -(top + bottom) / (top - bottom);
            lightProjection(2, 3) = -lightNear / (lightFar - lightNear);
            lightProjection(3, 3) = 1.0F;
            m_CascadeLightViewProjections[cascade] = lightProjection * lightView;
            cascadeNear = cascadeFar;
        }
        m_PreviewMaterial.Sanitize();
        m_PreviewShadowSettings.Sanitize();
        PreviewPbrConstants material;
        std::copy_n(m_PreviewMaterial.BaseColor, 4, material.BaseColor.begin());
        material.Material = {
            m_PreviewMaterial.Metallic, m_PreviewMaterial.Roughness,
            m_PreviewShadowSettings.LightRadius, m_PreviewAmbient};
        material.LightDirection = {
            lightDirection.x(), lightDirection.y(), lightDirection.z(), 0.0F};
        material.LightColor = {1.0F, 0.93F, 0.82F, m_PreviewLightIntensity};
        material.CameraPosition = {
            cameraPosition.x(), cameraPosition.y(), cameraPosition.z(), 1.0F};
        material.LightViewProjections = m_CascadeLightViewProjections;
        material.CascadeSplits = m_CascadeSplits;
        material.ShadowParameters = {
            m_PreviewShadowSettings.ConstantBias,
            m_PreviewShadowSettings.SlopeBias,
            m_PreviewShadowSettings.Enabled ? 1.0F : 0.0F,
            1.0F / static_cast<float>(m_PreviewShadowSettings.Resolution)};
        material.CascadeParameters = {
            0.10F, nearPlane,
            static_cast<float>(m_PreviewShadowSettings.CascadeCount),
            static_cast<float>(m_PreviewShadowSettings.Filter)};
        if (!m_Renderer->UpdateBuffer(
            m_PreviewMaterialBuffer, &material, sizeof(material), 0, error))
        {
            AppendLog(error);
            return;
        }
        m_PreviewContactShadowSettings.Sanitize();
        ToneMapConstants toneMapConstants;
        toneMapConstants.ToneParameters = {
            m_PreviewExposure,
            m_PreviewContactShadowSettings.Enabled ? m_PreviewContactShadowSettings.Strength : 0.0F,
            m_PreviewContactShadowSettings.MaxDistance,
            m_PreviewContactShadowSettings.Thickness};
        toneMapConstants.LightDirection = {
            lightDirection.x(), lightDirection.y(), lightDirection.z(),
            static_cast<float>(m_PreviewContactShadowSettings.StepCount)};
        toneMapConstants.ProjectionParameters = {
            projection(0, 0), projection(1, 1), nearPlane, farPlane};
        if (!m_Renderer->UpdateBuffer(
            m_ToneMapConstantBuffer, &toneMapConstants, sizeof(toneMapConstants), 0, error))
        {
            AppendLog(error);
            return;
        }
        m_PreviewDraw.ViewportX = 0.0F;
        m_PreviewDraw.ViewportY = 0.0F;
        m_PreviewDraw.ViewportWidth = static_cast<float>(targetWidth);
        m_PreviewDraw.ViewportHeight = static_cast<float>(targetHeight);
        m_GroundDraw.ViewportWidth = m_PreviewDraw.ViewportWidth;
        m_GroundDraw.ViewportHeight = m_PreviewDraw.ViewportHeight;
        m_ToneMapDraw.PixelTextures[0] = m_PreviewHdrColor;
        m_ToneMapDraw.PixelTextures[1] = m_PreviewDepth;
        if (!m_PreviewRenderGraph.Execute(error))
            AppendLog(error);
    }

    void EditorApplication::RenderMenuBar()
    {
        const ImGuiIO& io = ImGui::GetIO();
        const auto history = m_Scene.GetEditorState();
        const bool newShortcut = io.KeyCtrl && ImGui::IsKeyPressed(ImGuiKey_N, false);
        const bool openShortcut = io.KeyCtrl && ImGui::IsKeyPressed(ImGuiKey_O, false);
        const bool saveShortcut = io.KeyCtrl && ImGui::IsKeyPressed(ImGuiKey_S, false);
        const bool undoShortcut = io.KeyCtrl && ImGui::IsKeyPressed(ImGuiKey_Z, false);
        const bool redoShortcut = io.KeyCtrl && ImGui::IsKeyPressed(ImGuiKey_Y, false);
        const bool reloadShortcut = io.KeyCtrl && io.KeyShift && ImGui::IsKeyPressed(ImGuiKey_R, false);
        bool requestNew = newShortcut;
        bool requestOpen = openShortcut;
        bool requestSave = saveShortcut;
        bool requestUndo = undoShortcut;
        bool requestRedo = redoShortcut;
        bool requestReload = reloadShortcut;
        if (!ImGui::BeginMainMenuBar())
            return;
        if (ImGui::BeginMenu("File"))
        {
            ImGui::BeginDisabled(m_ScenePlaying);
            if (ImGui::MenuItem("New Scene", "Ctrl+N"))
                requestNew = true;
            if (ImGui::MenuItem("Open Scene", "Ctrl+O"))
                requestOpen = true;
            if (ImGui::MenuItem("Save Scene", "Ctrl+S"))
                requestSave = true;
            ImGui::EndDisabled();
            ImGui::Separator();
            if (ImGui::MenuItem("Exit"))
                m_Running = false;
            ImGui::EndMenu();
        }
        if (ImGui::BeginMenu("Edit"))
        {
            ImGui::BeginDisabled(m_ScenePlaying);
            const std::string undoLabel = history.UndoCount > 0
                ? "Undo " + history.UndoLabel : "Undo";
            const std::string redoLabel = history.RedoCount > 0
                ? "Redo " + history.RedoLabel : "Redo";
            if (ImGui::MenuItem(undoLabel.c_str(), "Ctrl+Z", false, history.UndoCount > 0))
                requestUndo = true;
            if (ImGui::MenuItem(redoLabel.c_str(), "Ctrl+Y", false, history.RedoCount > 0))
                requestRedo = true;
            ImGui::EndDisabled();
            ImGui::EndMenu();
        }
        if (ImGui::BeginMenu("Window"))
        {
            ImGui::MenuItem("Assets / Console", nullptr, &m_ShowAssets);
            ImGui::MenuItem("Action Animation Lab", nullptr, &m_ShowAnimationLab);
            ImGui::MenuItem("FBX Character Import", nullptr, &m_ShowFbxCharacter);
            ImGui::EndMenu();
        }
        if (ImGui::BeginMenu("AI"))
        {
            ImGui::MenuItem("Agent Workspace", nullptr, false, false);
            ImGui::TextDisabled("Capability registry ready");
            ImGui::EndMenu();
        }
        if (ImGui::BeginMenu("Gameplay"))
        {
            const bool loaded = m_GameplayRuntime && m_GameplayRuntime->IsStarted();
            ImGui::TextDisabled(loaded ? "C# host: READY" : "C# host: STOPPED");
            if (ImGui::MenuItem("Reload C# Assembly", "Ctrl+Shift+R", false, m_GameplayRuntime != nullptr))
                requestReload = true;
            ImGui::EndMenu();
        }
        ImGui::Separator();
        ImGui::TextColored(Color(166, 145, 255), "NcmaEngine");
        ImGui::TextDisabled("  C++20 | C# Gameplay | Python Modules | %s",
            Rhi::RenderBackendRegistry::ToString(m_BackendType).data());
        ImGui::EndMainMenuBar();
        if (m_ScenePlaying)
            requestNew = requestOpen = requestSave = requestUndo = requestRedo = false;
        if (requestNew)
            NewScene();
        if (requestOpen)
            OpenScene();
        if (requestSave)
            SaveScene();
        if (requestUndo)
            UndoSceneEdit();
        if (requestRedo)
            RedoSceneEdit();
        if (requestReload)
            ReloadGameplay();
    }

    void EditorApplication::RenderToolbar(const RECT& workArea)
    {
        const float height = 48.0F;
        ImGui::SetNextWindowPos({0.0F, static_cast<float>(workArea.top)});
        ImGui::SetNextWindowSize({static_cast<float>(workArea.right), height});
        ImGui::Begin("##Toolbar", nullptr,
            ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoResize |
            ImGuiWindowFlags_NoCollapse | ImGuiWindowFlags_NoBringToFrontOnFocus |
            ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoScrollWithMouse);
        ImGui::TextDisabled("SCENE");
        ImGui::SameLine();
        ImGui::Text("%s%s", m_Scene.GetName().c_str(), m_Scene.GetEditorState().Dirty ? " *" : "");
        ImGui::SameLine(ImGui::GetWindowWidth() * 0.43F);
        if (ImGui::Button(m_ScenePlaying ? "Stop" : "Play", {84.0F, 27.0F}))
        {
            TogglePlay();
        }
        ImGui::SameLine();
        ImGui::BeginDisabled(!m_ScenePlaying);
        if (ImGui::Button(m_ScenePaused ? "Resume" : "Pause", {74.0F, 27.0F}))
            m_ScenePaused = !m_ScenePaused;
        ImGui::EndDisabled();
        ImGui::SameLine(ImGui::GetWindowWidth() - 230.0F);
        ImGui::TextDisabled("Renderer");
        ImGui::SameLine();
        ImGui::TextColored(Color(116, 211, 162), "%s READY",
            Rhi::RenderBackendRegistry::ToString(m_BackendType).data());
        ImGui::End();
    }

    void EditorApplication::RenderSceneObjects(const RECT& workArea)
    {
        const float toolbarBottom = static_cast<float>(workArea.top) + 48.0F;
        const float statusHeight = 27.0F;
        const float bottomHeight = m_ShowAssets ? 225.0F : 0.0F;
        const float width = 265.0F;
        SetFixedWindow("Scene Objects", 0.0F, toolbarBottom, width,
            static_cast<float>(workArea.bottom) - toolbarBottom - bottomHeight - statusHeight);
        ImGui::BeginDisabled(m_ScenePlaying);
        if (ImGui::Button("+ GameObject")) CreateSceneObject();
        ImGui::SameLine();
        ImGui::BeginDisabled(m_ScenePlaying);
        if (ImGui::Button("Delete"))
            DeleteSelectedObject();
        ImGui::EndDisabled();
        ImGui::EndDisabled();
        ImGui::Separator();
        const auto objects = m_Scene.CaptureView();
        for (const auto& object : objects.Objects)
            RenderSceneObject(object.PersistentId);
        ImGui::End();
    }

    void EditorApplication::RenderSceneObject(SceneUuid object)
    {
        // An earlier row can commit a draft and invalidate all runtime handles. Resolve each row by UUID.
        const auto gameObject = m_Scene.FindObject(object);
        if (!m_Scene.Contains(gameObject)) return;
        ImGui::PushID(object.ToString().c_str());
        const auto label = m_Scene.GetObjectName(gameObject) + " [C#]";
        if (ImGui::Selectable(label.c_str(), gameObject == m_SelectedObject))
            SelectObject(gameObject);
        ImGui::PopID();
    }

    void EditorApplication::RenderViewport(const RECT& workArea)
    {
        const float leftWidth = 265.0F;
        const float rightWidth = 315.0F;
        const float toolbarBottom = static_cast<float>(workArea.top) + 48.0F;
        const float statusHeight = 27.0F;
        const float bottomHeight = m_ShowAssets ? 225.0F : 0.0F;
        const float width = static_cast<float>(workArea.right) - leftWidth - rightWidth;
        const float height = static_cast<float>(workArea.bottom) - toolbarBottom - bottomHeight - statusHeight;
        SetFixedWindow("Viewport", leftWidth, toolbarBottom, width, height);

        ImGui::TextDisabled("Perspective");
        ImGui::SameLine();
        ImGui::TextDisabled("|  Lit  |  PBR  |  Soft Shadows");
        ImGui::Separator();
        const ImVec2 topLeft = ImGui::GetCursorScreenPos();
        const ImVec2 size = ImGui::GetContentRegionAvail();
        m_ToneMapDraw.ViewportX = topLeft.x;
        m_ToneMapDraw.ViewportY = topLeft.y;
        m_ToneMapDraw.ViewportWidth = std::max(size.x, 1.0F);
        m_ToneMapDraw.ViewportHeight = std::max(size.y, 1.0F);
        m_PreviewDraw.ViewportWidth = m_ToneMapDraw.ViewportWidth;
        m_PreviewDraw.ViewportHeight = m_ToneMapDraw.ViewportHeight;
        ImDrawList* draw = ImGui::GetWindowDrawList();
        draw->AddRectFilled(topLeft, {topLeft.x + size.x, topLeft.y + size.y}, IM_COL32(18, 22, 30, 255));
        const float grid = 36.0F;
        for (float x = topLeft.x; x < topLeft.x + size.x; x += grid)
            draw->AddLine({x, topLeft.y}, {x, topLeft.y + size.y}, IM_COL32(42, 47, 59, 90));
        for (float y = topLeft.y; y < topLeft.y + size.y; y += grid)
            draw->AddLine({topLeft.x, y}, {topLeft.x + size.x, y}, IM_COL32(42, 47, 59, 90));

        const ImVec2 center{topLeft.x + size.x * 0.5F, topLeft.y + size.y * 0.53F};
        draw->AddLine({center.x, center.y}, {center.x + 115.0F, center.y}, IM_COL32(236, 86, 95, 255), 3.0F);
        draw->AddLine({center.x, center.y}, {center.x, center.y - 115.0F}, IM_COL32(91, 212, 125, 255), 3.0F);
        draw->AddLine({center.x, center.y}, {center.x - 62.0F, center.y + 72.0F}, IM_COL32(83, 142, 245, 255), 3.0F);
        const std::string label = m_SelectedObject == InvalidGameObjectId ? "No selection" : m_Scene.GetObjectName(m_SelectedObject);
        const char* text = label.c_str();
        draw->AddText({topLeft.x + 12.0F, topLeft.y + 10.0F}, IM_COL32(214, 219, 232, 255), text);
        ImGui::Dummy(size);
        ImGui::End();
    }

    void EditorApplication::RenderInspector(const RECT& workArea)
    {
        const float width = 315.0F;
        const float toolbarBottom = static_cast<float>(workArea.top) + 48.0F;
        const float statusHeight = 27.0F;
        const float bottomHeight = m_ShowAssets ? 225.0F : 0.0F;
        SetFixedWindow("Inspector", static_cast<float>(workArea.right) - width, toolbarBottom, width,
            static_cast<float>(workArea.bottom) - toolbarBottom - bottomHeight - statusHeight);
        if (m_SelectedObject == InvalidGameObjectId || !m_Scene.Contains(m_SelectedObject))
        {
            ImGui::TextDisabled("Select a scene gameObject to inspect it.");
            ImGui::End();
            return;
        }

        std::array<char, 1025> nameBuffer{};
        std::snprintf(nameBuffer.data(), nameBuffer.size(), "%s", (m_InspectorDraft ? m_InspectorDraft->Name : m_Scene.GetObjectName(m_SelectedObject)).c_str());
        ImGui::TextDisabled("GAME OBJECT");
        ImGui::TextDisabled("Gameplay: C#");
        ImGui::BeginDisabled(m_ScenePlaying || m_CancelledInspectorThisFrame);
        const bool nameChanged = ImGui::InputText("Name", nameBuffer.data(), nameBuffer.size());
        if (ImGui::IsItemActivated())
            BeginInspectorEdit("Rename GameObject");
        if (nameChanged)
        {
            if (!m_InspectorDraft) BeginInspectorEdit("Rename GameObject");
            m_InspectorDraft->Name = nameBuffer.data();
            try { m_Scene.PreviewName(m_InspectorDraft->Token, m_InspectorDraft->Object, m_InspectorDraft->Name); m_InspectorDraft->Valid = true; }
            catch (const std::exception& error) { m_InspectorDraft->Valid = false; AppendLog(std::string("Rename preview rejected: ") + error.what()); }
        }
        if (ImGui::IsItemDeactivated())
            CommitInspectorEdit("Rename GameObject");

        ImGui::SeparatorText("Transform");
        const bool hasTransform = m_Scene.HasTransform(m_SelectedObject);
        ImGui::BeginDisabled(!hasTransform);
        Transform transform = m_InspectorDraft ? m_InspectorDraft->LocalTransform : (hasTransform ? m_Scene.GetLocalTransform(m_SelectedObject) : Transform{});
        if (m_ScenePlaying && m_PlayScene)
        {
            const auto playObject = m_PlayScene->FindObject(m_Scene.GetPersistentId(m_SelectedObject));
            if (m_PlayScene->Contains(playObject) && m_PlayScene->HasTransform(playObject))
            {
                const auto& live = m_PlayScene->GetLocalTransform(playObject);
                ImGui::TextDisabled("Live rotation: %.3f %.3f %.3f %.3f",
                    live.Rotation.x(), live.Rotation.y(), live.Rotation.z(), live.Rotation.w());
            }
        }
        float position[]{transform.Position.x(), transform.Position.y(), transform.Position.z()};
        float rotation[]{transform.Rotation.x(), transform.Rotation.y(), transform.Rotation.z(), transform.Rotation.w()};
        float scale[]{transform.Scale.x(), transform.Scale.y(), transform.Scale.z()};
        const bool positionChanged = ImGui::DragFloat3("Position", position, 0.05F);
        if (ImGui::IsItemActivated())
            BeginInspectorEdit("Move GameObject");
        if (positionChanged)
        {
            transform.Position = {position[0], position[1], position[2]};
            if (!m_InspectorDraft) BeginInspectorEdit("Edit Transform");
            m_InspectorDraft->LocalTransform = transform;
            try { m_Scene.PreviewTransform(m_InspectorDraft->Token, m_InspectorDraft->Object, transform); m_InspectorDraft->Valid = true; }
            catch (const std::exception& error) { m_InspectorDraft->Valid = false; AppendLog(error.what()); }
        }
        if (ImGui::IsItemDeactivated())
            CommitInspectorEdit("Move GameObject");
        const bool rotationChanged = ImGui::DragFloat4("Rotation", rotation, 0.01F);
        if (ImGui::IsItemActivated())
            BeginInspectorEdit("Rotate GameObject");
        if (rotationChanged)
        {
            const Quaternion proposed(rotation[3], rotation[0], rotation[1], rotation[2]);
            if (proposed.squaredNorm() > 0.000001F)
            {
                transform.Rotation = proposed.normalized();
                if (!m_InspectorDraft) BeginInspectorEdit("Edit Transform");
            m_InspectorDraft->LocalTransform = transform;
            try { m_Scene.PreviewTransform(m_InspectorDraft->Token, m_InspectorDraft->Object, transform); m_InspectorDraft->Valid = true; }
            catch (const std::exception& error) { m_InspectorDraft->Valid = false; AppendLog(error.what()); }
            }
            else if (m_InspectorDraft) m_InspectorDraft->Valid = false;
        }
        if (ImGui::IsItemDeactivated())
            CommitInspectorEdit("Rotate GameObject");
        const bool scaleChanged = ImGui::DragFloat3("Scale", scale, 0.02F, 0.001F, 100.0F);
        if (ImGui::IsItemActivated())
            BeginInspectorEdit("Scale GameObject");
        if (scaleChanged)
        {
            transform.Scale = {scale[0], scale[1], scale[2]};
            if (!m_InspectorDraft) BeginInspectorEdit("Edit Transform");
            m_InspectorDraft->LocalTransform = transform;
            try { m_Scene.PreviewTransform(m_InspectorDraft->Token, m_InspectorDraft->Object, transform); m_InspectorDraft->Valid = true; }
            catch (const std::exception& error) { m_InspectorDraft->Valid = false; AppendLog(error.what()); }
        }
        if (ImGui::IsItemDeactivated())
            CommitInspectorEdit("Scale GameObject");
        ImGui::EndDisabled();
        if (hasTransform && ImGui::SmallButton("Remove Transform"))
            ExecuteSceneMutation("Remove Transform", [&] { m_Scene.RemoveEditorComponent(m_Scene.GetPersistentId(m_SelectedObject), "ncma.transform"); });
        if (!hasTransform && ImGui::SmallButton("Add Transform"))
            ExecuteSceneMutation("Add Transform", [&] { m_Scene.SetEditorTransform(m_Scene.GetPersistentId(m_SelectedObject), Transform{}); });
        ImGui::EndDisabled();
        RenderBehaviourInspector();

        ImGui::SeparatorText("PBR Preview Material");
        ImGui::ColorEdit3("Base Color", m_PreviewMaterial.BaseColor);
        ImGui::SliderFloat("Metallic", &m_PreviewMaterial.Metallic, 0.0F, 1.0F);
        ImGui::SliderFloat("Roughness", &m_PreviewMaterial.Roughness, 0.04F, 1.0F);
        ImGui::SliderFloat("Light Intensity", &m_PreviewLightIntensity, 0.0F, 20.0F, "%.2f");
        ImGui::SliderFloat("Exposure", &m_PreviewExposure, 0.1F, 5.0F, "%.2f");
        ImGui::SliderFloat("Ambient", &m_PreviewAmbient, 0.0F, 0.25F, "%.3f");

        ImGui::SeparatorText("Directional Shadows");
        ImGui::Checkbox("Enabled", &m_PreviewShadowSettings.Enabled);
        ImGui::SliderFloat(
            "Constant Bias", &m_PreviewShadowSettings.ConstantBias, 0.0F, 0.01F, "%.5f");
        ImGui::SliderFloat(
            "Slope Bias", &m_PreviewShadowSettings.SlopeBias, 0.0F, 8.0F, "%.2f");
        ImGui::SliderFloat(
            "Shadow Distance", &m_PreviewShadowSettings.MaxDistance, 5.0F, 100.0F, "%.1f");
        ImGui::SliderFloat(
            "Cascade Lambda", &m_PreviewShadowSettings.CascadeLambda, 0.0F, 1.0F, "%.2f");
        int shadowFilter = static_cast<int>(m_PreviewShadowSettings.Filter);
        if (ImGui::Combo("Filter", &shadowFilter, "PCF 3x3\0PCF 5x5\0PCSS\0"))
            m_PreviewShadowSettings.Filter = static_cast<Rendering::ShadowFilter>(shadowFilter);
        if (m_PreviewShadowSettings.Filter == Rendering::ShadowFilter::Pcss)
            ImGui::SliderFloat(
                "Light Angular Radius", &m_PreviewShadowSettings.LightRadius,
                0.001F, 0.20F, "%.3f");
        ImGui::TextDisabled(
            "4 cascades | 2048 each | %u taps | 10%% blend",
            Rendering::ShadowSampleCount(m_PreviewShadowSettings.Filter));

        ImGui::SeparatorText("Contact Shadows");
        ImGui::Checkbox("Contact Enabled", &m_PreviewContactShadowSettings.Enabled);
        int contactSteps = static_cast<int>(m_PreviewContactShadowSettings.StepCount);
        if (ImGui::SliderInt("Contact Steps", &contactSteps, 4, 32))
            m_PreviewContactShadowSettings.StepCount = static_cast<std::uint32_t>(contactSteps);
        ImGui::SliderFloat(
            "Contact Distance", &m_PreviewContactShadowSettings.MaxDistance,
            0.05F, 3.0F, "%.2f");
        ImGui::SliderFloat(
            "Contact Thickness", &m_PreviewContactShadowSettings.Thickness,
            0.005F, 0.30F, "%.3f");
        ImGui::SliderFloat(
            "Contact Strength", &m_PreviewContactShadowSettings.Strength,
            0.0F, 1.0F, "%.2f");

        ImGui::End();
    }

    void EditorApplication::RenderBehaviourInspector()
    {
        ImGui::SeparatorText("C# Behaviours");
        ImGui::BeginDisabled(m_ScenePlaying || m_CancelledInspectorThisFrame);
        std::vector<const Scripting::BehaviourDescriptor*> types;
        if (m_GameplayRuntime && m_GameplayRuntime->IsStarted())
            for (const auto& type : m_GameplayRuntime->GetTypes())
                types.push_back(&type);
        const bool ready = !types.empty();
        ImGui::BeginDisabled(!ready);
        if (ImGui::Button("+ Add Behaviour", {-1.0F, 26.0F})) ImGui::OpenPopup("AddBehaviour");
        if (ImGui::BeginPopup("AddBehaviour"))
        {
            for (const auto* type : types)
                if (ImGui::MenuItem((type->TypeName + " [C#]").c_str()))
                    ExecuteSceneMutation("Attach Behaviour", [&] {
                        auto values = m_Scene.GetBehaviours(m_SelectedObject);
                        values.push_back(type->CreateBinding());
                        m_Scene.SetEditorBindings(m_Scene.GetPersistentId(m_SelectedObject), values, "Attach Behaviour");
                    });
            ImGui::EndPopup();
        }
        ImGui::EndDisabled();
        if (!ready) ImGui::TextDisabled("Load C# types from the Gameplay menu.");
        auto bindings = m_InspectorDraft ? m_InspectorDraft->Bindings : m_Scene.GetBehaviours(m_SelectedObject);
        for (std::size_t index = 0; index < bindings.size(); ++index)
        {
            auto& binding = bindings[index];
            ImGui::PushID(binding.Id.ToString().c_str());
            ImGui::Separator();
            ImGui::TextWrapped("%s", binding.TypeName.c_str());
            ImGui::TextDisabled("C#");
            bool enabled = binding.Enabled;
            if (ImGui::Checkbox("Enabled", &enabled))
                ExecuteSceneMutation("Enable Behaviour", [&] {
                    binding.Enabled = enabled;
                    m_Scene.SetEditorBindings(m_Scene.GetPersistentId(m_SelectedObject), bindings, "Edit C# Behaviour");
                });
            ImGui::SameLine();
            if (ImGui::SmallButton("Remove"))
            {
                ExecuteSceneMutation("Remove Behaviour", [&] {
                    const auto removed = binding.Id;
                    std::erase_if(bindings, [&](const auto& value) { return value.Id == removed; });
                    m_Scene.SetEditorBindings(m_Scene.GetPersistentId(m_SelectedObject), bindings, "Remove Behaviour");
                });
                ImGui::PopID();
                break;
            }
            const Scripting::BehaviourDescriptor* descriptor = nullptr;
            if (ready)
                for (const auto* type : types)
                    if (type->TypeName == binding.TypeName) descriptor = type;
            if (!descriptor) ImGui::TextDisabled("Missing script (saved settings retained)");
            if (descriptor)
            {
                const bool schemaMatches = binding.Properties.size() == descriptor->Properties.size() &&
                    std::all_of(descriptor->Properties.begin(), descriptor->Properties.end(), [&](const auto& metadata) {
                        return std::any_of(binding.Properties.begin(), binding.Properties.end(), [&](const auto& value) {
                            return value.Name == metadata.Default.Name && value.Kind == metadata.Default.Kind;
                        });
                    });
                if (!schemaMatches && ImGui::SmallButton("Update Export Schema"))
                    ExecuteSceneMutation("Update Export Schema", [&] {
                        std::vector<ExportValue> refreshed;
                        for (const auto& metadata : descriptor->Properties)
                        {
                            const auto old = std::find_if(binding.Properties.begin(), binding.Properties.end(), [&](const auto& value) {
                                return value.Name == metadata.Default.Name && value.Kind == metadata.Default.Kind;
                            });
                            refreshed.push_back(old == binding.Properties.end() ? metadata.Default : *old);
                        }
                        binding.Properties = std::move(refreshed);
                        m_Scene.SetEditorBindings(m_Scene.GetPersistentId(m_SelectedObject), bindings, "Edit C# Behaviour");
                    });
            }
            for (auto& property : binding.Properties)
            {
                ImGui::PushID(property.Name.c_str());
                std::string label = property.Name;
                if (descriptor)
                    for (const auto& metadata : descriptor->Properties)
                        if (metadata.Default.Name == property.Name)
                            label = metadata.Category + ": " + metadata.DisplayName;
                double value = property.Value;
                bool changed = false;
                if (property.Kind == ExportKind::Boolean)
                {
                    bool boolean = value != 0;
                    changed = ImGui::Checkbox(label.c_str(), &boolean);
                    value = boolean ? 1.0 : 0.0;
                }
                else if (property.Kind == ExportKind::Integer)
                {
                    int integer = static_cast<int>(value);
                    changed = ImGui::DragInt(label.c_str(), &integer);
                    value = integer;
                }
                else
                    changed = ImGui::DragScalar(label.c_str(), ImGuiDataType_Double, &value, 0.1F);
                if (ImGui::IsItemActivated()) BeginInspectorEdit("Edit Export Property");
                if (changed)
                {
                    if (!m_InspectorDraft) BeginInspectorEdit("Edit Export Property");
                    m_InspectorDraft->Valid = ValidExportValue({property.Name, property.Kind, value});
                    if (m_InspectorDraft->Valid)
                    {
                        property.Value = value;
                        m_InspectorDraft->Bindings = bindings;
                        try { m_Scene.PreviewBindings(m_InspectorDraft->Token, m_InspectorDraft->Object, bindings); }
                        catch (const std::exception& error) { m_InspectorDraft->Valid = false; AppendLog(error.what()); }
                    }
                }
                if (ImGui::IsItemDeactivated() || (changed && property.Kind == ExportKind::Boolean))
                    CommitInspectorEdit("Edit Export Property");
                ImGui::PopID();
            }
            ImGui::PopID();
        }
        ImGui::EndDisabled();
    }

    void EditorApplication::RenderBottomPanel(const RECT& workArea)
    {
        if (!m_ShowAssets)
            return;
        const float leftWidth = 265.0F;
        const float statusHeight = 27.0F;
        const float height = 225.0F;
        SetFixedWindow("Project", 0.0F, static_cast<float>(workArea.bottom) - height - statusHeight,
            static_cast<float>(workArea.right), height);
        if (ImGui::BeginTabBar("ProjectTabs"))
        {
            if (ImGui::BeginTabItem("Assets"))
            {
                constexpr std::array assets{
                    "Scenes", "Materials", "Meshes", "Textures", "Animations", "UI", "Scripts", "Python Tools"
                };
                for (const char* asset : assets)
                {
                    ImGui::BeginChild(asset, {132.0F, 78.0F}, ImGuiChildFlags_Borders);
                    ImGui::TextColored(Color(149, 132, 255), "[ %s ]", asset);
                    ImGui::TextDisabled("Asset group");
                    ImGui::EndChild();
                    ImGui::SameLine();
                }
                ImGui::EndTabItem();
            }
            if (ImGui::BeginTabItem("Console"))
            {
                for (const auto& log : m_Logs)
                    ImGui::TextUnformatted(log.c_str());
                ImGui::EndTabItem();
            }
            if (ImGui::BeginTabItem("Animation Graph"))
            {
                ImGui::TextUnformatted("Action animation: skeleton poses, crossfades, root motion and gameplay notifies.");
                if (ImGui::Button("Open Action Animation Lab")) m_ShowAnimationLab = true;
                ImGui::SameLine();
                if (ImGui::Button("Import FBX Character")) m_ShowFbxCharacter = true;
                ImGui::TextDisabled("Procedural rig preview. Imported meshes and visual graph authoring are not yet available.");
                ImGui::EndTabItem();
            }
            ImGui::EndTabBar();
        }
        ImGui::End();
        (void)leftWidth;
    }

    void EditorApplication::RenderStatusBar(const RECT& workArea)
    {
        constexpr float height = 27.0F;
        SetFixedWindow("##StatusBar", 0.0F, static_cast<float>(workArea.bottom) - height,
            static_cast<float>(workArea.right), height);
        ImGui::TextColored(Color(105, 210, 149), m_Scene.GetEditorState().Dirty ? "Modified" : "Saved");
        ImGui::SameLine();
        ImGui::TextDisabled("| Objects: %zu | Backend: %s | Agent capabilities: schema-ready",
            m_Scene.Size(), Rhi::RenderBackendRegistry::ToString(m_BackendType).data());
        ImGui::End();
    }

    void EditorApplication::SelectObject(GameObjectId gameObject)
    {
        const auto selected = m_Scene.Contains(gameObject) ? std::optional<SceneUuid>(m_Scene.GetPersistentId(gameObject)) : std::nullopt;
        CommitInspectorEdit("Edit GameObject");
        m_Scene.SelectEditorObject(selected);
        RefreshEditorSelection();
    }

    void EditorApplication::CreateSceneObject()
    {
        CommitInspectorEdit("Edit GameObject");
        try { m_SelectedObject = m_Scene.CreateEditorObject("New GameObject"); AppendLog("Created C# GameObject"); }
        catch (const std::exception& error) { AppendLog(error.what()); }
    }
    void EditorApplication::DeleteSelectedObject()
    {
        CommitInspectorEdit("Edit GameObject");
        if (!m_Scene.Contains(m_SelectedObject)) return;
        try { m_Scene.DeleteEditorObject(m_Scene.GetPersistentId(m_SelectedObject)); RefreshEditorSelection(); }
        catch (const std::exception& error) { AppendLog(error.what()); }
    }
    void EditorApplication::NewScene()
    {
        CommitInspectorEdit("Edit GameObject");
        try { m_Scene.NewEditorDocument(); RefreshEditorSelection(); AppendLog("New scene"); }
        catch (const std::exception& error) { AppendLog(error.what()); }
    }
    void EditorApplication::OpenScene()
    {
        CommitInspectorEdit("Edit GameObject");
        try { m_Scene.OpenEditorDocument(m_ScenePath); RefreshEditorSelection(); AppendLog("Opened scene"); }
        catch (const std::exception& error) { AppendLog(error.what()); }
    }
    void EditorApplication::SaveScene()
    {
        CommitInspectorEdit("Edit GameObject");
        try
        {
            const auto path = m_Scene.GetEditorState().FilePath;
            if (path.empty()) m_Scene.SaveEditorDocument(m_ScenePath);
            else m_Scene.SaveEditorDocument();
            AppendLog("Saved scene");
        }
        catch (const std::exception& error) { AppendLog(error.what()); }
    }
    void EditorApplication::UndoSceneEdit()
    {
        CommitInspectorEdit("Edit GameObject");
        std::string error;
        if (!m_Scene.UndoEditor(false, error)) { if (!error.empty()) AppendLog(error); return; }
        RefreshEditorSelection(); AppendLog("Undid scene command");
    }
    void EditorApplication::RedoSceneEdit()
    {
        CommitInspectorEdit("Edit GameObject");
        std::string error;
        if (!m_Scene.UndoEditor(true, error)) { if (!error.empty()) AppendLog(error); return; }
        RefreshEditorSelection(); AppendLog("Redid scene command");
    }

    void EditorApplication::TogglePlay()
    {
        CommitInspectorEdit("Edit GameObject");
        if (m_ScenePlaying)
        {
            StopGameplay();
            AppendLog("Stopped gameplay; edit scene preserved");
            return;
        }
        std::string error;
        auto playScene = std::make_unique<ManagedSceneClient>();
        const auto snapshot = m_Scene.CaptureDocument();
        const auto view = m_Scene.CaptureView();
        bool hasCSharp = false;
        for (const auto& gameObject : view.Objects)
            hasCSharp = hasCSharp || !gameObject.Behaviours.empty();
        if (!playScene->RestoreDocument(snapshot, error) ||
            (hasCSharp && !m_GameplayRuntime->BindScene(*playScene, error)))
        {
            if (m_GameplayRuntime) m_GameplayRuntime->EndScene();
                AppendLog("Could not enter play mode: " + error);
            return;
        }
        m_Scene.FreezeEditing(true);
        m_PlayScene = std::move(playScene);
        m_ScenePlaying = true;
        m_ScenePaused = false;
        AppendLog(std::format("Playing {} C# Behaviour(s)", m_GameplayRuntime->GetBehaviourCount()));
    }

    void EditorApplication::StopGameplay()
    {
        if (m_GameplayRuntime) m_GameplayRuntime->EndScene();
        m_PlayScene.reset();
        if (m_ScenePlaying) m_Scene.FreezeEditing(false);
        m_ScenePlaying = false;
        m_ScenePaused = false;
    }

    void EditorApplication::ReloadGameplay()
    {
        if (!m_GameplayRuntime)
            return;
        std::string error;
        if (m_GameplayRuntime->Reload(error))
        {
            if (m_ScenePlaying && m_PlayScene && !m_GameplayRuntime->BindScene(*m_PlayScene, error))
            {
                AppendLog("C# rebind failed: " + error);
                StopGameplay();
                return;
            }
            AppendLog(std::format(
                "Reloaded C# gameplay: {} types", m_GameplayRuntime->GetTypes().size()));
        }
        else
        {
            AppendLog("C# gameplay reload failed: " + error);
            StopGameplay();
        }
    }

    void EditorApplication::ExecuteSceneMutation(const std::string& label, const std::function<void()>& mutation)
    {
        CommitInspectorEdit("Edit GameObject");
        try { mutation(); RefreshEditorSelection(); AppendLog(label); }
        catch (const std::exception& error) { AppendLog(error.what()); }
    }
    void EditorApplication::RestoreSelection(const std::optional<SceneUuid>& selection)
    {
        m_SelectedObject = selection ? m_Scene.FindObject(*selection) : InvalidGameObjectId;
    }
    void EditorApplication::RefreshEditorSelection() { RestoreSelection(m_Scene.GetEditorState().Selection); }
    void EditorApplication::BeginInspectorEdit(std::string label)
    {
        CommitInspectorEdit("Edit GameObject");
        if (!m_Scene.Contains(m_SelectedObject)) return;
        InspectorDraft draft;
        draft.Object = m_Scene.GetPersistentId(m_SelectedObject);
        draft.Token = m_Scene.BeginInteraction(draft.Object, std::move(label));
        draft.Name = m_Scene.GetObjectName(m_SelectedObject);
        draft.Bindings = m_Scene.GetBehaviours(m_SelectedObject);
        draft.HasTransform = m_Scene.HasTransform(m_SelectedObject);
        if (draft.HasTransform) draft.LocalTransform = m_Scene.GetLocalTransform(m_SelectedObject);
        m_InspectorDraft = std::move(draft);
    }
    void EditorApplication::CommitInspectorEdit(const std::string&)
    {
        if (!m_InspectorDraft) return;
        const auto draft = std::move(*m_InspectorDraft); m_InspectorDraft.reset();
        try
        {
            if (draft.Valid) m_Scene.CommitInteraction(draft.Token);
            else m_Scene.CancelInteraction(draft.Token);
            RefreshEditorSelection();
        }
        catch (const std::exception& error) { AppendLog(error.what()); }
    }
    void EditorApplication::CancelInspectorEdit()
    {
        if (!m_InspectorDraft) return;
        const auto token = m_InspectorDraft->Token; m_InspectorDraft.reset();
        try { m_Scene.CancelInteraction(token); } catch (const std::exception& error) { AppendLog(error.what()); }
    }

    void EditorApplication::AppendLog(std::string message)
    {
        if (Log::IsInitialized())
            ENGINE_LOG_INFO("{}", message);
        m_Logs.push_back(std::move(message));
        if (m_Logs.size() > 200)
            m_Logs.erase(m_Logs.begin());
    }

}
