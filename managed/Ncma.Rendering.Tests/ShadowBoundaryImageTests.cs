using System.Numerics;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int ShadowBoundaryImageTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);int checks=0,maximumError=0;ulong frame=1;var rows=new List<object>();
        void Pass(bool ok,string why){Check(ok,"Shadow boundary oracle: "+why);checks++;}
        var repo=new DirectoryInfo(Path.GetFullPath(plugins));while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");
        string config=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;using var index=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repo.FullName,"out/verification/m2-7",config,"packages.json")));
        string product=index.RootElement.GetProperty("editor").GetString()!;Ncma.Application.DeploymentManifest.Validate(product);
        using var files=RuntimeShaderFileSet.ForHost(product,null,null)!;using var highFiles=RuntimeShadowLibrary.LoadDeployment(product);
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Independent shadow boundary reference",128,128,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,128,128);files.InstallSelection(renderer,()=>true);
        var high=highFiles.Prepare(renderer,files,()=>true).Resolve(files.Get(ShaderProfile.Scene3D,true,false));
        var standard=RuntimeShaderPreparation.Prepare(renderer,files.Get(ShaderProfile.Scene3D,true,false),()=>true);
        var off=RuntimeShaderPreparation.Prepare(renderer,files.Get(ShaderProfile.Scene3D,false,false),()=>true);
        using var cache=new RenderResourceCache(renderer);using var mesh=renderer.CreateStaticMesh(Quad());using var target=renderer.CreateViewTarget(128,128);
        var baseMaterial=MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(.6f,.25f,.1f,1),Metallic=.1f,Roughness=.6f};
        using var groundMaterial=cache.AcquireMaterial(Version(baseMaterial),baseMaterial,_=>null,true,true,out _);
        // Model scales XY by four; choose a local normal whose inverse-transpose
        // transformation gives the intended world normal. Oracle uses that world normal.
        var localNormal=Vector3.Normalize(new Vector3(3.2f,0,.6f));var normal=Vector3.Normalize(new Vector3(localNormal.X/4,0,localNormal.Z));
        ImportVertex V(float x,float y,float u,float v)=>new(new(x,y,.5f),localNormal,new(u,v),default,Vector4.Zero);
        var slopeData=MeshUploadData.PrepareStatic(new(false,0,[V(-.8f,.8f,0,0),V(.8f,.8f,1,0),V(-.8f,-.8f,0,1),V(.8f,-.8f,1,1)],Enumerable.Repeat(new Vector4(Vector3.Normalize(new Vector3(.6f,0,-3.2f)),-1),4).ToArray(),[0,1,2,2,1,3],[0,0],[],1));
        using var slopeMesh=renderer.CreateStaticMesh(slopeData);
        var vp=Matrix4x4.CreateOrthographic(4,4,.1f,10);var light=new ResourceLighting(Vector3.Zero,Vector3.UnitZ,new(1,1,1,3));
        var cases=new[]{("opaque",3f,80f,0f,0,false), ("alpha-half",3f,80f,0f,1,false), ("alpha-empty",3f,80f,0f,2,false),
            ("contact",.01f,80f,0f,0,false), ("self",0f,80f,0f,0,false), ("slope-bias",.05f,80f,0f,0,true),
            ("border",3f,4f,1.9f,0,false), ("outside-volume",3f,2f,0f,0,false), ("near-clipped-caster",6f,80f,0f,0,false), ("subpixel",3f,80f,.137f,0,false)};
        byte[] image=new byte[128*128*4],reference=new byte[image.Length];
        foreach(var quality in Enum.GetValues<SceneShadowQuality>()){
            var preset=SceneShadowPresets.Get(quality);using var pipeline=new ScenePipelineSession(renderer,preset.CreatePipeline(true),128,128,shaders:quality==SceneShadowQuality.Off?off.Scene:quality==SceneShadowQuality.High?high.Scene:standard.Scene);
            foreach(var(name,separation,extent,offset,mask,sloped)in cases){
                var texture=TextureData.Prepare(2,1,TextureSemantic.Color,new byte[]{255,255,255,(byte)(mask==2?0:255),255,255,255,(byte)(mask==0?255:0)});Guid textureId=Guid.NewGuid();
                var casterMaterial=baseMaterial with{AssetId=Guid.NewGuid(),BaseTexture=textureId,Mode=MaterialMode.AlphaMask,AlphaCutoff=.5f};
                using var casterLease=cache.AcquireMaterial(Version(casterMaterial),casterMaterial,_=>new ResolvedTexture(Version(textureId,texture),texture),true,true,out _);
                int resolution=(int)(preset.Enabled?preset.Budget.Resolution:2048);var lightVp=Matrix4x4.CreateOrthographic(extent,extent,.1f,10);lightVp.M41=lightVp.M42=.5f/resolution;
                var settings=preset.Settings with{SlopeBias=sloped?.02f:preset.Settings.SlopeBias};
                SceneGpuDraw[] geometry=[SceneGpuDraw.Create(sloped?slopeMesh:mesh,groundMaterial.Resource,Quad().Ranges[0],Matrix4x4.CreateScale(4,4,1)*Matrix4x4.CreateTranslation(0,0,-4.5f),vp)];
                SceneGpuDraw[] casters=preset.Enabled?[SceneGpuDraw.Create(mesh,casterLease.Resource,Quad().Ranges[0],Matrix4x4.CreateTranslation(offset,0,-4.5f+separation),vp)]:[];
                pipeline.Submit(frame++,geometry,casters,light,lightVp,settings,target);renderer.CaptureTarget(target,image);renderer.Present();
                int error=0,worst=0,occluded=0;var n=sloped?normal:Vector3.UnitZ;
                for(int y=0;y<128;y++)for(int x=0;x<128;x++){
                    var p=new Vector3((x+.5f)/32-2,2-(y+.5f)/32,-4);
                    double visibility=preset.Enabled?ShadowBoundaryVisibility(p,lightVp,resolution,extent,offset,separation,mask,settings,n.Z):1;
                    if(visibility<.99)occluded++;
                    var color=ScenePbrOracle(new(.6f,.25f,.1f),.1f,.6f,p,n,Vector3.Zero,Vector3.UnitZ,(float)(3*visibility),.03f,1);int at=(y*128+x)*4;
                    for(int c=0;c<4;c++){reference[at+c]=c==3?(byte)255:(byte)Math.Clamp(MathF.Round(color[c]*255),0,255);int delta=Math.Abs(reference[at+c]-image[at+c]);if(delta>error){error=delta;worst=at+c;}}
                }
                string label=quality+"-"+name;File.WriteAllBytes(Path.Combine(output,label+".png"),Png(128,128,image));File.WriteAllBytes(Path.Combine(output,label+"-oracle.png"),Png(128,128,reference));
                File.WriteAllText(Path.Combine(output,label+".json"),JsonSerializer.Serialize(new{error,worstX=worst/4%128,worstY=worst/4/128,actual=image[worst],expected=reference[worst],occluded}));maximumError=Math.Max(maximumError,error);
                Pass(error<=2,label+" whole image error="+error+" at "+worst/4%128+","+worst/4/128);
                if(name is "alpha-empty" or "self" or "slope-bias" or "near-clipped-caster"||!preset.Enabled)Pass(occluded==0,label+" no false shadow/leak");
                else Pass(occluded>0,label+" actual shadow");rows.Add(new{label,error,occluded});
            }
        }
        renderer.WaitIdle();Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"API0/0");
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{checks,maximumError,rows,independentDepthAndFilter=true,sourceFree=true,animated=false,fullPerformanceAccepted=false}));Console.WriteLine($"PASS M7.5 boundary {checks};40 independent whole images max={maximumError};API0/0;{output}");return 0;
    }

    private static double ShadowBoundaryVisibility(Vector3 point,Matrix4x4 projection,int resolution,double extent,double offset,double separation,int mask,SceneShadowSettings settings,double nl)
    {
        var q=Vector4.Transform(new Vector4(point,1),projection);double u=q.X*.5+.5,v=.5-q.Y*.5;
        if(u<0||u>1||v<0||v>1||q.Z<0||q.Z>1)return 1;
        double z=q.Z-settings.Bias-settings.SlopeBias*(1-nl);var projectedCaster=Vector4.Transform(new Vector4(0,0,(float)(-4+separation),1),projection);
        double Depth(int x,int y){
            if(x<0||y<0||x>=resolution||y>=resolution||mask==2||projectedCaster.Z<0||projectedCaster.Z>1)return 1;
            double wx=((x+.5)/resolution-.5-projection.M41*.5)*extent,wy=(.5-(y+.5)/resolution-projection.M42*.5)*extent;
            // Linear alpha at a two-texel white/transparent boundary crosses .5 at local x=0.
            return Math.Abs(wx-offset)<.8&&Math.Abs(wy)<.8&&(mask!=1||wx<=offset)?projectedCaster.Z:1;
        }
        double Near(double s,double t)=>Depth((int)Math.Floor(s*resolution),(int)Math.Floor(t*resolution));
        if(!settings.Pcss){double visible=0;for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)if(z<=Near(u+x/(double)resolution,v+y/(double)resolution))visible++;return visible/9;}
        double blockers=0;int count=0;for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){double d=Near(u+x*settings.LightRadius/resolution,v+y*settings.LightRadius/resolution);if(d<z){count++;blockers+=d;}}
        if(count==0)return 1;double radius=Math.Clamp((z-blockers/count)*settings.LightRadius*64,1,8),sum=0;
        for(int y=-3;y<=3;y++)for(int x=-3;x<=3;x++){
            double sx=(u+x*radius/3/resolution)*resolution-.5,sy=(v+y*radius/3/resolution)*resolution-.5;int ix=(int)Math.Floor(sx),iy=(int)Math.Floor(sy);double fx=sx-ix,fy=sy-iy;
            for(int j=0;j<2;j++)for(int i=0;i<2;i++)if(z<=Depth(ix+i,iy+j))sum+=(i==0?1-fx:fx)*(j==0?1-fy:fy);
        }
        return Math.Clamp(sum/49,0,1);
    }
}
