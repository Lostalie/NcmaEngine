#include "scene/SceneSerializer.h"

#include <fstream>
#include <iomanip>
#include <limits>
#include <locale>
#include <sstream>

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
        output << "nodes " << snapshot.Nodes.size() << '\n';
        output << std::setprecision(std::numeric_limits<float>::max_digits10);
        for (const SceneNodeSnapshot& node : snapshot.Nodes)
        {
            output << "node " << std::quoted(node.PersistentId.ToString()) << ' '
                << std::quoted(node.ParentId.has_value() ? node.ParentId->ToString() : std::string{}) << ' '
                << std::quoted(node.Name) << ' '
                << node.LocalTransform.Position.x() << ' '
                << node.LocalTransform.Position.y() << ' '
                << node.LocalTransform.Position.z() << ' '
                << node.LocalTransform.Rotation.x() << ' '
                << node.LocalTransform.Rotation.y() << ' '
                << node.LocalTransform.Rotation.z() << ' '
                << node.LocalTransform.Rotation.w() << ' '
                << node.LocalTransform.Scale.x() << ' '
                << node.LocalTransform.Scale.y() << ' '
                << node.LocalTransform.Scale.z() << '\n';
            output << "behaviours " << node.Behaviours.size() << '\n';
            for (const auto& binding : node.Behaviours)
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

        std::size_t nodeCount = 0;
        if (!ReadLine(input, text, lineNumber))
        {
            error = "Missing scene node count";
            return false;
        }
        {
            std::istringstream line(text);
            std::string keyword;
            if (!(line >> keyword >> nodeCount) || keyword != "nodes" || nodeCount > 100000 || HasTrailingValue(line))
            {
                error = "Invalid scene node count on line " + std::to_string(lineNumber);
                return false;
            }
        }
        parsed.Nodes.reserve(nodeCount);
        for (std::size_t index = 0; index < nodeCount; ++index)
        {
            if (!ReadLine(input, text, lineNumber))
            {
                error = "Scene ended before all nodes were read";
                return false;
            }
            std::istringstream line(text);
            line.imbue(std::locale::classic());
            std::string keyword;
            std::string uuidText;
            std::string parentText;
            SceneNodeSnapshot node;
            float rotationX = 0.0F;
            float rotationY = 0.0F;
            float rotationZ = 0.0F;
            float rotationW = 1.0F;
            if (!(line >> keyword >> std::quoted(uuidText) >> std::quoted(parentText) >> std::quoted(node.Name)
                >> node.LocalTransform.Position.x() >> node.LocalTransform.Position.y()
                >> node.LocalTransform.Position.z() >> rotationX >> rotationY >> rotationZ >> rotationW
                >> node.LocalTransform.Scale.x() >> node.LocalTransform.Scale.y()
                >> node.LocalTransform.Scale.z()) || keyword != "node" || HasTrailingValue(line))
            {
                error = "Invalid scene node on line " + std::to_string(lineNumber);
                return false;
            }
            const auto uuid = SceneUuid::Parse(uuidText);
            if (!uuid.has_value())
            {
                error = "Invalid node UUID on line " + std::to_string(lineNumber);
                return false;
            }
            node.PersistentId = *uuid;
            if (!parentText.empty())
            {
                node.ParentId = SceneUuid::Parse(parentText);
                if (!node.ParentId.has_value())
                {
                    error = "Invalid parent UUID on line " + std::to_string(lineNumber);
                    return false;
                }
            }
            node.LocalTransform.Rotation = Quaternion(rotationW, rotationX, rotationY, rotationZ);
            if (!IsFinite(node.LocalTransform) || node.LocalTransform.Rotation.squaredNorm() < 0.000001F)
            {
                error = "Invalid transform on line " + std::to_string(lineNumber);
                return false;
            }
            node.LocalTransform.Rotation.normalize();
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
                    binding.Language = static_cast<BehaviourLanguage>(language);
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
                    node.Behaviours.push_back(std::move(binding));
                }
            }
            parsed.Nodes.push_back(std::move(node));
        }

        while (ReadLine(input, text, lineNumber))
        {
            if (text.find_first_not_of(" \t") != std::string::npos)
            {
                error = "Unexpected content on line " + std::to_string(lineNumber);
                return false;
            }
        }

        SceneWorld validator;
        if (!validator.RestoreSnapshot(parsed, error))
            return false;
        parsed.Version = SceneSnapshotVersion;
        snapshot = std::move(parsed);
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
