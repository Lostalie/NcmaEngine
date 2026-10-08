using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Runtime;
using Ncma.Scene.Rendering;

internal static class M36WorkflowTests
{
    private static void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or IOException){return;}throw new InvalidOperationException("Expected rejection.");}
    private static void Wait(EditorAssetWorkflow flow,EditorSessionOwner owner,Func<bool> finished,bool refreshAssets=true)
    {
        var watch=Stopwatch.StartNew();while(watch.Elapsed<TimeSpan.FromSeconds(45)){if(refreshAssets)owner.RefreshAssets();flow.Pump();if(finished())return;Thread.Sleep(10);}throw new InvalidOperationException("Workflow timeout: "+flow.Code);
    }
    private static void PreparePlacement(EditorAssetWorkflow flow,EditorSessionOwner owner,EditorWorkspace workspace,Guid root,AssetKind kind,Vector3 position)
    {
        // Undo/Redo publications can still deliver delayed watcher events. A revision change
        // correctly rejects the old plan; this trusted fixture explicitly requests a fresh one,
        // never retries a commit or relaxes the production stale-plan check.
        for(int attempt=0;attempt<8;attempt++){
            owner.Assets!.Refresh(owner.Assets.Generation,force:true);flow.PreparePlacement(workspace.Stamp,root,kind,position);
            if(attempt==0)owner.Assets!.Clock.Advance(); // deterministic stale-preparation oracle, no concurrent file scan
            // Metadata scans request transaction-style DELETE leases, incompatible with the
            // background immutable reader. Pump completion only; refresh before each new request.
            Wait(flow,owner,()=>flow.PlacementPlan is not null||!flow.PreparingPlacement&&flow.Code=="asset_placement_stale",refreshAssets:false);
            if(attempt==0)Check(flow.PlacementPlan is null&&flow.Code=="asset_placement_stale","Stale preparation must be rejected before retry.");
            if(flow.PlacementPlan is not null)return;
            Check(flow.Code=="asset_placement_stale","Unexpected placement preparation rejection: "+flow.Code);
        }
        throw new InvalidOperationException("Placement fixture exceeded bounded watcher-revision retries.");
    }
    public static void Run(string repository,string plugins,string output)
    {
        string projectRoot=Path.Combine(output,"m3-6-project");Directory.CreateDirectory(Path.Combine(projectRoot,"assets"));
        File.Copy(Path.Combine(repository,"tests/assets/fbx/ncma_static_asymmetric_7400_ascii.fbx"),Path.Combine(projectRoot,"assets/Static.fbx"));
        File.Copy(Path.Combine(repository,"tests/assets/fbx/blender_279_sausage_7400_binary.fbx"),Path.Combine(projectRoot,"assets/Hero.fbx"));
        string configuration=plugins.Contains("windows-ninja-release",StringComparison.OrdinalIgnoreCase)?"Release":"Debug";
        string worker=Path.Combine(repository,"managed/Ncma.Asset.ImportWorker/bin",configuration,"net8.0/Ncma.Asset.ImportWorker.dll");
        string kernel=Path.GetFullPath(Path.Combine(plugins,"../../NcmaImportKernel.dll"));
        string dotnet=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"../../../dotnet.exe"));
        string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        Guid project=Guid.NewGuid();string scenePath=Path.Combine(projectRoot,"Main.ncmascene");Guid modelId,meshId,objectId,heroObject;
        AssetProjectAuthoring? closingAssets=null;
        using(var owner=new EditorSessionOwner("Asset UI",components:RenderComponentRegistry.CreateRegistry(),validateComposition:SceneRenderValidation.RequireComposition)) {
            var workspace=new EditorWorkspace(owner);using var flow=new EditorAssetWorkflow(workspace,projectRoot,project,1);
            owner.ConfigureAssets(projectRoot,project,1,flow.Scope);owner.ConfigureImportTools(new(dotnet,worker,Hash(worker),kernel,Hash(kernel)),flow.IsSourceApproved);
            Reject(()=>flow.PrepareSource(workspace.Stamp,"../outside.fbx",30,true));
            flow.PrepareSource(workspace.Stamp,"assets/Static.fbx",30,true);Wait(flow,owner,()=>flow.SourcePlan is not null);
            var plan=flow.SourcePlan!;modelId=plan.Record.AssetId;Check(!File.Exists(Path.Combine(projectRoot,plan.MetadataPath))&&!flow.IsSourceApproved(plan.Record.SourcePath),"Read-only planning created/approved metadata");
            var presenter=new EditorPresenter(workspace,null,projectRoot,assetWorkflow:flow);presenter.Build(1,1280,720);
            // Disabled approval cannot be forged by merely dispatching its current widget.
            var f=presenter.Frame;var begin=presenter.Items.ToArray().Single(i=>i.WidgetHigh==17&&i.WidgetLow==8);
            presenter.Apply([new(){Kind=begin.Kind,WidgetHigh=17,WidgetLow=8,Phase=3,Frame=f.Frame,ViewGeneration=f.ViewGeneration,DocumentGeneration=f.DocumentGeneration,Revision=f.Revision}],[]);
            Check(flow.Job is null,"Unreviewed UI import dispatched");
            var review=presenter.Items.ToArray().Single(i=>i.WidgetHigh==17&&i.WidgetLow==7);
            presenter.Apply([new(){Kind=review.Kind,WidgetHigh=17,WidgetLow=7,Phase=3,Value=1,Frame=f.Frame,ViewGeneration=f.ViewGeneration,DocumentGeneration=f.DocumentGeneration,Revision=f.Revision}],[]);
            presenter.Build(2,1280,720);f=presenter.Frame;
            presenter.Apply([new(){Kind=begin.Kind,WidgetHigh=17,WidgetLow=8,Phase=3,Frame=f.Frame,ViewGeneration=f.ViewGeneration,DocumentGeneration=f.DocumentGeneration,Revision=f.Revision}],[]);
            Wait(flow,owner,()=>flow.Job?.State is ImportJobState.Ready or ImportJobState.Failed);Check(flow.Job!.State==ImportJobState.Ready,flow.Job.Code);
            flow.PrepareCommit(flow.Job.JobId);Wait(flow,owner,()=>flow.Prepared is not null);var prepared=flow.Prepared!;
            Check(prepared.Record.Subassets.Length>0,"No typed subasset report");flow.CommitImport(workspace.Stamp,prepared.Ticket,true);owner.RefreshAssets();
            var record=flow.Inspect(modelId,AssetKind.StaticMesh);meshId=record.Subassets.Single(s=>s.Kind==AssetKind.StaticMesh&&!s.Tombstone).AssetId;
            Check(record.Generation?.Number==1&&flow.Count==1,"Imported generation not visible");
            // Same session history owns both metadata and scene commands.
            workspace.History(workspace.Stamp,false);owner.RefreshAssets();Check(flow.Count==0,"Asset Undo did not restore catalog");
            workspace.History(workspace.Stamp,true);owner.RefreshAssets();Check(flow.Count==1,"Asset Redo did not restore catalog");
            PreparePlacement(flow,owner,workspace,modelId,AssetKind.StaticMesh,new(1,2,-3));
            var placement=flow.PlacementPlan!;objectId=placement.Objects[0];placement.Objects[0]=Guid.NewGuid();placement.Operations[0]=new{op="create",objectId=Guid.NewGuid(),name="Tampered"};
            flow.CommitPlacement(placement.PlanId);Check(owner.Document.World.FindObject(objectId).Get<StaticMeshData>().MeshId==meshId,"Owned placement plan was modified by caller");
            Check(owner.Document.World.FindObject(objectId).Get<TransformData>().Position==new System.Numerics.Vector3(1,2,-3),"Placement not baked into flat transform");
            int count=owner.Document.World.Count;workspace.History(workspace.Stamp,false);Check(owner.Document.World.Count==0,"Placement must be one history entry");workspace.History(workspace.Stamp,true);Check(owner.Document.World.Count==count,"Scene placement Redo");
            flow.Assign(workspace.Stamp,objectId,meshId,AssetKind.StaticMesh);Reject(()=>flow.Assign(workspace.Stamp,objectId,modelId,AssetKind.StaticMesh));
            Guid materials=record.Subassets.Single(s=>s.Kind==AssetKind.MaterialSet&&!s.Tombstone).AssetId;
            Check(owner.Assets!.Imports.Generations.MaterialSlots(record,materials)>=owner.Assets.Imports.Generations.MaterialSlots(record,meshId),"Cached material slot coverage");
            Reject(()=>owner.Assets.Imports.Generations.MaterialSlots(record,modelId));
            // Deterministic contention fixture: preparation holds its disk/publication lock.
            // Cached presentation/role reads must finish before that lock is released.
            using(var held=new DerivedGenerationStore(new(projectRoot),project)) {
                held.Prepare(record,null);
                ModelImportPlan next;
                using(var importer=new Ncma.Asset.Import.ImportKernel(kernel,Hash(kernel)))next=ModelImportPlanner.Build(importer.LoadAndCopy(Path.Combine(projectRoot,"assets/Static.fbx"),30,staticOnly:true),record with{AssetId=Guid.NewGuid(),Subassets=[],Generation=null},project,true);
                using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
                var preparing=Task.Run(()=>held.Prepare(next.Record,next.DerivedBytes,fault:phase=>{if(phase=="generation_written"){entered.Set();if(!release.Wait(TimeSpan.FromSeconds(10)))throw new IOException("Contention fixture release timeout");}}));
                Task reading=Task.CompletedTask;
                try {Check(entered.Wait(TimeSpan.FromSeconds(5)),"Preparation lock fixture not entered");reading=Task.Run(()=>{held.RequirePinned(record);Check(held.RetainedUsage.Count==1,"Unpublished generation leaked into browser");Check(held.CopyManifest(record).Root==record.AssetId&&held.MaterialSlots(record,materials)>0,"Cached role reads");});Check(reading.Wait(TimeSpan.FromSeconds(2)),"Browser/role read waited on background disk lock");}
                finally{release.Set();preparing.GetAwaiter().GetResult();reading.GetAwaiter().GetResult();}
                Check(held.RetainedUsage.Count==2,"Prepared generation index was not published");
            }
            flow.Assign(workspace.Stamp,objectId,materials,AssetKind.MaterialSet);
            Check(owner.Document.World.FindObject(objectId).Get<StaticMeshData>().MaterialSetId==materials,"Typed material set assignment");
            PreparePlacement(flow,owner,workspace,modelId,AssetKind.StaticMesh,Vector3.Zero);
            var stale=flow.PlacementPlan!;workspace.CreateObject(workspace.Stamp);Reject(()=>flow.CommitPlacement(stale.PlanId));workspace.History(workspace.Stamp,false);
            workspace.Save(workspace.Stamp,scenePath);
            workspace.PlayControl(workspace.Stamp,"start");Reject(()=>flow.PrepareSource(workspace.Stamp,"assets/Hero.fbx",6,false));Reject(()=>flow.Assign(workspace.Stamp,objectId,meshId,AssetKind.StaticMesh));workspace.PlayControl(workspace.Stamp,"stop");
            flow.ForgetImport(flow.Job!.JobId);
            flow.PrepareSource(workspace.Stamp,"assets/Hero.fbx",6,false);Wait(flow,owner,()=>flow.SourcePlan is not null);flow.BeginImport(flow.SourcePlan!.PlanId);
            Wait(flow,owner,()=>flow.Job?.State is ImportJobState.Ready or ImportJobState.Failed);Check(flow.Job!.State==ImportJobState.Ready,flow.Job.Code);
            flow.PrepareCommit(flow.Job.JobId);Wait(flow,owner,()=>flow.Prepared is not null);var hero=flow.Prepared!;
            flow.CommitImport(workspace.Stamp,hero.Ticket,true);owner.RefreshAssets();
            PreparePlacement(flow,owner,workspace,hero.Record.AssetId,AssetKind.Character,new(0,0,-3));var heroPlacement=flow.PlacementPlan!;heroObject=heroPlacement.Objects[0];flow.CommitPlacement(heroPlacement.PlanId);
            var character=owner.Document.World.FindObject(heroObject);Check(character.Has<SkinnedMeshData>()&&character.Has<ClipPlaybackData>(),"Character placement missing skin/clip components");
            var clip=hero.Record.Subassets.First(s=>s.Kind==AssetKind.Clip&&!s.Tombstone);flow.Assign(workspace.Stamp,heroObject,clip.AssetId,AssetKind.Clip);
            Check(owner.Document.World.FindObject(heroObject).Get<ClipPlaybackData>().ClipId==clip.AssetId,"Typed clip selection");Reject(()=>flow.Assign(workspace.Stamp,objectId,clip.AssetId,AssetKind.Clip));
            workspace.Save(workspace.Stamp,scenePath);
            string metadata=Path.Combine(projectRoot,"assets/Hero.fbx.ncmeta"),beforeCancel=Hash(metadata);
            // Cached browser copies still work when metadata is locked: no per-frame disk reads.
            using(var locked=new FileStream(metadata,FileMode.Open,FileAccess.ReadWrite,FileShare.None))for(ulong index=3;index<35;index++)presenter.Build(index,1280,720);
            flow.ForgetImport(flow.Job!.JobId);flow.PrepareSource(workspace.Stamp,"assets/Hero.fbx",6,false);Wait(flow,owner,()=>flow.SourcePlan is not null);flow.BeginImport(flow.SourcePlan!.PlanId);
            flow.CancelImport(flow.Job!.JobId);if(!flow.CanForget){Reject(()=>flow.ForgetImport(flow.Job.JobId));Check(flow.Job is not null,"Busy forget discarded current job");}
            Wait(flow,owner,()=>flow.Job?.State==ImportJobState.Cancelled&&flow.CanForget);Check(Hash(metadata)==beforeCancel,"Cancelled reimport replaced metadata");
            // Reimport review locates existing built-in scene references without parsing whole documents.
            flow.ForgetImport(flow.Job!.JobId);flow.PrepareSource(workspace.Stamp,"assets/Hero.fbx",6,false);Wait(flow,owner,()=>flow.SourcePlan is not null);flow.BeginImport(flow.SourcePlan!.PlanId);
            Wait(flow,owner,()=>flow.Job?.State is ImportJobState.Ready or ImportJobState.Failed);Check(flow.Job!.State==ImportJobState.Ready,flow.Job.Code);
            flow.PrepareCommit(flow.Job.JobId);Wait(flow,owner,()=>flow.Prepared is not null);
            Check(flow.AffectedRenderObjects(flow.Prepared!).Contains(heroObject),"Reimport reference report did not include existing character");
            Check(flow.RetainedUsage.Count>=2&&flow.RetainedUsage.Bytes>0,"Generation retention usage");flow.CancelImport(flow.Job.JobId);
            // A late read-only result must not become a new document's approved plan.
            flow.PrepareSource(workspace.Stamp,"assets/Hero.fbx",6,false);workspace.New(workspace.Stamp,true);Wait(flow,owner,()=>!flow.PreparingSource);Check(flow.SourcePlan is null,"Late source plan survived document replacement");
            closingAssets=owner.Assets;
        }
        Check(closingAssets!.Completion.Wait(TimeSpan.FromSeconds(10)),"Authoring owner shutdown did not release project lock");
        using(var reopened=new EditorSessionOwner("Reopen",components:RenderComponentRegistry.CreateRegistry(),validateComposition:SceneRenderValidation.RequireComposition)) {
            var workspace=new EditorWorkspace(reopened);workspace.Open(workspace.Stamp,scenePath,true);reopened.Edit!.Resynchronize();
            reopened.ConfigureAssets(projectRoot,project,2);reopened.PrepareRenderAssets(projectRoot,project);
            Check(reopened.Document.World.FindObject(objectId).Get<StaticMeshData>().MeshId==meshId,"Scene restart lost UUID reference");
            Check(reopened.RenderAssets!.Assets.RequireMesh(meshId,AssetKind.StaticMesh).Generation==1,"Restart reparsed or lost generation");
            Check(reopened.Document.World.FindObject(heroObject).Has<ClipPlaybackData>(),"Restart lost character animation references");closingAssets=reopened.Assets;
        }
        Check(closingAssets!.Completion.Wait(TimeSpan.FromSeconds(10)),"Reopened owner shutdown");
        // Same application entry services render this newly authored scene through the real offscreen GUI path.
        string appPlugins=Path.Combine(output,"m3-6-app-plugins");Directory.CreateDirectory(appPlugins);
        foreach(string name in new[]{"NcmaPlatform.dll","NcmaRenderer.dll","NcmaGui.dll","glfw3.dll","NcmaNative.dll","NcmaAnimationKernel.dll"}) {
            string path=Path.Combine(plugins,name);if(!File.Exists(path))path=Path.GetFullPath(Path.Combine(plugins,"../..",name));File.Copy(path,Path.Combine(appPlugins,name));
        }
        File.Copy(Path.Combine(repository,"out/managed/Ncma.Gameplay.Sample.dll"),Path.Combine(projectRoot,"Gameplay.dll"));
        string projectPath=Path.Combine(projectRoot,"Project.ncmaproject");
        File.WriteAllText(projectPath,JsonSerializer.Serialize(new ProjectConfiguration(1,project,"M3.6 workflow","Main.ncmascene","Gameplay.dll","Direct3D11",[]),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        using var lifetime=new ApplicationLifetime();var presentation=new CandidatePresentation(appPlugins,ProjectContext.Load(projectPath),true,false,false);lifetime.Start([presentation]);
        Check(lifetime.Run(new MonotonicClock(),presentation,3)==3,"Real Editor offscreen scene frames");
    }
    public static void CameraAndPicking()
    {
        var browser=new EditorOrbitCamera();var before=browser.View(256,256);browser.Orbit(.3f,.2f);browser.Pan(1,2);browser.Zoom(8);Check(browser.View(256,256)!=before&&browser.Revision==3,"Independent browser state");
        Reject(()=>browser.Orbit(float.NaN,0));Reject(()=>browser.Zoom(0));
        var camera=new SceneCameraView(Guid.Empty,Matrix4x4.CreateLookAt(new(0,0,5),Vector3.Zero,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,1,.1f,100),new(0,0,5),CameraData.Default);
        var ray=BoundsPicking.Ray(camera,.5f,.5f);Check(ray.Direction.Z<-.99,"Right-handed pick unprojection");
        Check(BoundsPicking.Hit(new(-1),new(1),Matrix4x4.CreateScale(2,1,1),ray.Origin,ray.Direction,out float distance)&&distance>0,"Ray/local bounds intersection");
        Check(!BoundsPicking.Hit(new(-1),new(1),Matrix4x4.CreateTranslation(10,0,0),ray.Origin,ray.Direction,out _),"Ray missed displaced bounds");
        Reject(()=>BoundsPicking.Ray(camera,2,0));
    }
}
