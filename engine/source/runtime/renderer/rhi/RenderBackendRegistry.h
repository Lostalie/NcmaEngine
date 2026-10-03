#pragma once

#include "renderer/rhi/RenderBackend.h"

#include <optional>
#include <unordered_map>
#include <vector>

namespace NcmaEngine::Rhi
{
    class RenderBackendRegistry final
    {
    public:
        RenderBackendRegistry();

        void Register(BackendType type, BackendFactory factory);
        [[nodiscard]] bool IsRegistered(BackendType type) const;
        [[nodiscard]] std::unique_ptr<IRenderBackend> Create(BackendType type) const;
        [[nodiscard]] std::vector<BackendType> GetRegisteredBackends() const;

        [[nodiscard]] static std::optional<BackendType> Parse(std::string_view name);
        [[nodiscard]] static std::string_view ToString(BackendType type);

    private:
        std::unordered_map<BackendType, BackendFactory> m_Factories;
    };
}

