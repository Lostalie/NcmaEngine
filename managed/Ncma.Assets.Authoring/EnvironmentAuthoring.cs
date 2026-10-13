using Ncma.Assets.Authoring.Storage;
using Ncma.Editor.Core;

namespace Ncma.Assets.Authoring;

// Paths are trusted local authoring metadata; the separate inspection service redacts them.
public sealed record EnvironmentAuthoringEntry(string Path, EnvironmentAssetDescriptor Descriptor);
public sealed class EnvironmentAuthoringCatalog
{
    private readonly Dictionary<Guid,string> _identities;
    public IReadOnlyList<EnvironmentAuthoringEntry> Entries { get; }
    private EnvironmentAuthoringCatalog(List<EnvironmentAuthoringEntry> entries, Dictionary<Guid,string> identities)
    { Entries = entries.OrderBy(e=>e.Descriptor.AssetId).ToList().AsReadOnly(); _identities=identities; }
    public void RequireIdentity(string path, Guid id)
    { if (_identities.TryGetValue(id,out var old) && old != path) throw new EditCommandRejectedException("environment_uuid_collision"); }
    public static EnvironmentAuthoringCatalog Scan(AssetProjectPaths paths, Guid project)
    {
        if(project==Guid.Empty)throw new ArgumentException("Environment project identity.");
        var queue=new Queue<string>();queue.Enqueue("assets/.probe");int count=0,bytes=0;
        var entries=new List<EnvironmentAuthoringEntry>();var identities=new Dictionary<Guid,string>();
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while(queue.TryDequeue(out var probe)) {
            using var parents=new AssetDirectoryLease(paths,[probe]);
            foreach(var file in Directory.EnumerateFileSystemEntries(System.IO.Path.GetDirectoryName(paths.Resolve(probe))!)) {
                if(++count>AssetCatalogScanner.MaxEntries)throw new ArgumentException("Environment catalog entry budget.");
                string relative=System.IO.Path.GetRelativePath(paths.Root,file).Replace('\\','/');paths.Resolve(relative);
                if(!names.Add(relative))throw new ArgumentException("Environment catalog case collision.");
                if(Directory.Exists(file)){queue.Enqueue(relative+"/.probe");continue;}
                if(relative.EndsWith(".journal",StringComparison.Ordinal))throw new EditCommandRejectedException("asset_recovery_required");
                bool environment=relative.EndsWith(".ncenv",StringComparison.Ordinal);
                if(!environment&&!relative.EndsWith(".ncmeta",StringComparison.Ordinal)&&!MaterialCommands.IsPath(relative)&&!relative.EndsWith(".ncmaanim",StringComparison.Ordinal)&&!relative.EndsWith(".ncmaui",StringComparison.Ordinal))continue;
                using var input=WindowsAssetFile.OpenReadLease(file);byte[] data=input.Read(environment?EnvironmentAssetDescriptor.MaxBytes:AssetRecordCodec.MaxBytes);
                bytes=checked(bytes+data.Length);if(bytes>AssetCatalogScanner.MaxTotalDescriptorBytes)throw new ArgumentException("Environment catalog byte budget.");
                void Identity(Guid id){if(!identities.TryAdd(id,relative))throw new ArgumentException("Duplicate environment/asset identity.");}
                if(environment){var d=EnvironmentAssetDescriptor.Decode(data);if(d.ProjectId!=project||entries.Count>=128)throw new ArgumentException("Environment catalog project/budget.");Identity(d.AssetId);entries.Add(new(relative,d));}
                else if(relative.EndsWith(".ncmeta",StringComparison.Ordinal)){var d=AssetRecordCodec.Decode(data);Identity(d.AssetId);foreach(var s in d.Subassets)Identity(s.AssetId);}
                else if(relative.EndsWith(".ncmaterial",StringComparison.Ordinal))Identity(MaterialCodec.Decode(data).AssetId);
                else if(relative.EndsWith(".ncmatset",StringComparison.Ordinal))Identity(MaterialCodec.DecodeSet(data).AssetId);
                else if(relative.EndsWith(".ncmaanim",StringComparison.Ordinal))Identity(Ncma.Animation.AnimationGraphCodec.Decode(data).AssetId);
                else Identity(Ncma.Ui.UiCodec.Decode(data).AssetId);
            }
        }
        return new(entries,identities);
    }
}

