using Ncma.Runtime;

namespace Ncma.Scene.Prefabs;

public static class PrefabDocumentCodec
{
    public const int Version=1,MaxObjects=16,MaxReferences=4096,MaxBytes=World.MaxSnapshotBytes;
    public static byte[] Encode(PrefabDocument value,PrefabPolicy policy)
    {
        // Serialize to owned bounded bytes before callbacks. A validator cannot mutate the caller
        // DTO after checking it and cause unvalidated data to escape the encoder.
        var owned=Decode(SceneJson.EncodeBounded(value,MaxBytes),policy);return SceneJson.EncodeBounded(owned,MaxBytes);
    }
    public static PrefabDocument Decode(ReadOnlySpan<byte> bytes,PrefabPolicy policy)
    {
        if(bytes.Length is 0 or >MaxBytes)throw new ArgumentException("Invalid prefab byte budget.");
        using var json=System.Text.Json.JsonDocument.Parse(bytes.ToArray(),new(){MaxDepth=32});SceneJson.RejectDuplicateFields(json.RootElement);
        var result=System.Text.Json.JsonSerializer.Deserialize<PrefabDocument>(json.RootElement,SceneJson.Options)??throw new ArgumentException("Missing prefab.");return Validate(result,policy);
    }
    public static string RequireExtension(string path)
    {if(string.IsNullOrWhiteSpace(path)||!path.EndsWith(".ncprefab",StringComparison.Ordinal))throw new ArgumentException("Only .ncprefab JSON v1 is supported.");return path;}
    internal static void Point(PrefabPoint point)
    {if(point is null||!float.IsFinite(point.X)||!float.IsFinite(point.Y)||!float.IsFinite(point.Z)||Math.Abs(point.X)>1e6||Math.Abs(point.Y)>1e6||Math.Abs(point.Z)>1e6)throw new ArgumentException("Invalid bounded prefab point.");}
    internal static PrefabDocument Validate(PrefabDocument value,PrefabPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(value);ArgumentNullException.ThrowIfNull(policy);policy.Freeze();
        if(value.Version!=Version||value.AssetId==Guid.Empty||value.BaseVersion is 0 or >long.MaxValue||value.Objects is null||value.Objects.Length is <1 or >MaxObjects||value.ObjectReferences is null||value.ObjectReferences.Length>MaxReferences||value.Dependencies is null||value.Dependencies.Length>MaxReferences)throw new ArgumentException("Invalid prefab version/identity/capacity.");
        World.ValidateName(value.Name);Point(value.Pivot);
        var scene=new SceneDocumentSnapshot(1,value.Name,value.Objects.Select(o=>o is null?throw new ArgumentException("Null prefab object."):new SceneObjectData(o.Id,o.Name,o.Components,o.Behaviours)).ToArray());
        // Complete registered schema/value/normalization preflight; no live World is touched.
        var isolated=new SceneDocument(value.Name,policy.Components,policy.Composition);isolated.RestoreSnapshot(scene);
        value=value with{Objects=isolated.CaptureSnapshot().Objects.Select(o=>new PrefabObject(o.Id,o.Name,o.Components,o.Behaviours)).ToArray()};
        var objects=value.Objects.Select(o=>o.Id).ToHashSet();
        var identities=new HashSet<Guid>{value.AssetId};
        foreach(var obj in value.Objects) {
            if(!identities.Add(obj.Id))throw new ArgumentException("Prefab identity namespace collision.");
            foreach(var binding in obj.Behaviours){if(!identities.Add(binding.Id))throw new ArgumentException("Binding identity namespace collision.");policy.RequireBinding(binding);}
        }
        var expectedReferences=new HashSet<PrefabObjectReference>();var expectedDependencies=new HashSet<PrefabDependency>();
        foreach(var obj in value.Objects)foreach(var component in obj.Components)foreach(var field in policy.Rule(component).References) {
            Guid id=policy.Reference(component,field);if(id==Guid.Empty)continue;
            if(field.Role==PrefabReferenceRole.Object){if(!objects.Contains(id))throw new ArgumentException("prefab_external_object_reference");expectedReferences.Add(new(obj.Id,component.TypeId,field.FieldPath,id));}
            else {if(identities.Contains(id))throw new ArgumentException("Prefab self/identity asset reference.");expectedDependencies.Add(new(id,field.AssetKind!));}
        }
        if(value.ObjectReferences.Any(r=>r is null)||value.Dependencies.Any(d=>d is null)||expectedReferences.Count!=value.ObjectReferences.Length||!expectedReferences.SetEquals(value.ObjectReferences)||expectedDependencies.Count!=value.Dependencies.Length||!expectedDependencies.SetEquals(value.Dependencies)||value.Dependencies.GroupBy(d=>d.AssetId).Any(g=>g.Select(d=>d.Kind).Distinct().Count()!=1))throw new ArgumentException("Prefab reference/dependency declarations do not exactly match registered payload fields.");
        return value;
    }
}
