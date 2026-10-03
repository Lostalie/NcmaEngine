#include "ai/AgentCapabilityRegistry.h"

#include <algorithm>
#include <stdexcept>

namespace NcmaEngine::AI
{
    namespace
    {
        std::string EscapeJson(std::string_view value)
        {
            std::string result;
            result.reserve(value.size());
            for (const char ch : value)
            {
                switch (ch)
                {
                case '\\': result += "\\\\"; break;
                case '"': result += "\\\""; break;
                case '\n': result += "\\n"; break;
                case '\r': result += "\\r"; break;
                case '\t': result += "\\t"; break;
                default: result += ch; break;
                }
            }
            return result;
        }

        std::string_view ToString(MutationRisk risk)
        {
            switch (risk)
            {
            case MutationRisk::ReadOnly: return "read_only";
            case MutationRisk::Reversible: return "reversible";
            case MutationRisk::Destructive: return "destructive";
            }
            return "unknown";
        }
    }

    void AgentCapabilityRegistry::Register(AgentCapability capability)
    {
        if (capability.Name.empty())
            throw std::invalid_argument("Agent capability name cannot be empty");
        if (capability.InputSchemaJson.empty())
            capability.InputSchemaJson = "{}";
        if (capability.OutputSchemaJson.empty())
            capability.OutputSchemaJson = "{}";
        m_Capabilities[capability.Name] = std::move(capability);
    }

    const AgentCapability* AgentCapabilityRegistry::Find(std::string_view name) const
    {
        const auto it = m_Capabilities.find(std::string(name));
        return it == m_Capabilities.end() ? nullptr : &it->second;
    }

    std::vector<AgentCapability> AgentCapabilityRegistry::List() const
    {
        std::vector<AgentCapability> result;
        result.reserve(m_Capabilities.size());
        for (const auto& [name, capability] : m_Capabilities)
        {
            (void)name;
            result.push_back(capability);
        }
        std::sort(result.begin(), result.end(), [](const auto& lhs, const auto& rhs) {
            return lhs.Name < rhs.Name;
        });
        return result;
    }

    std::string AgentCapabilityRegistry::ExportManifestJson() const
    {
        const auto capabilities = List();
        std::string json = "{\"schema_version\":1,\"capabilities\":[";
        for (std::size_t index = 0; index < capabilities.size(); ++index)
        {
            const auto& capability = capabilities[index];
            if (index != 0)
                json += ',';
            json += "{\"name\":\"" + EscapeJson(capability.Name) + "\",";
            json += "\"description\":\"" + EscapeJson(capability.Description) + "\",";
            json += "\"risk\":\"" + std::string(ToString(capability.Risk)) + "\",";
            json += "\"requires_editor\":" + std::string(capability.RequiresEditor ? "true" : "false") + ',';
            json += "\"input_schema\":" + capability.InputSchemaJson + ',';
            json += "\"output_schema\":" + capability.OutputSchemaJson + '}';
        }
        json += "]}";
        return json;
    }
}
