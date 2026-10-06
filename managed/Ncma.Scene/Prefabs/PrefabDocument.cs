using System.Numerics;
using System.Text.Json.Serialization;
using Ncma.Runtime;

namespace Ncma.Scene.Prefabs;

public sealed record PrefabPoint([property: JsonRequired] float X,[property: JsonRequired] float Y,[property: JsonRequired] float Z)
{
    public Vector3 ToVector()=>new(X,Y,Z);
    public static PrefabPoint From(Vector3 value)=>new(value.X,value.Y,value.Z);
}
public sealed record PrefabObject([property: JsonRequired] Guid Id,[property: JsonRequired] string Name,
    [property: JsonRequired] ComponentSnapshot[] Components,[property: JsonRequired] BehaviourBindingData[] Behaviours);
public sealed record PrefabObjectReference([property: JsonRequired] Guid ObjectId,[property: JsonRequired] string TypeId,
    [property: JsonRequired] string FieldPath,[property: JsonRequired] Guid TargetId);
public sealed record PrefabDependency([property: JsonRequired] Guid AssetId,[property: JsonRequired] string Kind);
// Authoring-only flat template. No parents, inherited transforms, nested templates or runtime state.
public sealed record PrefabDocument([property: JsonRequired] int Version,[property: JsonRequired] Guid AssetId,
    [property: JsonRequired] ulong BaseVersion,[property: JsonRequired] string Name,[property: JsonRequired] PrefabPoint Pivot,
    [property: JsonRequired] PrefabObject[] Objects,[property: JsonRequired] PrefabObjectReference[] ObjectReferences,
    [property: JsonRequired] PrefabDependency[] Dependencies);

// Detached inspection only, NOT a published instance or an executable project transaction.
public sealed record PrefabExpansionPreview(Guid PrefabId,ulong BaseVersion,Guid InstanceId,PrefabPoint Placement,
    SceneObjectData[] Objects,PrefabObjectMapping[] Mappings,PrefabDependency[] Dependencies,int OperationCount);
public sealed record PrefabObjectMapping(Guid TemplateObjectId,Guid ObjectId);
