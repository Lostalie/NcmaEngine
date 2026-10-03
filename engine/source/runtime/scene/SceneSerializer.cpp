#include "scene/SceneSerializer.h"

#include <cmath>
#include <fstream>
#include <iomanip>
#include <limits>
#include <locale>
#include <sstream>
#include <unordered_map>

namespace NcmaEngine
{
    namespace
    {
        bool ReadLine(std::istringstream& input, std::string& line, std::size_t& lineNumber)
        {
            if (!std::getline(input, line))
                return false;
            ++lineNumber;
            if (!line.empty() && line.back() == '\r')
                line.pop_back();
            return true;
        }

        bool HasTrailingValue(std::istringstream& line)
        {
            line >> std::ws;
            return !line.eof();
        }

        bool IsFinite(const Transform& transform)
        {
            return transform.Position.allFinite() && transform.Rotation.coeffs().allFinite() &&
                transform.Scale.allFinite();
        }
    }

    std::string SceneSerializer::Serialize(const SceneSnapshot& snapshot)
    {
        std::ostringstream output;
        output.imbue(std::locale::classic());
        output << "NCMA_SCENE " << SceneSnapshotVersion << '\n';
        output << "name " << std::quoted(snapshot.Name) << '\n';
        output << "objects " << snapshot.Objects.size() << '\n';
        output << std::setprecision(std::numeric_limits<float>::max_digits10);
        for (const SceneObjectSnapshot& gameObject : snapshot.Objects)
        {
            output << "object " << std::quoted(gameObject.PersistentId.ToString()) << ' '
                << std::quoted(gameObject.Name) << ' ' << static_cast<std::uint32_t>(gameObject.LogicLanguage) << ' '
                << gameObject.LocalTransform.Position.x() << ' '
                << gameObject.LocalTransform.Position.y() << ' '
                << gameObject.LocalTransform.Position.z() << ' '
                << gameObject.LocalTransform.Rotation.x() << ' '
                << gameObject.LocalTransform.Rotation.y() << ' '
                << gameObject.LocalTransform.Rotation.z() << ' '
                << gameObject.LocalTransform.Rotation.w() << ' '
                << gameObject.LocalTransform.Scale.x() << ' '
                << gameObject.LocalTransform.Scale.y() << ' '
                << gameObject.LocalTransform.Scale.z() << '\n';
            output << "behaviours " << gameObject.Behaviours.size() << '\n';
            for (const auto& binding : gameObject.Behaviours)
            {
                output << "behaviour " << std::quoted(binding.Id.ToString()) << ' '
                    << std::quoted(binding.TypeName) << ' ' << static_cast<std::uint32_t>(binding.Language)
                    << ' ' << binding.Enabled << ' '
                    << binding.Properties.size() << '\n';
                for (const auto& property : binding.Properties)
                    output << "property " << std::quoted(property.Name) << ' '
                        << static_cast<std::uint32_t>(property.Kind) << ' '
                        << std::setprecision(std::numeric_limits<double>::max_digits10)
                        << property.Value << '\n';
            }
        }
        return output.str();
    }

