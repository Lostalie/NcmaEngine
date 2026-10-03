#pragma once

#include <functional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace NcmaEngine::AI
{
    enum class MutationRisk
    {
        ReadOnly,
        Reversible,
        Destructive
    };

    struct AgentCapability final
    {
        std::string Name;
        std::string Description;
        std::string InputSchemaJson;
        std::string OutputSchemaJson;
        MutationRisk Risk = MutationRisk::ReadOnly;
        bool RequiresEditor = false;
    };

    class AgentCapabilityRegistry final
    {
    public:
        void Register(AgentCapability capability);
        [[nodiscard]] const AgentCapability* Find(std::string_view name) const;
        [[nodiscard]] std::vector<AgentCapability> List() const;
        [[nodiscard]] std::string ExportManifestJson() const;

    private:
        std::unordered_map<std::string, AgentCapability> m_Capabilities;
    };
}

