using System.Reflection;
using System.Collections.Frozen;
using System.Text.Json;
using Ncma.ComponentContracts;

namespace Ncma.Runtime;

// Descriptive policy only. Neither these flags nor a descriptor authorize any access.
[Flags]
public enum ComponentFieldFlags { Persistent = 1, Editable = 2, RuntimeRead = 4, RuntimeWrite = 8 }

[AttributeUsage(AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class ComponentFieldAttribute(string fieldId, string memberPath, string jsonPath) : Attribute
{
    public string FieldId { get; } = fieldId;
    public string MemberPath { get; } = memberPath;
    public string JsonPath { get; } = jsonPath;
    public ComponentFieldFlags Flags { get; set; } = ComponentFieldFlags.Persistent | ComponentFieldFlags.RuntimeRead;
}

public sealed record ComponentFieldDefinition(string FieldId, string MemberPath, string JsonPath, ComponentFieldFlags Flags);
public sealed record ComponentFieldDescriptor(string FieldId, string MemberPath, string JsonPath,
    string ValueKind, ComponentFieldFlags Flags, JsonElement DefaultValue);

public sealed class ComponentMetadata
{
    public const int ContractVersion = 1;
    public const int MaxFields = FieldSchema.MaxFields;
    public IReadOnlyList<ComponentFieldDescriptor> Fields { get; }
    public JsonElement DefaultValue { get; }
    private readonly FrozenDictionary<string, (ComponentFieldDescriptor Field, string[] Path)> _byId;
    private ComponentMetadata(ComponentFieldDescriptor[] fields, JsonElement defaultValue)
    {
        Fields = Array.AsReadOnly(fields); DefaultValue = defaultValue;
        _byId = fields.ToFrozenDictionary(f => f.FieldId, f => (f, f.JsonPath.Split('.')), StringComparer.Ordinal);
    }

    public ComponentFieldDescriptor Field(string fieldId) => _byId.TryGetValue(fieldId, out var item) ? item.Field :
        throw new ArgumentException("Unknown declared FieldId: " + fieldId);

    /// <summary>Read an existing serialized value using a precompiled path. Does not validate or authorize the payload.</summary>
    public JsonElement ReadSerializedField(string fieldId, JsonElement data)
    {
        if (!_byId.TryGetValue(fieldId, out var item)) throw new ArgumentException("Unknown declared FieldId: " + fieldId);
        foreach (string part in item.Path) {
            if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(part, out var next))
                throw new ArgumentException("Serialized component lacks declared field: " + fieldId);
            data = next;
        }
        return data;
    }

    internal sealed class Prepared((ComponentFieldDefinition Field, string Kind)[] fields)
    {
        internal ComponentMetadata Capture(JsonElement defaults)
        {
            var frozen = defaults.Clone(); // One owned document; immutable field views share storage.
            var result = new ComponentFieldDescriptor[fields.Length];
            for (int i = 0; i < fields.Length; i++) {
                var (field, kind) = fields[i]; var value = frozen;
                foreach (string part in field.JsonPath.Split('.')) value = value.GetProperty(part);
                result[i] = new(field.FieldId, field.MemberPath, field.JsonPath, kind, field.Flags, value);
            }
            return new(result, frozen);
        }
    }

    internal static Prepared Prepare<T>(ComponentFieldDefinition[] definitions, JsonElement schema) where T : struct, IComponent
    {
        var fields = FieldContract.Prepare(typeof(T), new ReflectionAdapter(), definitions.Select(field =>
            field is null ? throw new ArgumentException("Null field definition.") :
                new FieldDeclaration(field.FieldId, field.MemberPath, field.JsonPath, (int)field.Flags)), schema);
        return new(fields.Select(f => (new ComponentFieldDefinition(f.Declaration.Id, f.Declaration.MemberPath,
            f.Declaration.JsonPath, (ComponentFieldFlags)f.Declaration.Flags), f.Kind)).ToArray());
    }

    private sealed class ReflectionAdapter : IFieldTypeAdapter<Type>
    {
        public FieldTypeFacts Describe(Type type) => new(type.FullName ?? type.Name, type.IsEnum, type.IsValueType,
            type.IsByRefLike, typeof(System.Collections.IEnumerable).IsAssignableFrom(type));
        public IEnumerable<string> AttributeNamespaces(Type type) => Namespaces(type);
        private static IEnumerable<string> Namespaces(MemberInfo member) => member.CustomAttributes
            .Select(a => a.AttributeType.Namespace ?? "");
        public IEnumerable<FieldMemberFacts<Type>> Members(Type type)
        {
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
                if (member is FieldInfo f) yield return new(f.Name, f.FieldType) {
                    IsField = true, IsStatic = f.IsStatic, IsPublic = f.IsPublic, IsReadOnly = f.IsInitOnly,
                    IsConst = f.IsLiteral,
                    AttributeNamespaces = Namespaces(f)
                };
                else if (member is PropertyInfo p) yield return new(p.Name, p.PropertyType) {
                    IsStatic = (p.GetMethod ?? p.SetMethod)?.IsStatic == true,
                    IsPublic = p.GetMethod?.IsPublic == true || p.SetMethod?.IsPublic == true,
                    GetterPublic = p.GetMethod?.IsPublic == true, SetterPublic = p.SetMethod?.IsPublic == true,
                    HasSetter = p.SetMethod is not null, IsIndexer = p.GetIndexParameters().Length != 0,
                    AttributeNamespaces = Namespaces(p)
                };
            }
        }
    }
}
