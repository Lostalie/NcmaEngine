using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Scene;
using Ncma.Assets;
using System.Numerics;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Ncma.Asset.Import;
using Ncma.Assets.Authoring;
internal static unsafe partial class Program
{
    private static long _cachedThreadBytes, _cachedProcessBytes;
    private static double _cachedElapsedMs;
    private static StaticMeshDraw _oldDeviceDraw;
    private static ResourceDraw _oldResourceDraw;
    static void Check(bool value,string message) { if(!value) { Console.Error.WriteLine("FAIL assertion: "+message);throw new Exception(message); } }
    static void Reject(Action action,string? code=null)
    {
        try {action();} catch(RenderGraphException e) { Check(code is null || e.Code==code,"Unexpected diagnostic "+e.Code);return; }
        catch(Exception e) when(code is null && e is ArgumentException or InvalidOperationException or PluginException) {return;}
        throw new Exception("Expected rejection "+code);
    }
    static PluginSpecification[] Specs() => [
        new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),
        new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"]),
        new("gui",ModuleKind.Gui,"NcmaGui.dll","NcmaGui.dll",1,0,["platform","renderer"])];
    static void ExportBmp(string path,byte[] pixels,uint width,uint height)
    {
        using var file=new BinaryWriter(File.Create(path));
        file.Write((ushort)0x4d42);file.Write(54+pixels.Length);file.Write(0);file.Write(54);file.Write(40);
        file.Write(width);file.Write(-(int)height);file.Write((ushort)1);file.Write((ushort)32);file.Write(0);
        file.Write(pixels.Length);file.Write(2835);file.Write(2835);file.Write(0);file.Write(0);
        for(int i=0;i<pixels.Length;i+=4){file.Write(pixels[i+2]);file.Write(pixels[i+1]);file.Write(pixels[i]);file.Write(pixels[i+3]);}
    }
    static void TestConfiguration(RendererSession renderer,ref ulong frame,byte[] baseline,byte[] image)
    {
        using var owner=new EditorSessionOwner("Rendering commands",components:RenderConfiguration.CreateRegistry());
        var edit=owner.Edit!; Guid objectId=Guid.NewGuid(),configId=Guid.NewGuid();
        var permission=new CapabilityPermissions(["ncma.scene.transaction","ncma.history.undo","ncma.history.redo"],objectScope:[objectId],createScope:[objectId],componentScope:[RenderConfiguration.ComponentType],allowDocumentHistory:false);
        CapabilityRequest Request(string capability,JsonElement input)=>new(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,edit.Revision,capability,input);
        var create=Request("ncma.scene.transaction",JsonSerializer.SerializeToElement(new{operations=new[]{new{op="create",objectId,name="Rendering settings"}}}));
        Check(edit.Invoke(create,permission).Changed,"Create configuration object via Core.");
        var config=RenderConfiguration.Default(configId);var settings=RenderingEditorAdapter.SettingsRequest(edit,objectId,config,edit.Revision);
        Check(edit.Invoke(settings).Status=="denied","Agent defaults read-only.");
        Check(edit.Invoke(settings,permission).Changed,"Configure via Core.");
        byte[] saved=owner.Document.CaptureBytes();
        var copy=new SceneDocument("Copy",RenderConfiguration.CreateRegistry());copy.RestoreBytes(saved);
        Check(copy.World.FindObject(objectId).Get<RenderConfiguration>()==config,"Persisted configuration value/UUID.");
        using var service=new RenderPipelineService(renderer);service.Configure(config,256,256);
        var generation=service.Generation;service.Configure(config,256,256);Check(service.Generation==generation,"Unchanged plan not reused.");
        RenderingEditorAdapter.RegisterInspections(edit,service,renderer);
        foreach(string capability in new[]{"ncma.render.inspect_pipeline","ncma.render.inspect_graph","ncma.render.get_profile"})
            Check(edit.Invoke(Request(capability,JsonSerializer.SerializeToElement(new{}))).Status=="ok","Rendering inspection "+capability);
        Reject(()=>edit.RegisterInspection(new("illegal","Illegal mutation",MutationRisk.Reversible,JsonSerializer.SerializeToElement(new{}),JsonSerializer.SerializeToElement(new{})),_=>new{}));
        var changed=config with{Exposure=2};
        var request=RenderingEditorAdapter.SettingsRequest(edit,objectId,changed,edit.Revision);
        var wrongScope=new CapabilityPermissions(["ncma.scene.transaction"],objectScope:[Guid.NewGuid()]);
        Check(edit.Invoke(request,wrongScope).Status=="denied","Scoped settings mutation denied.");
        Check(edit.Invoke(request,permission).Changed,"Settings commit.");
        Check(edit.Invoke(RenderingEditorAdapter.SettingsRequest(edit,objectId,config,0),permission).Status=="conflict","Stale configuration revision.");
        ulong builds=service.PlanBuilds;
        service.Configure(RenderingEditorAdapter.ReadConfiguration(edit,objectId),256,256);
        Check(service.PlanBuilds==builds,"Uniform update rebuilt topology.");
        service.Submit(frame++);renderer.Capture(image);renderer.Present();Check(!image.SequenceEqual(baseline),"Settings produced no pixels.");
        Check(edit.Invoke(Request("ncma.history.undo",JsonSerializer.SerializeToElement(new{})),permission).Changed,"Shared Undo.");
        service.Configure(RenderingEditorAdapter.ReadConfiguration(edit,objectId),256,256);
        service.Submit(frame++);renderer.Capture(image);renderer.Present();Check(image.SequenceEqual(baseline),"Settings Undo reference mismatch.");
        Check(edit.Invoke(Request("ncma.history.redo",JsonSerializer.SerializeToElement(new{})),permission).Changed,"Shared Redo.");
        var active=service.Plan; generation=service.Generation;
        Reject(()=>service.Configure(changed with{PipelineType="arbitrary.dll"},256,256));
        Check(ReferenceEquals(active,service.Plan)&&generation==service.Generation,"Failed candidate destroyed active plan.");
        Reject(()=>RenderingEditorAdapter.SettingsRequest(edit,objectId,changed with{Exposure=float.NaN},edit.Revision));
        for(int i=0;i<8;i++){service.Submit(frame++);renderer.Present();}
        long processAllocated=GC.GetTotalAllocatedBytes(true);
        long allocated=GC.GetAllocatedBytesForCurrentThread();long started=Stopwatch.GetTimestamp();
        for(int i=0;i<64;i++){service.Submit(frame++);renderer.Present();}
        long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
        _cachedThreadBytes=bytes;_cachedProcessBytes=GC.GetTotalAllocatedBytes(true)-processAllocated;
        _cachedElapsedMs=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Console.WriteLine($"Managed cached graph encode/submit: frames=64 allocation={bytes} bytes elapsed_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3}; GPU rendering included, VSync off");
        Check(bytes<=1024,"Cached submit allocated unexpectedly.");
        // Same one-call submission with a 4096-object managed document; no per-object native call.
        var objects=Enumerable.Range(0,4096).Select(i=>new SceneObjectData(Guid.NewGuid(),"Object "+i,[],[])).ToArray();
        var many=new SceneDocument();many.RestoreSnapshot(new(1,"4096 objects",objects));
        ulong calls=renderer.SubmitCalls;service.Submit(frame++);renderer.Present();
        Check(renderer.SubmitCalls==calls+1,"Per-object ABI regression.");
    }
    static void TestMeshUpload()
    {
        var vs = new ImportVertex[] {
            new(new(-1, 2, 3), Vector3.UnitZ, new(0, 0), default, Vector4.Zero),
            new(new(4, 2, 3), Vector3.UnitZ, new(1, 0), default, Vector4.Zero),
            new(new(-1, 7, 3), Vector3.UnitZ, new(0, 1), default, Vector4.Zero) };
        var tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1) };
        var source = new MeshPayload(false, 0, vs, tangents, [0, 1, 2, 2, 1, 0], [1, 0], [], 2);
        var data = MeshUploadData.PrepareStatic(source);
        Check(data.VertexCount == 3 && data.UploadBytes == 168 && data.CanUseNormalMap, "Static upload layout.");
        Check(data.Bounds == new MeshBounds(new(-1, 2, 3), new(4, 7, 3)), "Asymmetric mesh bounds.");
        Check(data.Ranges.SequenceEqual(new MeshDrawRange[] { new(0, 0, 3), new(1, 3, 3) }) && data.Indices.SequenceEqual(new uint[] { 2, 1, 0, 0, 1, 2 }), "Stable material buckets.");
        Check(BinaryPrimitives.ReadSingleLittleEndian(data.Vertices[44..]) == -1 && BinaryPrimitives.ReadSingleLittleEndian(data.Vertices[48..]) == 4, "Interleaved LE tangent/position offsets.");
        vs[0] = vs[0] with { Position = new(99) }; source.Indices[0] = 2; tangents[0] = new(0, 1, 0, 1);
        Check(BinaryPrimitives.ReadSingleLittleEndian(data.Vertices) == -1 && data.Indices[3] == 0, "Caller changes leaked into upload snapshot.");
        source.Indices[0] = 0;
        var fallback = MeshUploadData.PrepareStatic(source with { Tangents = [], MaterialSlots = 0, TriangleMaterials = [0, 0] });
        Check(!fallback.CanUseNormalMap && fallback.MaterialSlots == 1 && fallback.Diagnostics.Length == 2, "Named fallback/normal-map disable.");
        var uv = vs.Select(v => v with { UV = Vector2.Zero }).ToArray();
        Check(!MeshUploadData.PrepareStatic(source with { Vertices = uv }).CanUseNormalMap, "Degenerate UV normal-map safety.");
        Reject(() => MeshUploadData.PrepareStatic(source with { Skinned = true }));
        Reject(() => MeshUploadData.PrepareStatic(source with { Indices = [uint.MaxValue, 1, 2], TriangleMaterials = [0] }));
        Reject(() => MeshUploadData.PrepareStatic(source with { TriangleMaterials = [99, 0] }));
        Reject(() => MeshUploadData.PrepareStatic(source with { Tangents = [new(0, 0, 1, 1), new(1, 0, 0, 1), new(1, 0, 0, 1)] }));
        Reject(() => MeshUploadData.PrepareStatic(source with { Vertices = vs.Select(v => v with { Normal = new(float.MaxValue) }).ToArray() }));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { MeshUploadData.PrepareStatic(source, cancelled.Token); throw new Exception("Expected preparation cancellation."); } catch (OperationCanceledException) { }
        Console.WriteLine("Mesh upload preparation: layout/bounds/material ranges/owned snapshots/fallback/negative inputs/cancellation passed; GPU drawing tested separately.");
    }
    static (MeshPayload Mesh,SkeletonPayload Rig) BindFixture()
    {
        float[] Identity() => [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
        var offset=Identity();offset[12]=-2;
        var vs=new ImportVertex[] {
            new(new(0,0,0),Vector3.UnitZ,new(0,0),new(0,1,0,0),new(.25f,.75f,0,0)),
            new(new(4,0,0),Vector3.UnitZ,new(1,0),new(0,1,0,0),new(.25f,.75f,0,0)),
            new(new(0,1,0),Vector3.UnitZ,new(0,1),new(0,1,0,0),new(.25f,.75f,0,0)) };
        return (new(true,2,vs,[],[0,1,2],[0],[new(0,Identity()),new(1,offset)],1),
            new([new("root",-1,new(new(1,.5f,.25f),Quaternion.Identity,Vector3.One)),new("child",0,new(new(2,0,0),Quaternion.Identity,Vector3.One))]));
    }
    static void TestBindPoseUpload()
    {
        var (source,rig)=BindFixture();var data=BindPoseMeshUploadData.Prepare(source,rig);
        Check(data.VertexCount==3&&data.BindingCount==2&&data.UploadBytes==252&&data.Bounds==new MeshBounds(new(1,.5f,.25f),new(5,1.5f,.25f)),"Bind-pose global hierarchy/geometry binding");
        Check(BinaryPrimitives.ReadUInt16LittleEndian(data.Vertices[50..])==1&&BinaryPrimitives.ReadSingleLittleEndian(data.Vertices[60..])==.75f&&data.Vertices.Slice(72,8).SequenceEqual(new byte[8]),"Skin layout/weights/padding");
        Check(BinaryPrimitives.ReadSingleLittleEndian(data.Palette[48..])==1&&BinaryPrimitives.ReadSingleLittleEndian(data.Palette[112..])==1,"Binding palette, not bone palette");
        Check(ModelPayloadCodec.DecodeMesh(data.SourceMesh.ToArray()).Skinned&&!data.CanUseNormalMap,"Original skin must remain independently retained");
        var mixed=source with {Bindings=[source.Bindings[0],new(1,[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1])]};
        Check(BindPoseMeshUploadData.Prepare(mixed,rig).Bounds.Min.X==2.5f,"Weighted bind transform numeric oracle");
        var rotated=new SkeletonPayload([rig.Bones[0] with {BindLocal=new(new(1,.5f,.25f),Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/2),new(2,1,1))},rig.Bones[1]]);
        var rotationData=BindPoseMeshUploadData.Prepare(source,rotated);
        Check(Vector3.Distance(rotationData.Bounds.Min,new(0,.5f,.25f))<.00001f&&Vector3.Distance(rotationData.Bounds.Max,new(1,8.5f,.25f))<.00001f,"RH bind rotation/scale oracle");
        source.Vertices[0]=source.Vertices[0] with {Position=new(99)};source.Bindings[1].GeometryToBone[12]=99;
        Check(BinaryPrimitives.ReadSingleLittleEndian(data.Vertices)==1&&ModelPayloadCodec.DecodeMesh(data.SourceMesh.ToArray()).Vertices[0].Position==Vector3.Zero,"Bind source ownership");
        (source,rig)=BindFixture();
        Reject(()=>BindPoseMeshUploadData.Prepare(source with {Skinned=false},rig));
        Reject(()=>BindPoseMeshUploadData.Prepare(source,new([rig.Bones[0]])));
        Reject(()=>BindPoseMeshUploadData.Prepare(source with {Vertices=source.Vertices.Select(v=>v with {Weights=Vector4.Zero}).ToArray()},rig));
        Reject(()=>BindPoseMeshUploadData.Prepare(source with {Vertices=source.Vertices.Select(v=>v with {Joints=new(99,1,0,0)}).ToArray()},rig));
        float[] singular=[0,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
        Reject(()=>BindPoseMeshUploadData.Prepare(source with {Bindings=[new(0,singular),new(0,singular)]},rig));
        float[] reflected=[-1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
        Reject(()=>BindPoseMeshUploadData.Prepare(source with {Bindings=[new(0,reflected),new(0,reflected)]},rig));
        Reject(()=>BindPoseMeshUploadData.Prepare(source with {Vertices=null!},rig));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        try {BindPoseMeshUploadData.Prepare(source,rig,cancelled.Token);throw new Exception("Expected bind cancellation");}catch(OperationCanceledException){}
        Console.WriteLine("Bind-pose preparation: hierarchy/binding/rotation/weights/owned skin/negative inputs/cancellation passed; GPU animation not implemented.");
    }
    static void TestBindPoseMeshes(RendererSession renderer,ref ulong frame,string repository,string kernelPath,string output)
    {
        Check(Marshal.SizeOf<BindPoseMeshDescription>()==64&&Marshal.SizeOf<SceneRenderApiV2>()==56,"Bind service ABI sizes");
        var (source,rig)=BindFixture();var data=Task.Run(()=>BindPoseMeshUploadData.Prepare(source,rig)).GetAwaiter().GetResult();
        var mvp=Matrix4x4.CreateTranslation(-3,-1,0)*Matrix4x4.CreateLookAt(new(0,0,2),Vector3.Zero,Vector3.UnitY)*Matrix4x4.CreateOrthographic(6,2.5f,.1f,10);
        var color=new Vector4(.125f,.25f,.5f,1);var viewport=new Vector4(0,0,256,256);
        byte[] image=new byte[256*256*4],baseline=new byte[image.Length];var before=renderer.SceneStats;
        for(int cycle=0;cycle<8;cycle++) {
            using var mesh=renderer.CreateBindPoseMesh(data);Check(mesh.IsBindPose&&ReferenceEquals(mesh.BindPoseSource,data),"GPU lease retains skin source/palette");
            var draw=StaticMeshDraw.Create(mesh,data.Ranges[0],mvp,color);StaticMeshDraw[] batch=[draw];
            renderer.SubmitStaticMeshes(frame++,batch,viewport,Vector4.UnitW);renderer.Capture(image);renderer.Present();
            if(cycle==0){image.CopyTo(baseline,0);ExportBmp(Path.Combine(output,"bind-pose.bmp"),image,256,256);}
            else Check(image.SequenceEqual(baseline),"Bind-pose repeat pixels");
            Check(image[(140*256+75)*4+2]==188,"Bind-pose transform drawn twice/stride incorrect");
            var uploaded=renderer.SceneStats;
            for(int i=0;i<32;i++){renderer.SubmitStaticMeshes(frame++,batch,viewport,Vector4.UnitW);renderer.Present();}
            Check(renderer.SceneStats.UploadedBytes==uploaded.UploadedBytes,"Bind pose stable frame upload");
            mesh.Dispose();Check(renderer.SceneStats.LiveMeshes==0&&renderer.SceneStats.ResidentBytes==0,"Bind-pose lease leaked");
            Reject(()=>StaticMeshDraw.Create(mesh,data.Ranges[0],mvp,color));
        }
        // Compare EVERY pixel to an equivalent independently specified static triangle.
        var positions=new Vector3[]{new(1,.5f,.25f),new(5,.5f,.25f),new(1,1.5f,.25f)};
        var staticData=MeshUploadData.PrepareStatic(new(false,0,positions.Select(p=>new ImportVertex(p,Vector3.UnitZ,Vector2.Zero,default,Vector4.Zero)).ToArray(),[],[0,1,2],[0],[],1));
        using(var mesh=renderer.CreateStaticMesh(staticData)) {
            renderer.SubmitStaticMeshes(frame++,[StaticMeshDraw.Create(mesh,staticData.Ranges[0],mvp,color)],viewport,Vector4.UnitW);
            renderer.Capture(image);renderer.Present();Check(image.SequenceEqual(baseline),"Bind pose full-image numeric/static oracle");
        }
        string fixture=Path.Combine(repository,"tests","assets","fbx","ncma_skin_weights_7400_ascii.fbx");
        var imported=Task.Run(()=> {
            string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture)));
            using var importer=new ImportKernel(kernelPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(kernelPath))));
            var model=importer.LoadAndCopy(fixture,30,staticOnly:false);
            var record=new AssetRecord(1,Guid.NewGuid(),AssetKind.Character,"assets/Skin.fbx",hash,"ncma.ufbx",1,new(1,30,false),[],[],null);
            var plan=ModelImportPlanner.Build(model,record,Guid.NewGuid(),false);var manifest=ModelAssetManifestCodec.ValidateBundle(plan.DerivedBytes,plan.Record);
            var blocks=DerivedAssetCodec.Decode(plan.DerivedBytes);
            var sk=ModelPayloadCodec.DecodeSkeleton(blocks.Single(b=>b.AssetId==manifest.Skeleton).Data);
            return BindPoseMeshUploadData.Prepare(ModelPayloadCodec.DecodeMesh(blocks.Single(b=>b.AssetId==manifest.Meshes.Single().Mesh).Data),sk);
        }).GetAwaiter().GetResult();
        int colored=0;
        using(var mesh=renderer.CreateBindPoseMesh(imported)) {
            var bounds=imported.Bounds;var center=(bounds.Min+bounds.Max)/2;float radius=Math.Max(.01f,(bounds.Max-bounds.Min).Length());
            var camera=Matrix4x4.CreateTranslation(-center)*Matrix4x4.CreateLookAt(new(0,0,radius*2),Vector3.Zero,Vector3.UnitY)*Matrix4x4.CreateOrthographic(radius*1.5f,radius*1.5f,.001f,radius*4);
            renderer.SubmitStaticMeshes(frame++,[StaticMeshDraw.Create(mesh,imported.Ranges[0],camera,color)],viewport,Vector4.UnitW);
            renderer.Capture(image);renderer.Present();for(int i=0;i<image.Length;i+=4)if(image[i+2]==188)colored++;
            Check(colored>100,"Actual FBX bind pose rendered no measurable geometry");ExportBmp(Path.Combine(output,"bind-fbx.bmp"),image,256,256);
        }
        var stats=renderer.SceneStats;var validation=renderer.Stats;
        Check(stats.LiveMeshes==0&&stats.ResidentBytes==0&&validation.ValidationErrors==0&&validation.ValidationWarnings==0,"Bind resource/API validation closure");
        File.WriteAllText(Path.Combine(output,"bind-pose-results.json"),JsonSerializer.Serialize(new {schema=1,service="cpu-bind-pose-v2",cycles=8,residentFramesPerCycle=33,fullImageMatchesStaticOracle=true,fbxColoredPixels=colored,sourceRetained=true,paletteAppliedOnce=true,meshCreates=stats.MeshCreates-before.MeshCreates,uploadedBytes=stats.UploadedBytes-before.UploadedBytes,liveMeshes=stats.LiveMeshes,residentBytes=stats.ResidentBytes,validationErrors=validation.ValidationErrors,validationWarnings=validation.ValidationWarnings,gpuSkinning=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Bind-pose v2: 8 resident cycles, exact static-oracle image, actual FBX pixels={colored}, validation=0/0; no animated GPU skinning.");
    }
    static void TestStaticMeshes(RendererSession renderer, ref ulong frame, string repository, string kernelPath, string output)
    {
        Check(Marshal.SizeOf<MeshDescription>()==48 && Marshal.SizeOf<StaticMeshDraw>()==112 && Marshal.SizeOf<MeshFrame>()==64 && Marshal.SizeOf<SceneRenderStats>()==56 && Marshal.SizeOf<SceneRenderApi>()==48,"Static mesh ABI layout");
        // Trusted fixture importer, OFF render thread. Authoring/Worker dependencies belong to tests, not Rendering or Player.
        string source=Path.Combine(repository,"tests","assets","fbx","ncma_static_asymmetric_7400_ascii.fbx");
        var prepared=Task.Run(()=> {
            string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            using var importer=new ImportKernel(kernelPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(kernelPath))));
            var model=importer.LoadAndCopy(source,30,staticOnly:true);
            var record=new AssetRecord(1,Guid.NewGuid(),AssetKind.StaticMesh,"assets/Static.fbx",hash,"ncma.ufbx",1,new(1,30,true),[],[],null);
            var plan=ModelImportPlanner.Build(model,record,Guid.NewGuid(),true);
            var manifest=ModelAssetManifestCodec.ValidateBundle(plan.DerivedBytes,plan.Record);
            byte[] mesh=DerivedAssetCodec.Decode(plan.DerivedBytes).Single(b=>b.AssetId==manifest.Meshes.Single().Mesh).Data;
            return (Data:MeshUploadData.PrepareStatic(ModelPayloadCodec.DecodeMesh(mesh)),Blob:plan.DerivedBytes,SourceHash:hash);
        }).GetAwaiter().GetResult();
        var data=prepared.Data;
        Check(Task.Run(()=> { try {renderer.CreateStaticMesh(data);return false;} catch(PluginException e) {return e.Result==PluginResult.WrongThread;} }).GetAwaiter().GetResult(),"Managed GPU owner thread rejection");
        Check(data.Bounds==new MeshBounds(new(1,.5f,.25f),new(5,1.5f,.25f)),"FBX metres/transform bake or bounds changed");
        File.WriteAllBytes(Path.Combine(output,"static-asymmetric.nca"),prepared.Blob);
        var view=Matrix4x4.CreateLookAt(new(0,0,2),Vector3.Zero,Vector3.UnitY);
        var mvp=Matrix4x4.CreateTranslation(-3,-1,0)*view*Matrix4x4.CreateOrthographic(6,2.5f,.1f,10);
        var color=new Vector4(.125f,.25f,.5f,1);var viewport=new Vector4(0,0,256,256);
        byte[] baseline=new byte[256*256*4],image=new byte[baseline.Length];
        var before=renderer.SceneStats; StaticMeshDraw stale=default; int maximumColorError=0,checkedPixels=0;
        long stableOwnerAllocatedBytes=0;double stableElapsedMs=0;
        for(int cycle=0;cycle<8;cycle++) {
            using var mesh=renderer.CreateStaticMesh(data);
            var draw=StaticMeshDraw.Create(mesh,data.Ranges[0],mvp,color);stale=draw;
            StaticMeshDraw[] batch=[draw];
            Check(mesh.Bounds==data.Bounds && mesh.IndexCount==3,"Managed mesh lease metadata");
            Reject(renderer.Dispose);
            var invalid=draw;invalid.IndexCount=6;ulong submitted=renderer.Stats.SubmittedFrames;
            Reject(()=>renderer.SubmitStaticMeshes(1,[draw,invalid],viewport,Vector4.UnitW)); // frame also stale; separately test current frame below.
            ulong candidate=frame;
            Reject(()=>renderer.SubmitStaticMeshes(candidate,[draw,invalid],viewport,Vector4.UnitW));
            invalid=draw;invalid.Mesh.Generation++;
            Reject(()=>renderer.SubmitStaticMeshes(candidate,[invalid],viewport,Vector4.UnitW));
            invalid=StaticMeshDraw.Create(mesh,data.Ranges[0],mvp,new(float.NaN,0,0,1));
            Reject(()=>renderer.SubmitStaticMeshes(candidate,[invalid],viewport,Vector4.UnitW));
            Check(renderer.Stats.SubmittedFrames==submitted,"Invalid mesh batch changed frame state");
            renderer.SubmitStaticMeshes(frame++,[draw],viewport,Vector4.UnitW);
            Reject(mesh.Dispose);Reject(()=>renderer.Resize(320,200));
            renderer.Capture(image);renderer.Present();
            if(cycle==0) {
                image.CopyTo(baseline,0);ExportBmp(Path.Combine(output,"static-asymmetric.bmp"),image,256,256);
                // Analytic projected triangle oracle, not a capture from the new renderer.
                Vector2[] triangle=[new(256f/6,179.2f),new(256f*5/6,179.2f),new(256f/6,76.8f)];
                byte[] expected=[99,137,188,255];
                for(int y=0;y<256;y++)for(int x=0;x<256;x++) {
                    var p=new Vector2(x+.5f,y+.5f);var a=triangle[0];var b=triangle[1];var c=triangle[2];
                    float u=(p.X-a.X)/(b.X-a.X),v=(a.Y-p.Y)/(a.Y-c.Y),w=1-u-v;
                    bool inside=Math.Min(u,Math.Min(v,w))>.02f;
                    bool outside=Math.Min(u,Math.Min(v,w))<-.02f;
                    if(!inside&&!outside)continue;
                    for(int channel=0;channel<4;channel++) {
                        int error=Math.Abs(image[(y*256+x)*4+channel]-(inside?expected[channel]:channel==3?255:0));
                        maximumColorError=Math.Max(maximumColorError,error);
                        Check(error<=1,$"Imported RH mesh analytic pixel mismatch ({x},{y}) channel={channel} error={error}");
                    }
                    if(inside)checkedPixels++;
                }
                Check(checkedPixels>7000,"Imported mesh did not cover expected analytic area");
            } else Check(image.SequenceEqual(baseline),"Repeated imported mesh draw changed pixels");
            var uploaded=renderer.SceneStats;
            long allocated=GC.GetAllocatedBytesForCurrentThread(),started=Stopwatch.GetTimestamp();ulong copied=renderer.CopiedBytes;
            for(int i=0;i<32;i++){renderer.SubmitStaticMeshes(frame++,batch,viewport,Vector4.UnitW);renderer.Present();}
            stableOwnerAllocatedBytes+=GC.GetAllocatedBytesForCurrentThread()-allocated;stableElapsedMs+=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Check(renderer.CopiedBytes-copied==32*176,"Per-frame ABI byte accounting");
            Check(renderer.SceneStats.MeshCreates==uploaded.MeshCreates && renderer.SceneStats.UploadedBytes==uploaded.UploadedBytes,"Stable frame reuploaded geometry");
            if(cycle==0) {
                var farther=StaticMeshDraw.Create(mesh,data.Ranges[0],Matrix4x4.CreateTranslation(-3,-1,-.5f)*view*Matrix4x4.CreateOrthographic(6,2.5f,.1f,10),new(1,0,0,1));
                renderer.SubmitStaticMeshes(frame++,[draw,farther],viewport,Vector4.UnitW);renderer.Capture(image);renderer.Present();
                Check(image.SequenceEqual(baseline),"Depth test failed: farther draw overwrote near triangle");
                renderer.Resize(320,200);
                renderer.SubmitStaticMeshes(frame++,[draw],new(16,24,192,160),Vector4.UnitW);
                byte[] resized=new byte[320*200*4];renderer.Capture(resized);renderer.Present();
                Check(resized[(120*320+75)*4+2]==188 && resized[(5*320+5)*4+2]==0,"Resize/viewport pixel offset");
                ExportBmp(Path.Combine(output,"static-resized.bmp"),resized,320,200);renderer.Resize(256,256);
            }
            mesh.Dispose();Check(renderer.SceneStats.LiveMeshes==0 && renderer.SceneStats.ResidentBytes==0,"Mesh lease/geometry leak");
            candidate=frame;Reject(()=>renderer.SubmitStaticMeshes(candidate,[stale],viewport,Vector4.UnitW));
        }
        // Distinct submesh index ranges, separate opaque colors. Not a PBR/material asset implementation.
        var positions=new Vector3[]{new(-.9f,-.8f,.5f),new(-.1f,-.8f,.5f),new(-.9f,.8f,.5f),new(.1f,-.8f,.5f),new(.9f,-.8f,.5f),new(.9f,.8f,.5f)};
        var ranged=MeshUploadData.PrepareStatic(new(false,0,positions.Select(p=>new ImportVertex(p,Vector3.UnitZ,Vector2.Zero,default,Vector4.Zero)).ToArray(),[],[0,1,2,3,4,5],[1,0],[],2));
        using(var mesh=renderer.CreateStaticMesh(ranged)) {
            var red=StaticMeshDraw.Create(mesh,ranged.Ranges[0],Matrix4x4.Identity,new(1,0,0,1));
            var green=StaticMeshDraw.Create(mesh,ranged.Ranges[1],Matrix4x4.Identity,new(0,1,0,1));
            renderer.SubmitStaticMeshes(frame++,[red,green],viewport,Vector4.UnitW);renderer.Capture(image);renderer.Present();
            Check(image[(190*256+40)*4+1]==255 && image[(190*256+210)*4]==255,"Draw index ranges did not select material segments");
            ExportBmp(Path.Combine(output,"static-ranges.bmp"),image,256,256);
        }
        renderer.SubmitStaticMeshes(frame++,[],viewport,new(.125f,.25f,.5f,1));renderer.Capture(image);renderer.Present();
        Check(image[0]==99&&image[1]==137&&image[2]==188,"Linear clear sRGB encoding");
        renderer.WaitIdle();var stats=renderer.SceneStats;var validation=renderer.Stats;
        _oldDeviceDraw=stale;
        Check(stats.LiveMeshes==0 && stats.ResidentBytes==0 && stats.MeshCreates==before.MeshCreates+9 && stats.UploadedBytes==before.UploadedBytes+8*(ulong)data.UploadBytes+(ulong)ranged.UploadBytes,"GPU create/upload/release accounting");
        Check(validation.ValidationErrors==0 && validation.ValidationWarnings==0,"Static mesh actual debug-layer errors: "+renderer.Module.ReadDiagnostics());
        Check(stableOwnerAllocatedBytes<=1024,"Cached managed static submit allocated unexpectedly");
        File.WriteAllText(Path.Combine(output,"static-mesh-results.json"),JsonSerializer.Serialize(new{schema=1,sourceHash=prepared.SourceHash,service="static-unlit-v1",deviceGeneration=stats.DeviceGeneration,cycles=8,residentFramesPerCycle=33,stableMeasuredFrames=256,stableOwnerAllocatedBytes,stableElapsedMs,perDrawFrameBytes=176,meshCreates=stats.MeshCreates-before.MeshCreates,uploadedBytes=stats.UploadedBytes-before.UploadedBytes,liveMeshes=stats.LiveMeshes,residentBytes=stats.ResidentBytes,maximumColorError,checkedPixels,validation=true,validationErrors=validation.ValidationErrors,validationWarnings=validation.ValidationWarnings,depth=true,resize=true,materialRanges=true,pbr=false,gpuSkinning=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS actual FBX->NCA->static mesh GPU/RH/sRGB/depth/ranges/resize/8 cycles; pixel max={maximumColorError}; validation=0/0; stable geometry uploaded once per lease");
    }
    public static int Main(string[] args)
    {
        if(args.Length==4 && args[0]=="--graph-root-player-child")
            return Ncma.Player.App.PlayerRunner.Run(Ncma.Player.App.PlayerOptions.Parse(["--project",args[2],"--ticks","8","--report",args[3]]),pluginRoot:args[1],visible:false).ExitCode;
        try {
            if (args.Length == 3 && args[0] == "--shader-compile-tests") return ShaderCompileTests(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--registered-tone-tests") return RegisteredToneTests(args[1], args[2]);
            TestShaderContracts(); if (args.SequenceEqual(new[] { "--shader-contract-tests" })) return 0;
            TestMeshUpload(); TestBindPoseUpload(); TestResourceData(); if (args.SequenceEqual(new[] { "--mesh-upload-tests" })) return 0;
            if(args.Length is not (3 or 4 or 6))return 2;
            string root=Path.GetFullPath(args[0]),baselinePath=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
            Check(Marshal.SizeOf<RenderFrame>()==112 && Marshal.SizeOf<RendererStats>()==88 && Marshal.SizeOf<RenderPass>()==32,"Renderer layout");
            var frameView=new RenderFrameView(Guid.NewGuid(),1,Guid.NewGuid(),new Ncma.Runtime.TransformData(new(1,2,3),System.Numerics.Quaternion.Identity,new(2,3,4)));
            Span<float> matrix=stackalloc float[16];frameView.CopyColumnMajorModel(matrix);
            Check(matrix[0]==2 && matrix[5]==3 && matrix[10]==4 && matrix[12]==1 && matrix[13]==2 && matrix[14]==3,"Matrix ABI transpose/translation.");
            var emptyWorld=new Ncma.Runtime.World();var nonSpatial=emptyWorld.CreateObject("Empty");
            Check(!RenderFrameView.Capture(emptyWorld,nonSpatial.PersistentId).HasReferenceModel,"Empty object silently gained a transform.");
            var caps=RenderCapabilities.ReferenceDx11;
            var graph=new ReferencePreviewPipeline().Build(256,256);var compiled=graph.Compile(caps);
            Check(ReferenceEquals(compiled,graph.Compile(caps)) && compiled.PassCount==3,"Cached default graph");
            Reject(()=>graph.Compile(caps with {ReferencePbr=false}),"capability");
            Reject(()=>graph.Compile(caps with {Arrays=false}),"capability");
            Reject(()=>new ReferencePreviewPipeline().Build(0,256).Compile(caps),"resource_dimensions");
            Reject(()=>new ReferencePreviewPipeline().Build(256,256).Compile(caps with{MaxPasses=2}),"graph_budget");
            Reject(()=>new ReferencePreviewPipeline(toneExposureOverride:float.NaN).Build(256,256).Compile(caps),"parameter");
            var cycle=new RenderGraph();var a=cycle.AddResource(new("A",RenderRole.Output,RenderFormat.Rgba8,RenderUsage.ColorTarget,256,256,Imported:true));
            var b=cycle.AddResource(new("B",RenderRole.Output,RenderFormat.Rgba8,RenderUsage.ColorTarget,256,256,Imported:true));
            cycle.AddPass("A",RenderOperation.Clear,[b],[a],0);cycle.AddPass("B",RenderOperation.Clear,[a],[b],0);cycle.SetOutput(b);
            Reject(()=>cycle.Compile(caps),"cycle");
            var missing=new RenderGraph();var m=missing.AddResource(new("Output",RenderRole.Output,RenderFormat.Rgba8,RenderUsage.ColorTarget,256,256,Imported:true));
            var uninit=missing.AddResource(new("Input",RenderRole.Output,RenderFormat.Rgba8,RenderUsage.ColorTarget,256,256,Imported:true));
            missing.AddPass("Uninitialized",RenderOperation.Clear,[uninit],[m],0);missing.SetOutput(m);
            Reject(()=>missing.Compile(caps),"uninitialized_read");
            var mismatch=new RenderGraph();var r=mismatch.AddResource(new("Output",RenderRole.Output,RenderFormat.Rgba16Float,RenderUsage.ColorTarget,256,256,Imported:true));
            mismatch.AddPass("Invalid",RenderOperation.Clear,[],[r],0);mismatch.SetOutput(r);Reject(()=>mismatch.Compile(caps),"resource_format");
            var shader=new RenderGraph();var o=shader.AddResource(new("Output",RenderRole.Output,RenderFormat.Rgba8,RenderUsage.ColorTarget,256,256,Imported:true));
            shader.AddPass("Invalid shader",RenderOperation.Clear,[],[o],1);shader.SetOutput(o);Reject(()=>shader.Compile(caps),"shader_contract");
            var absent=new RenderGraph();absent.AddResource(new("Empty",RenderRole.Output,RenderFormat.Rgba8,RenderUsage.ColorTarget,256,256,Imported:true));Reject(()=>absent.Compile(caps),"graph_budget");
            Console.WriteLine("PASS graph validation/cache/capabilities");

            ShaderCompileTests(root,output);
            RegisteredToneTests(root, Path.Combine(output, "registered-tone"));
            using var loader=new PluginLoader();loader.Load(root,Specs());
            var platform=loader.Modules.Single(m=>m.Kind==ModuleKind.Platform);var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);var guiModule=loader.Modules.Single(m=>m.Kind==ModuleKind.Gui);
            byte[] baseline=File.ReadAllBytes(baselinePath);Check(baseline.Length==256*256*4,"Kernel fixture dimension");
            int legacyMax=0;double legacyMean=0;
            if(args.Length>=4) {
                byte[] legacy=FrozenReference.Load(Path.GetFullPath(args[3]));
                Check(legacy.Length==baseline.Length,"Legacy comparison fixture dimension");
                long sum=0;
                for(int i=0;i<baseline.Length;i++){int delta=Math.Abs(baseline[i]-legacy[i]);legacyMax=Math.Max(legacyMax,delta);sum+=delta;}
                legacyMean=sum/(double)baseline.Length;
                Check(legacyMax<=4 && legacyMean<=.1,$"Kernel/legacy mismatch max={legacyMax} mean={legacyMean}");
                Console.WriteLine($"PASS kernel/legacy reference parity max={legacyMax}; mean={legacyMean}");
            }
            byte[] image=new byte[baseline.Length];double mean=0;int max=0;ulong calls=0,bytes=0;double submit=0,gpu=0;
            string hardware="";
            for(int cycleIndex=0;cycleIndex<32;cycleIndex++) {
                using var window=new PlatformWindow(platform,"Renderer reference test",256,256,false);
                Reject(()=>new RendererSession(native,window,256,256,backend:2));
                using var renderer=new RendererSession(native,window,256,256);
                if(cycleIndex>0 && args.Length==6) {
                    Reject(()=>renderer.SubmitStaticMeshes(1,[_oldDeviceDraw],new(0,0,256,256),Vector4.UnitW));
                    Reject(()=>renderer.SubmitResources(1,[_oldResourceDraw],new(0,0,256,256),Vector4.UnitW,new(new(0,0,2),Vector3.UnitZ,Vector4.One)));
                    Check(renderer.Stats.SubmittedFrames==0 && renderer.SceneStats.LiveMeshes==0,"Stale device generation mutated new renderer");
                }
                using var resources=renderer.CreateReferenceResources();
                using var gui=new GuiSession(guiModule,window,cycleIndex==0?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc"):"");
                gui.AttachRenderer(renderer);
                ulong frame=1;
                renderer.Submit(compiled,resources,RenderFrame.Reference(frame++,256,256));
                renderer.Capture(image);
                if(cycleIndex==0) {
                    long total=0;
                    for(int i=0;i<image.Length;i++){int d=Math.Abs(image[i]-baseline[i]);max=Math.Max(max,d);total+=d;}
                    mean=total/(double)image.Length;
                    // Declared before execution: max <=4/255, mean <=.1/255, includes alpha.
                    Check(max<=4 && mean<=.1,$"Kernel reference mismatch max={max} mean={mean}");
                    Check(image.Where((_,i)=>i%4!=3).Distinct().Count()>32,"Reference rendered no scene.");
                    ExportBmp(Path.Combine(output,"reference.bmp"),image,256,256);
                    hardware=native.ReadDiagnostics();
                }
                renderer.Present();Reject(renderer.Present);
                var invalid=RenderFrame.Reference(frame,256,256);invalid.Metallic=float.NaN;
                Reject(()=>renderer.Submit(compiled,resources,invalid));Check(renderer.Stats.SubmittedFrames==1,"Invalid batch mutated frame");
                Reject(()=>renderer.Capture(new byte[8]));Reject(()=>renderer.Resize(0,256));
                Reject(()=>renderer.Dispose());Reject(()=>window.Dispose());

                if(cycleIndex==0) {
                    TestGuiImages(renderer,gui,window,ref frame,output);
                    if(args.Length==6){TestStaticMeshes(renderer,ref frame,Path.GetFullPath(args[4]),Path.GetFullPath(args[5]),output);TestBindPoseMeshes(renderer,ref frame,Path.GetFullPath(args[4]),Path.GetFullPath(args[5]),output);TestResourceDrawing(renderer,ref frame,Path.GetFullPath(args[4]),Path.GetFullPath(args[5]),output);TestScenePipeline(renderer,ref frame,output);TestSceneResources(renderer,ref frame,output);TestSceneProfiles(renderer,ref frame,output);TestSkinnedScenes(renderer,ref frame,Path.GetFullPath(args[4]),Path.GetFullPath(args[5]),output);}
                    TestConfiguration(renderer,ref frame,baseline,image);
                    var variations=new RenderPipeline[]{
                        new ReferencePreviewPipeline(toneExposureOverride:2),
                        new ReferencePreviewPipeline(features:[new ExposureFeature(2)]),
                        new ReferencePreviewPipeline(toneMapping:new ExposureToneStage(2)),
                        new ClearPipeline(.2f,.3f,.4f)};
                    foreach(var variant in variations){
                        var plan=variant.Build(256,256).Compile(caps);
                        renderer.Submit(plan,resources,RenderFrame.Reference(frame++,256,256));renderer.Capture(image);
                        Check(!image.SequenceEqual(baseline),"Customization produced no pixel change.");renderer.Present();
                    }
                    renderer.Submit(compiled,resources,RenderFrame.Reference(frame++,256,256));renderer.Capture(image);renderer.Present();
                    Check(image.SequenceEqual(baseline),"Restored pipeline differs from reference.");
                    ExportBmp(Path.Combine(output,"reference-restored.bmp"),image,256,256);
                    renderer.Resize(320,200);var resized=new ReferencePreviewPipeline().Build(320,200).Compile(caps);
                    renderer.Submit(resized,resources,RenderFrame.Reference(frame++,320,200));renderer.Capture(new byte[320*200*4]);renderer.Present();
                    renderer.Resize(256,256);
                }
                WindowState state=window.Poll();state.Focused=1;
                for(int scaleIndex=1;scaleIndex<=3;scaleIndex++) {
                    state.ScaleX=state.ScaleY=scaleIndex==1?1:scaleIndex==2?1.5f:2;
                    gui.Begin(state,1.0/60);
                    byte[] text=System.Text.Encoding.UTF8.GetBytes(cycleIndex==0?"中文 / Renderer":"GUI / Renderer");
                    GuiItem[] items=new GuiItem[3];
                    items[0]=new(){Kind=1,Enabled=1,WidgetHigh=1,WidgetLow=1,LabelLength=(uint)text.Length};
                    items[0].Rect[2]=150;items[0].Rect[3]=80;
                    items[1]=new(){Kind=3,Enabled=1,WidgetHigh=1,WidgetLow=2,LabelLength=(uint)text.Length};
                    items[2].Kind=2;
                    gui.Draw(new(){StructSize=48,Frame=frame,ViewGeneration=1,DocumentGeneration=1,Revision=0,ItemCount=3,TextBytes=(uint)text.Length},items,text);
                    Reject(gui.RenderGpu); // GUI cannot draw before renderer submit.
                    renderer.Submit(compiled,resources,RenderFrame.Reference(frame++,256,256));gui.RenderGpu();
                    if(cycleIndex==0 && scaleIndex==1){renderer.Capture(image);ExportBmp(Path.Combine(output,"gui-composited.bmp"),image,256,256);Check(!image.SequenceEqual(baseline),"No GUI pixels.");}
                    Reject(gui.RenderGpu);renderer.Present();
                }
                renderer.WaitIdle();
                var stats=renderer.Stats;
                Check(stats.ValidationErrors==0 && stats.ValidationWarnings==0,"DX11 validation: "+native.ReadDiagnostics());
                Check(stats.SubmittedFrames==stats.Presents,"Present count mismatch");
                Check(stats.GpuSampleValid==1 && stats.GpuMilliseconds>=0,"Missing valid GPU timestamp.");gpu+=stats.GpuMilliseconds;
                calls+=renderer.SubmitCalls;bytes+=renderer.CopiedBytes;submit+=stats.SubmitMilliseconds;
                // Explicit order: GUI borrow first, then GPU resources, then renderer, then window.
                gui.Dispose();resources.Dispose();Check(renderer.Stats.LiveGroups==0,"GPU resource group leak");renderer.Dispose();window.Dispose();
                Check(native.Status.LiveResources==0 && guiModule.Status.LiveResources==0 && platform.Status.LiveResources==0,"Native live resource leak");
            }
            File.WriteAllText(Path.Combine(output,"render-results.json"),JsonSerializer.Serialize(new{schema=1,backend="DX11 reference",hardware,cycles=32,width=256,height=256,vsync=false,validation=true,maxChannelError=max,meanChannelError=mean,submitCalls=calls,copiedBytes=bytes,lastSubmitMsAverage=submit/32,gpuMsAverage=gpu/32,kernelReference=true,legacyComparison=args.Length>=4,staticMesh=args.Length==6,legacyMaxChannelError=legacyMax,legacyMeanChannelError=legacyMean,cachedSubmitFrames=64,cachedOwnerAllocatedBytes=_cachedThreadBytes,cachedProcessAllocatedBytes=_cachedProcessBytes,cachedSubmitElapsedMs=_cachedElapsedMs,manualAcceptance=false},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"PASS real reference/GUI/customization/32 cycles; max={max}; mean={mean}; validation=0/0");
            return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