// Same immutable-generation visibility boundary as FBX/NCA: preparation can leave retained
// unreferenced cache data; only the reversible .ncenv publication makes a project asset visible.
// Never delete an NCE on Undo, never relax RuntimeReadPin and never overwrite a generation.
public sealed class EnvironmentDerivedStore : IDisposable
{
    private readonly AssetProjectPaths _paths;private readonly Guid _project;private readonly int _thread=Environment.CurrentManagedThreadId;
    private readonly Dictionary<string,(EnvironmentPackage Package,WindowsAssetFile File,AssetDirectoryLease Parents)> _pins=[];
    private bool _disposed;
    public EnvironmentDerivedStore(AssetProjectPaths paths,Guid project){_paths=paths;if(project==Guid.Empty)throw new ArgumentException("Project required.");_project=project;}
    private void Verify(){if(_thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Environment authoring owner required.");ObjectDisposedException.ThrowIf(_disposed,this);}
    public EnvironmentAssetDescriptor Prepare(EnvironmentPackage package,CancellationToken cancellation=default,Action<string>? fault=null)
    {
        Verify();ArgumentNullException.ThrowIfNull(package);cancellation.ThrowIfCancellationRequested();var d=EnvironmentAssetDescriptor.FromPackage(_project,package);
        if(_pins.ContainsKey(d.Path))return d;
        if(_pins.Count>=32||_pins.Values.Sum(p=>(long)p.Package.ByteCount)+package.ByteCount>64L*1024*1024)throw new ArgumentException("Environment retained generation budget.");
        _paths.EnsureCacheDirectory(d.Path[..d.Path.LastIndexOf('/')],derived:true);
        var parents=new AssetDirectoryLease(_paths,[d.Path]);WindowsAssetFile? pin=null;
        try {
            string full=_paths.ResolveDerived(d.Path);
            if(!File.Exists(full)) {
                RequireDiskBudget(package.ByteCount,cancellation);
                string stage=d.Path+"."+Guid.NewGuid().ToString("N")+".prepared";
                using var candidate=WindowsAssetFile.Open(_paths.ResolveDerived(stage),create:true);candidate.Write(package.CopyBytes());
                fault?.Invoke("environment_generation_written");cancellation.ThrowIfCancellationRequested();candidate.Rename(full);fault?.Invoke("environment_generation_published");
            }
            pin=WindowsAssetFile.OpenReadLease(full);var checkedPackage=EnvironmentPackage.Decode(pin.Read(EnvironmentPackage.MaxBytes),d.ContentHash);
            if(checkedPackage.AssetId!=d.AssetId||checkedPackage.Generation!=d.Generation)throw new ArgumentException("Environment generation identity.");
            cancellation.ThrowIfCancellationRequested();_pins.Add(d.Path,(checkedPackage,pin,parents));pin=null;return d;
        }catch{parents.Dispose();throw;}finally{pin?.Dispose();}
    }
    public void RequirePinned(EnvironmentAssetDescriptor d)
    { Verify();d.Validate();if(d.ProjectId!=_project||!_pins.TryGetValue(d.Path,out var pin)||pin.Package.AssetId!=d.AssetId||pin.Package.Generation!=d.Generation||pin.Package.ContentHash!=d.ContentHash)throw new EditCommandRejectedException("environment_generation_not_prepared"); }
    private void RequireDiskBudget(int incoming,CancellationToken cancellation)
    {
        int entries=0,files=0;long bytes=incoming;var queue=new Queue<string>();queue.Enqueue($"out/assets/{_project:N}/.probe");
        while(queue.TryDequeue(out var probe)) {
            cancellation.ThrowIfCancellationRequested();using var lease=new AssetDirectoryLease(_paths,[probe]);
            foreach(string file in Directory.EnumerateFileSystemEntries(System.IO.Path.GetDirectoryName(_paths.ResolveDerived(probe))!)) {
                if(++entries>AssetCatalogScanner.MaxEntries)throw new ArgumentException("Environment retained tree budget.");
                string relative=System.IO.Path.GetRelativePath(_paths.Root,file).Replace('\\','/');_paths.ResolveDerived(relative);
                if(Directory.Exists(file))queue.Enqueue(relative+"/.probe");
                else if(relative.EndsWith(".nce",StringComparison.Ordinal)||relative.Contains(".nce.",StringComparison.Ordinal)) {
                    using var input=WindowsAssetFile.OpenReadLease(file);bytes=checked(bytes+input.Length);
                    if(++files>=32||bytes>64L*1024*1024)throw new ArgumentException("Environment retained disk budget; explicit recovery/collection required.");
                }
            }
        }
    }
    public void Dispose(){if(_disposed)return;Verify();foreach(var pin in _pins.Values){pin.File.Dispose();pin.Parents.Dispose();}_pins.Clear();_disposed=true;}
}

public static class EnvironmentAuthoringFiles
{
    public static EnvironmentPackage ReadPackage(AssetProjectPaths paths,EnvironmentAssetDescriptor descriptor)
    {
        descriptor.Validate();using var parents=new AssetDirectoryLease(paths,[descriptor.Path]);
        using var file=WindowsAssetFile.OpenReadLease(paths.ResolveDerived(descriptor.Path,true));
        var package=EnvironmentPackage.Decode(file.Read(EnvironmentPackage.MaxBytes),descriptor.ContentHash);
        if(package.AssetId!=descriptor.AssetId||package.Generation!=descriptor.Generation)throw new ArgumentException("Environment authoring package identity.");return package;
    }
}

public sealed class EnvironmentPublication : IDisposable
{
    private readonly AssetProjectPaths _paths;private readonly Guid _project;private readonly AssetRevisionClock _clock;
    private readonly Func<string,EnvironmentAssetDescriptor,bool> _approved;private readonly AssetDiskTransaction _disk;
    private readonly int _thread=Environment.CurrentManagedThreadId;private bool _disposed;
    public EnvironmentDerivedStore Generations { get; }
    public EnvironmentPublication(AssetProjectPaths paths,Guid project,AssetRevisionClock clock,Func<string,EnvironmentAssetDescriptor,bool> approved,Action<string>? fault=null)
    { _paths=paths;_project=project;_clock=clock;_approved=approved;_disk=new(paths,fault);Generations=new(paths,project); }
    private void Verify(){if(_thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Environment publication owner required.");ObjectDisposedException.ThrowIf(_disposed,this);}
    private void Require(string path,EnvironmentAssetDescriptor d){Verify();AssetPaths.Validate(path);d.Validate();if(!path.EndsWith(".ncenv",StringComparison.Ordinal)||d.ProjectId!=_project||!_approved(path,d))throw new EditCommandRejectedException("environment_scope_denied",denied:true);}
    public ParticipantMemento Prepare(string path,EnvironmentAssetDescriptor descriptor,ulong revision)
    {
        Require(path,descriptor);if(revision!=_clock.Revision)throw new EditCommandRejectedException("asset_revision_conflict");Generations.RequirePinned(descriptor);
        EnvironmentAuthoringCatalog.Scan(_paths,_project).RequireIdentity(path,descriptor.AssetId);RequireNoJournal(path);
        using var parents=new AssetDirectoryLease(_paths,[path]);string full=_paths.Resolve(path);byte[]? before=null;
        if(Directory.Exists(full))throw new ArgumentException("Environment file target required.");
        if(File.Exists(full)){using var input=WindowsAssetFile.OpenReadLease(full);before=input.Read(EnvironmentAssetDescriptor.MaxBytes);var old=EnvironmentAssetDescriptor.Decode(before);Require(path,old);if(old!=descriptor)throw new ArgumentException("Preset publication never overwrites another generation.");}
        return new(AssetDiskTransaction.Encode([AssetDiskTransaction.Inline(path,before)]),AssetDiskTransaction.Encode([AssetDiskTransaction.Inline(path,descriptor.Encode())]));
    }
    private static string PathOf(ParticipantMemento m){var(a,b)=AssetDiskTransaction.States(m,true);if(a.Length!=1||b.Length!=1||a[0].Path!=b[0].Path||a[0].Blob is not null||b[0].Blob is not null||b[0].Data is null)throw new ArgumentException("Environment memento.");return b[0].Path;}
    public void Authorize(ParticipantMemento m){Verify();string path=PathOf(m);var(a,b)=AssetDiskTransaction.States(m,true);var d=EnvironmentAssetDescriptor.Decode(b[0].Data!);Require(path,d);if(a[0].Data is {} old&&EnvironmentAssetDescriptor.Decode(old)!=d)throw new ArgumentException("Environment history identity.");}
    public void Validate(ParticipantMemento m,bool forward){Authorize(m);RequireNoJournal(PathOf(m));var(_,b)=AssetDiskTransaction.States(m,true);Generations.RequirePinned(EnvironmentAssetDescriptor.Decode(b[0].Data!));_disk.Validate(m,forward);_=checked(_clock.Revision+1);}
    public void Publish(ParticipantMemento m,bool forward){Validate(m,forward);_disk.Publish(m,forward,PathOf(m)+".journal");_clock.Advance();}
    public void Complete(ParticipantMemento m,bool forward){Verify();_disk.Complete();}
    public void Compensate(ParticipantMemento m,bool forward){Verify();_disk.Compensate(m,forward,PathOf(m)+".journal");}
    public void Recover(string path){Verify();AssetPaths.Validate(path);if(!path.EndsWith(".ncenv",StringComparison.Ordinal))throw new ArgumentException("Environment recovery target.");_disk.Recover(path+".journal",Authorize);_clock.Advance();}
    private void RequireNoJournal(string path){string full=_paths.Resolve(path+".journal");if(File.Exists(full)||Directory.Exists(full))throw new EditCommandRejectedException("asset_recovery_required");}
    public void Dispose(){if(_disposed)return;Verify();_disk.Dispose();Generations.Dispose();_disposed=true;}
}
