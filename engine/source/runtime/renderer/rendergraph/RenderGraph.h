#pragma once

#include <cstdint>
#include <functional>
#include <string>
#include <string_view>
#include <vector>

namespace NcmaEngine::Rendering
{
    struct RenderGraphPassDescription final
    {
        std::string Name;
        std::vector<std::string> Reads;
        std::vector<std::string> Writes;
    };

    class RenderGraph final
    {
    public:
        using ExecuteCallback = std::function<bool(std::string&)>;

        std::uint32_t AddPass(RenderGraphPassDescription description, ExecuteCallback execute);
        [[nodiscard]] bool Compile(std::string& error);
        [[nodiscard]] bool Execute(std::string& error) const;
        void Reset() noexcept;

        [[nodiscard]] std::vector<std::string_view> GetExecutionOrder() const;

    private:
        struct Pass final
        {
            RenderGraphPassDescription Description;
            ExecuteCallback Execute;
        };

        std::vector<Pass> m_Passes;
        std::vector<std::uint32_t> m_ExecutionOrder;
    };
}
