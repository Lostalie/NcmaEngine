#include "ScriptLayer.h"
#include "ScriptModule.h"
#include "Core.h"
#include "core\log\Log.h"
#include "scene\Scene.h"

namespace NcmaEngine
{
    ScriptLayer::ScriptLayer()
        : Layer("ScriptLayer")
    {
    }

    ScriptLayer::~ScriptLayer()
    {
    }

    void ScriptLayer::OnAttach()
    {
        ENGINE_LOG_INFO("ScriptLayer attached");
    }

    void ScriptLayer::OnDetach()
    {
        ENGINE_LOG_INFO("ScriptLayer detached");
        m_ActiveScripts.clear();
        m_TrackedEntities.clear();
    }

    void ScriptLayer::OnUpdate(float deltaTime)
    {
        // Refresh if needed (after entity creation/destruction)
        if (m_NeedsRefresh)
        {
            RefreshScriptList();
            m_NeedsRefresh = false;
        }

        // Call OnStart on first frame for each script
        static bool firstFrame = true;
        if (firstFrame)
        {
            for (auto* script : m_ActiveScripts)
            {
                if (script->IsEnabled() && script->HasScript())
                {
                    auto* instance = script->GetScript();
                    if (instance)
                    {
                        instance->OnAwake();
                        instance->OnStart();
                    }
                }
            }
            firstFrame = false;
        }

        // Update all enabled scripts
        for (auto* script : m_ActiveScripts)
        {
            if (script->IsEnabled() && script->HasScript())
            {
                auto* instance = script->GetScript();
                if (instance)
                {
                    instance->OnUpdate(deltaTime);
                }
            }
        }
    }

    void ScriptLayer::OnLateUpdate(float deltaTime)
    {
        for (auto* script : m_ActiveScripts)
        {
            if (script->IsEnabled() && script->HasScript())
            {
                auto* instance = script->GetScript();
                if (instance)
                {
                    instance->OnLateUpdate(deltaTime);
                }
            }
        }
    }

    void ScriptLayer::OnEvent(Event& event)
    {
        // TODO: Route events to scripts that listen for them
    }

    void ScriptLayer::RegisterScript(ScriptComponent* script)
    {
        if (script == nullptr)
            return;

        // Check if already registered
        auto it = std::find(m_ActiveScripts.begin(), m_ActiveScripts.end(), script);
        if (it != m_ActiveScripts.end())
            return;

        m_ActiveScripts.push_back(script);
        ENGINE_LOG_INFO("Script registered: {}", script->GetScriptPath());
    }

    void ScriptLayer::UnregisterScript(ScriptComponent* script)
    {
        if (script == nullptr)
            return;

        auto it = std::find(m_ActiveScripts.begin(), m_ActiveScripts.end(), script);
        if (it != m_ActiveScripts.end())
        {
            m_ActiveScripts.erase(it);
        }
    }

    void ScriptLayer::OnEntityCreated(Entity entity)
    {
        if (!entity.IsValid())
            return;

        m_TrackedEntities.insert(entity.GetID());
        m_NeedsRefresh = true;
    }

    void ScriptLayer::OnEntityDestroyed(Entity entity)
    {
        if (!entity.IsValid())
            return;

        m_TrackedEntities.erase(entity.GetID());
        m_NeedsRefresh = true;
    }

    void ScriptLayer::RefreshScriptList()
    {
        // Re-scan all tracked entities for ScriptComponents
        // This is called after entity creation/destruction
        // In a full implementation, we'd iterate through the scene

        // For now, we rely on direct registration via RegisterScript
        // and components are already in the list
    }

} // namespace NcmaEngine