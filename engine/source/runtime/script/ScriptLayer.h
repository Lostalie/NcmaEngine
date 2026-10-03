#pragma once

#include "application\Layer.h"
#include "script\ScriptComponent.h"
#include <vector>
#include <unordered_set>

namespace NcmaEngine
{
    /**
     * @brief Script layer - updates all script components each frame.
     *
     * This layer should be added to the application layer stack.
     * It calls OnUpdate on all active ScriptComponents.
     */
    class ScriptLayer : public Layer
    {
    public:
        ScriptLayer();
        virtual ~ScriptLayer();

        // Layer interface
        void OnAttach() override;
        void OnDetach() override;
        void OnUpdate(float deltaTime) override;
        void OnLateUpdate(float deltaTime);
        void OnEvent(Event& event) override;

        // Register a script component for updates
        void RegisterScript(ScriptComponent* script);

        // Unregister a script component
        void UnregisterScript(ScriptComponent* script);

        // Register/unregister entity (for automatic script discovery)
        void OnEntityCreated(Entity entity);
        void OnEntityDestroyed(Entity entity);

    private:
        std::vector<ScriptComponent*> m_ActiveScripts;
        std::unordered_set<EntityID> m_TrackedEntities;
        bool m_NeedsRefresh = false;

        void RefreshScriptList();
    };

} // namespace NcmaEngine