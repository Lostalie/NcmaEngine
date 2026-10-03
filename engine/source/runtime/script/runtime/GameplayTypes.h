#pragma once

#include "scene/BehaviourBinding.h"

namespace NcmaEngine::Scripting
{
    struct ExportDescriptor final
    {
        ExportValue Default;
        std::string DisplayName;
        std::string Category;
    };
    struct BehaviourDescriptor final
    {
        std::string TypeName;
        std::vector<ExportDescriptor> Properties;
        BehaviourLanguage Language = BehaviourLanguage::CSharp;
        [[nodiscard]] BehaviourBinding CreateBinding() const
        {
            BehaviourBinding binding{SceneUuid::New(), TypeName, true, {}};
            binding.Language = Language;
            for (const auto& property : Properties) binding.Properties.push_back(property.Default);
            return binding;
        }
    };
}
