using System.Numerics;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
internal static unsafe partial class Program
{
    private static int EnvironmentSceneTests(string root,string output){
        Directory.CreateDirectory(output);int cases=0;ulong frame=1;
        void Pass(bool condition,string label){Check(condition,"M7.3-B2 "+label);cases++;}
        void Bad(Action action){Reject(action);cases++;}
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(root),Specs().Take(2).ToArray());
        var module=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"M7.3 B2 environment binding",256,256,false);
        using var renderer=new RendererSession(module,window,256,256);
        var old=renderer.DefaultRuntimeShaders.Prepare(ShaderProfile.Scene3D,false,false,()=>true);
        var catalog=DefaultEnvironmentSceneShaders.CopyCatalog(renderer,false);
        var selected=DefaultEnvironmentSceneShaders.Select(catalog,false);
        Pass(catalog.Profile==ShaderProfile.SceneEnvironment&&catalog.Count==4,"independent environment profile");
        Pass(selected.GeometryPixel.CopyDefinition().Constants.Single().ByteSize==416&&selected.GeometryPixel.CopyDefinition().Resources.Any(x=>x.Kind==ShaderResourceKind.TextureCube&&x.Slot==8),"C416/cube contract");
        var compiled=RegisteredEnvironmentSceneShaders.Prepare(renderer,catalog,selected,()=>true);
        var skin=RegisteredSkinShader.Prepare(renderer,DefaultSkinShader.CopyCatalog(renderer),DefaultSkinShader.Select(DefaultSkinShader.CopyCatalog(renderer)),()=>true);
        var package=compiled.CookPackage(skin);
        Pass(!package.GpuValidated&&package.Count==5&&package.Profile==ShaderProfile.SceneEnvironment,"source-free closure is not GPU credential");
        var copy=package.CopyBytes();copy[0]=0;Pass(package.CopyBytes()[0]!=0,"immutable package ownership");
        Bad(()=>RuntimeShaderPreparation.Prepare(renderer,package,()=>true));
        Bad(()=>EnvironmentShaderPreparation.Prepare(renderer,old.Package,()=>true));
        Bad(()=>EnvironmentShaderPreparation.Prepare(renderer,package,()=>false));
        var runtime=EnvironmentShaderPreparation.Prepare(renderer,package,()=>true);
        Pass(runtime.BindingsValidated&&runtime.Skin is not null&&runtime.CopyMetadata().Length==5,"whole actual reflection before wrappers");
        using var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false,ambient:0),256,256,environmentShaders:compiled);
        using var standard=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false,ambient:0),256,256,shaders:old.Scene);
        using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(256,256);using var mesh=renderer.CreateStaticMesh(Quad());
        var definition=MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(.6f,.3f,.1f,1),Metallic=.2f,Roughness=.5f};
        using var material=cache.AcquireMaterial(Version(definition),definition,_=>null,true,true,out _);
        byte[] pixels=new byte[256*256*4];var model=Matrix4x4.CreateScale(4,3,1)*Matrix4x4.CreateTranslation(0,0,-4.5f);var vp=Matrix4x4.CreateOrthographic(4,4,.1f,50);
        var lighting=new ResourceLighting(Vector3.Zero,Vector3.UnitZ,new(1,1,1,0));
        byte[] Render(ScenePipelineSession session){session.Submit(frame++,[SceneGpuDraw.Create(mesh,material.Resource,Quad().Ranges[0],model,vp)],[],lighting,Matrix4x4.Identity,new(),target);renderer.CaptureTarget(target,pixels);renderer.Present();return (byte[])pixels.Clone();}
        var baseline=Render(standard);var off=Render(scene);Pass(baseline.SequenceEqual(off),"Off equals old unlit output");
        Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"Off API0/0: "+module.ReadDiagnostics());
        Pass(renderer.EnvironmentStats.Live==0&&renderer.EnvironmentStats.UploadedBytes==0,"Off no environment texture allocation");
        using var cook=new EnvironmentCookService(module,()=>true);var env=cook.Cook(Guid.NewGuid(),1,HdrEnvironmentSource.Neutral(Guid.NewGuid()),new(8,4,8,128));
        using var resource=renderer.CreateEnvironment(env,()=>true);
        EnvironmentLightingConfiguration Config(float strength=1,float rotation=0)=>new(env.AssetId,env.Generation,env.ContentHash,strength,rotation,true);
        Bad(()=>standard.ConfigureEnvironment(resource,Config(),()=>true));
        Bad(()=>scene.ConfigureEnvironment(resource,Config() with{Generation=2},()=>true));
        Bad(()=>scene.ConfigureEnvironment(resource,Config() with{ContentHash=new string('0',64)},()=>true));
        Bad(()=>scene.ConfigureEnvironment(resource,Config(float.NaN),()=>true));
        Bad(()=>scene.ConfigureEnvironment(resource,Config(17),()=>true));
        Bad(()=>scene.ConfigureEnvironment(resource,Config(1,MathF.PI+.1f),()=>true));
        Bad(()=>Task.Run(()=>scene.ConfigureEnvironment(resource,Config(),()=>true)).GetAwaiter().GetResult());
        Bad(()=>scene.ConfigureEnvironment(resource,Config(),()=>false));
        Bad(()=>scene.ConfigureEnvironment(resource,EnvironmentLightingConfiguration.Off,()=>true));
        Bad(()=>scene.ConfigureEnvironment(null,Config(),()=>true));
        scene.ConfigureEnvironment(resource,Config(),()=>true);var lit=Render(scene);
        Pass(lit[(128*256+128)*4]>off[(128*256+128)*4],"actual IBL lighting, not metadata-only");
        Pass(scene.EnvironmentLighting==Config(),"managed exact accepted configuration");
        var stats=renderer.EnvironmentStats;Bad(resource.Dispose);Bad(()=>resource.Replace(env,()=>true));Pass(renderer.EnvironmentStats.Publications==stats.Publications&&renderer.EnvironmentStats.Live==1,"bound pin rejects release/replacement atomically");
        scene.ConfigureEnvironment(resource,Config(.25f),()=>true);var dim=Render(scene);Pass(dim[(128*256+128)*4]<lit[(128*256+128)*4],"strength applies before tone");
        scene.ConfigureEnvironment(resource,Config(0),()=>true);Pass(Render(scene).SequenceEqual(off),"zero strength Off pixels retains valid pin");
        scene.ConfigureEnvironment(resource,Config(1,MathF.PI),()=>true);Pass(Render(scene).SequenceEqual(lit),"constant environment rotation invariant");
        Bad(()=>scene.ReplaceShaders(old.Scene!));Pass(Render(scene).SequenceEqual(lit),"old Scene group cannot downgrade environment");
        scene.ReplaceEnvironmentShaders(runtime.Scene);Pass(Render(scene).SequenceEqual(lit),"source-free actual environment equals source group");
        var sourceDefinition=selected.GeometryPixel.CopyDefinition();string userSource=sourceDefinition.Source.Replace("*ao*EnvironmentSettings.x;","*ao*EnvironmentSettings.x*.5;",StringComparison.Ordinal);
        Pass(userSource!=sourceDefinition.Source,"actual user shader mutation fixture");
        var userDefinition=sourceDefinition with{AssetId=Guid.NewGuid(),Name="User Environment Half",Source=userSource,SourceHash=ShaderContractCodec.HashSource(userSource)};
        var userCatalog=ShaderCatalog.Create(ShaderProfile.SceneEnvironment,new[]{selected.GeometryVertex.CopyDefinition(),userDefinition,selected.ToneVertex.CopyDefinition(),selected.TonePixel.CopyDefinition()});
        var userCompiled=RegisteredEnvironmentSceneShaders.Prepare(renderer,userCatalog,selected with{GeometryPixel=ShaderDescriptor.Prepare(userDefinition)},()=>true);
        scene.ConfigureEnvironment(resource,Config(.5f),()=>true);var half=Render(scene);scene.ConfigureEnvironment(resource,Config(1),()=>true);
        scene.ReplaceEnvironmentShaders(userCompiled);Pass(Render(scene).SequenceEqual(half),"user environment shader uses same GPU contract");
        var userRuntime=EnvironmentShaderPreparation.Prepare(renderer,userCompiled.CookPackage(),()=>true);
        scene.ReplaceEnvironmentShaders(userRuntime.Scene);Pass(Render(scene).SequenceEqual(half),"user source-free package matches its actual source pixels");
        scene.ReplaceEnvironmentShaders(runtime.Scene);scene.ConfigureEnvironment(resource,Config(1,MathF.PI),()=>true);
        Bad(()=>scene.ConfigureEnvironment(null,EnvironmentLightingConfiguration.Off,()=>{Bad(resource.Dispose);Bad(()=>scene.ConfigureEnvironment(null,EnvironmentLightingConfiguration.Off,()=>true));throw new InvalidOperationException("revoked");}));
        Pass(scene.EnvironmentLighting==Config(1,MathF.PI)&&Render(scene).SequenceEqual(lit),"reentry/revocation preserves binding and image");
        scene.ConfigureEnvironment(null,EnvironmentLightingConfiguration.Off,()=>true);Pass(Render(scene).SequenceEqual(off),"unbind restores Off output");resource.Replace(env,()=>true);Pass(renderer.EnvironmentStats.Publications==stats.Publications+1,"unbound replacement succeeds");
        float[] directional=new float[32*16*4];for(int y=0;y<16;y++)for(int x=0;x<32;x++){
            int i=(y*32+x)*4;float z=MathF.Sin(((x+.5f)/32-.5f)*2*MathF.PI)*MathF.Sin((y+.5f)/16*MathF.PI);
            directional[i]=directional[i+1]=directional[i+2]=MathF.Max(0,z)*4+.01f;directional[i+3]=1;}
        var directed=cook.Cook(Guid.NewGuid(),1,HdrEnvironmentSource.Prepare(Guid.NewGuid(),32,16,directional),new(8,4,8,128));
        using(var directedResource=renderer.CreateEnvironment(directed,()=>true)){
            var directedConfig=new EnvironmentLightingConfiguration(directed.AssetId,directed.Generation,directed.ContentHash,1,0,true);
            scene.ConfigureEnvironment(directedResource,directedConfig,()=>true);var forward=Render(scene);
            scene.ConfigureEnvironment(directedResource,directedConfig with{RotationRadians=MathF.PI},()=>true);var backward=Render(scene);
            Pass(forward[(128*256+128)*4]>backward[(128*256+128)*4]+20,"Y rotation applied to actual directional IBL forward="+forward[(128*256+128)*4]+" backward="+backward[(128*256+128)*4]);
            scene.ConfigureEnvironment(null,EnvironmentLightingConfiguration.Off,()=>true);
        }
        // Both shadow variants share old Tone/Shadow/skin contracts, but independent environment Geometry.
        var shadowCatalog=DefaultEnvironmentSceneShaders.CopyCatalog(renderer,true);var shadowCompiled=RegisteredEnvironmentSceneShaders.Prepare(renderer,shadowCatalog,DefaultEnvironmentSceneShaders.Select(shadowCatalog,true),()=>true);
        var shadowRuntime=EnvironmentShaderPreparation.Prepare(renderer,shadowCompiled.CookPackage(skin),()=>true);
        using(var shadowScene=new ScenePipelineSession(renderer,new Scene3DPipeline(ambient:0,shadowResolution:256),256,256,environmentShaders:shadowRuntime.Scene)){
            shadowScene.ConfigureEnvironment(resource,Config(),()=>true);
            var synthetic=SyntheticModel();var sm=synthetic.Meshes[0];var payload=new MeshPayload(true,2,sm.Vertices,[],sm.Indices,sm.TriangleMaterials,sm.Bindings,1);
            using var skinned=renderer.CreateSkinnedMesh(SkinUploadData.Prepare(payload),shadowRuntime.Skin!);
            renderer.UpdateSkins(frame,[new(skinned,0)],[GpuSkinPalette.Create(Matrix4x4.Identity),GpuSkinPalette.Create(Matrix4x4.Identity)]);
            var draw=SceneGpuDraw.Create(skinned,material.Resource,SkinUploadData.Prepare(payload).Ranges[0],Matrix4x4.CreateTranslation(0,0,-4),vp);
            shadowScene.Submit(frame++,[draw],[draw],lighting,Matrix4x4.Identity,new(),target);renderer.CaptureTarget(target,pixels);renderer.Present();
            Pass(renderer.PipelineStats.ShadowDraws>0&&renderer.SkinStats.Batches>0,"actual registered skin + shadow + environment draw");
            Bad(resource.Dispose);
        }
        resource.Replace(env,()=>true);Pass(renderer.EnvironmentStats.Live==1,"scene close releases its pin");
        var gpu=renderer.Stats;Pass(gpu.ValidationErrors==0&&gpu.ValidationWarnings==0,"D3D11 API validation 0/0");
        File.WriteAllBytes(Path.Combine(output,"environment-bound.png"),Png(256,256,lit));
        File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{phase="M7.3-B2",cases,apiErrors=gpu.ValidationErrors,apiWarnings=gpu.ValidationWarnings,fullIblImageAcceptance=false,formalHosts=false}));
        Console.WriteLine($"M7.3-B2 environment scene tests passed ({cases} cases), API {gpu.ValidationErrors}/{gpu.ValidationWarnings}; B3/C acceptance pending.");return 0;
    }
}
