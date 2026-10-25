namespace Ncma.Runtime;

// Generated assembly manifest, inspected by the compiler only. This is NOT a registry
// or an authorization contract; the original ComponentRegistry validates at runtime.
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GeneratedComponentIdentityAttribute(string typeId, int version, Type componentType) : Attribute
{
    public string ComponentTypeId { get; } = typeId;
    public int Version { get; } = version;
    public Type ComponentType { get; } = componentType;
}

// Build-time declaration only: no assembly scanning, registration side effects or write grant.
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class GenerateComponentAttribute(string typeId, int version, string schema,
    string validator, string defaultMember) : Attribute
{
    public string ComponentTypeId { get; } = typeId;
    public int Version { get; } = version;
    public string Schema { get; } = schema;
    public string Validator { get; } = validator;
    public string DefaultMember { get; } = defaultMember;
    public bool RuntimeAttachable { get; set; } = true;
}
