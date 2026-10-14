using System.Security.Cryptography;
using Ncma.Assets.Authoring.Storage;
using Ncma.Editor.Core;

namespace Ncma.Assets.Authoring;

// Trusted semantic decoder is supplied by the UI composition layer. Storage itself has no
// rendering/native dependency and cannot manufacture action/resource authority.
public delegate void UiGenerationValidator(ReadOnlySpan<byte> bytes,Guid project,UiPackageReference reference);

// Immutable preparation ONLY: no scene/project selection, World mutation or second history.
// A failed/undone selection may leave bounded immutable generations for explicit later GC.
public sealed class UiPackageGenerationStore : IDisposable
{
    public const int MaxBytes=64*1024*1024,MaxGenerations=32;
    public const long MaxRetainedBytes=256L*1024*1024;
    private readonly AssetProjectPaths _paths;private readonly Guid _project;
    private readonly Func<UiPackageReference,bool> _approved;private readonly UiGenerationValidator _validate;
    private readonly Dictionary<string,(WindowsAssetFile File,AssetDirectoryLease Parents)> _pins=[];
    private readonly int _owner=Environment.CurrentManagedThreadId;private bool _busy,_disposed;
    public UiPackageGenerationStore(AssetProjectPaths paths,Guid project,Func<UiPackageReference,bool> approved,UiGenerationValidator validate)
    {
        if(project==Guid.Empty)throw new ArgumentException("UI generation project required.");
        _paths=paths??throw new ArgumentNullException(nameof(paths));_project=project;
        _approved=approved??throw new ArgumentNullException(nameof(approved));_validate=validate??throw new ArgumentNullException(nameof(validate));
    }
    private void Verify(){if(_owner!=Environment.CurrentManagedThreadId||_busy)throw new InvalidOperationException("UI generation owner/nonreentry boundary required.");ObjectDisposedException.ThrowIf(_disposed,this);}
    public static UiPackageReference Reference(Guid project,Guid document,ulong generation,string hash)
    {
        if(project==Guid.Empty)throw new ArgumentException("UI project identity required.");
        hash=new UiPackageReference(1,document,generation,"assets/validation.ncupak",hash).Validate().Hash;
        var result=new UiPackageReference(1,document,generation,$"assets/hudgen-{project:N}-{document:N}-{generation}-{hash}.ncupak",hash).Validate();
        return result;
    }
    private void Require(UiPackageReference reference)
    {if(!_approved(reference))throw new EditCommandRejectedException("ui_generation_scope_denied",denied:true);}
    public UiPackageReference Prepare(Guid document,ulong generation,ReadOnlySpan<byte> input,CancellationToken cancellation=default,Action<string>? fault=null)
    {
        Verify();if(input.Length is <52 or >MaxBytes)throw new ArgumentException("UI package byte budget.");
        _busy=true;
        try{
            cancellation.ThrowIfCancellationRequested();byte[] bytes=input.ToArray();
            var reference=Reference(_project,document,generation,Convert.ToHexString(SHA256.HashData(bytes)));
            Require(reference);_validate(bytes,_project,reference);Require(reference);cancellation.ThrowIfCancellationRequested();
            if(_pins.ContainsKey(reference.Path))return reference;
            if(_pins.Count>=MaxGenerations)throw new ArgumentException("UI generation pin budget.");
            var parents=new AssetDirectoryLease(_paths,[reference.Path]);WindowsAssetFile? pin=null;
            try{
                string full=_paths.Resolve(reference.Path);
                // Do not allow another payload to reuse a persistent document/generation number.
                long retained=0;int count=0,entries=0;bool exists=File.Exists(full);
                string prefix=$"hudgen-{_project:N}-",identity=$"{prefix}{document:N}-{generation}-";
                foreach(string path in Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(full)!)){
                    if(++entries>AssetCatalogScanner.MaxEntries)throw new ArgumentException("UI generation directory budget.");
                    string name=Path.GetFileName(path);if(!name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))continue;
                    _paths.Resolve("assets/"+name);
                    using var file=WindowsAssetFile.OpenReadLease(path);retained=checked(retained+file.Length);count++;
                    if(name.StartsWith(identity,StringComparison.OrdinalIgnoreCase)&&!StringComparer.Ordinal.Equals(path,full))throw new EditCommandRejectedException("ui_generation_conflict_or_recovery_required");
                }
                if(count+(exists?0:1)>MaxGenerations||retained+(exists?0:bytes.Length)>MaxRetainedBytes)throw new ArgumentException("UI retained generation budget; explicit collection required.");
                if(!exists){
                    string stage=reference.Path+"."+Guid.NewGuid().ToString("N")+".prepared";
                    using(var file=WindowsAssetFile.Open(_paths.Resolve(stage),create:true)){
                        file.Write(bytes);fault?.Invoke("ui_generation_written");Require(reference);cancellation.ThrowIfCancellationRequested();
                        file.Rename(full);fault?.Invoke("ui_generation_published");
                    }
                }
                pin=WindowsAssetFile.OpenReadLease(full);byte[] stored=pin.Read(MaxBytes);
                if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(stored),SHA256.HashData(bytes)))throw new EditCommandRejectedException("ui_generation_hash_conflict");
                _validate(stored,_project,reference);Require(reference);cancellation.ThrowIfCancellationRequested();
                _pins.Add(reference.Path,(pin,parents));pin=null;return reference;
            }catch{parents.Dispose();throw;}finally{pin?.Dispose();}
        }finally{_busy=false;}
    }
    public void RequirePinned(UiPackageReference reference)
    {
        Verify();reference=reference.Validate();if(reference!=Reference(_project,reference.DocumentId,reference.Generation,reference.Hash))throw new ArgumentException("UI generation canonical identity.");
        _busy=true;try{Require(reference);if(!_pins.ContainsKey(reference.Path))throw new EditCommandRejectedException("ui_generation_not_prepared");}finally{_busy=false;}
    }
    // Exact retained stage only, explicitly reapproved after a failed preparation. No enumeration
    // deletion, overwrite, fallback content or automatic startup repair is performed.
    public UiPackageReference RecoverPrepared(UiPackageReference reference,string preparedPath,CancellationToken cancellation=default)
    {
        Verify();reference=reference.Validate();if(reference!=Reference(_project,reference.DocumentId,reference.Generation,reference.Hash))throw new ArgumentException("UI recovery identity.");
        string prefix=reference.Path+".";
        if(!preparedPath.StartsWith(prefix,StringComparison.Ordinal)||!preparedPath.EndsWith(".prepared",StringComparison.Ordinal)||
            !Guid.TryParseExact(preparedPath[prefix.Length..^9],"N",out _))throw new ArgumentException("Exact owned preparation stage required.");
        byte[] bytes;_busy=true;
        try{
            Require(reference);cancellation.ThrowIfCancellationRequested();using var parents=new AssetDirectoryLease(_paths,[reference.Path,preparedPath]);
            using var file=WindowsAssetFile.Open(_paths.Resolve(preparedPath,true));bytes=file.Read(MaxBytes);
            if(Convert.ToHexString(SHA256.HashData(bytes))!=reference.Hash)throw new EditCommandRejectedException("ui_recovery_hash_conflict");
            _validate(bytes,_project,reference);Require(reference);cancellation.ThrowIfCancellationRequested();file.Rename(_paths.Resolve(reference.Path));
        }finally{_busy=false;}
        return Prepare(reference.DocumentId,reference.Generation,bytes,cancellation);
    }
    public void Dispose(){if(_disposed)return;Verify();foreach(var pin in _pins.Values){pin.File.Dispose();pin.Parents.Dispose();}_pins.Clear();_disposed=true;}
}
