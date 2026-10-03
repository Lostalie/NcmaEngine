#pragma once

#include "scene/SceneUuid.h"
#include <cmath>
#include <cstdint>
#include <string>
#include <vector>

namespace NcmaEngine
{
    enum class ExportKind : std::uint32_t { Float = 1, Double = 2, Integer = 3, Boolean = 4 };
    enum class BehaviourLanguage : std::uint32_t { CSharp = 0 };

    struct ExportValue final
    {
        std::string Name;
        ExportKind Kind = ExportKind::Float;
        double Value = 0.0;
        friend bool operator==(const ExportValue&, const ExportValue&) = default;
    };

    struct BehaviourBinding final
    {
        SceneUuid Id;
        std::string TypeName;
        bool Enabled = true;
        std::vector<ExportValue> Properties;
        BehaviourLanguage Language = BehaviourLanguage::CSharp;
        friend bool operator==(const BehaviourBinding&, const BehaviourBinding&) = default;
    };

    inline bool ValidExportValue(const ExportValue& property)
    {
        if (property.Name.empty() || property.Name.find_first_of("\r\n") != std::string::npos ||
            !std::isfinite(property.Value))
            return false;
        switch (property.Kind)
        {
        case ExportKind::Float: return std::isfinite(static_cast<float>(property.Value));
        case ExportKind::Double: return true;
        case ExportKind::Integer:
            return property.Value >= -2147483648.0 && property.Value <= 2147483647.0 &&
                std::trunc(property.Value) == property.Value;
        case ExportKind::Boolean: return property.Value == 0.0 || property.Value == 1.0;
        default: return false;
        }
    }
}
