using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Animation.Native;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    // Independent CPU weighted vertex/normal/tangent oracle; raster/shading still use the
    // same admitted program. Filter arithmetic has a separate analytical image suite.
    private static int ShadowAnimationTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        int checks=0,maximumError=0;ulong frame=1;var rows=new List<object>();
        void Pass(bool ok,string reason){Check(ok,"Animated shadow: "+reason);checks++;}
        var repo=new DirectoryInfo(Path.GetFullPath(plugins));while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");
        string configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        using var index=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repo.FullName,"out/verification/m2-7",configuration,"packages.json")));
        string product=index.RootElement.GetProperty("editor").GetString()!;Ncma.Application.DeploymentManifest.Validate(product);
        using var files=RuntimeShaderFileSet.ForHost(product,null,null)!;using var highFiles=RuntimeShadowLibrary.LoadDeployment(product);
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"FBX animated shadow presets",160,160,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,160,160);
        files.InstallSelection(renderer,()=>true);var high=highFiles.Prepare(renderer,files,()=>true);
        var standard=RuntimeShaderPreparation.Prepare(renderer,files.Get(ShaderProfile.Scene3D,true,true),()=>true);
        var noShadow=RuntimeShaderPreparation.Prepare(renderer,files.Get(ShaderProfile.Scene3D,false,true),()=>true);
        var highProgram=high.Resolve(files.Get(ShaderProfile.Scene3D,true,true));
        string native=Directory.GetParent(plugins)!.Parent!.FullName;
        string kernelPath=Path.Combine(native,"NcmaAnimationKernel.dll"),importPath=Path.Combine(native,"NcmaImportKernel.dll");
        using var kernel=new PoseKernel(kernelPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(kernelPath))));
        using var cache=new RenderResourceCache(renderer);var quad=Quad();using var ground=renderer.CreateStaticMesh(quad);
        var definition=MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(.6f,.25f,.1f,1),Metallic=.1f,Roughness=.6f};
        using var material=cache.AcquireMaterial(Version(definition),definition,_=>null,true,true,out _);
        foreach(string file in new[]{"blender_279_sausage_6100_ascii.fbx","blender_279_sausage_7400_binary.fbx"}){
            string label=file.Contains("ascii",StringComparison.Ordinal)?"ascii":"binary",folder=Path.Combine(output,label);Directory.CreateDirectory(folder);
            string source=Path.Combine(repo.FullName,"tests/assets/fbx",file);SkinFixture fixture;
            using(var importer=new Ncma.Asset.Import.ImportKernel(importPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(importPath)))))fixture=new(folder,importer.LoadAndCopy(source,30));
            var manifest=fixture.Manifest;var blocks=DerivedAssetCodec.Decode(fixture.Plan.DerivedBytes).ToDictionary(b=>b.AssetId,b=>b.Data);
            var payload=ModelPayloadCodec.DecodeMesh(blocks[manifest.Meshes[0].Mesh]);var skeleton=ModelPayloadCodec.DecodeSkeleton(blocks[manifest.Skeleton!.Value]);
            var animated=manifest.Clips.Select(id=>ModelPayloadCodec.DecodeClip(blocks[id])).OrderByDescending(c=>c.Clip.Tracks.Sum(t=>t.Keys.Count(k=>k.Value!=t.Keys[0].Value))).First();
            Pass(animated.Clip.Tracks.Any(t=>t.Keys.Any(k=>k.Value!=t.Keys[0].Value)),label+" non-bind imported animation");
            using var rig=kernel.CreateRig(skeleton);using var clip=kernel.CreateClip(rig,animated);var upload=SkinUploadData.Prepare(payload);var ranges=upload.Ranges.ToArray();
            using var skin=renderer.CreateSkinnedMesh(upload,standard.Skin!);
            var bounds=BindPoseMeshUploadData.Prepare(payload,skeleton).Bounds;float scale=1.6f/Math.Max(bounds.Max.X-bounds.Min.X,Math.Max(bounds.Max.Y-bounds.Min.Y,bounds.Max.Z-bounds.Min.Z));
            var normalize=Matrix4x4.CreateTranslation(-(bounds.Min+bounds.Max)*.5f)*Matrix4x4.CreateScale(scale);
            var models=new Matrix4x4[rig.BoneCount];var locals=new PoseTrs[rig.BoneCount];var matrices=new Matrix4x4[payload.Bindings.Length];var bindings=new MeshBindingPalette(payload,rig.BoneCount);
            var palettes=new GpuSkinPalette[matrices.Length];GpuSkinRequest[] requests=[new(skin,0)];
            var hashes=new HashSet<string>();int shadowedPoses=0;var poseRows=new List<object>();
            foreach(var quality in Enum.GetValues<SceneShadowQuality>()){
                var preset=SceneShadowPresets.Get(quality);int qualityError=0,minShadow=int.MaxValue;
                // Two exact viewport allocations. Recreate at an off-frame boundary and
                // require release back to baseline; no silent size mismatch tolerance.
                for(int block=0;block<2;block++){
                    uint size=block==0?128u:160u;var before=renderer.PipelineStats;
                    using(var target=renderer.CreateViewTarget(size,size))
                    using(var pipeline=new ScenePipelineSession(renderer,preset.CreatePipeline(true),size,size,shaders:quality==SceneShadowQuality.Off?noShadow.Scene:quality==SceneShadowQuality.High?highProgram.Scene:standard.Scene))
                    using(var plain=new ScenePipelineSession(renderer,SceneShadowPresets.Get(SceneShadowQuality.Off).CreatePipeline(true),size,size,shaders:noShadow.Scene)){
                        Pass(renderer.PipelineStats.ResidentBytes-before.ResidentBytes==2UL*size*size*12+816+preset.Budget.DepthTextureBytes,label+" exact resident "+quality+"/"+size);
                        byte[] gpu=new byte[size*size*4],cpuImage=new byte[gpu.Length],lit=new byte[gpu.Length];
                        SceneGpuDraw[] gpuDraws=[],gpuCasters=[];ResourceLighting lighting=default;Matrix4x4 lightVp=default;
                        for(int j=0;j<32;j++){
                            int pose=block*32+j;double time=clip.Duration*pose/63;
                            kernel.Sample(new[]{new PoseSample(rig,clip,new(time,time,1))},locals,models);bindings.Compose(models,matrices);
                            for(int k=0;k<matrices.Length;k++)palettes[k]=GpuSkinPalette.Create(matrices[k]);
                            var vertices=ShadowCpuVertices(payload,upload,matrices,out var tangents);
                            if(quality==SceneShadowQuality.Off)hashes.Add(Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(matrices.AsSpan()))));
                            using var cpu=renderer.CreateStaticMesh(MeshUploadData.PrepareStatic(new(false,0,vertices,tangents,payload.Indices,payload.TriangleMaterials,[],upload.Ranges.Length)));
                            float phase=pose*.08f;var eye=new Vector3(.12f*MathF.Sin(phase),.08f*MathF.Cos(phase),0);
                            var vp=Matrix4x4.CreateTranslation(-eye)*Matrix4x4.CreateOrthographic(4,4,.1f,50);
                            var camera=new SceneCameraView(Guid.Empty,vp,eye,CameraData.Default);var light=Vector3.Normalize(new Vector3(.8f+.1f*MathF.Sin(phase),.08f*MathF.Cos(phase),1));
                            lighting=new(eye,light,new(1,1,1,3));lightVp=ShadowVolume.Create(camera,light,preset.Settings.Distance);
                            var main=normalize*Matrix4x4.CreateRotationZ(.13f)*Matrix4x4.CreateTranslation(-.65f,.2f,-2.5f);
                            var caster=normalize*Matrix4x4.CreateTranslation(3.2f,0,-.5f);
                            Pass(vertices.All(v=>Vector3.Transform(v.Position,caster).X>eye.X+2),"entire animated caster lies beyond camera right plane");
                            var floor=SceneGpuDraw.Create(ground,material.Resource,quad.Ranges[0],Matrix4x4.CreateScale(4,4,1)*Matrix4x4.CreateTranslation(0,0,-4.5f),vp);
                            SceneGpuDraw[] Draws(GpuMesh mesh,Matrix4x4 model)=>ranges.Select(r=>SceneGpuDraw.Create(mesh,material.Resource,r,model,vp)).ToArray();
                            gpuDraws=[floor,..Draws(skin,main)];gpuCasters=preset.Enabled?[..Draws(skin,main),..Draws(skin,caster)]:[];
                            var cpuDraws=new[]{floor}.Concat(Draws(cpu,main)).ToArray();var cpuCasters=preset.Enabled?Draws(cpu,main).Concat(Draws(cpu,caster)).ToArray():[];
                            ulong invalidFrame=frame,copied=renderer.PipelineStats.CopiedBytes;
                            Reject(()=>pipeline.Submit(invalidFrame,gpuDraws,gpuCasters,lighting,lightVp,preset.Settings,target));checks++;
                            Pass(renderer.PipelineStats.CopiedBytes==copied,"stale animated frame no GPU work");
                            renderer.UpdateSkins(frame,requests,palettes);pipeline.Submit(frame++,gpuDraws,gpuCasters,lighting,lightVp,preset.Settings,target);renderer.CaptureTarget(target,gpu);renderer.Present();
                            pipeline.Submit(frame++,cpuDraws,cpuCasters,lighting,lightVp,preset.Settings,target);renderer.CaptureTarget(target,cpuImage);renderer.Present();
                            int error=0,worst=0;for(int i=0;i<gpu.Length;i++){int delta=Math.Abs(gpu[i]-cpuImage[i]);if(delta>error){error=delta;worst=i;}}
                            qualityError=Math.Max(qualityError,error);maximumError=Math.Max(maximumError,error);
                            if(error>2||j==0||j==31){string name=$"{quality}-{pose:D2}";File.WriteAllBytes(Path.Combine(folder,name+"-gpu.png"),Png(size,size,gpu));File.WriteAllBytes(Path.Combine(folder,name+"-cpu.png"),Png(size,size,cpuImage));}
                            Pass(error<=2,$"{label}/{quality}/pose{pose} whole-image CPU skin error {error} at {(worst/4)%(int)size},{(worst/4)/(int)size}");
                            if(preset.Enabled){
                                pipeline.Submit(frame++,cpuDraws,Draws(cpu,main),lighting,lightVp,preset.Settings,target);renderer.CaptureTarget(target,lit);renderer.Present();
                                int extra=0;for(int i=0;i<gpu.Length;i+=4)if(lit[i]-gpu[i]>20)extra++;
                                Pass(extra>10,$"{label}/{quality}/pose{pose} off-camera animated caster independently visible: {extra}");
                            }
                            plain.Submit(frame++,cpuDraws,[],lighting,lightVp,preset.Settings,target);renderer.CaptureTarget(target,lit);renderer.Present();
                            int dark=0;for(int i=0;i<gpu.Length;i+=4)if(lit[i]-gpu[i]>20)dark++;
                            Pass(preset.Enabled?dark>10:dark==0,$"{label}/{quality}/pose{pose} visible shadow count={dark}");
                            if(preset.Enabled)shadowedPoses++;minShadow=Math.Min(minShadow,dark);poseRows.Add(new{quality=quality.ToString(),pose,time,size,error,dark});
                        }
                        void Warm(){renderer.UpdateSkins(frame,requests,palettes);pipeline.Submit(frame++,gpuDraws,gpuCasters,lighting,lightVp,preset.Settings,target);renderer.Present();}
                        for(int warm=0;warm<8;warm++)Warm();
                        var sceneBefore=renderer.PipelineStats;var skinBefore=renderer.SkinStats;var resourceBefore=renderer.ResourceStats;
                        long allocation=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();for(int warm=0;warm<64;warm++)Warm();double ms=Stopwatch.GetElapsedTime(start).TotalMilliseconds;allocation=GC.GetAllocatedBytesForCurrentThread()-allocation;
                        var sceneAfter=renderer.PipelineStats;var skinAfter=renderer.SkinStats;
                        Pass(allocation==0&&sceneAfter.Creates==sceneBefore.Creates&&renderer.ResourceStats.UploadedBytes==resourceBefore.UploadedBytes,"warm64 no allocations/creation/static uploads");
                        Pass(sceneAfter.GeometryDraws-sceneBefore.GeometryDraws==64UL*(ulong)gpuDraws.Length&&sceneAfter.ShadowDraws-sceneBefore.ShadowDraws==64UL*(ulong)gpuCasters.Length,"exact geometry and caster count");
                        rows.Add(new{label,quality=quality.ToString(),size,poses=32,qualityError,minShadow,allocation,cpuMilliseconds=ms,frames=64,depthBytes=preset.Budget.DepthTextureBytes,sceneCopiedBytes=sceneAfter.CopiedBytes-sceneBefore.CopiedBytes,sceneUploadBytes=sceneAfter.ConstantUploadBytes-sceneBefore.ConstantUploadBytes,skinBefore,skinAfter,gpuLastValidMilliseconds=renderer.Stats.GpuMilliseconds,gpuOriginFrameAvailable=false});
                    }
                    Pass(renderer.PipelineStats.ResidentBytes==before.ResidentBytes&&renderer.PipelineStats.Pipelines==before.Pipelines,"resize block releases exact resources");
                }
            }
            Pass(hashes.Count>=32,label+" at least32 distinct actual palettes across64 poses");Pass(shadowedPoses==192,label+" all64 poses on3 enabled qualities");
            File.WriteAllText(Path.Combine(folder,"poses.json"),JsonSerializer.Serialize(new{sourceHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),clip=animated.Clip.Name,clip.Duration,distinctPalettes=hashes.Count,poseRows}));
        }
        renderer.WaitIdle();Pass(renderer.SkinStats.Meshes==0&&renderer.PipelineStats.Pipelines==0,"skin and pipeline leases all released");
        Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"API0/0");
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{checks,maximumError,rows,sourceFreeShaders=true,posesPerFbxPerQuality=64,filterOracleIndependent=false,relocatedProducts=false,fullPerformanceAccepted=false},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Console.WriteLine($"PASS M7.5 real FBX animated shadow {checks};512 CPU/GPU image pairs max={maximumError}; API0/0; {output}");return 0;
    }

    private static ImportVertex[] ShadowCpuVertices(MeshPayload payload,SkinUploadData upload,Matrix4x4[] matrices,out Vector4[] tangents)
    {
        var result=new ImportVertex[payload.Vertices.Length];tangents=new Vector4[result.Length];
        for(int i=0;i<result.Length;i++){
            var v=payload.Vertices[i];Vector3 p=default,n=default,t=default;
            var values=MemoryMarshal.Cast<byte,float>(upload.Vertices.Slice(i*80+32,16));var sourceT=new Vector4(values[0],values[1],values[2],values[3]);
            Add(v.Joints.X,v.Weights.X);Add(v.Joints.Y,v.Weights.Y);Add(v.Joints.Z,v.Weights.Z);Add(v.Joints.W,v.Weights.W);
            n=Vector3.Normalize(n);t=Vector3.Normalize(t-n*Vector3.Dot(n,t));result[i]=v with{Position=p,Normal=n,Joints=default,Weights=default};tangents[i]=new(t,sourceT.W);
            void Add(ushort j,float w){if(w==0)return;p+=Vector3.Transform(v.Position,matrices[j])*w;Matrix4x4.Invert(matrices[j],out var inverse);n+=Vector3.TransformNormal(v.Normal,Matrix4x4.Transpose(inverse))*w;t+=Vector3.TransformNormal(new(sourceT.X,sourceT.Y,sourceT.Z),matrices[j])*w;}
        }
        return result;
    }
}
