#pragma once
#include "foundation/MathTypes.h"
#include "scene/SceneUuid.h"
#include "scene/BehaviourBinding.h"
#include <string>
#include <vector>
namespace NcmaEngine
{
    using GameObjectId = std::uint64_t;
    inline constexpr GameObjectId InvalidGameObjectId = 0;
    // Read-only UI projection, never a persistence or restore source.
    // Complete components remain owned and serialized by the C# SceneDocument.
    struct SceneObjectView final
    {
        SceneUuid PersistentId;
        std::string Name;
        Transform LocalTransform{};
        std::vector<BehaviourBinding> Behaviours;
        bool HasTransform = false;
    };
    struct SceneView final
    {
        std::string Name;
        std::vector<SceneObjectView> Objects;
    };
}
