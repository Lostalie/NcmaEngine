using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;

internal static unsafe partial class Program
{
    private static int EnvironmentShaderFileTests(string root, string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(output,"assets/shaders"));
        int cases=0;
        void Pass(bool value,string label){Check(value,"M7.3-C2-A "+label);cases++;}
        void Bad(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or IOException or JsonException or OperationCanceledException or PluginException){cases++;return;}throw new Exception("Expected C2-A rejection");}
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(root),Specs().Take(2).ToArray());
        var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Environment shader files",256,256,false);
        var packages=new List<RuntimeShaderPackage>();RuntimeShaderPackage wrong,userOff,userShadow,otherSkin,otherSkinOff;
        using(var cook=new RendererSession(native,window,256,256)){
            packages.Add(cook.DefaultRuntimeShaders.Prepare(ShaderProfile.Flat2D,false,false,()=>true).Package);
            foreach(bool skin in new[]{false,true})foreach(bool shadow in new[]{false,true}){
                packages.Add(cook.DefaultRuntimeShaders.Prepare(ShaderProfile.Scene3D,shadow,skin,()=>true).Package);
                packages.Add(cook.DefaultRuntimeShaders.PrepareEnvironment(shadow,skin,()=>true).Package);
            }
            using var compiler=new ShaderCompilerService(cook,()=>true);
            RuntimeShaderPackage Changed(bool shadows,bool skin,string mode){
                var selected=DefaultEnvironmentSceneShaders.Select(DefaultEnvironmentSceneShaders.CopyCatalog(cook,shadows),shadows);
                var descriptors=shadows?new[]{selected.GeometryVertex,selected.GeometryPixel,selected.ToneVertex,selected.TonePixel,selected.ShadowVertex!,selected.ShadowPixel!}:
                    new[]{selected.GeometryVertex,selected.GeometryPixel,selected.ToneVertex,selected.TonePixel};
                var inputs=new List<RuntimeShaderInput>();
                for(int i=0;i<descriptors.Length;i++){
                    var descriptor=descriptors[i];if(i==1){
                        var d=descriptor.CopyDefinition();string source=mode=="wrong"?d.Source.Replace(":SV_TARGET",":SV_TARGET1",StringComparison.Ordinal):
                            d.Source.Replace("*ao*EnvironmentSettings.x;","*ao*EnvironmentSettings.x*.5;",StringComparison.Ordinal);
                        Pass(source!=d.Source,"actual pixel fixture "+mode);
                        descriptor=ShaderDescriptor.Prepare(d with{AssetId=Guid.NewGuid(),Source=source,SourceHash=ShaderContractCodec.HashSource(source)});
                    }
                    inputs.Add(new(i<2?(i==0?RuntimeShaderRole.EnvironmentGeometryVertex:RuntimeShaderRole.EnvironmentGeometryPixel):(RuntimeShaderRole)(i+2),compiler.Prepare(descriptor)));
                }
                if(skin){
                    var d=DefaultSkinShader.Select(DefaultSkinShader.CopyCatalog(cook)).CopyDefinition();
                    string source=d.Source.Replace("n=Unit(n,float3(0,1,0));","p+=float3(.25,0,0);n=Unit(n,float3(0,1,0));",StringComparison.Ordinal);
                    Pass(source!=d.Source,"actual distinct shared skin fixture");
                    inputs.Add(new(RuntimeShaderRole.SkinCompute,compiler.Prepare(ShaderDescriptor.Prepare(d with{AssetId=Guid.NewGuid(),Source=source,SourceHash=ShaderContractCodec.HashSource(source)}))));
                }
                return RuntimeShaderPackage.Cook(ShaderProfile.SceneEnvironment,shadows,skin,inputs);
            }
            wrong=Changed(true,false,"wrong");userOff=Changed(false,false,"half");userShadow=Changed(true,false,"half");otherSkin=Changed(true,true,"half");otherSkinOff=Changed(false,true,"half");
            Pass(packages.Count==9&&packages.All(p=>!p.GpuValidated),"nine bounded immutable packages not GPU credentials");
            Bad(()=>cook.DefaultRuntimeShaders.Prepare(ShaderProfile.SceneEnvironment,false,false,()=>true));
            Pass(cook.EnvironmentStats.Live==0&&cook.PipelineStats.Pipelines==0&&cook.SkinStats.Meshes==0,"preparation creates no environment/scene/skin resources");
        }
        ShaderPackageFile Save(RuntimeShaderPackage p,string name){
            string path="assets/shaders/"+name+".ncshader";File.WriteAllBytes(Path.Combine(output,path),p.CopyBytes());return new(path,p.ContentHash,p.Profile.ToString(),p.Shadows,p.Skinning);
        }
        var entries=packages.Select((p,i)=>Save(p,"package"+i)).ToArray();
        var environment=entries.Where(e=>e.Profile=="SceneEnvironment").ToArray();
        var original=entries.Where(e=>e.Profile!="SceneEnvironment").ToArray();
        var selection=new ShaderPackageSelection(2,entries);
        Pass(ShaderPackageSelection.Decode(selection.Encode()).Packages.SequenceEqual(entries),"closed version2 nine-package roundtrip");
        Pass(new ShaderPackageSelection(1,original).CopyValidated().Packages.Length==5,"original version1 unchanged");
        Bad(()=>new ShaderPackageSelection(1,entries).CopyValidated());
        Bad(()=>new ShaderPackageSelection(1,environment.Take(2).ToArray()).CopyValidated());
        Bad(()=>new ShaderPackageSelection(2,[environment[0]]).CopyValidated());
        Bad(()=>new ShaderPackageSelection(2,entries.Concat(new[]{entries[0]}).ToArray()).CopyValidated());
        Bad(()=>new ShaderPackageSelection(3,environment).CopyValidated());
        Bad(()=>new ShaderPackageSelection(2,[environment[0] with{Profile="Unknown"},environment[1]]).CopyValidated());
        Bad(()=>new ShaderPackageSelection(2,[environment[0] with{Skinning=true},environment[1]]).CopyValidated());
        Bad(()=>new ShaderPackageSelection(2,[environment[0],environment[1] with{Path=environment[0].Path}]).CopyValidated());
        var owned=selection.CopyValidated();owned.Packages[0]=entries[0] with{Sha256=new string('0',64)};
        Pass(selection.Packages[0]==entries[0],"selection owns caller array");
        string json=Encoding.UTF8.GetString(selection.Encode());
        Bad(()=>ShaderPackageSelection.Decode(Encoding.UTF8.GetBytes(json.Replace("\"schemaVersion\": 2","\"schemaVersion\": 2, \"schemaVersion\": 2",StringComparison.Ordinal))));
        Bad(()=>ShaderPackageSelection.Decode(Encoding.UTF8.GetBytes(json.Replace("\"packages\":","\"unknown\":0,\"packages\":",StringComparison.Ordinal))));
        Bad(()=>RuntimeShaderFileSet.Load(output,selection,new CancellationToken(true)).Dispose());
        var mismatched=environment.Where(e=>e.Skinning).ToArray();mismatched[1]=Save(otherSkin,"other-skin");
        Bad(()=>RuntimeShaderFileSet.Load(output,new(2,mismatched)).Dispose());
        var sameOtherSkin=new[]{Save(otherSkinOff,"other-skin-off"),mismatched[1]};
        using(var independent=RuntimeShaderFileSet.Load(output,new(2,sameOtherSkin)))Pass(independent.Count==2,"independent custom skin pair valid");
        Bad(()=>RuntimeShaderFileSet.Load(output,new(2,original.Concat(environment.Where(e=>!e.Skinning)).Concat(sameOtherSkin).ToArray())).Dispose());
        Bad(()=>RuntimeShaderFileSet.Load(output,new(2,[environment[0] with{Sha256=new string('0',64)},environment[1]])).Dispose());
        // A structurally valid environment file may never be declared as the old profile.
        Bad(()=>RuntimeShaderFileSet.Load(output,new(2,environment.Take(2).Select(e=>e with{Profile="Scene3D"}).ToArray())).Dispose());
        string index=Path.Combine(output,RuntimeShaderFileSet.DefaultSelectionPath);File.WriteAllBytes(index,selection.Encode());
        SceneEnvironmentRuntimeShaders? retained=null;
        using(var files=RuntimeShaderFileSet.Load(output))using(var renderer=new RendererSession(native,window,256,256)){
            Pass(files.Count==9,"all nine exact file pins");
            Bad(()=>File.Open(index,FileMode.Open,FileAccess.Write,FileShare.ReadWrite).Dispose());
            Bad(()=>File.Open(Path.Combine(output,environment[3].Path),FileMode.Open,FileAccess.Write,FileShare.ReadWrite).Dispose());
            Bad(()=>files.InstallSelection(renderer,()=>false));
            files.InstallSelection(renderer,()=>true);
            foreach(var p in packages){
                var actual=p.Profile==ShaderProfile.SceneEnvironment?renderer.DefaultRuntimeShaders.PrepareEnvironment(p.Shadows,p.Skinning,()=>true).Package:
                    renderer.DefaultRuntimeShaders.Prepare(p.Profile,p.Shadows,p.Skinning,()=>true).Package;
                Pass(actual.ContentHash==p.ContentHash,"actual file-backed admission "+p.Profile+"/"+p.Shadows+"/"+p.Skinning);
            }
            bool allowed=true;var pair=SceneEnvironmentRuntimeShaders.PrepareDefault(renderer,true,()=>allowed);retained=pair;
            Pass(pair.Unshadowed.Package.Skinning&&pair.Shadowed.Package.Shadows&&pair.Unshadowed.CopyMetadata().Length==5,"source-free exact pair/shared compute");
            Bad(()=>new SceneEnvironmentRuntimeShaders(renderer,pair.Shadowed,pair.Unshadowed));
            var unskinned=renderer.DefaultRuntimeShaders.PrepareEnvironment(true,false,()=>true);
            Bad(()=>new SceneEnvironmentRuntimeShaders(renderer,pair.Unshadowed,unskinned));
            var differentSkin=EnvironmentShaderPreparation.Prepare(renderer,otherSkin,()=>true);
            Bad(()=>new SceneEnvironmentRuntimeShaders(renderer,pair.Unshadowed,differentSkin));
            Bad(()=>renderer.DefaultRuntimeShaders.PrepareEnvironment(false,true,()=>false));
            Exception? threadError=null;var thread=new Thread(()=>{try{pair.VerifyFor(renderer);}catch(Exception e){threadError=e;}});thread.Start();thread.Join();
            Pass(threadError is InvalidOperationException,"renderer owner thread enforced");
            allowed=false;Bad(()=>pair.VerifyFor(renderer));allowed=true;pair.VerifyFor(renderer);
            Bad(()=>renderer.DefaultRuntimeShaders.PrepareEnvironment(false,false,()=>{renderer.DefaultRuntimeShaders.PrepareEnvironment(false,false,()=>true);return true;}));
            renderer.SubmitResources(1,[],new(0,0,256,256),Vector4.Zero,new(Vector3.Zero,Vector3.UnitY,Vector4.Zero));
            Bad(()=>pair.VerifyFor(renderer));renderer.Present();pair.VerifyFor(renderer);
            Pass(renderer.EnvironmentStats.Live==0&&renderer.EnvironmentStats.UploadedBytes==0&&renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0,"selection/pair no 3D/environment allocation");
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"whole mixed profile admission API0/0");
        }
        using(var foreign=new RendererSession(native,window,256,256)) {
            Bad(()=>retained!.VerifyFor(foreign));
            var fresh=foreign.DefaultRuntimeShaders.PrepareEnvironment(false,true,()=>true);
            Bad(()=>new SceneEnvironmentRuntimeShaders(foreign,fresh,retained!.Shadowed));
        }
        using(File.Open(index,FileMode.Open,FileAccess.Write,FileShare.Read))Pass(true,"index pin released");
        using(File.Open(Path.Combine(output,environment[3].Path),FileMode.Open,FileAccess.Write,FileShare.Read))Pass(true,"last environment pin released");
        File.WriteAllBytes(Path.Combine(output,"deployment-manifest.json"),JsonSerializer.SerializeToUtf8Bytes(new{files=new[]{new{path=RuntimeShaderFileSet.DefaultSelectionPath,size=new FileInfo(index).Length,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(index)))}}}));
        using(var deployed=RuntimeShaderFileSet.ForHost(output,null,null))Pass(deployed!.Count==9,"version2 index independently bound to manifest");
        File.WriteAllBytes(index,new ShaderPackageSelection(1,original).Encode());Bad(()=>RuntimeShaderFileSet.ForHost(output,null,null)?.Dispose());
        using(var files=RuntimeShaderFileSet.Load(output,new(1,original)))using(var renderer=new RendererSession(native,window,256,256)){
            files.InstallSelection(renderer,()=>true);Bad(()=>SceneEnvironmentRuntimeShaders.PrepareDefault(renderer,false,()=>true));
            Pass(renderer.DefaultRuntimeShaders.Prepare(ShaderProfile.Scene3D,false,false,()=>true).Package.Profile==ShaderProfile.Scene3D,"old selection does not fallback/upgrade");
        }
        // The last environment group passes CPU DXBC preflight but fails actual output reflection.
        var bad=Save(wrong,"last-wrong-output");var badEntries=original.Concat(new[]{environment[0],bad}).ToArray();
        using(var files=RuntimeShaderFileSet.Load(output,new(2,badEntries)))using(var renderer=new RendererSession(native,window,256,256)){
            Bad(()=>files.InstallSelection(renderer,()=>true));
            Pass(renderer.EnvironmentStats.Live==0&&renderer.PipelineStats.Pipelines==0&&renderer.UiShaderGeneration==0,"last actual admission failure publishes no selection/resources");
            using var correct=RuntimeShaderFileSet.Load(output,selection);correct.InstallSelection(renderer,()=>true);
            Pass(SceneEnvironmentRuntimeShaders.PrepareDefault(renderer,false,()=>true).Shadowed.BindingsValidated,"whole selection retry after late failure");
        }
        // Actual custom profile3 programs travel through the same file/default-service path.
        var userEntries=new[]{Save(userOff,"user-off"),Save(userShadow,"user-shadow")};
        using(var files=RuntimeShaderFileSet.Load(output,new(2,userEntries)))using(var renderer=new RendererSession(native,window,256,256)){
            files.InstallSelection(renderer,()=>true);var pair=SceneEnvironmentRuntimeShaders.PrepareDefault(renderer,false,()=>true);
            Bad(()=>renderer.DefaultRuntimeShaders.Prepare(ShaderProfile.Scene3D,false,false,()=>true));
            Bad(()=>SceneEnvironmentRuntimeShaders.PrepareDefault(renderer,true,()=>true));
            using var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false,ambient:0),256,256,environmentShaders:pair.Unshadowed.Scene);
            using var cook=new EnvironmentCookService(native,()=>true);var env=cook.Cook(Guid.NewGuid(),1,HdrEnvironmentSource.Neutral(Guid.NewGuid()),new(8,4,8,128));
            using var gpu=renderer.CreateEnvironment(env,()=>true);
            using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(256,256);using var mesh=renderer.CreateStaticMesh(Quad());
            var material=MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(.6f,.3f,.1f,1),Roughness=.7f};
            using var m=cache.AcquireMaterial(Version(material),material,_=>null,true,true,out _);
            byte[] pixels=new byte[256*256*4];var model=Matrix4x4.CreateScale(4,3,1)*Matrix4x4.CreateTranslation(0,0,-4.5f);var vp=Matrix4x4.CreateOrthographic(4,4,.1f,50);
            byte[] Render(ulong f){scene.Submit(f,[SceneGpuDraw.Create(mesh,m.Resource,Quad().Ranges[0],model,vp)],[],new(Vector3.Zero,Vector3.UnitZ,new(1,1,1,0)),Matrix4x4.Identity,new(),target);renderer.CaptureTarget(target,pixels);renderer.Present();return (byte[])pixels.Clone();}
            var off=Render(1);
            scene.ConfigureEnvironment(gpu,new(env.AssetId,env.Generation,env.ContentHash,1,0,true),()=>true);var user=Render(2);
            Pass(!user.SequenceEqual(off)&&user[(128*256+128)*4]>off[(128*256+128)*4],"file-selected user actually illuminates pixels");
            var reference=EnvironmentShaderPreparation.Prepare(renderer,packages.Single(p=>p.Profile==ShaderProfile.SceneEnvironment&&!p.Shadows&&!p.Skinning),()=>true);
            scene.ReplaceEnvironmentShaders(reference.Scene);scene.ConfigureEnvironment(gpu,new(env.AssetId,env.Generation,env.ContentHash,.5f,0,true),()=>true);var expected=Render(3);
            Pass(user.SequenceEqual(expected),"pinned user half-IBL equals official half strength full-image");
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"actual custom file scene API0/0");
            scene.ConfigureEnvironment(null,EnvironmentLightingConfiguration.Off,()=>true);
        }
        using(var files=RuntimeShaderFileSet.Load(output,new(1,[original[0]])))using(var renderer=new RendererSession(native,window,256,256,pureUi:true)){
            files.InstallSelection(renderer,()=>true);renderer.PrepareDefaultUiShaders();
            Bad(()=>renderer.DefaultRuntimeShaders.PrepareEnvironment(false,false,()=>true));
            Pass(renderer.EnvironmentStats.Live==0&&renderer.EnvironmentStats.UploadedBytes==0&&renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0,"pure UI has no environment/3D resources");
        }
        File.WriteAllText(Path.Combine(output,"environment-shader-files.json"),JsonSerializer.Serialize(new{cases,sourceFree=true,selectionVersion=2,formalHostEnabled=false,manualAcceptance=false}));
        Console.WriteLine($"PASS M7.3-C2-A environment shader files/pair/whole admission: {cases} cases; API0/0");return 0;
    }
}
