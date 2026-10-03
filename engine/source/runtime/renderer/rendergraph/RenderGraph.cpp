#include "renderer/rendergraph/RenderGraph.h"

#include <algorithm>
#include <format>
#include <unordered_map>
#include <unordered_set>

namespace NcmaEngine::Rendering
{
    std::uint32_t RenderGraph::AddPass(
        RenderGraphPassDescription description, ExecuteCallback execute)
    {
        const std::uint32_t id = static_cast<std::uint32_t>(m_Passes.size());
        m_Passes.push_back({std::move(description), std::move(execute)});
        m_ExecutionOrder.clear();
        return id;
    }

    bool RenderGraph::Compile(std::string& error)
    {
        m_ExecutionOrder.clear();
        std::unordered_set<std::string> passNames;
        std::unordered_map<std::string, std::uint32_t> lastWriter;
        std::vector<std::vector<std::uint32_t>> outgoing(m_Passes.size());
        std::vector<std::uint32_t> incoming(m_Passes.size(), 0);

        for (std::uint32_t passIndex = 0; passIndex < m_Passes.size(); ++passIndex)
        {
            const Pass& pass = m_Passes[passIndex];
            if (pass.Description.Name.empty() || !pass.Execute)
            {
                error = "Render-graph passes require a stable name and execute callback";
                return false;
            }
            if (!passNames.insert(pass.Description.Name).second)
            {
                error = std::format("Render graph contains duplicate pass name '{}'", pass.Description.Name);
                return false;
            }

            std::unordered_set<std::uint32_t> dependencies;
            const auto consumeResource = [&](const std::string& resource) -> bool {
                if (resource.empty())
                {
                    error = std::format("Render pass '{}' contains an empty resource name", pass.Description.Name);
                    return false;
                }
                const auto writer = lastWriter.find(resource);
                if (writer != lastWriter.end())
                    dependencies.insert(writer->second);
                return true;
            };
            for (const std::string& resource : pass.Description.Reads)
            {
                if (!consumeResource(resource))
                    return false;
            }
            for (const std::string& resource : pass.Description.Writes)
            {
                if (!consumeResource(resource))
                    return false;
                lastWriter[resource] = passIndex;
            }
            for (const std::uint32_t dependency : dependencies)
            {
                outgoing[dependency].push_back(passIndex);
                ++incoming[passIndex];
            }
        }

        std::vector<std::uint32_t> ready;
        for (std::uint32_t passIndex = 0; passIndex < incoming.size(); ++passIndex)
        {
            if (incoming[passIndex] == 0)
                ready.push_back(passIndex);
        }
        while (!ready.empty())
        {
            const std::uint32_t passIndex = ready.front();
            ready.erase(ready.begin());
            m_ExecutionOrder.push_back(passIndex);
            for (const std::uint32_t dependent : outgoing[passIndex])
            {
                if (--incoming[dependent] == 0)
                {
                    const auto position = std::lower_bound(ready.begin(), ready.end(), dependent);
                    ready.insert(position, dependent);
                }
            }
        }
        if (m_ExecutionOrder.size() != m_Passes.size())
        {
            error = "Render graph contains a cyclic resource dependency";
            m_ExecutionOrder.clear();
            return false;
        }
        error.clear();
        return true;
    }

    bool RenderGraph::Execute(std::string& error) const
    {
        if (m_ExecutionOrder.size() != m_Passes.size())
        {
            error = "Render graph must be compiled before execution";
            return false;
        }
        for (const std::uint32_t passIndex : m_ExecutionOrder)
        {
            if (!m_Passes[passIndex].Execute(error))
            {
                error = std::format("Render pass '{}' failed: {}", m_Passes[passIndex].Description.Name, error);
                return false;
            }
        }
        error.clear();
        return true;
    }

    void RenderGraph::Reset() noexcept
    {
        m_Passes.clear();
        m_ExecutionOrder.clear();
    }

    std::vector<std::string_view> RenderGraph::GetExecutionOrder() const
    {
        std::vector<std::string_view> result;
        result.reserve(m_ExecutionOrder.size());
        for (const std::uint32_t passIndex : m_ExecutionOrder)
            result.push_back(m_Passes[passIndex].Description.Name);
        return result;
    }
}
