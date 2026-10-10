using System.Numerics;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int MaterialSurfaceTests(string root,string output)
    {
        Directory.CreateDirectory(output);int cases=0,maxError=0,skinError=0;ulong frame=1;
        void Pass(bool value,string message){Check(value,"M7.2 "+message);cases++;}
        void Bad(Action action){Reject(action);cases++;}
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(root),Specs().Take(2).ToArray());
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"M7.2 PBR surfaces",256,256,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,256,256);
        var prepared=renderer.DefaultRuntimeShaders.Prepare(ShaderProfile.Scene3D,false,true,()=>true);
        using var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false,ambient:.13f),256,256,shaders:prepared.Scene);
        using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(256,256);
        byte[] image=new byte[256*256*4],skinImage=new byte[image.Length];
        var vp=Matrix4x4.CreateOrthographic(4,4,.1f,50);var light=Vector3.Normalize(new Vector3(.3f,.7f,1));
        var lighting=new ResourceLighting(Vector3.Zero,light,new(1,1,1,2));
        var textures=new Dictionary<Guid,TextureData>();
        Guid Texture(TextureSemantic role,byte[] bytes,bool flip=false){var id=Guid.NewGuid();textures.Add(id,TextureData.Prepare(1,1,role,bytes,flip));return id;}
        ResolvedTexture? Resolve(Guid id)=>textures.TryGetValue(id,out var t)?new(Version(id,t),t):null;
        Guid baseId=Texture(TextureSemantic.Color,[128,180,220,255]),packed=Texture(TextureSemantic.Data,[191,153,102,255]);
        Guid emissive=Texture(TextureSemantic.Color,[190,100,40,255]);
        var basic=MaterialSurfaceContract.WithPackedSurface(MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(.7f,.5f,.3f,1),BaseTexture=baseId},packed,PackedSurfaceLayout.OcclusionRoughnessMetallic);
        float Decode(byte b){float x=b/255f;return x<=.04045f?x/12.92f:MathF.Pow((x+.055f)/1.055f,2.4f);}
        Vector3 albedo=new Vector3(Decode(128),Decode(180),Decode(220))*new Vector3(.7f,.5f,.3f);
        void Render(GpuMesh mesh,GpuMaterial material,MeshDrawRange range,Matrix4x4 model,byte[] pixels,ResourceLighting? lights=null){
            scene.Submit(frame++,[SceneGpuDraw.Create(mesh,material,range,model,vp)],[],lights??lighting,Matrix4x4.Identity,new SceneShadowSettings(),target);
            renderer.CaptureTarget(target,pixels);renderer.Present();
        }
        void Oracle(Vector3 normal,float metal,float rough,float ao,string label){
            int error=0;
            foreach(int y in new[]{96,128,160})foreach(int x in new[]{96,128,160}){
                var world=new Vector3((x+.5f)/256*4-2,2-(y+.5f)/256*4,-4);
                var expected=ScenePbrOracle(albedo,metal,rough,world,normal,Vector3.Zero,light,2,.13f*ao,1);
                for(int c=0;c<3;c++)error=Math.Max(error,Math.Abs(image[(y*256+x)*4+c]-(int)MathF.Round(expected[c]*255)));
            }
            maxError=Math.Max(maxError,error);Pass(error<=3,label+" independent linear texture/GGX oracle error="+error);
        }
        using var quad=renderer.CreateStaticMesh(Quad());var range=Quad().Ranges[0];
        var objectModel=Matrix4x4.CreateScale(4,3,1)*Matrix4x4.CreateTranslation(0,0,-4.5f);
        using(var material=cache.AcquireMaterial(Version(basic),basic,Resolve,true,true,out var diagnostics)){
            Pass(diagnostics.Count==0,"complete candidate has no fallback");Render(quad,material.Resource,range,objectModel,image);
            Oracle(Vector3.UnitZ,102/255f,153/255f,191/255f,"sRGB base and linear ORM");
            ulong creates=renderer.ResourceStats.Creates,uploads=renderer.ResourceStats.UploadedBytes;
            using var same=cache.AcquireMaterial(Version(basic),basic,Resolve,true,true,out _);
            Pass(ReferenceEquals(same.Resource,material.Resource),"resident material reused");
            for(int i=0;i<8;i++)Render(quad,material.Resource,range,objectModel,image);
            Pass(creates==renderer.ResourceStats.Creates&&uploads==renderer.ResourceStats.UploadedBytes,"stable frames no texture/mesh creation or upload");
            File.WriteAllBytes(Path.Combine(output,"orm-pbr.png"),Png(256,256,image));
        }
        var mra=MaterialSurfaceContract.WithPackedSurface(basic with{AssetId=Guid.NewGuid()},packed,PackedSurfaceLayout.MetallicRoughnessOcclusion);
        using(var material=cache.AcquireMaterial(Version(mra),mra,Resolve,true,true,out _)){
            Render(quad,material.Resource,range,objectModel,image);Oracle(Vector3.UnitZ,191/255f,153/255f,102/255f,"explicit MRA channel remap");
        }
        var glow=basic with{AssetId=Guid.NewGuid(),EmissiveTexture=emissive,Emissive=new(.3f,.5f,.7f,0)};
        using(var material=cache.AcquireMaterial(Version(glow),glow,Resolve,true,true,out _)){
            Render(quad,material.Resource,range,objectModel,image,lighting with{LightColor=new(1,1,1,0)});
            var linear=albedo*(.13f*191/255f)+new Vector3(Decode(190),Decode(100),Decode(40))*new Vector3(.3f,.5f,.7f);
            int error=0;for(int c=0;c<3;c++){float x=linear[c];x=Math.Clamp(x*(2.51f*x+.03f)/(x*(2.43f*x+.59f)+.14f),0,1);float display=x<=.0031308f?12.92f*x:1.055f*MathF.Pow(x,1/2.4f)-.055f;
                error=Math.Max(error,Math.Abs(image[(128*256+128)*4+c]-(int)MathF.Round(display*255)));}
            maxError=Math.Max(maxError,error);Pass(error<=3,"emissive sRGB decode then linear HDR add="+error);
        }
        // Alpha is linear despite Color RGB being sRGB: 128/255 must survive .5 and fail .51.
        Guid alpha=Texture(TextureSemantic.Color,[128,180,220,128]);
        foreach(float cutoff in new[]{.5f,.51f}) {
            var mask=basic with{AssetId=Guid.NewGuid(),BaseTexture=alpha,Mode=MaterialMode.AlphaMask,AlphaCutoff=cutoff};
            using var material=cache.AcquireMaterial(Version(mask),mask,Resolve,true,true,out _);Render(quad,material.Resource,range,objectModel,image);
            Pass(cutoff==.5f?image[(128*256+128)*4]>0:image[(128*256+128)*4]==0,"linear alpha cutoff "+cutoff);
        }
        // Smooth tilted normals, mirrored UV signs and positive nonuniform matrices; independent CPU TBN.
        Vector3 sourceN=Vector3.Normalize(new Vector3(.25f,.4f,1)),sourceT=Vector3.Normalize(new Vector3(1,0,-.25f));
        foreach(float sign in new[]{-1f,1f})foreach(bool flip in new[]{false,true})foreach(float scale in new[]{0f,.6f,1.5f})foreach(bool deform in new[]{false,true}){
            ImportVertex V(float x,float y,float u,float v)=>new(new(x,y,.5f),sourceN,new(u,v),default,new(1,0,0,0));
            var vertices=new[]{V(-.8f,.8f,0,0),V(.8f,.8f,1,0),V(-.8f,-.8f,0,1),V(.8f,-.8f,1,1)};
            var tangents=Enumerable.Repeat(new Vector4(sourceT,sign),4).ToArray();
            var source=new MeshPayload(true,1,vertices,tangents,[0,1,2,2,1,3],[0,0],[new(0,[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1])],1);
            var stat=MeshUploadData.PrepareStatic(source with{Skinned=false,BoneCount=0,Bindings=[],Vertices=vertices.Select(v=>v with{Weights=default}).ToArray()});
            var skin=SkinUploadData.Prepare(source);using var staticMesh=renderer.CreateStaticMesh(stat);using var skinned=renderer.CreateSkinnedMesh(skin,prepared.Skin!);
            Guid normalId=Texture(TextureSemantic.Normal,[178,210,220,255],flip);
            var definition=basic with{AssetId=Guid.NewGuid(),NormalTexture=normalId,NormalScale=scale};
            using var material=cache.AcquireMaterial(Version(definition),definition,Resolve,true,true,out _);
            var model=Matrix4x4.CreateScale(4,3,1)*Matrix4x4.CreateRotationZ(.2f)*Matrix4x4.CreateTranslation(0,0,-4.5f);
            var bone=deform?Matrix4x4.CreateScale(.9f,1.15f,1)*Matrix4x4.CreateRotationZ(.1f):Matrix4x4.Identity;
            var combined=bone*model;Matrix4x4.Invert(combined,out var inverse);var n=Vector3.Normalize(Vector3.TransformNormal(sourceN,Matrix4x4.Transpose(inverse)));
            var t=Vector3.TransformNormal(sourceT,combined);t=Vector3.Normalize(t-n*Vector3.Dot(n,t));var b=Vector3.Cross(n,t)*sign;
            var sampled=new Vector3(178,210,220)/127.5f-Vector3.One;sampled.X*=scale;sampled.Y*=scale*(flip?-1:1);
            var expected=Vector3.Normalize(t*sampled.X+b*sampled.Y+n*sampled.Z);
            Render(staticMesh,material.Resource,stat.Ranges[0],combined,image);Oracle(expected,102/255f,153/255f,191/255f,$"TBN {sign}/{flip}/{scale}/{deform}");
            renderer.UpdateSkins(frame,[new(skinned,0)],[GpuSkinPalette.Create(bone)]);
            Render(skinned,material.Resource,skin.Ranges[0],model,skinImage);
            int error=image.Zip(skinImage,(a,b)=>Math.Abs(a-b)).Max();skinError=Math.Max(skinError,error);Pass(error<=1,"static/actual compute skin image="+error);
        }
        using(var material=cache.AcquireMaterial(Version(basic),basic,Resolve,true,true,out _)){
            Bad(()=>SceneGpuDraw.Create(quad,material.Resource,range,Matrix4x4.CreateScale(-1,1,1),vp));
            Bad(()=>SceneGpuDraw.Create(quad,material.Resource,range,Matrix4x4.CreateScale(0,1,1),vp));
        }
        // Wrong-role assets must fail even on a UV/tangent fallback path, before native candidates.
        Guid wrongId=Texture(TextureSemantic.Color,[255,128,128,255]);var wrong=basic with{AssetId=Guid.NewGuid(),NormalTexture=wrongId};
        ulong beforeCreates=renderer.ResourceStats.Creates,beforeUploads=renderer.ResourceStats.UploadedBytes;
        foreach(bool uv in new[]{false,true})foreach(bool tangent in new[]{false,true})Bad(()=>cache.AcquireMaterial(Version(wrong),wrong,Resolve,uv,tangent,out _));
        Pass(renderer.ResourceStats.Creates==beforeCreates&&renderer.ResourceStats.UploadedBytes==beforeUploads,"wrong semantic rejected before GPU allocation");
        using(var texture=renderer.CreateTexture(textures[wrongId])){
            ulong creates=renderer.ResourceStats.Creates;
            Bad(()=>renderer.CreateMaterial(wrong,[null,texture,null,null,null,null],true));
            Bad(()=>renderer.CreateMaterial(wrong,[null,texture,null,null,null,null],false));
            Pass(renderer.ResourceStats.Creates==creates,"direct native material wrapper rejects wrong role even with normal map disabled");
        }
        var missing=basic with{AssetId=Guid.NewGuid(),NormalTexture=Guid.NewGuid()};
        using(var fallback=cache.AcquireMaterial(Version(missing),missing,Resolve,true,true,out var diagnostics))Pass(diagnostics.Count==1&&diagnostics[0].Code=="missing_texture","explicit Editor missing normal fallback");
        Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"DX11 validation 0/0");
        File.WriteAllText(Path.Combine(output,"material-surface.json"),JsonSerializer.Serialize(new{schema=1,cases,maxError,skinError,validationErrors=renderer.Stats.ValidationErrors,validationWarnings=renderer.Stats.ValidationWarnings,manualAcceptance=false}));
        Console.WriteLine($"PASS M7.2 material surface {cases} cases; independent oracle={maxError}, static/compute skin={skinError}, API=0/0");return 0;
    }
}