    bool SceneSerializer::Deserialize(
        std::string_view serialized, SceneSnapshot& snapshot, std::string& error)
    {
        std::istringstream input{std::string(serialized)};
        input.imbue(std::locale::classic());
        std::string text;
        std::size_t lineNumber = 0;

        if (!ReadLine(input, text, lineNumber))
        {
            error = "Scene file is empty";
            return false;
        }
        SceneSnapshot parsed;
        {
            std::istringstream line(text);
            std::string header;
            if (!(line >> header >> parsed.Version) || header != "NCMA_SCENE" || HasTrailingValue(line))
            {
                error = "Invalid scene header on line 1";
                return false;
            }
        }
        if (parsed.Version < 1 || parsed.Version > SceneSnapshotVersion)
        {
            error = "Unsupported scene file version " + std::to_string(parsed.Version);
            return false;
        }

        if (!ReadLine(input, text, lineNumber))
        {
            error = "Missing scene name";
            return false;
        }
        {
            std::istringstream line(text);
            std::string keyword;
            if (!(line >> keyword >> std::quoted(parsed.Name)) || keyword != "name" || HasTrailingValue(line))
            {
                error = "Invalid scene name on line " + std::to_string(lineNumber);
                return false;
            }
        }

        std::size_t objectCount = 0;
        if (!ReadLine(input, text, lineNumber))
        {
            error = "Missing scene object count";
            return false;
        }
        {
            std::istringstream line(text);
            std::string keyword;
            if (!(line >> keyword >> objectCount) || keyword != (parsed.Version < 4 ? "nodes" : "objects") || objectCount > 100000 || HasTrailingValue(line))
            {
                error = "Invalid scene object count on line " + std::to_string(lineNumber);
                return false;
            }
        }
        parsed.Objects.reserve(objectCount);
        std::vector<std::string> legacyParents;
        legacyParents.reserve(objectCount);
        for (std::size_t index = 0; index < objectCount; ++index)
        {
            if (!ReadLine(input, text, lineNumber))
            {
                error = "Scene ended before all objects were read";
                return false;
            }
            std::istringstream line(text);
            line.imbue(std::locale::classic());
            std::string keyword;
            std::string uuidText;
            std::string parentText;
            SceneObjectSnapshot gameObject;
            std::uint32_t objectLanguage = 0;
            float rotationX = 0.0F;
            float rotationY = 0.0F;
            float rotationZ = 0.0F;
            float rotationW = 1.0F;
            if (!(line >> keyword >> std::quoted(uuidText)) ||
                (parsed.Version < 4 && !(line >> std::quoted(parentText))) ||
                !(line >> std::quoted(gameObject.Name)) ||
                (parsed.Version >= 5 && !(line >> objectLanguage)) ||
                !(line >> gameObject.LocalTransform.Position.x() >> gameObject.LocalTransform.Position.y()
                >> gameObject.LocalTransform.Position.z() >> rotationX >> rotationY >> rotationZ >> rotationW
                >> gameObject.LocalTransform.Scale.x() >> gameObject.LocalTransform.Scale.y()
                >> gameObject.LocalTransform.Scale.z()) || keyword != (parsed.Version < 4 ? "node" : "object") || HasTrailingValue(line))
            {
                error = "Invalid scene object on line " + std::to_string(lineNumber);
                return false;
            }
            const auto uuid = SceneUuid::Parse(uuidText);
            if (!uuid.has_value())
            {
                error = "Invalid object UUID on line " + std::to_string(lineNumber);
                return false;
            }
            gameObject.PersistentId = *uuid;
            if (objectLanguage != 0)
            {
                error = "Unsupported GameObject language; Python gameplay has been removed on line " + std::to_string(lineNumber);
                return false;
            }
            gameObject.LogicLanguage = BehaviourLanguage::CSharp;
            if (!parentText.empty() && !SceneUuid::Parse(parentText).has_value())
            {
                error = "Invalid parent UUID on line " + std::to_string(lineNumber);
                return false;
            }
            if (!parentText.empty())
                parentText = SceneUuid::Parse(parentText)->ToString();
            legacyParents.push_back(std::move(parentText));
            gameObject.LocalTransform.Rotation = Quaternion(rotationW, rotationX, rotationY, rotationZ);
            if (!IsFinite(gameObject.LocalTransform) ||
                !std::isfinite(gameObject.LocalTransform.Rotation.squaredNorm()) ||
                gameObject.LocalTransform.Rotation.squaredNorm() < 0.000001F)
            {
                error = "Invalid transform on line " + std::to_string(lineNumber);
                return false;
            }
            gameObject.LocalTransform.Rotation.normalize();
            if (parsed.Version >= 2)
            {
                std::size_t bindingCount = 0;
                if (!ReadLine(input, text, lineNumber)) { error = "Missing behaviours"; return false; }
                std::istringstream bindings(text);
                if (!(bindings >> keyword >> bindingCount) || keyword != "behaviours" ||
                    bindingCount > 1024 || HasTrailingValue(bindings))
                { error = "Invalid behaviour count"; return false; }
                for (std::size_t b = 0; b < bindingCount; ++b)
                {
                    if (!ReadLine(input, text, lineNumber)) { error = "Missing behaviour"; return false; }
                    std::istringstream bindingLine(text);
                    BehaviourBinding binding;
                    std::size_t propertyCount = 0;
                    int enabled = 0;
                    std::uint32_t language = 0;
                    if (!(bindingLine >> keyword >> std::quoted(uuidText) >> std::quoted(binding.TypeName)) ||
                        (parsed.Version >= 3 && !(bindingLine >> language)) ||
                        !(bindingLine >> enabled >> propertyCount) || keyword != "behaviour" || propertyCount > 1024 ||
                        (enabled != 0 && enabled != 1) || HasTrailingValue(bindingLine))
                    { error = "Invalid behaviour record"; return false; }
                    const auto bindingId = SceneUuid::Parse(uuidText);
                    if (!bindingId) { error = "Invalid behaviour UUID"; return false; }
                    binding.Id = *bindingId;
                    if (language != 0)
                    {
                        error = "Unsupported Behaviour language; Python gameplay has been removed on line " + std::to_string(lineNumber);
                        return false;
                    }
                    binding.Language = BehaviourLanguage::CSharp;
                    binding.Enabled = enabled != 0;
                    for (std::size_t p = 0; p < propertyCount; ++p)
                    {
                        if (!ReadLine(input, text, lineNumber)) { error = "Missing property"; return false; }
                        std::istringstream propertyLine(text);
                        propertyLine.imbue(std::locale::classic());
                        ExportValue property;
                        std::uint32_t kind = 0;
                        if (!(propertyLine >> keyword >> std::quoted(property.Name) >> kind >> property.Value) ||
                            keyword != "property" || HasTrailingValue(propertyLine))
                        { error = "Invalid property record"; return false; }
                        property.Kind = static_cast<ExportKind>(kind);
                        binding.Properties.push_back(std::move(property));
                    }
                    gameObject.Behaviours.push_back(std::move(binding));
                }
            }
            parsed.Objects.push_back(std::move(gameObject));
        }

        while (ReadLine(input, text, lineNumber))
        {
            if (text.find_first_not_of(" \t") != std::string::npos)
            {
                error = "Unexpected content on line " + std::to_string(lineNumber);
                return false;
            }
        }

        // Legacy hierarchy is ingestion-only: bake the old engine's world TRS into independent objects.
        // Queue traversal avoids recursion and accepts children appearing before their parents.
        if (parsed.Version < 4)
        {
            std::unordered_map<std::string, std::size_t> indices;
            for (std::size_t index = 0; index < parsed.Objects.size(); ++index)
                if (!indices.emplace(parsed.Objects[index].PersistentId.ToString(), index).second)
                {
                    error = "Scene contains a duplicate GameObject UUID";
                    return false;
                }
            std::vector<std::vector<std::size_t>> children(parsed.Objects.size());
            std::vector<std::size_t> ready;
            ready.reserve(parsed.Objects.size());
            for (std::size_t index = 0; index < legacyParents.size(); ++index)
            {
                if (legacyParents[index].empty())
                    ready.push_back(index);
                else
                {
                    const auto parent = indices.find(legacyParents[index]);
                    if (parent == indices.end())
                    {
                        error = "Legacy scene references a missing parent UUID";
                        return false;
                    }
                    children[parent->second].push_back(index);
                }
            }
            for (std::size_t cursor = 0; cursor < ready.size(); ++cursor)
            {
                const auto parentIndex = ready[cursor];
                for (const auto childIndex : children[parentIndex])
                {
                    auto& transform = parsed.Objects[childIndex].LocalTransform;
                    transform = Transform::Combine(parsed.Objects[parentIndex].LocalTransform, transform);
                    if (!IsFinite(transform) || !std::isfinite(transform.Rotation.squaredNorm()) ||
                        transform.Rotation.squaredNorm() < 0.000001F)
                    {
                        error = "Legacy world transform is not finite";
                        return false;
                    }
                    transform.Rotation.normalize();
                    ready.push_back(childIndex);
                }
            }
            if (ready.size() != parsed.Objects.size())
            {
                error = "Legacy scene hierarchy contains a cycle";
                return false;
            }
        }
        parsed.Version = SceneSnapshotVersion;
        SceneWorld validator;
        if (!validator.RestoreSnapshot(parsed, error))
            return false;
        snapshot = validator.CaptureSnapshot();
        error.clear();
        return true;
    }

