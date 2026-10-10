using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation.Native;
using Ncma.Asset.Import;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static int EnvironmentJointImages(string root,string output,RendererSession renderer,RenderResourceCache cache,RegisteredSkinShader skin,
        GpuEnvironmentResource environment,EnvironmentLightingConfiguration config,ref ulong frame)
    {
        int cases=0;void Pass(bool value,string message){Check(value,"M7.3-B3 joint "+message);cases++;}
        var build=Directory.GetParent(root)!.Parent!.FullName;
        var repository=new DirectoryInfo(Path.GetFullPath(root));while(!Directory.Exists(Path.Combine(repository.FullName,"tests/assets/fbx")))repository=repository.Parent??throw new InvalidOperationException("Repository fixtures missing.");
        string importPath=Path.Combine(build,"NcmaImportKernel.dll"),posePath=Path.Combine(build,"NcmaAnimationKernel.dll");
        using var kernel=new PoseKernel(posePath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(posePath))));
        var sc=DefaultEnvironmentSceneShaders.CopyCatalog(renderer,true);var compiled=RegisteredEnvironmentSceneShaders.Prepare(renderer,sc,DefaultEnvironmentSceneShaders.Select(sc,true),()=>true);
        var runtime=EnvironmentShaderPreparation.Prepare(renderer,compiled.CookPackage(skin),()=>true);
        using var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(ambient:0,shadowResolution:256),256,256,environmentShaders:runtime.Scene);
        using var target=renderer.CreateViewTarget(256,256);
        var baseId=Guid.NewGuid();var texture=TextureData.Prepare(1,1,TextureSemantic.Color,[128,180,220,255]);
        var packedId=Guid.NewGuid();var packed=TextureData.Prepare(1,1,TextureSemantic.Data,[153,179,204,255]);
        ResolvedTexture? Resolve(Guid id)=>id==baseId?new(Version(id,texture),texture):id==packedId?new(Version(id,packed),packed):null;
        var d=MaterialSurfaceContract.WithPackedSurface(MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(.7f,.5f,.3f,1),BaseTexture=baseId},packedId,PackedSurfaceLayout.OcclusionRoughnessMetallic);
        using var material=cache.AcquireMaterial(Version(d),d,Resolve,true,true,out var diagnostics);Pass(diagnostics.Count==0,"material complete");
        using var ground=renderer.CreateStaticMesh(Quad());var vp=Matrix4x4.CreateOrthographic(4,4,.1f,50);
        var camera=new SceneCameraView(Guid.Empty,vp,Vector3.Zero,CameraData.Default with{Projection=CameraProjection.Orthographic,OrthographicHeight=4});
        var light=Vector3.Normalize(new Vector3(.7f,.8f,1));var lightVp=ShadowVolume.Create(camera,light);var lights=new ResourceLighting(Vector3.Zero,light,new(1,1,1,2));var shadow=new SceneShadowSettings(.0001f,.0001f,false,3);
        byte[] actual=new byte[256*256*4],expected=new byte[actual.Length],alternate=new byte[actual.Length];var evidence=new List<object>();
        (ModelAssetManifest Manifest,DerivedAssetBlock[] Blocks) SaveNca(string fixture,bool staticOnly)
        {
            using var importer=new ImportKernel(importPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(importPath))));
            string path=Path.Combine(repository.FullName,"tests/assets/fbx",fixture);var model=importer.LoadAndCopy(path,30,staticOnly:staticOnly);
            var record=new AssetRecord(1,Guid.NewGuid(),staticOnly?AssetKind.StaticMesh:AssetKind.Character,"assets/"+fixture,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),"ncma.ufbx",1,new(1,30,true),[],[],null);
            var plan=ModelImportPlanner.Build(model,record,Guid.NewGuid(),staticOnly);string nca=Path.Combine(output,fixture+".nca");File.WriteAllBytes(nca,plan.DerivedBytes);
            byte[] restarted=File.ReadAllBytes(nca);return(ModelAssetManifestCodec.ValidateBundle(restarted,plan.Record),DerivedAssetCodec.Decode(restarted));
        }
        void Render(SceneGpuDraw[] geometry,SceneGpuDraw[] casters,byte[] pixels,ulong next){scene.Submit(next,geometry,casters,lights,lightVp,shadow,target);renderer.CaptureTarget(target,pixels);renderer.Present();}
        int Changed(byte[] a,byte[] b){int count=0;for(int i=0;i<a.Length;i+=4)if(Math.Abs(a[i]-b[i])+Math.Abs(a[i+1]-b[i+1])+Math.Abs(a[i+2]-b[i+2])>3)count++;return count;}
        scene.ConfigureEnvironment(environment,config,()=>true);
        try{
            var stat=SaveNca("ncma_static_asymmetric_7400_ascii.fbx",true);var staticPayload=ModelPayloadCodec.DecodeMesh(stat.Blocks.Single(b=>b.AssetId==stat.Manifest.Meshes[0].Mesh).Data);var staticData=MeshUploadData.PrepareStatic(staticPayload);
            using(var sm=renderer.CreateStaticMesh(staticData)){
                var model=Matrix4x4.CreateTranslation(-3,-1,-4.25f);var staticVp=Matrix4x4.CreateOrthographic(6,2.5f,.1f,50);var draws=staticData.Ranges.ToArray().Select(r=>SceneGpuDraw.Create(sm,material.Resource,r,model,staticVp,directLight:false)).ToArray();
                Render(draws,[],actual,frame++);var oracle=new IblImageOracle(environment.Package);int max=0,pixels=0;
                float Decode(int b){float x=b/255f;return x<=.04045f?x/12.92f:MathF.Pow((x+.055f)/1.055f,2.4f);}
                var baseColor=new Vector3(Decode(128),Decode(180),Decode(220))*new Vector3(.7f,.5f,.3f);
                // Interior of the analytically projected asymmetric triangle; do not compare raster edges as lighting samples.
                for(int y=105;y<165;y+=10)for(int x=60;x<145;x+=10){float u=(x+.5f-256f/6)/(256f*4/6),v=(179.2f-y-.5f)/102.4f;if(u<.1f||v<.1f||1-u-v<.1f)continue;
                    var p=new Vector3((x+.5f)/256*6-3,1.25f-(y+.5f)/256*2.5f,-4);var result=oracle.Evaluate(p,Vector3.UnitZ,baseColor,204/255f,179/255f,153/255f,config.Strength,config.RotationRadians);
                    for(int c=0;c<3;c++)max=Math.Max(max,Math.Abs(actual[(y*256+x)*4+c]-(int)MathF.Round(result[c]*255)));pixels++;}
                Pass(pixels>=10&&max<=3,"actual static FBX/NCA analytic interior IBL pixels="+pixels+" error="+max);File.WriteAllBytes(Path.Combine(output,"ibl-static-fbx.png"),Png(256,256,actual));
            }
            foreach(string fixture in new[]{"blender_279_sausage_6100_ascii.fbx","blender_279_sausage_7400_binary.fbx"}){
                var nca=SaveNca(fixture,false);var rigData=ModelPayloadCodec.DecodeSkeleton(nca.Blocks.Single(b=>b.AssetId==nca.Manifest.Skeleton!.Value).Data);using var rig=kernel.CreateRig(rigData);
                var payloads=nca.Manifest.Meshes.Select(m=>ModelPayloadCodec.DecodeMesh(nca.Blocks.Single(b=>b.AssetId==m.Mesh).Data)).ToArray();var uploads=payloads.Select(SkinUploadData.Prepare).ToArray();
                var meshes=uploads.Select(u=>renderer.CreateSkinnedMesh(u,runtime.Skin!)).ToArray();
                try{
                    foreach(Guid clipId in nca.Manifest.Clips){var clipData=ModelPayloadCodec.DecodeClip(nca.Blocks.Single(b=>b.AssetId==clipId).Data);using var clip=kernel.CreateClip(rig,clipData);
                        foreach(double time in new[]{0,clip.Duration*.5,clip.Duration,clip.Duration*(1-1e-8)}){
                            var local=new PoseTrs[rig.BoneCount];var models=new Matrix4x4[rig.BoneCount];kernel.Sample([new(rig,clip,new(time,time,1))],local,models);
                            var palettes=new List<GpuSkinPalette>();var requests=new List<GpuSkinRequest>();var baked=new List<MeshUploadData>();double maxVertexError=0;
                            for(int m=0;m<meshes.Length;m++){
                                var source=payloads[m];var binding=new MeshBindingPalette(source,rig.BoneCount);var matrices=new Matrix4x4[binding.BindingCount];binding.Compose(models,matrices);int offset=palettes.Count;palettes.AddRange(matrices.Select(GpuSkinPalette.Create));requests.Add(new(meshes[m],offset));
                                var vertices=new ImportVertex[source.Vertices.Length];var tangents=new Vector4[vertices.Length];byte[] initial=uploads[m].Vertices.ToArray();
                                for(int i=0;i<vertices.Length;i++){
                                    var src=source.Vertices[i];Vector3 p=default,n=default,t=default;var st=new Vector3(F(initial,i*80+32),F(initial,i*80+36),F(initial,i*80+40));
                                    Add(src.Joints.X,src.Weights.X);Add(src.Joints.Y,src.Weights.Y);Add(src.Joints.Z,src.Weights.Z);Add(src.Joints.W,src.Weights.W);n=Vector3.Normalize(n);t=Vector3.Normalize(t-n*Vector3.Dot(n,t));
                                    vertices[i]=src with{Position=p,Normal=n,Joints=default,Weights=default};tangents[i]=new(t,F(initial,i*80+44));
                                    void Add(ushort j,float w){if(w==0)return;p+=Vector3.Transform(src.Position,matrices[j])*w;Matrix4x4.Invert(matrices[j],out var inverse);n+=Vector3.TransformNormal(src.Normal,Matrix4x4.Transpose(inverse))*w;t+=Vector3.TransformNormal(st,matrices[j])*w;}
                                }
                                baked.Add(MeshUploadData.PrepareStatic(source with{Skinned=false,BoneCount=0,Bindings=[],Vertices=vertices,Tangents=tangents}));
                            }
                            renderer.UpdateSkins(frame,requests.ToArray(),palettes.ToArray());
                            for(int m=0;m<meshes.Length;m++){byte[] captured=new byte[meshes[m].VertexCount*48];renderer.CaptureSkinVertices(meshes[m],captured);var cpu=baked[m].Vertices;
                                for(int i=0;i<meshes[m].VertexCount;i++)for(int c=0;c<12;c++){double err=Math.Abs(F(captured,i*48+c*4)-BinaryPrimitives.ReadSingleLittleEndian(cpu.Slice(i*48+c*4,4)));maxVertexError=Math.Max(maxVertexError,err);}}
                            Pass(maxVertexError<=1e-4,"independent CPU four-weight full vertex oracle="+maxVertexError);
                            var min=baked.Select(b=>b.Bounds.Min).Aggregate(Vector3.Min);var max=baked.Select(b=>b.Bounds.Max).Aggregate(Vector3.Max);float scale=2.5f/Math.Max(max.X-min.X,Math.Max(max.Y-min.Y,max.Z-min.Z));var objectModel=Matrix4x4.CreateTranslation(-(min+max)/2)*Matrix4x4.CreateScale(scale)*Matrix4x4.CreateTranslation(0,0,-3.5f);
                            var floor=SceneGpuDraw.Create(ground,material.Resource,Quad().Ranges[0],Matrix4x4.CreateScale(6,6,1)*Matrix4x4.CreateTranslation(0,0,-5.5f),vp);
                            SceneGpuDraw[] Draws(GpuMesh[] ms)=>ms.SelectMany((mesh,m)=>baked[m].Ranges.ToArray().Select(r=>SceneGpuDraw.Create(mesh,material.Resource,r,objectModel,vp))).ToArray();
                            var actualDraws=Draws(meshes);Render(new[]{floor}.Concat(actualDraws).ToArray(),actualDraws,actual,frame++);
                            var cpuMeshes=baked.Select(renderer.CreateStaticMesh).ToArray();try{var cpuDraws=Draws(cpuMeshes);Render(new[]{floor}.Concat(cpuDraws).ToArray(),cpuDraws,expected,frame++);}finally{foreach(var mesh in cpuMeshes)mesh.Dispose();}
                            int imageError=actual.Zip(expected,(a,b)=>Math.Abs(a-b)).Max();Pass(imageError<=3,"whole FBX/NCA animated skin-shadow-material IBL image error="+imageError);
                            scene.ConfigureEnvironment(null,EnvironmentLightingConfiguration.Off,()=>true);renderer.UpdateSkins(frame,requests.ToArray(),palettes.ToArray());Render(new[]{floor}.Concat(actualDraws).ToArray(),actualDraws,alternate,frame++);int iblChanged=Changed(actual,alternate);Pass(iblChanged>100,"IBL actually contributes to joint image");scene.ConfigureEnvironment(environment,config,()=>true);
                            renderer.UpdateSkins(frame,requests.ToArray(),palettes.ToArray());Render(new[]{floor}.Concat(actualDraws).ToArray(),[],alternate,frame++);int shadowChanged=Changed(actual,alternate);Pass(shadowChanged>100,"actual geometry shadow has image effect");
                            evidence.Add(new{fixture,clip=clipId,time,duration=clip.Duration,maxVertexError,imageError,iblChanged,shadowChanged,ncaRestart=true});
                            if(time==clip.Duration*.5)File.WriteAllBytes(Path.Combine(output,$"ibl-{fixture}-{clipId:N}.png"),Png(256,256,actual));
                        }
                    }
                }finally{foreach(var mesh in meshes)mesh.Dispose();}
            }
            Pass(renderer.SkinStats.Meshes==0&&kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0,"joint native leases released");
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"joint API0/0");
        }finally{scene.ConfigureEnvironment(null,EnvironmentLightingConfiguration.Off,()=>true);}
        File.WriteAllText(Path.Combine(output,"joint-images.json"),JsonSerializer.Serialize(new{cases,evidence,formalWorldEnvironment=false,manualUserFbxAccepted=false},new JsonSerializerOptions{WriteIndented=true}));return cases;
        static float F(byte[] bytes,int at)=>BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at,4));
    }
}
