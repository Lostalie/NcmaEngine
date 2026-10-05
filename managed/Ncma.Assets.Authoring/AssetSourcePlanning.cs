using Ncma.Assets.Authoring.Storage;

namespace Ncma.Assets.Authoring;

public sealed record SourceImportPlan(Guid PlanId,Guid ProjectId,ulong ProjectGeneration,ulong AssetRevision,string MetadataPath,AssetRecord Record);
// Explicit human-requested, read-only preparation. No approval, worker start or scene mutation.
public static class AssetSourcePlanning
{
    public static Task<SourceImportPlan> PrepareAsync(string root,Guid project,ulong generation,ulong revision,
        AssetScanResult snapshot,string source,int sampleRate,bool staticOnly,CancellationToken cancellation=default)
    {
        AssetPaths.Validate(source);
        if(project==Guid.Empty||generation==0||!source.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase)||sampleRate is <1 or >120)throw new ArgumentException("Invalid source plan.");
        return Task.Run(()=> {
            cancellation.ThrowIfCancellationRequested();var paths=new AssetProjectPaths(root);
            var records=new List<AssetRecord>();for(int offset=0;offset<snapshot.Catalog.Count;offset+=128)records.AddRange(snapshot.Catalog.List(offset,128));
            var old=records.SingleOrDefault(r=>r.SourcePath==source);
            var kind=staticOnly?AssetKind.StaticMesh:AssetKind.Character;
            if(old is not null&&old.Kind!=kind)throw new ArgumentException("Source kind cannot change its existing root identity.");
            string metadata=old is null?source+".ncmeta":snapshot.MetadataFiles?.GetValueOrDefault(old.AssetId)??throw new ArgumentException("Metadata location unavailable.");
            using var parents=new AssetDirectoryLease(paths,[source,metadata]);
            if(old is null&&File.Exists(paths.Resolve(metadata)))throw new ArgumentException("Unknown metadata occupant; no overwrite authority.");
            using var file=WindowsAssetFile.OpenReadLease(paths.Resolve(source,true));
            string hash=file.Hash(AssetDiskTransaction.MaxSourceBytes,cancellation);
            var record=old is null?new AssetRecord(1,Guid.NewGuid(),kind,source,hash,"ncma.ufbx",1,new(1,sampleRate,true),[],[],null):old with{SourceHash=hash,Settings=new(1,sampleRate,true)};
            _=AssetRecordCodec.Encode(record);cancellation.ThrowIfCancellationRequested();
            return new SourceImportPlan(Guid.NewGuid(),project,generation,revision,metadata,record);
        },cancellation);
    }
}
