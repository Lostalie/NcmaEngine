using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Buffers.Binary;
using Ncma.Animation;
using Ncma.Animation.Native;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Asset.Import;
using Ncma.Editor.Services;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private sealed class SkinFixture
    {
        internal readonly Guid Project=Guid.NewGuid(),Model=Guid.NewGuid();
        internal readonly string Root;
        internal ModelImportPlan Plan;
        internal readonly ImportedModel Source;
        internal ModelAssetManifest Manifest=>ModelAssetManifestCodec.ValidateBundle(Plan.DerivedBytes,Plan.Record);
        internal SkinFixture(string output,ImportedModel? source=null) {
            Root=Path.Combine(output,"skin-scene",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(Root,"assets"));
            Source=source??SyntheticModel();
            var record=new AssetRecord(1,Model,AssetKind.Character,"assets/model.fbx",new string('A',64),"ufbx",1,new(1,checked((int)Source.SampleRate),true),[],[],null);
            Plan=ModelImportPlanner.Build(Source,record,Project,false); Publish();
        }
        internal void Publish() {
            string path=Path.Combine(Root,Plan.Record.Generation!.RelativePath);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path,Plan.DerivedBytes);File.WriteAllBytes(Path.Combine(Root,"assets/model.fbx.ncmeta"),AssetRecordCodec.Encode(Plan.Record));
        }
        internal string GenerationPath=>Path.Combine(Root,Plan.Record.Generation!.RelativePath);
        internal (SceneDocument Document,Guid Camera,Guid[] Objects) Scene(int count=1) {
            var d=new SceneDocument("Skinned scene",RenderComponentRegistry.Register(RenderConfiguration.CreateRegistry()),SceneRenderValidation.RequireComposition);
            var m=Manifest;var objects=new Guid[count];
            for(int i=0;i<count;i++) {
                var obj=d.World.CreateObject("Character "+i);objects[i]=obj.PersistentId;
                obj.Set(TransformData.Identity with{Position=new(-.7f+(i%8)*.12f,0,-3),Scale=new(.8f,1.1f,1)});
                obj.Set(new SkinnedMeshData(Model,m.Meshes[0].Mesh,m.Skeleton!.Value,m.Meshes[0].Materials,true,true,uint.MaxValue));
                obj.Set(new ClipPlaybackData(m.Clips[i% m.Clips.Length],true,true,1+i*.01,0));
            }
            var camera=d.World.CreateObject("Camera");camera.Set(TransformData.Identity);camera.Set(CameraData.Default with{Projection=CameraProjection.Orthographic,OrthographicHeight=4});
            var light=d.World.CreateObject("Light");light.Set(TransformData.Identity with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.35f)});light.Set(DirectionalLightData.Default);
            d.ValidateAuthoring();return(d,camera.PersistentId,objects);
        }
    }
    private static ImportedModel SyntheticModel() {
        var identity=new ImportTransform(Vector3.Zero,Quaternion.Identity,Vector3.One);
        var helper=identity with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.2f)};
        ImportVertex V(float x,float y,float u,float v)=>new(new(x,y,0),Vector3.UnitZ,new(u,v),new(0,1,0,0),new(.7f,.3f,0,0));
        var mesh=new ImportMesh("Mixed-weight quad",[V(-.5f,.5f,0,0),V(.5f,.5f,1,0),V(-.5f,-.5f,0,1),V(.5f,-.5f,1,1)],
            [0,1,2,2,1,3],[0,0],[new(0,AssetMatrices.EncodeColumnMajor(Matrix4x4.Identity)),new(1,AssetMatrices.EncodeColumnMajor(Matrix4x4.CreateRotationY(-.2f)))],["Default"]);
        return new(7400,true,1,30,[mesh],[new("Root",-1,identity),new("Helper",0,helper)],
            [new("Move right",1,[new(0,[new(0,identity),new(1,identity with{Position=new(1,0,0)})]),new(1,[new(0,helper),new(1,helper with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.8f)})])]),
             new("Move left",1,[new(0,[new(0,identity),new(1,identity with{Position=new(-1,0,0)})])])],[]);
    }
    private static void TestGpuSkinNumerics(RendererSession renderer,ref ulong frame,string repository,string native,string output) {
        string path=Path.Combine(native,"NcmaAnimationKernel.dll");using var kernel=new PoseKernel(path,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        string importPath=Path.Combine(native,"NcmaImportKernel.dll");
        var rows=new List<object>();
        foreach(string fixture in new[]{"blender_279_sausage_6100_ascii.fbx","blender_279_sausage_7400_binary.fbx"}) {
            using var importer=new ImportKernel(importPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(importPath))));
            string source=Path.Combine(repository,"tests/assets/fbx",fixture);var imported=importer.LoadAndCopy(source,30);
            using var cpu=new Ncma.ImportedCharacterResource(Path.Combine(native,"NcmaNative.dll"),source);using var rig=kernel.CreateRig(new(imported.Bones));
            var models=new Matrix4x4[rig.BoneCount];var locals=new PoseTrs[rig.BoneCount];var reference=new float[cpu.SampleFloatCount];double max=0,normalDegrees=0;
            var payloads=imported.Meshes.Select(m=>new MeshPayload(true,rig.BoneCount,m.Vertices,[],m.Indices,m.TriangleMaterials,m.Bindings,m.Materials.Length)).ToArray();
            var gpu=payloads.Select(p=>renderer.CreateSkinnedMesh(SkinUploadData.Prepare(p))).ToArray();
            try {
                for(int c=0;c<imported.Clips.Length;c++) {
                    using var clip=kernel.CreateClip(rig,new(rig.BoneCount,imported.Clips[c]));
                    foreach(double time in new[]{0,clip.Duration*.5,clip.Duration,clip.Duration*(1-1e-8)}) {
                        kernel.Sample(new[]{new PoseSample(rig,clip,new(time,time,1))},locals,models);
                        // Character diagnostic clips loop at exact duration; sample the left limit there.
                        // Pose v1 separately tests the inclusive nonloop endpoint; do not weaken numeric tolerance.
                        cpu.Sample(c,time==clip.Duration?clip.Duration*(1-1e-8):time,reference);
                        int offset=0,referenceOffset=rig.BoneCount*16;var requests=new GpuSkinRequest[gpu.Length];var palettes=new List<GpuSkinPalette>();
                        for(int m=0;m<gpu.Length;m++){var binding=new MeshBindingPalette(payloads[m],rig.BoneCount);var matrices=new Matrix4x4[binding.BindingCount];binding.Compose(models,matrices);requests[m]=new(gpu[m],offset);palettes.AddRange(matrices.Select(GpuSkinPalette.Create));offset+=matrices.Length;}
                        renderer.UpdateSkins(frame++,requests,palettes.ToArray());
                        for(int m=0;m<gpu.Length;m++) {
                            var read=new byte[gpu[m].VertexCount*48];renderer.CaptureSkinVertices(gpu[m],read);var mesh=payloads[m];var upload=SkinUploadData.Prepare(mesh);
                            int paletteOffset=requests[m].PaletteOffset;
                            for(int v=0;v<mesh.Vertices.Length;v++) {
                                var vertex=mesh.Vertices[v];Vector3 n=default;
                                Add(vertex.Joints.X,vertex.Weights.X);Add(vertex.Joints.Y,vertex.Weights.Y);Add(vertex.Joints.Z,vertex.Weights.Z);Add(vertex.Joints.W,vertex.Weights.W);
                                var p=new Vector3(F(read,v*48),F(read,v*48+4),F(read,v*48+8));
                                var expected=new Vector3(reference[referenceOffset++],reference[referenceOffset++],reference[referenceOffset++]);
                                for(int a=0;a<3;a++){double error=Math.Abs(p[a]-expected[a]);Check(error<=1e-4+1e-5*Math.Abs(expected[a]),"GPU/CPU position error "+error+" fixture="+fixture+" time="+time+" mesh="+m+" vertex="+v);max=Math.Max(max,error);}
                                var gn=new Vector3(F(read,v*48+12),F(read,v*48+16),F(read,v*48+20));var gt=new Vector3(F(read,v*48+32),F(read,v*48+36),F(read,v*48+40));
                                double angle=Math.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(n),gn),-1,1))*180/Math.PI;normalDegrees=Math.Max(normalDegrees,angle);
                                Check(angle<=1 && Math.Abs(gn.Length()-1)<.002 && Math.Abs(gt.Length()-1)<.002 && Math.Abs(Vector3.Dot(gn,gt))<.002,"GPU normal/tangent policy");
                                Check(F(read,v*48+24)==vertex.UV.X&&F(read,v*48+28)==vertex.UV.Y,"UV changed during GPU skinning");
                                Check(F(read,v*48+44)==F(upload.Vertices.ToArray(),v*80+44),"Tangent sign changed");
                                void Add(ushort j,float w){if(w!=0)n+=Vector3.TransformNormal(vertex.Normal,palettes[paletteOffset+j].Normal)*w;}
                            }
                        }
                    }
                }
            } finally {foreach(var g in gpu)g.Dispose();}
            rows.Add(new{fixture,sourceHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),meshes=gpu.Length,bones=rig.BoneCount,maxPositionError=max,maxNormalDegrees=normalDegrees,
                oracle="ufbx-imported Character ABI 2 four-weight CPU position; inverse-transpose four-weight normal; original ufbx/full-weight oracle retained in importer tests",cpuSkinnedVertexUploads=0});
        }
        Check(renderer.SkinStats.Meshes==0&&renderer.SkinStats.ResidentBytes==0,"GPU numerical leases leaked");
        File.WriteAllText(Path.Combine(output,"skin-numerics.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS real ASCII/binary FBX GPU positions/normal/tangent/UV/sign vs ufbx CPU at start/middle/end/loop edge");
        static float F(byte[] bytes,int at)=>BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at,4));
    }
    private static void TestSkinnedScenes(RendererSession renderer,ref ulong frame,string repository,string native,string output) {
        native=Path.GetDirectoryName(native)!;
        TestAnimatorScenes(renderer,ref frame,Path.Combine(native,"NcmaNative.dll"),output);
        TestGpuSkinNumerics(renderer,ref frame,repository,native,output);
        TestAnimatedShadow(renderer,ref frame,native,output);
        string path=Path.Combine(native,"NcmaAnimationKernel.dll");using var kernel=new PoseKernel(path,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        var fixture=new SkinFixture(output);var (document,camera,objects)=fixture.Scene();
        string saved=Path.Combine(fixture.Root,"start.ncmascene");SceneDocumentFiles.Save(document,saved);
        using var editor=new EditorSessionOwner("Restart scene",components:RenderComponentRegistry.Register(RenderConfiguration.CreateRegistry()),validateComposition:SceneRenderValidation.RequireComposition);
        SceneDocumentFiles.Load(editor.Document,saved);editor.PrepareRenderAssets(fixture.Root,fixture.Project);
        using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(256,256);
        var original=new byte[256*256*4];var moved=new byte[original.Length];var vertices=new byte[4*48];
        var edit=new SceneRenderSession(renderer,cache,editor.Document.World,editor.RenderAssets!,editor.Document.CaptureSnapshot(),poseKernel:kernel);
        try {
            Check(edit.Submit(frame++,256,256,camera,target:target),"Skinned production scene not rendered");renderer.CaptureTarget(target,original);renderer.Present();
            Check(edit.Costs.GeometryDraws==1&&edit.Costs.ShadowDraws==1&&edit.Animation!.Costs.Characters==1,"Shared animated geometry/shadow set");
            edit.Animation!.SetPreviewTime(.5);edit.Submit(frame++,256,256,camera,target:target);renderer.CaptureTarget(target,moved);renderer.Present();
            Check(!original.SequenceEqual(moved)&&editor.Document.World.Tick==0,"Edit preview did not animate or advanced Play");
            edit.CaptureCharacterVertices(objects[0],vertices);Check(Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(vertices)-0)<.01,"Mixed binding GPU midpoint oracle");
            Check(Math.Abs(edit.Animation.RootDisplacement(objects[0]).X-.5)<1e-5,"Read-only root displacement");
            File.WriteAllBytes(Path.Combine(output,"skin-scene-start.png"),Png(256,256,original));File.WriteAllBytes(Path.Combine(output,"skin-scene-middle.png"),Png(256,256,moved));
            edit.Animation.SetPreviewTime(0);
            for(int cycle=0;cycle<8;cycle++) {
                var play=editor.StartPlay();
                using(var session=new SceneRenderSession(renderer,cache,play.Document.World,editor.PlayRenderAssets!,play.Document.CaptureSnapshot(),poseKernel:kernel,play:play)) {
                    play.Pause();play.Step();Check(play.Tick==1,"Paused step");session.Submit(frame++,256,256,camera,target:target);renderer.Present();
                    Check(Math.Abs(session.Animation!.RootDisplacement(objects[0]).X-1f/60)<1e-5,"Paused Step pose not current");
                    play.Resume();play.AdvanceFrame(1.0/60);session.Submit(frame++,256,256,camera,target:target);renderer.Present();
                    Check(play.Tick==2&&Math.Abs(session.Animation.RootDisplacement(objects[0]).X-1f/60)<1e-5,"Running interpolation did not use adjacent committed times");
                    Check(editor.Document.World.Tick==0&&play.Document.World.FindObject(objects[0]).Get<TransformData>()==editor.Document.World.FindObject(objects[0]).Get<TransformData>(),"Root motion wrote gameplay transform");
                    ulong rigCreates=kernel.Statistics.Rigs;play.Reload(_=>throw new Exception("No behaviours"));session.Animation.ResynchronizeAfterReload(play);
                    session.Submit(frame++,256,256,camera,target:target);renderer.Present();Check(play.Tick==2&&kernel.Statistics.Rigs==rigCreates&&Math.Abs(session.Animation.RootDisplacement(objects[0]).X-2f/60)<1e-5,"Reload reset active rig/clock instead of retaining committed time");
                    editor.StopPlay(); // Derived leases can detach after the application disposed Play.
                }
                Check(renderer.SkinStats.Meshes==1&&kernel.Statistics.Rigs==1&&kernel.Statistics.Clips==2,"Play stop retained resources");
            }
            // Successful reimport retains persistent IDs only with exact unchanged evidence; Play pins old generation.
            var pinned=editor.StartPlay();
            using(var session=new SceneRenderSession(renderer,cache,pinned.Document.World,editor.PlayRenderAssets!,pinned.Document.CaptureSnapshot(),poseKernel:kernel,play:pinned)) {
                string old=fixture.GenerationPath;fixture.Plan=ModelImportPlanner.Build(fixture.Source,fixture.Plan.Record,fixture.Project,false,fixture.Plan.Record);fixture.Publish();editor.RefreshRenderAssets();
                using(var candidate=new SceneRenderSession(renderer,cache,editor.Document.World,editor.RenderAssets!,editor.Document.CaptureSnapshot(),poseKernel:kernel)) {
                    pinned.Pause();pinned.Step();session.Submit(frame++,256,256,camera,target:target);renderer.Present();Check(PinLocked(old),"Active Play lost old-generation pin");
                    candidate.Submit(frame++,256,256,camera,target:target);renderer.Present();
                }
                // Bad candidate retains installed resources and document.
                byte[] prior=editor.Document.CaptureBytes();var obj=editor.Document.World.FindObject(objects[0]);var data=obj.Get<ClipPlaybackData>();obj.Set(data with{StartTime=2});
                Reject(()=>new SceneAnimationSession(editor.Document.World,editor.RenderAssets!,editor.Document.CaptureSnapshot(),kernel));obj.Set(data);
                Check(editor.Document.CaptureBytes().SequenceEqual(prior)&&renderer.SkinStats.Meshes==2,"Failed animation candidate changed live resources/document");editor.StopPlay();
            }
        } finally {edit.Dispose();}
        Check(renderer.SkinStats.Meshes==0&&kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0,"Final animation native leases nonzero");
        // Editor missing references are diagnostic-only, with no bind-pose fallback or UUID rewrite.
        var (missingDoc,missingCamera,missingObjects)=fixture.Scene();var missingId=Guid.NewGuid();
        var missingObj=missingDoc.World.FindObject(missingObjects[0]);missingObj.Set(missingObj.Get<ClipPlaybackData>() with{ClipId=missingId});byte[] missingBytes=missingDoc.CaptureBytes();
        using(var prepared=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,missingDoc.CaptureSnapshot(),false))
        using(var scene=new SceneRenderSession(renderer,cache,missingDoc.World,prepared,missingDoc.CaptureSnapshot(),poseKernel:kernel)) {
            Check(!scene.Submit(frame,256,256,missingCamera,target:target)&&scene.Diagnostics.Any(d=>d.Code=="animation_clip_missing"&&d.AssetId==missingId),"Missing clip silently fell back to bind pose");
            Check(missingBytes.SequenceEqual(missingDoc.CaptureBytes())&&renderer.SkinStats.Meshes==0,"Missing clip changed UUID or allocated GPU skin resources");
        }
        Reject(()=>SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,missingDoc.CaptureSnapshot(),true));
        var rows=new List<object>();
        foreach(bool different in new[]{false,true}) foreach(int count in new[]{0,1,8,32}) {
            var f=new SkinFixture(output);var (d,cam,ids)=f.Scene(count);
            if(different&&count>1) {
                var fixtures=Enumerable.Range(1,count-1).Select(_=>new SkinFixture(output)).ToArray();
                foreach(var other in fixtures) {
                    string from=other.GenerationPath;string relative=other.Plan.Record.Generation!.RelativePath.Replace(other.Project.ToString("N"),f.Project.ToString("N"));string to=Path.Combine(f.Root,relative);Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(from,to);
                    File.WriteAllBytes(Path.Combine(f.Root,"assets",other.Model+".fbx.ncmeta"),AssetRecordCodec.Encode(other.Plan.Record with{SourcePath="assets/"+other.Model+".fbx",Generation=other.Plan.Record.Generation with{RelativePath=relative}}));
                }
                for(int i=1;i<count;i++){var other=fixtures[i-1];var manifest=other.Manifest;d.World.FindObject(ids[i]).Set(new SkinnedMeshData(other.Model,manifest.Meshes[0].Mesh,manifest.Skeleton!.Value,manifest.Meshes[0].Materials,true,true,uint.MaxValue));d.World.FindObject(ids[i]).Set(new ClipPlaybackData(manifest.Clips[0],true,true,1,0));}
            }
            using var prepared=SceneAssetPreparation.Prepare(f.Root,f.Project,d.CaptureSnapshot(),true);
            using var scene=new SceneRenderSession(renderer,cache,d.World,prepared,d.CaptureSnapshot(),poseKernel:kernel);
            for(int i=0;i<4;i++){scene.Animation?.SetPreviewTime(i*.03);if(scene.Submit(frame++,256,256,cam,target:target))renderer.Present();renderer.WaitIdle();}
            ulong uploads=renderer.SceneStats.UploadedBytes,creates=renderer.SceneStats.MeshCreates;var before=renderer.SkinStats;ulong calls=renderer.SubmitCalls;
            long bytes=GC.GetAllocatedBytesForCurrentThread();long start=System.Diagnostics.Stopwatch.GetTimestamp();
            for(int i=0;i<8;i++){scene.Animation?.AdvancePreview(1.0/60);if(scene.Submit(frame++,256,256,cam,target:target))renderer.Present();renderer.WaitIdle();}
            double elapsed=System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;renderer.WaitIdle();var after=renderer.SkinStats;
            Check(bytes==0,"Animation cached frame allocated "+bytes);Check(uploads==renderer.SceneStats.UploadedBytes&&creates==renderer.SceneStats.MeshCreates,"Animation frame uploaded CPU vertices");
            Check(after.Batches-before.Batches==(count==0?0ul:8ul)&&after.PaletteBytes-before.PaletteBytes==(ulong)count*2*128*8,"Bounded palette upload counters");
            rows.Add(new{count,differentMesh=different,frames=8,forcedGpuDrain=true,allocatedBytes=bytes,totalMilliseconds=elapsed,pose=scene.Animation?.Costs,skinAbiMilliseconds=scene.SkinAbiMilliseconds,skinBackpressureFrames=scene.SkinBackpressureFrames,
                sceneAbiCalls=renderer.SubmitCalls-calls,skinAbiCalls=after.Batches-before.Batches,paletteBytes=after.PaletteBytes-before.PaletteBytes,immutableVertexUploadBytes=renderer.SceneStats.UploadedBytes-uploads,
                skinGpuSampleValid=after.GpuSampleValid,skinGpuMilliseconds=count==0?(double?)null:after.GpuMilliseconds,nativeSkinCpuMilliseconds=count==0?(double?)null:after.CpuMilliseconds,skinResidentBytes=after.ResidentBytes,
                validationErrors=renderer.Stats.ValidationErrors,validationWarnings=renderer.Stats.ValidationWarnings});
        }
        Check(renderer.SkinStats.Meshes==0&&kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"Animation profiles retained resources/validation");
        File.WriteAllText(Path.Combine(output,"skin-scene-profiles.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS NCA save/restart->skinned geometry/shared shadow; committed Play/Pause/Step/root reporting/Edit isolation/reimport/8 cycles/0-1-8-32 same/different mesh profiles");
    }
}