    bool SceneSerializer::Save(
        const std::filesystem::path& path, const SceneSnapshot& snapshot, std::string& error)
    {
        SceneWorld validator;
        if (!validator.RestoreSnapshot(snapshot, error))
            return false;
        std::error_code filesystemError;
        if (path.has_parent_path())
            std::filesystem::create_directories(path.parent_path(), filesystemError);
        if (filesystemError)
        {
            error = "Could not create scene directory: " + filesystemError.message();
            return false;
        }
        // Loading never writes. Explicitly saving an old asset preserves its original bytes.
        if (std::filesystem::exists(path, filesystemError))
        {
            std::ifstream existing(path, std::ios::binary);
            if (!existing)
            {
                error = "Could not inspect existing scene before saving: " + path.string();
                return false;
            }
            std::string header;
            std::uint32_t version = 0;
            if (existing >> header >> version; header == "NCMA_SCENE" && version >= 1 && version < SceneSnapshotVersion)
            {
                auto backup = path;
                backup += ".v" + std::to_string(version) + "." + SceneUuid::New().ToString() + ".bak";
                existing.close();
                if (!std::filesystem::copy_file(path, backup, std::filesystem::copy_options::none, filesystemError))
                {
                    error = "Could not back up legacy scene before migration: " + filesystemError.message();
                    return false;
                }
            }
        }
        if (filesystemError)
        {
            error = "Could not inspect scene before saving: " + filesystemError.message();
            return false;
        }
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        if (!output)
        {
            error = "Could not open scene for writing: " + path.string();
            return false;
        }
        const std::string serialized = Serialize(snapshot);
        output.write(serialized.data(), static_cast<std::streamsize>(serialized.size()));
        if (!output)
        {
            error = "Could not write scene: " + path.string();
            return false;
        }
        error.clear();
        return true;
    }

    bool SceneSerializer::Load(
        const std::filesystem::path& path, SceneSnapshot& snapshot, std::string& error)
    {
        std::ifstream input(path, std::ios::binary);
        if (!input)
        {
            error = "Could not open scene: " + path.string();
            return false;
        }
        std::ostringstream buffer;
        buffer << input.rdbuf();
        if (!input.good() && !input.eof())
        {
            error = "Could not read scene: " + path.string();
            return false;
        }
        return Deserialize(buffer.str(), snapshot, error);
    }
}
