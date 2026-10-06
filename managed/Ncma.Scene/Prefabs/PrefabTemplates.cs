using System.Numerics;
using Ncma.Runtime;

namespace Ncma.Scene.Prefabs;

public static class PrefabTemplates
{
    public static PrefabDocument Extract(SceneDocument edit,Guid[] selected,PrefabExtractionScope scope,PrefabPolicy policy,
        Guid prefabId,string name,PrefabPoint pivot,ulong expectedRevision,Guid expectedWorldId)
    {
        ArgumentNullException.ThrowIfNull(edit);ArgumentNullException.ThrowIfNull(policy);ArgumentNullException.ThrowIfNull(selected);ArgumentNullException.ThrowIfNull(scope);edit.VerifyAccess();
        selected=(Guid[])selected.Clone();
        if(selected.Length is <1 or >PrefabDocumentCodec.MaxObjects||selected.Any(id=>id==Guid.Empty)||selected.Distinct().Count()!=selected.Length)throw new ArgumentException("Invalid exact extraction set.");
        if(edit.Revision!=expectedRevision||edit.World.Identity!=expectedWorldId)throw new InvalidOperationException("prefab_extract_stale");
        using var read=edit.World.ReadOnly();scope.Require(edit,selected);
        var source=edit.CaptureSnapshot();var chosen=selected.Select(id=>source.Objects.SingleOrDefault(o=>o.Id==id)??throw new ArgumentException("Unknown source object.")).ToArray();
        // Validate all policy/reference intent before allocating persistent template identities.
        foreach(var obj in chosen){foreach(var component in obj.Components){foreach(var field in policy.Rule(component).References){Guid id=policy.Reference(component,field);if(field.Role==PrefabReferenceRole.Object&&id!=Guid.Empty&&!selected.Contains(id))throw new ArgumentException("prefab_external_object_reference");}}
            foreach(var binding in obj.Behaviours)policy.RequireBinding(binding);}
        var reserved=source.Objects.Select(o=>o.Id).Concat(source.Objects.SelectMany(o=>o.Behaviours.Select(b=>b.Id))).ToHashSet();
        if(prefabId==Guid.Empty||!reserved.Add(prefabId))throw new ArgumentException("Invalid prefab asset identity.");
        foreach(var obj in chosen)foreach(var c in obj.Components)foreach(var f in policy.Rule(c).References)if(f.Role==PrefabReferenceRole.Asset){Guid id=policy.Reference(c,f);if(id==prefabId)throw new ArgumentException("Prefab asset self reference.");reserved.Add(id);}
        var mapping=selected.ToDictionary(id=>id,_=>Fresh(reserved));var references=new List<PrefabObjectReference>();var dependencies=new HashSet<PrefabDependency>();
        var result=chosen.Select(obj=>{
            var components=obj.Components.Select(component=> {
                var copy=component with{Data=component.Data.Clone()};
                foreach(var field in policy.Rule(component).References) {
                    Guid id=policy.Reference(component,field);if(id==Guid.Empty)continue;
                    if(field.Role==PrefabReferenceRole.Object){copy=PrefabFields.Replace(copy,field.FieldPath,mapping[id]);references.Add(new(mapping[obj.Id],component.TypeId,field.FieldPath,mapping[id]));}
                    else dependencies.Add(new(id,field.AssetKind!));
                }return copy;
            }).ToArray();
            return new PrefabObject(mapping[obj.Id],obj.Name,components,obj.Behaviours.Select(b=>b with{Id=Fresh(reserved),Exports=(ExportData[])b.Exports.Clone()}).ToArray());
        }).ToArray();
        var document=new PrefabDocument(1,prefabId,1,name,pivot,result,references.OrderBy(r=>r.ObjectId).ThenBy(r=>r.TypeId,StringComparer.Ordinal).ThenBy(r=>r.FieldPath,StringComparer.Ordinal).ToArray(),dependencies.OrderBy(d=>d.AssetId).ToArray());
        byte[] bytes=PrefabDocumentCodec.Encode(document,policy);scope.Require(edit,selected);
        if(edit.Revision!=expectedRevision||edit.World.Identity!=expectedWorldId)throw new InvalidOperationException("prefab_extract_stale");return PrefabDocumentCodec.Decode(bytes,policy);
    }
    public static PrefabExpansionPreview PreviewExpansion(PrefabDocument prefab,PrefabPolicy policy,PrefabPoint placement,IEnumerable<Guid> occupiedIdentities)
    {
        ArgumentNullException.ThrowIfNull(occupiedIdentities);
        // Copy first: caller mutation cannot alias the detached result. No live World/file writes.
        prefab=PrefabDocumentCodec.Decode(PrefabDocumentCodec.Encode(prefab,policy),policy);PrefabDocumentCodec.Point(placement);
        int operations=prefab.Objects.Sum(o=>1+o.Components.Length+(o.Behaviours.Length>0?1:0));
        if(operations>128)throw new ArgumentException("prefab_operation_budget");
        const int maxOccupied=65536;
        var occupied=occupiedIdentities.Take(maxOccupied+1).ToArray();
        if(occupied.Length>maxOccupied||occupied.Any(id=>id==Guid.Empty))throw new ArgumentException("Invalid occupied identity budget.");
        var reserved=occupied.Concat(prefab.Objects.Select(o=>o.Id)).Concat(prefab.Objects.SelectMany(o=>o.Behaviours.Select(b=>b.Id))).Concat(prefab.Dependencies.Select(d=>d.AssetId)).Append(prefab.AssetId).ToHashSet();
        Guid instance=Fresh(reserved);var mapping=prefab.Objects.ToDictionary(o=>o.Id,_=>Fresh(reserved));Vector3 delta=placement.ToVector()-prefab.Pivot.ToVector();
        var objects=prefab.Objects.Select(obj=>new SceneObjectData(mapping[obj.Id],obj.Name,obj.Components.Select(component=> {
            var copy=component with{Data=component.Data.Clone()};
            foreach(var field in policy.Rule(component).References)if(field.Role==PrefabReferenceRole.Object){Guid id=policy.Reference(component,field);if(id!=Guid.Empty)copy=PrefabFields.Replace(copy,field.FieldPath,mapping[id]);}
            if(component.TypeId=="ncma.transform") {var transform=policy.Components.Decode<TransformData>(copy);copy=copy with{Data=policy.Components.Encode(transform with{Position=transform.Position+delta})};}
            return copy;
        }).ToArray(),obj.Behaviours.Select(b=>b with{Id=Fresh(reserved),Exports=(ExportData[])b.Exports.Clone()}).ToArray())).ToArray();
        var snapshot=new SceneDocumentSnapshot(1,prefab.Name,objects);var isolated=new SceneDocument(prefab.Name,policy.Components,policy.Composition);isolated.RestoreSnapshot(snapshot);
        // Validation only, not instance publication; membership/override project files are M3.7-B/C/D.
        return new(prefab.AssetId,prefab.BaseVersion,instance,placement,SceneDocumentCodec.Decode(isolated.CaptureBytes()).Objects,mapping.Select(p=>new PrefabObjectMapping(p.Key,p.Value)).ToArray(),(PrefabDependency[])prefab.Dependencies.Clone(),operations);
    }
    private static Guid Fresh(HashSet<Guid> reserved)
    {for(int tries=0;tries<32;tries++){Guid id=Guid.NewGuid();if(id!=Guid.Empty&&reserved.Add(id))return id;}throw new InvalidOperationException("UUID allocation failed.");}
}
