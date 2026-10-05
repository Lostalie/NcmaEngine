using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    static void TestScenePipeline(RendererSession renderer, ref ulong frame, string output)
    {
        Check(Marshal.SizeOf<SceneGpuDraw>() == 256 && Marshal.SizeOf<ScenePipelineStats>() == 72, "Scene v4 POD");
        var graph = new Scene3DPipeline().Build(256, 256);
        Check(graph.Compile(RenderCapabilities.SceneDx11).RequiresSceneService, "Scene graph service");
        Reject(() => graph.Compile(RenderCapabilities.ResourceDx11), "capability");
        Reject(() => new Scene3DPipeline(shadowResolution: 300).Build(256,256).Compile(RenderCapabilities.SceneDx11));
        Reject(() => new Scene3DPipeline(exposure: float.NaN).Build(256,256).Compile(RenderCapabilities.SceneDx11));
        Reject(() => new Scene3DPipeline(ambient: 2).Build(256,256).Compile(RenderCapabilities.SceneDx11));
        using var cache = new RenderResourceCache(renderer);
        var data = Quad();
        using var mesh = cache.AcquireMesh(new(Guid.NewGuid(),1,new string('A',64)),data);
        var definition = MaterialDefinition.Default(Guid.NewGuid()) with { BaseColor = new(.6f,.25f,.1f,1), Metallic = .1f, Roughness = .6f };
        using var material = cache.AcquireMaterial(Version(definition), definition, _ => null, true, true, out _);
        using var target = renderer.CreateViewTarget(256,256);
        var camera = new Ncma.Scene.Rendering.SceneCameraView(Guid.Empty,Matrix4x4.CreateOrthographic(4,4,.1f,50),Vector3.Zero,Ncma.Scene.Rendering.CameraData.Default);
        Vector3 light = Vector3.Normalize(new Vector3(1,0,1));
        var lightVP = Ncma.Rendering.Scene.ShadowVolume.Create(camera,light);
        var groundModel = Matrix4x4.CreateScale(4,4,1)*Matrix4x4.CreateTranslation(0,0,-4.5f);
        var casterModel = Matrix4x4.CreateTranslation(3,0,-.5f);
        SceneGpuDraw[] geometry = [SceneGpuDraw.Create(mesh.Resource, material.Resource, data.Ranges[0], groundModel, camera.ViewProjection)];
        SceneGpuDraw[] casters = [SceneGpuDraw.Create(mesh.Resource, material.Resource, data.Ranges[0], casterModel, camera.ViewProjection)];
        var lighting = new ResourceLighting(Vector3.Zero,light,new(1,1,1,3));
        var shadow = new SceneShadowSettings(.0001f,.0001f,false,3);
        byte[] lit = new byte[256*256*4], shaded = new byte[lit.Length], changed = new byte[lit.Length];
        using var plain = new ScenePipelineSession(renderer, new Scene3DPipeline(shadows:false),256,256);
        plain.Submit(frame++,geometry,[],lighting,lightVP,shadow,target); renderer.CaptureTarget(target,lit); renderer.Present();
        Check(lit.Where((_,i)=>i%4!=3).Any(v=>v>50),"Actual scene HDR/tone produced no color");
        int pbrError=0;
        foreach(int y in new[]{64,128,192}) foreach(int x in new[]{64,128,192}) {
            var world=new Vector3((x+.5f)/256*4-2,2-(y+.5f)/256*4,-4);
            Vector3 expected=ScenePbrOracle(new(.6f,.25f,.1f),.1f,.6f,world,Vector3.UnitZ,Vector3.Zero,light,3,.03f,1);
            for(int c=0;c<3;c++) pbrError=Math.Max(pbrError,Math.Abs(lit[(y*256+x)*4+c]-(int)MathF.Round(expected[c]*255)));
        }
        Check(pbrError<=2,"Independent CPU GGX/HDR/ACES/sRGB scene oracle mismatch: "+pbrError);
        using var pipeline = new ScenePipelineSession(renderer,new Scene3DPipeline(),256,256);
        pipeline.Submit(frame++,geometry,casters,lighting,lightVP,shadow,target);renderer.CaptureTarget(target,shaded);
        Reject(()=>pipeline.Dispose()); Reject(()=>mesh.Resource.Dispose()); renderer.Present();
        int shadowPixels=0;for(int i=0;i<lit.Length;i+=4)if(lit[i]-shaded[i]>20)shadowPixels++;
        Check(shadowPixels>1000,"Off-camera geometry cast no visible directional shadow: "+shadowPixels);
        File.WriteAllBytes(Path.Combine(output,"scene-pbr.png"),Png(256,256,lit));
        File.WriteAllBytes(Path.Combine(output,"scene-shadow.png"),Png(256,256,shaded));
        pipeline.Submit(frame++,geometry,casters,lighting,lightVP,shadow with {Pcss=true,LightRadius=8},target);renderer.CaptureTarget(target,changed);renderer.Present();
        Check(!shaded.SequenceEqual(changed),"PCSS configuration did not affect shadow filtering");
        File.WriteAllBytes(Path.Combine(output,"scene-pcss.png"),Png(256,256,changed));
        SceneGpuDraw[] excluded=[SceneGpuDraw.Create(mesh.Resource,material.Resource,data.Ranges[0],groundModel,camera.ViewProjection,directLight:false)];
        plain.Submit(frame++,excluded,[],lighting,lightVP,shadow,target);renderer.CaptureTarget(target,changed);renderer.Present();
        var ambientOracle=ScenePbrOracle(new(.6f,.25f,.1f),.1f,.6f,new(0,0,-4),Vector3.UnitZ,Vector3.Zero,light,0,.03f,1);
        for(int c=0;c<3;c++)Check(Math.Abs(changed[(128*256+128)*4+c]-MathF.Round(ambientOracle[c]*255))<=2,"Object light-layer exclusion/ambient oracle mismatch");
        var texture=TextureData.Prepare(2,1,TextureSemantic.Color,new byte[]{255,255,255,255,255,255,255,0});Guid textureId=Guid.NewGuid();
        var masked=definition with{AssetId=Guid.NewGuid(),BaseTexture=textureId,Mode=MaterialMode.AlphaMask};
        using(var alpha=cache.AcquireMaterial(Version(masked),masked,_=>new ResolvedTexture(Version(textureId,texture),texture),true,true,out _)) {
            SceneGpuDraw[] maskGeo=[SceneGpuDraw.Create(mesh.Resource,alpha.Resource,data.Ranges[0],groundModel,camera.ViewProjection)];
            plain.Submit(frame++,maskGeo,[],lighting,lightVP,shadow,target);renderer.CaptureTarget(target,changed);renderer.Present();
            Check(changed[(128*256+64)*4]>50&&changed[(128*256+192)*4]==0,"Scene alpha geometry did not discard masked half");
            SceneGpuDraw[] maskCasters=[SceneGpuDraw.Create(mesh.Resource,alpha.Resource,data.Ranges[0],casterModel,camera.ViewProjection)];
            pipeline.Submit(frame++,geometry,maskCasters,lighting,lightVP,shadow,target);renderer.CaptureTarget(target,changed);renderer.Present();
            int count=0;for(int i=0;i<lit.Length;i+=4)if(lit[i]-changed[i]>20)count++;
            Check(count>1000&&count<shadowPixels*.75,"Scene shadow did not use identical alpha-mask geometry: "+count);
            File.WriteAllBytes(Path.Combine(output,"scene-alpha-shadow.png"),Png(256,256,changed));
        }
        using(var stage=new ScenePipelineSession(renderer,new Scene3DPipeline(geometry:new SceneGeometryStage(.15f),tone:new SceneToneStage(2)),256,256)) {
            stage.Submit(frame++,geometry,casters,lighting,lightVP,shadow,target);renderer.CaptureTarget(target,changed);renderer.Present();Check(!changed.SequenceEqual(shaded),"Public stage replacement failed");
        }
        using(var replacement=new ScenePipelineSession(renderer,new ReplacementScenePipeline(),256,256)) {
            replacement.Submit(frame++,geometry,[],lighting,lightVP,shadow,target);renderer.CaptureTarget(target,changed);renderer.Present();Check(!changed.SequenceEqual(lit),"Full public pipeline replacement failed");
        }
        using(var feature=new ScenePipelineSession(renderer,new Scene3DPipeline(features:[new SceneExposureFeature(2)]),256,256)) {
            feature.Submit(frame++,geometry,casters,lighting,lightVP,shadow,target);renderer.CaptureTarget(target,changed);renderer.Present();
            Check(!changed.SequenceEqual(shaded),"Public scene Feature did not execute");
        }
        ulong creates=renderer.ResourceStats.Creates,uploads=renderer.ResourceStats.UploadedBytes,pipelines=renderer.PipelineStats.Creates;
        for(int i=0;i<8;i++){pipeline.Submit(frame++,geometry,casters,lighting,lightVP,shadow,target);renderer.Present();}
        long allocated=GC.GetAllocatedBytesForCurrentThread(),started=System.Diagnostics.Stopwatch.GetTimestamp();
        for(int i=0;i<64;i++){pipeline.Submit(frame++,geometry,casters,lighting,lightVP,shadow,target);renderer.Present();}
        allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        Check(creates==renderer.ResourceStats.Creates&&uploads==renderer.ResourceStats.UploadedBytes&&pipelines==renderer.PipelineStats.Creates,"Stable scene rebuilt/uploaded resources");
        Check(allocated<=1024,"Cached scene ABI submit allocated: "+allocated);
        var before=renderer.PipelineStats;
        ulong rejectedFrame=frame;
        Reject(()=>pipeline.Submit(rejectedFrame,geometry,casters,lighting,lightVP,shadow with{Bias=float.NaN},target));
        Reject(()=>pipeline.Submit(rejectedFrame,Enumerable.Repeat(geometry[0],4097).ToArray(),casters,lighting,lightVP,shadow,target));
        Check(before.GeometryDraws==renderer.PipelineStats.GeometryDraws,"Invalid scene batch executed partially");
        renderer.Resize(300,256); pipeline.Submit(frame++,geometry,casters,lighting,lightVP,shadow,target);renderer.CaptureTarget(target,changed);renderer.Present();
        Check(changed.SequenceEqual(shaded),"Resize invalidated scene resources");renderer.Resize(256,256);
        File.WriteAllText(Path.Combine(output,"scene-pipeline-v4.json"),JsonSerializer.Serialize(new{version=4,shadowPixels,pbrError,allocatedBytes=allocated,
            elapsedMs=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,referenceResources=false,validationErrors=renderer.Stats.ValidationErrors,validationWarnings=renderer.Stats.ValidationWarnings}));
        Console.WriteLine($"PASS Scene v4 actual HDR/PBR/off-camera shadow/PCSS/Feature/resize; shadow_pixels={shadowPixels}; cached_bytes={allocated}");
    }
    private sealed class ReplacementScenePipeline:RenderPipeline {
        public override RenderGraph Build(uint width,uint height)=>new Scene3DPipeline(exposure:2,shadows:false).Build(width,height);
    }
    // Independent scalar/vector reference, not a shader readback/shared shader oracle.
    private static Vector3 ScenePbrOracle(Vector3 albedo,float metal,float rough,Vector3 p,Vector3 n,Vector3 camera,Vector3 light,float intensity,float ambient,float exposure) {
        n=Vector3.Normalize(n);var v=Vector3.Normalize(camera-p);var l=Vector3.Normalize(light);var h=Vector3.Normalize(v+l);
        float nl=Math.Max(0,Vector3.Dot(n,l)),nv=Math.Max(.0001f,Math.Max(0,Vector3.Dot(n,v))),nh=Math.Max(0,Vector3.Dot(n,h));
        float a=rough*rough,a2=a*a,d=nh*nh*(a2-1)+1,distribution=a2/Math.Max(MathF.PI*d*d,.000001f),k=(rough+1)*(rough+1)/8;
        float g=nv/(nv*(1-k)+k)*nl/Math.Max(nl*(1-k)+k,.0001f);
        var f0=Vector3.Lerp(new(.04f),albedo,metal);var f=f0+(Vector3.One-f0)*MathF.Pow(1-Math.Max(0,Vector3.Dot(h,v)),5);
        var color=((Vector3.One-f)*(1-metal)*albedo/MathF.PI+distribution*g*f/Math.Max(4*nv*nl,.0001f))*intensity*nl+albedo*ambient;
        for(int c=0;c<3;c++) {float x=color[c]*exposure;x=Math.Clamp(x*(2.51f*x+.03f)/(x*(2.43f*x+.59f)+.14f),0,1);color[c]=x<=.0031308f?12.92f*x:1.055f*MathF.Pow(x,1/2.4f)-.055f;}
        return color;
    }
}
