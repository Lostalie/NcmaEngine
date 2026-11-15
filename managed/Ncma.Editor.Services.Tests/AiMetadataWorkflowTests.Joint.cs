using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Assets.Runtime;
using Ncma.Editor.Core;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;

internal static partial class AiMetadataWorkflowTests
{
    private static string AssetHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void AssetNewFile(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        file.Write(bytes); file.Flush(true);
    }
    // Independent source-import authority, never inferred from AI settings approval.
    private sealed class ImportFixture : IDisposable
    {
        internal readonly Guid Id=Guid.NewGuid(), Project=Guid.NewGuid();
        internal readonly string Root, Metadata, Source;
        internal readonly byte[] Before, SourceBefore, SceneBefore;
        internal readonly EditorSessionOwner Owner;
        internal readonly AiAuthoringWorkspace Ui;
        internal readonly Handler Transport=new();
        internal bool ImportApproved;
        internal AssetProjectAuthoring Assets => Owner.Assets!;
        internal ImportCoordinator Imports => Owner.Imports!;
        internal EditorDomainProposals Domains => Owner.DomainProposals!;
        internal ImportFixture(string output,string repository,string plugins)
        {
            Root=Path.Combine(output,"m87-asset-"+Guid.NewGuid().ToString("N"));
            Source=Path.Combine(Root,"assets/Hero.fbx"); Metadata=Path.Combine(Root,"assets/Hero.ncmeta");
            SourceBefore=File.ReadAllBytes(Path.Combine(repository,"tests/assets/fbx/blender_279_sausage_7400_binary.fbx"));
            AssetNewFile(Source,SourceBefore);
            Before=AssetRecordCodec.Encode(new(1,Id,AssetKind.Character,"assets/Hero.fbx",AssetHash(SourceBefore),
                "ncma.ufbx",1,new(1,60,true),[],[],null));
            AssetNewFile(Metadata,Before);
            // Use the existing full-entry worker/native input convention; absence is a failure, not a skip.
            string configuration=typeof(ImportFixture).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
            Check(configuration is "Debug" or "Release");
            string worker=Path.Combine(repository,"managed/Ncma.Asset.ImportWorker/bin",configuration,"net8.0/Ncma.Asset.ImportWorker.dll");
            string kernel=Path.GetFullPath(Path.Combine(plugins,"../../NcmaImportKernel.dll"));
            string dotnet=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"../../../dotnet.exe"));
            var launch=new ImportWorkerLaunch(dotnet,worker,AssetHash(File.ReadAllBytes(worker)),kernel,AssetHash(File.ReadAllBytes(kernel)));
            Owner=new("M8.7 AI asset joint");
            Owner.ConfigureAssets(Root,Project,1,new AssetWriteScope(["assets/Hero.ncmeta"],["assets/Hero.fbx"],[Id],()=>true));
            Owner.ConfigureImportTools(launch,path=>ImportApproved && path=="assets/Hero.fbx");
            Owner.ConfigureDomainProposals(Project);
            Ui=new(Domains,(snapshot,operation,credential)=>new(snapshot,operation,credential,Transport),metadata:Owner.CreateMetadataContentReview());
            Check(Ui.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions","metadata-model"));
            Transport.Proposal=Draft(Id); SceneBefore=Owner.Document.CaptureBytes();
        }
        internal Guid Enqueue(AssetRecord record) => Imports.Enqueue(record.SourcePath,record.Settings.SampleRate,
            Assets.Clock.Revision,Assets.Generation,record.SourceHash);
        internal ImportJobSnapshot Wait(Guid job)
        {
            var watch=Stopwatch.StartNew();
            while(watch.Elapsed<TimeSpan.FromSeconds(20)) {
                var state=Imports.Inspect(job);
                if(state.State is ImportJobState.Ready or ImportJobState.Failed or ImportJobState.Cancelled)return state;
                Thread.Sleep(10);
            }
            throw new TimeoutException("M87 original import worker did not reach terminal candidate state");
        }
        internal void NoImport(AssetRecord record)
        {
            try { _=Enqueue(record); throw new Exception("AI settings granted import authority"); }
            catch(EditCommandRejectedException error) when(error.Code=="asset_source_denied" && error.Denied) { }
            Check(Imports.Pump().Length==0 && !ImportApproved && record.Generation is null && record.Subassets.Length==0);
        }
        public void Dispose()
        {
            Until(Ui.TryClose); var assets=Assets; var imports=Imports; Owner.Dispose();
            Check(Task.WhenAll(assets.Completion,imports.Completion).Wait(TimeSpan.FromSeconds(10)));
        }
    }
    private static (AssetRecord Record,byte[] Bytes,DomainProposalReceipt Receipt) CommitImportSettings(ImportFixture f)
    {
        var send=f.Ui.PrepareMetadata(f.Id,AssetKind.Character,"Use sample rate 30 and disable tangents; settings only");
        Reject(()=>f.Ui.Send(send,send.Observation.Fingerprint,false)); Check(f.Transport.Calls==0);
        f.Ui.Send(send,send.Observation.Fingerprint,true);
        Until(()=> { f.Domains.PumpModules(); return f.Ui.Status!="pending"; });
        var review=f.Ui.RefreshReview() ?? throw new Exception("Joint metadata review missing");
        Check(f.Transport.Calls==1 && f.Transport.Sent!.SequenceEqual(send.WireRequest.CopyBytes()));
        Check(review.Source==send.Observation.Source && review.ProposalId==send.Observation.RequestId);
        Reject(()=>f.Ui.Confirm(review,review.Fingerprint,false));
        Check(f.Before.SequenceEqual(File.ReadAllBytes(f.Metadata)) && f.Owner.Edit!.State.UndoCount==0 && f.Domains.Receipts.Length==0);
        var result=f.Ui.Confirm(review,review.Fingerprint,true); byte[] bytes=File.ReadAllBytes(f.Metadata);
        var record=AssetRecordCodec.Decode(bytes); var receipt=f.Domains.Receipts.Single();
        Check(result.Status=="ok" && result.Changed && !result.Replayed && result.RequestId==review.ProposalId && result.SessionId==review.Source.EditSessionId);
        Check(receipt.ProposalId==review.ProposalId && receipt.Fingerprint==review.Fingerprint && receipt.Status==result.Status &&
            receipt.Code==result.Code && receipt.Changed==result.Changed && receipt.ExecutionRevision==result.ExecutionRevision && !receipt.RecoveryRequired);
        Check(record.AssetId==f.Id && record.SourceHash==AssetHash(f.SourceBefore) && record.Settings==new ImportSettings(1,30,false) && record.Generation is null);
        Check(f.Owner.Edit!.State.UndoCount==1 && f.SceneBefore.SequenceEqual(f.Owner.Document.CaptureBytes()));
        Reject(()=>f.Ui.Confirm(review,review.Fingerprint,true)); f.NoImport(record);
        Console.WriteLine("M87-JOINT-ASSET-SETTINGS "+JsonSerializer.Serialize(new { send.Observation.RequestId,review.Source,receipt,
            assetId=f.Id,sourceHash=record.SourceHash,wireHash=AssetHash(send.WireRequest.CopyBytes()),
            beforeHash=AssetHash(f.Before),afterHash=AssetHash(bytes),importAuthorized=false }));
        return(record,bytes,receipt);
    }
    internal static IEnumerable<(string,Action)> JointImportCases(string output,string repository,string plugins)
    {
        yield return("M8.7-C2 AI settings separate FBX import closure package and original Undo Redo",()=>JointImportClosure(output,repository,plugins));
        yield return("M8.7-C2 approved AI settings cannot grant source import",()=> {
            using var f=new ImportFixture(output,repository,plugins); var settings=CommitImportSettings(f);
            f.NoImport(settings.Record);
            Check(settings.Bytes.SequenceEqual(File.ReadAllBytes(f.Metadata)) && f.SourceBefore.SequenceEqual(File.ReadAllBytes(f.Source)) && f.Owner.Edit!.State.UndoCount==1);
        });
        yield return("M8.7-C2 changed FBX source rejects import after approved AI settings",()=> {
            using var f=new ImportFixture(output,repository,plugins); var settings=CommitImportSettings(f);
            // Deliberate change in this disposable fixture only, before a worker/source lease exists.
            using(var file=new FileStream(f.Source,FileMode.Append,FileAccess.Write,FileShare.Read))file.WriteByte(0);
            f.ImportApproved=true; Guid job=f.Enqueue(settings.Record); var state=f.Wait(job);
            Check(state.State==ImportJobState.Failed && state.Code=="source_changed" && state.Candidate is null);
            Check(settings.Bytes.SequenceEqual(File.ReadAllBytes(f.Metadata)) && f.Owner.Edit!.State.UndoCount==1 && f.Domains.Receipts.Length==1);
            Check(f.SceneBefore.SequenceEqual(f.Owner.Document.CaptureBytes()));
            Console.WriteLine("M87-JOINT-ASSET-REJECT "+JsonSerializer.Serialize(new { job,state.Code,settings.Receipt,
                originalSourceHash=settings.Record.SourceHash,actualSourceHash=AssetHash(File.ReadAllBytes(f.Source)),importCommitted=false }));
        });
    }
    private static void JointImportClosure(string output,string repository,string plugins)
    {
        using var f=new ImportFixture(output,repository,plugins); var settings=CommitImportSettings(f);
        var workspace=new EditorWorkspace(f.Owner);
        workspace.History(workspace.Stamp,false); Check(f.Before.SequenceEqual(File.ReadAllBytes(f.Metadata)));
        workspace.History(workspace.Stamp,true); Check(settings.Bytes.SequenceEqual(File.ReadAllBytes(f.Metadata)));
        f.NoImport(settings.Record);
        // Trusted host explicitly authorizes the exact source, separately from the AI proposal.
        f.ImportApproved=true; Guid job=f.Enqueue(settings.Record); var state=f.Wait(job);
        Check(state.State==ImportJobState.Ready && state.Candidate is not null);
        var candidate=state.Candidate!;
        Check(candidate.JobId==job && candidate.ProjectId==f.Project && candidate.SourceHash==settings.Record.SourceHash &&
            candidate.SampleRate==30 && !candidate.StaticOnly && candidate.AssetRevision==f.Assets.Clock.Revision);
        Guid ticket=f.Assets.Imports.PrepareAsync(f.Imports,job,"assets/Hero.ncmeta",settings.Record);
        var watch=Stopwatch.StartNew();
        while(!f.Assets.Imports.IsPrepared(ticket) && watch.Elapsed<TimeSpan.FromSeconds(15))Thread.Sleep(5);
        var prepared=f.Assets.Imports.InspectPrepared(ticket);
        Check(prepared.Ticket==ticket && prepared.JobId==job && prepared.Record.AssetId==f.Id && prepared.Record.Generation is not null);
        Check(settings.Bytes.SequenceEqual(File.ReadAllBytes(f.Metadata)) && f.Owner.Edit!.State.UndoCount==1);
        var edit=f.Owner.Edit!;
        var request=new CapabilityRequest(2,Guid.NewGuid(),edit.SessionId,edit.Revision,AssetImportCommands.CapabilityName,
            JsonSerializer.SerializeToElement(new { ticket,expectedAssetRevision=f.Assets.Clock.Revision,projectGeneration=f.Assets.Generation,confirmNewIdentities=true }));
        var permissions=new CapabilityPermissions([AssetImportCommands.CapabilityName]);
        var imported=edit.Invoke(request,permissions);
        Check(imported.Status=="ok" && imported.Changed && !imported.Replayed && imported.RequestId==request.RequestId && imported.ExecutionRevision>settings.Receipt.ExecutionRevision);
        byte[] metadata=File.ReadAllBytes(f.Metadata); var record=AssetRecordCodec.Decode(metadata);
        Check(metadata.SequenceEqual(AssetRecordCodec.Encode(prepared.Record)) && edit.State.UndoCount==2);
        var replay=edit.Invoke(request,permissions); Check(replay.Replayed && replay.ExecutionRevision==imported.ExecutionRevision && edit.State.UndoCount==2);
        byte[] derived=File.ReadAllBytes(Path.Combine(f.Root,record.Generation!.RelativePath));
        Check(AssetHash(derived)==record.Generation.ContentHash);
        var manifest=ModelAssetManifestCodec.ValidateBundle(derived,record);
        Check(manifest.Settings==settings.Record.Settings && manifest.SourceHash==settings.Record.SourceHash && manifest.Skeleton is not null && manifest.Clips.Length>0);
        AssetRef[] required=[new(new(f.Id),AssetKind.Character)];
        using var snapshot=RuntimeAssetLoader.Prepare(f.Root,f.Project,required);
        using var lease=snapshot.AcquireLease();
        var expected=record.Subassets.Where(s=>!s.Tombstone).Select(s=>new AssetRef(new(s.AssetId),s.Kind)).Prepend(required[0]).ToArray();
        Check(lease.List().Count==expected.Length && expected.All(r=>lease.TryResolve(r.Id.Value,r.ExpectedKind,out _)));
        byte[] package=RuntimeAssetPackage.Encode(lease); var index=RuntimeAssetPackage.Inspect(package,f.Project);
        Check(index.Assets.Length==expected.Length && index.Assets.All(a=>a.Generation==record.Generation.Number &&
            lease.Require(a.AssetId,a.Kind).ContentHash==a.Hash));
        string packaged=Path.Combine(output,"m87-package-"+Guid.NewGuid().ToString("N"));
        AssetNewFile(Path.Combine(packaged,"assets/Hero.ncpak"),package);
        using(var reopened=RuntimeAssetPackage.Prepare(packaged,"assets/Hero.ncpak",f.Project,required))
        using(var view=reopened.AcquireLease())Check(RuntimeAssetPackage.Encode(view).SequenceEqual(package));
        // Cold runtime inputs intentionally omit FBX: loading/packaging must not silently reimport.
        string Cold(AssetRecord descriptor,byte[]? generation) {
            string root=Path.Combine(output,"m87-cold-"+Guid.NewGuid().ToString("N"));
            AssetNewFile(Path.Combine(root,"assets/Hero.ncmeta"),AssetRecordCodec.Encode(descriptor));
            if(generation is not null)AssetNewFile(Path.Combine(root,descriptor.Generation!.RelativePath),generation);
            return root;
        }
        string cold=Cold(record,derived);
        using(var reopened=RuntimeAssetLoader.Prepare(cold,f.Project,required))
        using(var view=reopened.AcquireLease())Check(RuntimeAssetPackage.Encode(view).SequenceEqual(package));
        string missing=Cold(record,null);
        Reject(()=> { using var rejected=RuntimeAssetLoader.Prepare(missing,f.Project,required); });
        byte[] damaged=(byte[])derived.Clone(); damaged[^1]^=1; string corrupt=Cold(record,damaged);
        Reject(()=> { using var rejected=RuntimeAssetLoader.Prepare(corrupt,f.Project,required); });
        Guid absent=Guid.NewGuid(); string dependency=Cold(record with { Dependencies=[.. record.Dependencies,new(absent,AssetKind.Texture)] },derived);
        Reject(()=> { using var rejected=RuntimeAssetLoader.Prepare(dependency,f.Project,required); });
        byte[] damagedPackage=(byte[])package.Clone(); damagedPackage[^1]^=1;
        Reject(()=>RuntimeAssetPackage.Inspect(damagedPackage,f.Project)); Reject(()=>RuntimeAssetPackage.Inspect(package,Guid.NewGuid()));
        Reject(()=> { using var rejected=RuntimeAssetPackage.Prepare(packaged,"assets/Hero.ncpak",f.Project,[new(new(f.Id),AssetKind.Texture)]); });
        workspace.History(workspace.Stamp,false); Check(settings.Bytes.SequenceEqual(File.ReadAllBytes(f.Metadata)) && edit.State.UndoCount==1);
        Check(RuntimeAssetPackage.Encode(lease).SequenceEqual(package)); // immutable old runtime generation survives authoring Undo
        workspace.History(workspace.Stamp,false); Check(f.Before.SequenceEqual(File.ReadAllBytes(f.Metadata)) && edit.State.UndoCount==0);
        workspace.History(workspace.Stamp,true); Check(settings.Bytes.SequenceEqual(File.ReadAllBytes(f.Metadata)));
        workspace.History(workspace.Stamp,true); Check(metadata.SequenceEqual(File.ReadAllBytes(f.Metadata)) && edit.State.UndoCount==2);
        Check(f.SourceBefore.SequenceEqual(File.ReadAllBytes(f.Source)) && derived.SequenceEqual(File.ReadAllBytes(Path.Combine(f.Root,record.Generation.RelativePath))) &&
            f.SceneBefore.SequenceEqual(f.Owner.Document.CaptureBytes()) && f.Owner.Play is null && f.Domains.Receipts.Length==1);
        Console.WriteLine("M87-JOINT-ASSET-CLOSURE "+JsonSerializer.Serialize(new { assetId=f.Id,projectId=f.Project,settingsReceipt=settings.Receipt,
            importRequest=request.RequestId,importResult=new { imported.RequestId,imported.SessionId,imported.Status,imported.Code,imported.Changed,imported.ExecutionRevision,imported.Replayed },
            job,ticket,sourceHash=record.SourceHash,settingsHash=AssetHash(settings.Bytes),
            metadataHash=AssetHash(metadata),generation=record.Generation.Number,generationHash=record.Generation.ContentHash,
            packageHash=AssetHash(package),closure=index.Assets.Select(a=>new {a.AssetId,a.Kind,a.Generation,a.Hash}),
            coldWithoutFbx=true,negativeControls=6,stageAccepted=false }));
    }
}
