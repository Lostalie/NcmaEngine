using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int RegisteredStagesTests(string root,string output)
    {
        Directory.CreateDirectory(output);int cases=0;bool allowed=true;int geometryError=0,shadowError=0;double skinError=0;
        void Pass(bool value,string message){Check(value,"M7.1-C2 "+message);cases++;}
        void Bad(Action action){Reject(action);cases++;}
        ShaderDefinition Change(ShaderDefinition d,string before,string after,string name){
            Check(d.Source.Contains(before,StringComparison.Ordinal),"Actual source mutation fixture missing");
            string source=d.Source.Replace(before,after,StringComparison.Ordinal);return d with{AssetId=Guid.NewGuid(),Name=name,Source=source,SourceHash=ShaderContractCodec.HashSource(source)};
        }
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(root),Specs().Take(2).ToArray());
        var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Registered Geometry Shadow Skin",256,256,false);
        RegisteredSceneShaders old;RegisteredSkinShader oldSkin;
        using(var renderer=new RendererSession(native,window,256,256)){
            var defaults=DefaultSceneShaders.CopyCatalog(renderer);var selection=DefaultSceneShaders.Select(defaults);
            var definitions=Enumerable.Range(0,defaults.Count/ShaderCatalog.PageSize+1).SelectMany(p=>defaults.CopyPage(p)).Select(r=>defaults.Require(r.AssetId,r.ContentHash).CopyDefinition()).ToArray();
            var color=Change(selection.GeometryPixel.CopyDefinition(),"return float4(clamp(color,0,65504),1);","return float4(clamp(color.bgr,0,65504),1);","User BGR Geometry");
            var shadow=Change(selection.ShadowPixel!.CopyDefinition(),"clip(base.a-Surface.w);","clip(base.a-Surface.w-1);","User clipped Shadow");
            var catalog=ShaderCatalog.Create(ShaderProfile.Scene3D,definitions.Concat(new[]{color,shadow}));
            old=RegisteredSceneShaders.Prepare(renderer,catalog,selection,()=>allowed);
            var colored=RegisteredSceneShaders.Prepare(renderer,catalog,selection with{GeometryPixel=ShaderDescriptor.Prepare(color)},()=>allowed);
            var clipped=RegisteredSceneShaders.Prepare(renderer,catalog,selection with{ShadowPixel=ShaderDescriptor.Prepare(shadow)},()=>allowed);
            Pass(old.CopyMetadata().Length==6&&old.CopyMetadata().All(m=>m.Compiled)&&old.CopyMetadata()[0].CatalogHash==colored.CopyMetadata()[0].CatalogHash&&clipped.CopyMetadata()[0].CatalogHash==colored.CopyMetadata()[0].CatalogHash,"default/user SAME catalog and B compiler");
            Pass(renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0&&renderer.Stats.SubmittedFrames==0,"preparation has no GPU resources/frames");
            using var cache=new RenderResourceCache(renderer);var data=Quad();using var mesh=renderer.CreateStaticMesh(data);
            var definition=MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(.6f,.25f,.1f,1),Metallic=.1f,Roughness=.6f,Mode=MaterialMode.AlphaMask};
            using var material=cache.AcquireMaterial(Version(definition),definition,_=>null,true,true,out _);
            using var target=renderer.CreateViewTarget(256,256);using var legacy=new ScenePipelineSession(renderer,new Scene3DPipeline(),256,256);
            using var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(),256,256,shaders:old);
            using var plain=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false),256,256);
            var camera=new Ncma.Scene.Rendering.SceneCameraView(Guid.Empty,Matrix4x4.CreateOrthographic(4,4,.1f,50),Vector3.Zero,Ncma.Scene.Rendering.CameraData.Default);
            Vector3 light=Vector3.Normalize(new Vector3(1,0,1));var vp=Ncma.Rendering.Scene.ShadowVolume.Create(camera,light);
            var lighting=new ResourceLighting(Vector3.Zero,light,new(1,1,1,3));var settings=new SceneShadowSettings(.0001f,.0001f,false,3);
            SceneGpuDraw[] geometry=[SceneGpuDraw.Create(mesh,material.Resource,data.Ranges[0],Matrix4x4.CreateScale(4,4,1)*Matrix4x4.CreateTranslation(0,0,-4.5f),camera.ViewProjection)];
            SceneGpuDraw[] casters=[SceneGpuDraw.Create(mesh,material.Resource,data.Ranges[0],Matrix4x4.CreateTranslation(3,0,-.5f),camera.ViewProjection)];
            byte[] baseline=new byte[256*256*4],image=new byte[baseline.Length],lit=new byte[baseline.Length];ulong frame=1;
            void Render(ScenePipelineSession s,byte[] pixels,bool shadows=true){s.Submit(frame++,geometry,shadows?casters:[],lighting,vp,settings,target);renderer.CaptureTarget(target,pixels);renderer.Present();}
            Render(legacy,baseline);Render(scene,image);geometryError=baseline.Zip(image,(a,b)=>Math.Abs(a-b)).Max();
            Pass(geometryError<=1,"registered Geometry/Shadow default image vs retained reference "+geometryError);
            Render(plain,lit,false);int pbr=0;
            foreach(int y in new[]{64,128,192})foreach(int x in new[]{64,128,192}){var position=new Vector3((x+.5f)/256*4-2,2-(y+.5f)/256*4,-4);
                var expected=ScenePbrOracle(new(.6f,.25f,.1f),.1f,.6f,position,Vector3.UnitZ,Vector3.Zero,light,3,.03f,1);
                for(int c=0;c<3;c++)pbr=Math.Max(pbr,Math.Abs(lit[(y*256+x)*4+c]-(int)MathF.Round(expected[c]*255)));}
            Pass(pbr<=2,"independent scalar GGX/ACES/sRGB retained");
            scene.ReplaceShaders(colored);Render(scene,image);int colorError=0;for(int i=0;i<image.Length;i++)colorError=Math.Max(colorError,Math.Abs(image[i]-baseline[i/4*4+(i%4==3?3:2-i%4)]));
            Pass(colorError<=1&&!image.SequenceEqual(baseline),"actual user Geometry channel oracle "+colorError);
            scene.ReplaceShaders(clipped);Render(scene,image);shadowError=lit.Zip(image,(a,b)=>Math.Abs(a-b)).Max();
            Pass(shadowError<=1&&!image.SequenceEqual(baseline),"actual user Shadow alpha discard oracle "+shadowError);
            scene.ReplaceShaders(old);Render(scene,image);Pass(image.SequenceEqual(baseline),"default restore exact image");
            RegisteredSceneShaders PrepareBad(SceneShaderSelection chosen,ShaderDefinition bad){
                var c=ShaderCatalog.Create(ShaderProfile.Scene3D,definitions.Concat(new[]{bad}));return RegisteredSceneShaders.Prepare(renderer,c,chosen,()=>allowed);
            }
            var link=Change(selection.GeometryVertex.CopyDefinition(),"float3 n:TEXCOORD1;","float3 n:TEXCOORD4;","Wrong actual Geometry link");
            var wrongLink=PrepareBad(selection with{GeometryVertex=ShaderDescriptor.Prepare(link)},link);Bad(()=>scene.ReplaceShaders(wrongLink));
            var targetCode=Change(selection.TonePixel.CopyDefinition(),"PSTone(Full i):SV_TARGET","PSTone(Full i):SV_TARGET1","Wrong actual final Tone target");
            var wrongLast=PrepareBad(selection with{TonePixel=ShaderDescriptor.Prepare(targetCode)},targetCode);Bad(()=>scene.ReplaceShaders(wrongLast));Bad(()=>new ScenePipelineSession(renderer,new Scene3DPipeline(),256,256,shaders:wrongLast));
            var offset=selection.GeometryVertex.CopyDefinition() with{AssetId=Guid.NewGuid(),Name="Bad vertex offset",Inputs=selection.GeometryVertex.CopyDefinition().Inputs.Select(i=>i.Semantic=="TANGENT"?i with{ByteOffset=36}:i).ToArray()};
            Bad(()=>PrepareBad(selection with{GeometryVertex=ShaderDescriptor.Prepare(offset)},offset));
            var member=selection.GeometryPixel.CopyDefinition() with{AssetId=Guid.NewGuid(),Name="Bad last member",Constants=selection.GeometryPixel.CopyDefinition().Constants.Select(b=>b with{Members=b.Members.Select(m=>m.Name=="ShadowParameters"?m with{Name="WrongLast"}:m).ToArray()}).ToArray()};
            Bad(()=>PrepareBad(selection with{GeometryPixel=ShaderDescriptor.Prepare(member)},member));
            var resource=Change(selection.ShadowPixel.CopyDefinition(),"BaseTex:register(t0)","BaseTex:register(t7)","Wrong Shadow slot");Bad(()=>PrepareBad(selection with{ShadowPixel=ShaderDescriptor.Prepare(resource)},resource));
            Bad(()=>RegisteredSceneShaders.Prepare(renderer,catalog,selection with{ShadowPixel=null},()=>true));
            var unshadowed=DefaultSceneShaders.CopyCatalog(renderer,false);var noShadow=RegisteredSceneShaders.Prepare(renderer,unshadowed,DefaultSceneShaders.Select(unshadowed,false),()=>true);
            Bad(()=>scene.ReplaceShaders(noShadow));using(var s=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false),256,256,shaders:noShadow)){Render(s,image,false);Pass(image.SequenceEqual(lit)&&s.RegisteredShaders!.CopyMetadata().Length==4,"real no-shadow closure has no Shadow code/resources");}
            allowed=false;Bad(()=>scene.ReplaceShaders(colored));allowed=true;
            Bad(()=>RegisteredSceneShaders.Prepare(renderer,catalog,selection,()=>{scene.ReplaceShaders(colored);return true;}));
            scene.Submit(frame++,geometry,casters,lighting,vp,settings,target);Bad(()=>scene.ReplaceShaders(colored));renderer.Present();
            Exception? wrongThread=null;var worker=new Thread(()=>{try{scene.ReplaceShaders(old);}catch(Exception e){wrongThread=e;}});worker.Start();worker.Join();Pass(wrongThread is PluginException{Result:PluginResult.WrongThread},"actual wrong thread");
            Render(scene,image);Pass(ReferenceEquals(scene.RegisteredShaders,old)&&image.SequenceEqual(baseline),"all rejected group candidates retain old group/pixels");
            ulong creates=renderer.PipelineStats.Creates,uploads=renderer.SceneStats.UploadedBytes;
            for(int i=0;i<8;i++)scene.ReplaceShaders(i%2==0?colored:old);Pass(creates==renderer.PipelineStats.Creates&&uploads==renderer.SceneStats.UploadedBytes,"same scene replacements no mesh/texture/target rebuild");

            var skinCatalog=DefaultSkinShader.CopyCatalog(renderer);var skinD=DefaultSkinShader.Select(skinCatalog);
            var userSkin=Change(skinD.CopyDefinition(),"n=Unit(n,float3(0,1,0));","p+=float3(.25,0,0);n=Unit(n,float3(0,1,0));","User translated Skin");
            var skinShared=ShaderCatalog.Create(ShaderProfile.Skinning,[skinD.CopyDefinition(),userSkin]);
            oldSkin=RegisteredSkinShader.Prepare(renderer,skinShared,skinD,()=>allowed);
            var skinUser=RegisteredSkinShader.Prepare(renderer,skinShared,ShaderDescriptor.Prepare(userSkin),()=>allowed);
            Pass(oldSkin.CopyMetadata().CatalogHash==skinUser.CopyMetadata().CatalogHash&&renderer.SkinStats.Meshes==0,"independent same Skinning catalog, no GPU preparation");
            Bad(()=>renderer.ReplaceSkinShader(oldSkin));
            var synthetic=SyntheticModel();var sm=synthetic.Meshes[0];var payload=new MeshPayload(true,2,sm.Vertices,[],sm.Indices,sm.TriangleMaterials,sm.Bindings,1);var upload=SkinUploadData.Prepare(payload);
            using(var skin=renderer.CreateSkinnedMesh(upload,oldSkin)){
                using var sibling=renderer.CreateSkinnedMesh(upload,oldSkin);
                Bad(()=>renderer.CreateSkinnedMesh(upload,skinUser));
                var palette=new[]{GpuSkinPalette.Create(Matrix4x4.Identity),GpuSkinPalette.Create(Matrix4x4.Identity)};
                byte[] originalVertices=new byte[skin.VertexCount*48],shifted=new byte[originalVertices.Length];
                void SkinCapture(GpuMesh m,byte[] bytes){renderer.UpdateSkins(frame++,[new(m,0)],palette);renderer.CaptureSkinVertices(m,bytes);}
                SkinCapture(skin,originalVertices);
                renderer.ReplaceSkinShader(skinUser);SkinCapture(skin,shifted);CheckSkin(originalVertices,shifted,.25f);
                byte[] siblingVertices=new byte[shifted.Length];SkinCapture(sibling,siblingVertices);Pass(siblingVertices.SequenceEqual(shifted),"shared kernel applies to ALL existing meshes");
                renderer.ReplaceSkinShader(oldSkin);SkinCapture(skin,shifted);Pass(shifted.SequenceEqual(originalVertices),"same mesh default compute restore");
                var group=Change(skinD.CopyDefinition(),"numthreads(64,1,1)","numthreads(32,1,1)","Bad actual dispatch");
                var groupCatalog=ShaderCatalog.Create(ShaderProfile.Skinning,[group]);var badGroup=RegisteredSkinShader.Prepare(renderer,groupCatalog,ShaderDescriptor.Prepare(group),()=>true);
                Bad(()=>renderer.ReplaceSkinShader(badGroup));Bad(()=>renderer.CreateSkinnedMesh(upload,badGroup));
                var stride=skinD.CopyDefinition() with{AssetId=Guid.NewGuid(),Name="Bad declared stride",Resources=skinD.CopyDefinition().Resources.Select(r=>r.Semantic=="Bones"?r with{ElementStride=112}:r).ToArray()};
                var strideCatalog=ShaderCatalog.Create(ShaderProfile.Skinning,[stride]);Bad(()=>RegisteredSkinShader.Prepare(renderer,strideCatalog,ShaderDescriptor.Prepare(stride),()=>true));
                allowed=false;Bad(()=>renderer.ReplaceSkinShader(skinUser));allowed=true;
                Bad(()=>RegisteredSkinShader.Prepare(renderer,skinShared,skinD,()=>{scene.ReplaceShaders(old);return true;}));
                SkinCapture(skin,shifted);Pass(shifted.SequenceEqual(originalVertices),"rejected Skin candidates retain old numerical result");
                ulong skinCreates=renderer.SkinStats.Creates,resident=renderer.SkinStats.ResidentBytes,paletteBytes=renderer.SkinStats.PaletteBytes;
                for(int i=0;i<8;i++)renderer.ReplaceSkinShader(i%2==0?skinUser:oldSkin);
                Pass(skinCreates==renderer.SkinStats.Creates&&resident==renderer.SkinStats.ResidentBytes&&paletteBytes==renderer.SkinStats.PaletteBytes,"Skin replacement keeps mesh/output/palette resources and no tick/upload");
                void CheckSkin(byte[] a,byte[] b,float dx){for(int v=0;v<skin.VertexCount;v++)for(int at=0;at<48;at+=4){
                    float expected=BinaryPrimitives.ReadSingleLittleEndian(a.AsSpan(v*48+at,4))+(at==0?dx:0),actual=BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(v*48+at,4));skinError=Math.Max(skinError,Math.Abs(actual-expected));
                    Check(Math.Abs(actual-expected)<=1e-6,"User Skin CPU position/normal/tangent/UV/sign oracle");}Pass(skinError<=1e-6,"actual user compute independent numerical oracle");}
            }
            Pass(renderer.SkinStats.Meshes==0&&renderer.SkinStats.ResidentBytes==0,"last mesh releases registered compute kernel/palettes");
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"actual DX11 API0/0");
            File.WriteAllBytes(Path.Combine(output,"registered-stages-default.png"),Png(256,256,baseline));File.WriteAllBytes(Path.Combine(output,"registered-stages-unshadowed.png"),Png(256,256,lit));
        }
        using(var renderer=new RendererSession(native,window,256,256,pureUi:true)){
            Bad(()=>new ScenePipelineSession(renderer,new Scene3DPipeline(),256,256,shaders:old));Bad(()=>renderer.ReplaceSkinShader(oldSkin));
            Pass(renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0&&renderer.SkinStats.ResidentBytes==0,"foreign preparations rejected and pure2D has no scene/skin resources");
        }
        File.WriteAllText(Path.Combine(output,"registered-stages-results.json"),JsonSerializer.Serialize(new{schema=1,cases,query=10,api=1,backend="DX11",sameCatalogDefaultUser=true,actualGeometry=true,actualShadow=true,actualSkin=true,geometryError,shadowError,skinError,validationErrors=0,validationWarnings=0,formalHostSwitch=false,runtimeShaderPackage=false,pure2DShaderRegistration=false,agentExecutionAuthority=false,manualAcceptance=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS M7.1-C2 {cases} actual Geometry/Shadow/Skin same-catalog/atomic/closed-boundary cases; image={geometryError}/{shadowError},skin={skinError}; API0/0");return 0;
    }
}
