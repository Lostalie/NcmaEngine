using System.Text.Json;

namespace Ncma.ComponentContracts;

// Linked source, not a runtime dependency on Roslyn. Adapters supply facts only;
// traversal order, ignore/reject policy, kind rules and definition matching live here.
internal readonly struct FieldTypeFacts(string name, bool isEnum, bool isValueType, bool isByRefLike, bool isEnumerable)
{
    internal string Name { get; } = name;
    internal bool IsEnum { get; } = isEnum;
    internal bool IsValueType { get; } = isValueType;
    internal bool IsByRefLike { get; } = isByRefLike;
    internal bool IsEnumerable { get; } = isEnumerable;
}

internal sealed class FieldMemberFacts<T>(string name, T type)
{
    internal string Name { get; } = name;
    internal T Type { get; } = type;
    internal bool IsField { get; set; }
    internal bool IsStatic { get; set; }
    internal bool IsPublic { get; set; }
    internal bool IsReadOnly { get; set; }
    internal bool IsConst { get; set; }
    internal bool GetterPublic { get; set; }
    internal bool SetterPublic { get; set; }
    internal bool HasSetter { get; set; }
    internal bool IsIndexer { get; set; }
    internal IEnumerable<string> AttributeNamespaces { get; set; } = Array.Empty<string>();
}

internal interface IFieldTypeAdapter<T>
{
    FieldTypeFacts Describe(T type);
    IEnumerable<string> AttributeNamespaces(T type);
    IEnumerable<FieldMemberFacts<T>> Members(T type);
}

internal sealed class FieldDeclaration(string id, string memberPath, string jsonPath, int flags)
{
    internal string Id { get; } = id;
    internal string MemberPath { get; } = memberPath;
    internal string JsonPath { get; } = jsonPath;
    internal int Flags { get; } = flags;
}

internal sealed class ContractField(FieldDeclaration declaration, string kind)
{
    internal FieldDeclaration Declaration { get; } = declaration;
    internal string Kind { get; } = kind;
}

internal static class FieldContract
{
    // Exact CLR metadata names, never language aliases or Type.IsPrimitive fallbacks.
    // Priority: enum, scalar table, explicit exclusions, value object, object policy.
    private static readonly Dictionary<string, string> ScalarKinds = new(StringComparer.Ordinal) {
        ["System.Boolean"] = "boolean", ["System.Char"] = "string", ["System.Guid"] = "string",
        ["System.Single"] = "number", ["System.Double"] = "number", ["System.Decimal"] = "number",
        ["System.SByte"] = "integer", ["System.Byte"] = "integer",
        ["System.Int16"] = "integer", ["System.UInt16"] = "integer",
        ["System.Int32"] = "integer", ["System.UInt32"] = "integer",
        ["System.Int64"] = "integer", ["System.UInt64"] = "integer"
    };

    internal static ContractField[] Prepare<T>(T type, IFieldTypeAdapter<T> adapter,
        IEnumerable<FieldDeclaration> definitions, JsonElement schema)
    {
        var schemaFields = FieldSchema.Flatten(schema);
        var members = new Dictionary<string, (string Member, string Kind)>(StringComparer.Ordinal);
        Collect(type, "", "", 0, adapter, members);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ContractField>();
        foreach (var field in definitions) {
            if (field is null) throw new ArgumentException("Null field definition.");
            FieldSchema.Definition(field.Id, field.MemberPath, field.JsonPath, field.Flags);
            if (!ids.Add(field.Id) || !paths.Add(field.JsonPath)) throw new ArgumentException("Duplicate FieldId or JSON path.");
            if (!members.TryGetValue(field.JsonPath, out var member) || member.Member != field.MemberPath ||
                !schemaFields.TryGetValue(field.JsonPath, out var kind) || kind != member.Kind)
                throw new ArgumentException("Field metadata/schema/serialized member mismatch: " + field.JsonPath);
            result.Add(new(field, kind));
        }
        if (result.Count != members.Count || members.Count != schemaFields.Count)
            throw new ArgumentException("Metadata must cover every serialized field, including object paths.");
        return result.OrderBy(f => f.Declaration.Id, StringComparer.Ordinal).ToArray();
    }

    private static string Kind(FieldTypeFacts type)
    {
        if (type.IsEnum) return "integer";
        if (ScalarKinds.TryGetValue(type.Name, out var kind)) return kind;
        if (!type.IsValueType || type.IsByRefLike || type.Name is "Ncma.Runtime.Entity" or "System.IntPtr" or "System.UIntPtr")
            throw new ArgumentException("Unsupported metadata value type.");
        return "object";
    }

    private static void RejectJsonCustomization(IEnumerable<string> namespaces)
    {
        if (namespaces.Any(n => n == "System.Text.Json.Serialization"))
            throw new ArgumentException("Custom JSON attributes require a separately supported metadata contract.");
    }

    private static void Collect<T>(T type, string memberPath, string jsonPath, int depth,
        IFieldTypeAdapter<T> adapter, Dictionary<string, (string Member, string Kind)> result)
    {
        if (depth > 16 || result.Count > FieldSchema.MaxFields) throw new ArgumentException("Component member depth/count budget exceeded.");
        RejectJsonCustomization(adapter.AttributeNamespaces(type));
        var facts = adapter.Describe(type);
        string kind = Kind(facts);
        if (jsonPath.Length != 0) {
            if (result.Count >= FieldSchema.MaxFields || result.ContainsKey(jsonPath)) throw new ArgumentException("Duplicate/excess serialized members.");
            result.Add(jsonPath, (memberPath, kind));
        }
        if (kind != "object") return;
        if (!FieldSchema.SupportedObject(facts.Name) || facts.IsEnumerable)
            throw new ArgumentException("Unsupported built-in converter/collection object in field metadata.");
        var members = adapter.Members(type).ToArray();
        // Even ignored/private instance members cannot customize the serializer silently.
        foreach (var member in members)
            if (!member.IsStatic) RejectJsonCustomization(member.AttributeNamespaces);
        foreach (var member in members) {
            if (member.IsStatic || !member.IsPublic) continue;
            if (member.IsField) {
                if (member.IsReadOnly) throw new ArgumentException("Readonly fields lack the supported deserialization contract; use public init properties.");
                if (member.IsConst) continue;
            } else {
                if (member.IsIndexer || !member.GetterPublic) continue;
                if (!member.SetterPublic) {
                    if (member.HasSetter) throw new ArgumentException("Nonpublic serialized setter not supported.");
                    continue; // Same IgnoreReadOnlyProperties policy as SceneJson.
                }
            }
            string name = JsonNamingPolicy.CamelCase.ConvertName(member.Name);
            Collect(member.Type, memberPath.Length == 0 ? member.Name : memberPath + "." + member.Name,
                jsonPath.Length == 0 ? name : jsonPath + "." + name, depth + 1, adapter, result);
        }
    }
}
