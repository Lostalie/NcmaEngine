using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int EnvironmentImageTests(string root,string output)
    {
        Directory.CreateDirectory(output);int cases=0,maxPixelError=0,maxIntegralPixelError=0;double maxBrdfError=0;ulong frame=1;
        void Pass(bool value,string message){Check(value,"M7.3-B3 "+message);cases++;}
        var rows=new List<object>();
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(root),Specs().Take(2).ToArray());
        var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"IBL image acceptance",256,256,false);
        using var renderer=new RendererSession(native,window,256,256);
        var catalog=DefaultEnvironmentSceneShaders.CopyCatalog(renderer,false);var selection=DefaultEnvironmentSceneShaders.Select(catalog,false);
        var compiled=RegisteredEnvironmentSceneShaders.Prepare(renderer,catalog,selection,()=>true);
        var skinCatalog=DefaultSkinShader.CopyCatalog(renderer);var skin=RegisteredSkinShader.Prepare(renderer,skinCatalog,DefaultSkinShader.Select(skinCatalog),()=>true);
        var runtime=EnvironmentShaderPreparation.Prepare(renderer,compiled.CookPackage(skin),()=>true);
        using var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false,ambient:0),256,256,environmentShaders:runtime.Scene);
        using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(256,256);using var mesh=renderer.CreateStaticMesh(Quad());
        using var cook=new EnvironmentCookService(native,()=>true);
        var settings=new EnvironmentCookSettings(16,8,32,1024);
        HdrEnvironmentSource Source(Vector3 center,Vector3 gradient){const int w=128,h=64;float[] values=new float[w*h*4];for(int y=0;y<h;y++)for(int x=0;x<w;x++){
            float dz=MathF.Sin(2*MathF.PI*((x+.5f)/w-.5f))*MathF.Sin(MathF.PI*(y+.5f)/h);var v=center+gradient*dz;int i=(y*w+x)*4;values[i]=v.X;values[i+1]=v.Y;values[i+2]=v.Z;values[i+3]=1;}return HdrEnvironmentSource.Prepare(Guid.NewGuid(),w,h,values);}
        var packages=new[]{cook.Cook(Guid.NewGuid(),1,Source(Vector3.Zero,Vector3.Zero),settings),cook.Cook(Guid.NewGuid(),1,Source(new(.18f),Vector3.Zero),settings),
            cook.Cook(Guid.NewGuid(),1,Source(new(4,2,.5f),Vector3.Zero),settings),cook.Cook(Guid.NewGuid(),1,Source(new(1.1f,.8f,.5f),new(.9f,.6f,.3f)),settings)};
        var oracles=packages.Select(p=>new IblImageOracle(p)).ToArray();var gpu=packages.Select(p=>renderer.CreateEnvironment(EnvironmentPackage.Decode(p.CopyBytes(),p.ContentHash),()=>true)).ToArray();
        EnvironmentLightingConfiguration Config(int e,float strength=1,float rotation=0)=>new(packages[e].AssetId,packages[e].Generation,packages[e].ContentHash,strength,rotation,true);
        byte[] image=new byte[256*256*4];var vp=Matrix4x4.CreateOrthographic(4,4,.1f,50);var model=Matrix4x4.CreateScale(4,3,1)*Matrix4x4.CreateTranslation(0,0,-4.5f);
        var lighting=new ResourceLighting(Vector3.Zero,Vector3.UnitZ,new(1,1,1,0));
        var textures=new Dictionary<Guid,TextureData>();Guid Texture(TextureSemantic role,byte[] bytes){var id=Guid.NewGuid();textures[id]=TextureData.Prepare(1,1,role,bytes);return id;}
        ResolvedTexture? Resolve(Guid id)=>textures.TryGetValue(id,out var t)?new(Version(id,t),t):null;
        float Decode(int b){float x=b/255f;return x<=.04045f?x/12.92f:MathF.Pow((x+.055f)/1.055f,2.4f);}
        Guid baseTex=Texture(TextureSemantic.Color,[128,180,220,255]);Vector3 albedo=new Vector3(Decode(128),Decode(180),Decode(220))*new Vector3(.7f,.5f,.3f);
        void Render(GpuMaterial m){scene.Submit(frame++,[SceneGpuDraw.Create(mesh,m,Quad().Ranges[0],model,vp)],[],lighting,Matrix4x4.Identity,new(),target);renderer.CaptureTarget(target,image);renderer.Present();}
        int Error(Vector3 expected,int x,int y){int err=0;for(int c=0;c<3;c++)err=Math.Max(err,Math.Abs(image[(y*256+x)*4+c]-(int)MathF.Round(expected[c]*255)));return err;}
        Vector3 Position(int x,int y)=>new((x+.5f)/256*4-2,2-(y+.5f)/256*4,-4);
        var quadrature=new Dictionary<(float,float),Vector2>();Vector2 IndependentBrdf(float nv,float rough){var key=(nv,rough);if(!quadrature.TryGetValue(key,out var q))quadrature[key]=q=UniformBrdf(nv,rough);return q;}
        try{
            foreach(int e in Enumerable.Range(0,4))foreach(float metal in new[]{0f,.5f,1f})foreach(float rough in new[]{.045f,.4f,.7f,1f}){
                var d=MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(.7f,.5f,.3f,1),BaseTexture=baseTex,Metallic=metal,Roughness=rough};
                using var material=cache.AcquireMaterial(Version(d),d,Resolve,true,true,out var diagnostics);Pass(diagnostics.Count==0,"complete material no fallback");
                scene.ConfigureEnvironment(gpu[e],Config(e),()=>true);Render(material.Resource);int pixelError=0,integralError=0;
                foreach(int y in new[]{104,128,152})foreach(int x in new[]{104,128,152}){
                    var p=Position(x,y);float nv=Vector3.Normalize(-p).Z;pixelError=Math.Max(pixelError,Error(oracles[e].Evaluate(p,Vector3.UnitZ,albedo,metal,rough,1,1,0),x,y));
                    if(e<3&&rough>=.4f){var brdf=IndependentBrdf(nv,rough);maxBrdfError=Math.Max(maxBrdfError,Vector2.Distance(brdf,oracles[e].Brdf(nv,rough)));
                        Vector3 radiance=e==0?Vector3.Zero:e==1?new(.18f):new(4,2,.5f);
                        var expected=IblImageOracle.Display(IblImageOracle.IblLinear(nv,albedo,metal,rough,1,1,radiance*MathF.PI,radiance,brdf));integralError=Math.Max(integralError,Error(expected,x,y));}
                }
                maxPixelError=Math.Max(maxPixelError,pixelError);maxIntegralPixelError=Math.Max(maxIntegralPixelError,integralError);
                Pass(pixelError<=3,$"software cube/mip/LUT pixel oracle env={e} metal={metal} rough={rough} error={pixelError}");
                if(e<3&&rough>=.4f)Pass(integralError<=4,$"independent uniform BRDF + analytic constant env={e} metal={metal} rough={rough} error={integralError}");
                rows.Add(new{environment=e,metal,rough,pixelError,integralError=rough>=.4f&&e<3?(int?)integralError:null});
                if(metal==.5f&&rough==.7f)File.WriteAllBytes(Path.Combine(output,$"ibl-environment-{e}.png"),Png(256,256,image));
            }
            Pass(maxBrdfError<=.02,"independent quadrature LUT error="+maxBrdfError);
            var basic=MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(.7f,.5f,.3f,1),BaseTexture=baseTex,Metallic=.6f,Roughness=.7f};
            using(var material=cache.AcquireMaterial(Version(basic),basic,Resolve,true,true,out _)){
                scene.ConfigureEnvironment(null,EnvironmentLightingConfiguration.Off,()=>true);Render(material.Resource);byte[] off=(byte[])image.Clone();
                foreach(float angle in new[]{-MathF.PI,-MathF.PI/2,-.5f,0,.5f,MathF.PI/2,MathF.PI})foreach(float strength in new[]{0f,.25f,1f,4f}){
                    scene.ConfigureEnvironment(gpu[3],Config(3,strength,angle),()=>true);Render(material.Resource);int error=0;
                    foreach(int y in new[]{120,128,136})foreach(int x in new[]{120,128,136})error=Math.Max(error,Error(oracles[3].Evaluate(Position(x,y),Vector3.UnitZ,albedo,.6f,.7f,1,strength,angle),x,y));
                    maxPixelError=Math.Max(maxPixelError,error);Pass(error<=3,$"rotated directional strength software pixel error={error} rotation={angle}");if(strength==0)Pass(image.SequenceEqual(off),"zero strength exact Off");
                }
                scene.ConfigureEnvironment(gpu[2],Config(2),()=>true);Render(material.Resource);var full=(byte[])image.Clone();
                scene.ConfigureEnvironment(gpu[2],Config(2,1,MathF.PI),()=>true);Render(material.Resource);Pass(image.SequenceEqual(full),"HDR constant rotation exact");
                scene.ConfigureEnvironment(gpu[2],Config(2,.5f),()=>true);Render(material.Resource);var half=(byte[])image.Clone();
                var sd=selection.GeometryPixel.CopyDefinition();string text=sd.Source.Replace("*ao*EnvironmentSettings.x;","*ao*EnvironmentSettings.x*.5;",StringComparison.Ordinal);Pass(text!=sd.Source,"user actual code mutation");
                var ud=sd with{AssetId=Guid.NewGuid(),Name="IBL half",Source=text,SourceHash=ShaderContractCodec.HashSource(text)};
                var uc=ShaderCatalog.Create(ShaderProfile.SceneEnvironment,[selection.GeometryVertex.CopyDefinition(),ud,selection.ToneVertex.CopyDefinition(),selection.TonePixel.CopyDefinition()]);
                var user=RegisteredEnvironmentSceneShaders.Prepare(renderer,uc,selection with{GeometryPixel=ShaderDescriptor.Prepare(ud)},()=>true);
                scene.ConfigureEnvironment(gpu[2],Config(2),()=>true);scene.ReplaceEnvironmentShaders(user);Render(material.Resource);Pass(image.SequenceEqual(half),"user HDR independent strength equivalence");
                var userPackage=user.CookPackage();var sourceFree=EnvironmentShaderPreparation.Prepare(renderer,RuntimeShaderPackage.Preflight(userPackage.CopyBytes(),userPackage.ContentHash),()=>true);
                scene.ReplaceEnvironmentShaders(sourceFree.Scene);Render(material.Resource);Pass(image.SequenceEqual(half),"source-free user HDR same image");scene.ReplaceEnvironmentShaders(runtime.Scene);
                // Warmed cost path excludes readback, cook, oracle, configuration and per-frame arrays.
                SceneGpuDraw[] batch=[SceneGpuDraw.Create(mesh,material.Resource,Quad().Ranges[0],model,vp)];
                void Tick(){scene.Submit(frame++,batch,[],lighting,Matrix4x4.Identity,new(),target);renderer.Present();}
                for(int i=0;i<32;i++)Tick();renderer.WaitIdle();var before=renderer.EnvironmentStats;var resources=renderer.ResourceStats;var meshes=renderer.SceneStats;var ps=renderer.PipelineStats;ulong calls=renderer.SubmitCalls,copyBytes=renderer.CopiedBytes,cookCalls=cook.NativeCalls;
                long allocated=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();for(int i=0;i<64;i++)Tick();double elapsed=Stopwatch.GetElapsedTime(start).TotalMilliseconds;allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;renderer.WaitIdle();var after=renderer.EnvironmentStats;var pafter=renderer.PipelineStats;
                Pass(allocated==0,"warmed static IBL submit allocation zero="+allocated);
                Pass(before.Publications==after.Publications&&before.UploadedBytes==after.UploadedBytes&&before.Captures==after.Captures&&before.ResidentBytes==after.ResidentBytes&&cook.NativeCalls==cookCalls,"static frames no environment upload/cook/readback");
                Pass(resources.Creates==renderer.ResourceStats.Creates&&resources.UploadedBytes==renderer.ResourceStats.UploadedBytes&&meshes.MeshCreates==renderer.SceneStats.MeshCreates&&meshes.UploadedBytes==renderer.SceneStats.UploadedBytes,"static frames no mesh/material rebuild");
                // Tone's frozen shader declaration remains C400; both operations update the shared C416 allocation in an environment scene.
                Pass(renderer.SubmitCalls-calls==64&&pafter.GeometryDraws-ps.GeometryDraws==64&&pafter.ConstantUploadBytes-ps.ConstantUploadBytes==64*416ul*2,"bounded one scene ABI and actual shared C416 geometry/tone uploads="+(pafter.ConstantUploadBytes-ps.ConstantUploadBytes));
                File.WriteAllText(Path.Combine(output,"costs.json"),JsonSerializer.Serialize(new{frames=64,warmFrames=32,allocatedBytes=allocated,elapsedMilliseconds=elapsed,abiCopiedBytes=renderer.CopiedBytes-copyBytes,constantUploadBytes=pafter.ConstantUploadBytes-ps.ConstantUploadBytes,environmentResidentBytes=after.ResidentBytes,environmentUploadBytes=after.UploadedBytes-before.UploadedBytes,fullPerformanceAccepted=false}));
            }
            foreach(byte occlusion in new byte[]{0,64,255}){
                Guid orm=Texture(TextureSemantic.Data,[occlusion,179,153,255]);var d=MaterialSurfaceContract.WithPackedSurface(basic with{AssetId=Guid.NewGuid(),Metallic=1,Roughness=1},orm,PackedSurfaceLayout.OcclusionRoughnessMetallic);
                using var m=cache.AcquireMaterial(Version(d),d,Resolve,true,true,out _);scene.ConfigureEnvironment(gpu[3],Config(3),()=>true);Render(m.Resource);
                int error=Error(oracles[3].Evaluate(Position(128,128),Vector3.UnitZ,albedo,153/255f,179/255f,occlusion/255f,1,0),128,128);maxPixelError=Math.Max(maxPixelError,error);Pass(error<=3,"explicit AO 0/64/255 pixel="+error);
            }
            // Tilted authored normals exercise +/-X and +/-Y cube lookups, not only +/-Z Y rotations.
            foreach(var worldNormal in new[]{new Vector3(.9f,0,.4358899f),new(-.9f,0,.4358899f),new(0,.9f,.4358899f),new(0,-.9f,.4358899f)}){
                var sourceNormal=Vector3.Normalize(worldNormal*new Vector3(4,3,1));var tangent=Vector3.Normalize(Vector3.UnitX-sourceNormal*sourceNormal.X);
                ImportVertex V(float x,float y,float u,float v)=>new(new(x,y,.5f),sourceNormal,new(u,v),default,default);
                var tilted=MeshUploadData.PrepareStatic(new(false,0,[V(-.8f,.8f,0,0),V(.8f,.8f,1,0),V(-.8f,-.8f,0,1),V(.8f,-.8f,1,1)],Enumerable.Repeat(new Vector4(tangent,-1),4).ToArray(),[0,1,2,2,1,3],[0,0],[],1));
                using var tiltedMesh=renderer.CreateStaticMesh(tilted);
                foreach(float roughness in new[]{.045f,.4f,.7f,1f}){
                    var d=basic with{AssetId=Guid.NewGuid(),Roughness=roughness};using var m=cache.AcquireMaterial(Version(d),d,Resolve,true,true,out _);scene.ConfigureEnvironment(gpu[3],Config(3),()=>true);
                    scene.Submit(frame++,[SceneGpuDraw.Create(tiltedMesh,m.Resource,tilted.Ranges[0],model,vp)],[],lighting,Matrix4x4.Identity,new(),target);renderer.CaptureTarget(target,image);renderer.Present();int error=0;
                    foreach(int y in new[]{120,128,136})foreach(int x in new[]{120,128,136})error=Math.Max(error,Error(oracles[3].Evaluate(Position(x,y),worldNormal,albedo,.6f,roughness,1,1,0),x,y));maxPixelError=Math.Max(maxPixelError,error);Pass(error<=3,"tilted cube face software oracle="+error);
                }
            }
            // Linear directional analytic cosine integral at roughness=1, not Cook output replay.
            foreach(float metal in new[]{0f,1f})foreach(float angle in new[]{0f,MathF.PI/2,MathF.PI}){
                var d=basic with{AssetId=Guid.NewGuid(),Metallic=metal,Roughness=1};using var material=cache.AcquireMaterial(Version(d),d,Resolve,true,true,out _);
                scene.ConfigureEnvironment(gpu[3],Config(3,1,angle),()=>true);Render(material.Resource);var p=Position(128,128);var v=Vector3.Normalize(-p);float nv=v.Z;var n=IblImageOracle.Rotate(Vector3.UnitZ,angle);var r=IblImageOracle.Rotate(Vector3.Reflect(-v,Vector3.UnitZ),angle);
                var center=new Vector3(1.1f,.8f,.5f);var gradient=new Vector3(.9f,.6f,.3f);var diffuse=(center+gradient*(2*n.Z/3))*MathF.PI;var pref=center+gradient*(2*r.Z/3);
                int error=Error(IblImageOracle.Display(IblImageOracle.IblLinear(nv,albedo,metal,1,1,1,diffuse,pref,IndependentBrdf(nv,1))),128,128);
                maxIntegralPixelError=Math.Max(maxIntegralPixelError,error);Pass(error<=4,"analytic directional cosine integral image="+error);
            }
            // All ORM channels, sRGB color, normal map and emissive consumed together by actual IBL.
            Guid packed=Texture(TextureSemantic.Data,[102,179,153,255]),normal=Texture(TextureSemantic.Normal,[178,210,220,255]),emissive=Texture(TextureSemantic.Color,[190,100,40,255]);
            var surface=MaterialSurfaceContract.WithPackedSurface(basic with{AssetId=Guid.NewGuid(),Metallic=1,Roughness=1,NormalTexture=normal,NormalScale=.6f,EmissiveTexture=emissive,Emissive=new(.3f,.5f,.7f,0)},packed,PackedSurfaceLayout.OcclusionRoughnessMetallic);
            using(var material=cache.AcquireMaterial(Version(surface),surface,Resolve,true,true,out _)){
                scene.ConfigureEnvironment(gpu[3],Config(3,.25f,.5f),()=>true);Render(material.Resource);var n=Vector3.Normalize(new Vector3((178/127.5f-1)*.6f,-(210/127.5f-1)*.6f,220/127.5f-1));var emission=new Vector3(Decode(190),Decode(100),Decode(40))*new Vector3(.3f,.5f,.7f);int error=0;
                foreach(int y in new[]{120,128,136})foreach(int x in new[]{120,128,136})error=Math.Max(error,Error(oracles[3].Evaluate(Position(x,y),n,albedo,153/255f,179/255f,102/255f,.25f,.5f,emission),x,y));
                maxPixelError=Math.Max(maxPixelError,error);Pass(error<=3,"normal/sRGB/ORM AO/emissive combined image="+error);File.WriteAllBytes(Path.Combine(output,"ibl-material-combined.png"),Png(256,256,image));
            }
            scene.ConfigureEnvironment(null,EnvironmentLightingConfiguration.Off,()=>true);
            int jointCases=EnvironmentJointImages(root,output,renderer,cache,skin,gpu[3],Config(3,.5f,.5f),ref frame);cases+=jointCases;
            // Resize is explicit recreation of target/scene leases; environment survives renderer resize.
            renderer.Resize(320,192);using(var resizedTarget=renderer.CreateViewTarget(320,192))using(var resizedScene=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false,ambient:0),320,192,environmentShaders:runtime.Scene)){
                using var mat=cache.AcquireMaterial(Version(basic),basic,Resolve,true,true,out _);resizedScene.ConfigureEnvironment(gpu[1],Config(1),()=>true);
                resizedScene.Submit(frame++,[SceneGpuDraw.Create(mesh,mat.Resource,Quad().Ranges[0],model,vp)],[],lighting,Matrix4x4.Identity,new(),resizedTarget);byte[] resized=new byte[320*192*4];renderer.CaptureTarget(resizedTarget,resized);renderer.Present();
                var position=new Vector3((160.5f)/320*4-2,2-(96.5f)/192*4,-4);var expected=oracles[1].Evaluate(position,Vector3.UnitZ,albedo,.6f,.7f,1,1,0);int error=0;for(int c=0;c<3;c++)error=Math.Max(error,Math.Abs(resized[(96*320+160)*4+c]-(int)MathF.Round(expected[c]*255)));Pass(error<=3,"resize independent pixel="+error);
            }renderer.Resize(256,256);
            var replacement=cook.Cook(packages[1].AssetId,2,Source(new(.36f),Vector3.Zero),settings);gpu[1].Replace(replacement,()=>true);Pass(gpu[1].Package.Generation==2,"explicit unbound resource update");
            using(var mat=cache.AcquireMaterial(Version(basic),basic,Resolve,true,true,out _)){
                for(int cycle=0;cycle<8;cycle++)using(var s=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false,ambient:0),256,256,environmentShaders:runtime.Scene)){
                    var c=new EnvironmentLightingConfiguration(replacement.AssetId,2,replacement.ContentHash,1,0,true);s.ConfigureEnvironment(gpu[1],c,()=>true);s.Submit(frame++,[SceneGpuDraw.Create(mesh,mat.Resource,Quad().Ranges[0],model,vp)],[],lighting,Matrix4x4.Identity,new(),target);renderer.CaptureTarget(target,image);renderer.Present();Pass(Error(new IblImageOracle(replacement).Evaluate(Position(128,128),Vector3.UnitZ,albedo,.6f,.7f,1,1,0),128,128)<=3,"recreated scene updated environment oracle");
                }
            }
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"actual DX11 API0/0: "+native.ReadDiagnostics());
        }finally{scene.ConfigureEnvironment(null,EnvironmentLightingConfiguration.Off,()=>true);foreach(var resource in gpu)resource.Dispose();}
        Pass(renderer.EnvironmentStats.Live==0&&renderer.EnvironmentStats.ResidentBytes==0,"final environment resources released");
        File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{phase="M7.3-B3",cases,maxPixelError,maxIntegralPixelError,maxBrdfError,rows,apiErrors=renderer.Stats.ValidationErrors,apiWarnings=renderer.Stats.ValidationWarnings,automaticIblImageCandidate=true,formalHosts=false,manualAcceptance=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS M7.3-B3 {cases} actual IBL image checks; pixel={maxPixelError},integral={maxIntegralPixelError},BRDF={maxBrdfError}; API0/0; formal hosts/manual pending");return 0;
    }
}
