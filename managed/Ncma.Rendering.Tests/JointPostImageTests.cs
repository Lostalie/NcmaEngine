using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Rendering.Scene.Examples;
using Ncma.Ui;

internal static unsafe partial class Program
{
    private delegate void JointImageObserver(SceneGpuDraw[] geometry,SceneGpuDraw[] casters,ResourceLighting lighting,
        Matrix4x4 lightVp,SceneShadowSettings shadow,GpuSkinRequest[] requests,GpuSkinPalette[] palettes,string label,ref ulong frame);

    private static int JointPostImageTests(string plugins,string output,SceneShadowQuality? costQuality=null)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        int cases=0,maxError=0,jointImages=0;ulong frame=1;var evidence=new List<object>();var observed=new HashSet<string>(StringComparer.Ordinal);
        void Pass(bool ok,string label){Check(ok,"D2 "+label);cases++;}
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());
        var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Joint HDR Bloom UI images",256,256,false);
        using var renderer=new RendererSession(native,window,256,256);
        using var cache=new RenderResourceCache(renderer);
        var skinCatalog=DefaultSkinShader.CopyCatalog(renderer);
        var skin=RegisteredSkinShader.Prepare(renderer,skinCatalog,DefaultSkinShader.Select(skinCatalog),()=>true);
        var preset=costQuality is {} quality?SceneShadowPresets.Get(quality):null;
        bool shadows=preset?.Enabled??true;
        var sc=DefaultEnvironmentSceneShaders.CopyCatalog(renderer,shadows);var selected=DefaultEnvironmentSceneShaders.Select(sc,shadows);
        if(preset?.RequiresHighShader==true){var high=SceneShadowShaders.ComposeHigh(sc,selected);sc=high.Catalog;selected=high.Selection;}
        var official=RegisteredEnvironmentSceneShaders.Prepare(renderer,sc,selected,()=>true);
        // A test-only transport of the actual half-float HDR buffer, NOT a shading oracle.
        // Each channel is encoded into two exact UNORM bytes before display conversion.
        var readers=new RegisteredEnvironmentSceneShaders[3];
        for(int c=0;c<3;c++){
            var tone=SceneTonePresets.Create(SceneToneCurve.LinearClamp,ShaderProfile.SceneEnvironment);var pixel=tone.Pixel.CopyDefinition();
            int begin=pixel.Source.IndexOf("float4 PSTone(",StringComparison.Ordinal);Check(begin>=0,"test reader entry");
            string source=pixel.Source[..begin]+$"float4 PSTone(Full i):SV_TARGET{{float3 v=BaseTex.SampleLevel(S,i.uv,0).rgb*Settings.x;uint h=f32tof16(v[{c}]);return float4((h&255)/255.0,(h>>8)/255.0,0,1);}}";
            pixel=pixel with{AssetId=Guid.NewGuid(),Source=source,SourceHash=ShaderContractCodec.HashSource(source)};
            var descriptor=ShaderDescriptor.Prepare(pixel);var catalog=ShaderCatalog.Create(ShaderProfile.SceneEnvironment,[tone.Vertex.CopyDefinition(),pixel]);
            var composition=SceneToneComposition.Compose(sc,selected,new(catalog,tone.Vertex,descriptor));
            readers[c]=RegisteredEnvironmentSceneShaders.Prepare(renderer,composition.Catalog,composition.Selection,()=>true);
        }
        var definitions=BloomFeature.CreatePrograms();var package=PostProcessPackage.Cook(renderer,definitions.Catalog,definitions.CopyPrograms(),()=>true);
        var programs=PreparedPostProcessPrograms.Prepare(renderer,PostProcessPackage.Preflight(package.CopyBytes(),package.ContentHash),()=>true);
        using var cook=new EnvironmentCookService(native,()=>true);
        const int ew=32,eh=16;float[] values=new float[ew*eh*4];
        for(int y=0;y<eh;y++)for(int x=0;x<ew;x++){
            float dz=MathF.Sin(2*MathF.PI*((x+.5f)/ew-.5f))*MathF.Sin(MathF.PI*(y+.5f)/eh);int i=(y*ew+x)*4;
            values[i]=1.1f+.9f*dz;values[i+1]=.8f+.6f*dz;values[i+2]=.5f+.3f*dz;values[i+3]=1;
        }
        var env=cook.Cook(Guid.NewGuid(),1,HdrEnvironmentSource.Prepare(Guid.NewGuid(),ew,eh,values),new(16,8,32,1024));
        using var environment=renderer.CreateEnvironment(env,()=>true);
        var config=new EnvironmentLightingConfiguration(env.AssetId,1,env.ContentHash,.5f,.5f,true);
        using var white=renderer.CreateUiImage(1,1,[255,255,255,255]);
        // Verify that the measurement transport itself preserves exact Half bits, including HDR.
        using(var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(ambient:0,shadowResolution:512,shadows:shadows),32,32,environmentShaders:official))
        using(var target=renderer.CreateViewTarget(32,32)){
            byte[] bytes=new byte[32*32*4];using var quad=renderer.CreateStaticMesh(Quad());
            foreach(float value in new[]{0,.001f,.25f,4,16})for(int c=0;c<3;c++){
                // Supply exactly representable Half input: ClearRenderTargetView's float-to-Half
                // conversion is not the measurement transport being tested here.
                float exact=(float)(Half)value;
                var definition=MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(0,0,0,1),Emissive=new(exact,exact,exact,1)};
                using var material=renderer.CreateMaterial(definition,new GpuTexture?[6],false);
                SceneGpuDraw[] batch=[SceneGpuDraw.Create(quad,material,Quad().Ranges[0],Matrix4x4.CreateScale(4,4,1)*Matrix4x4.CreateTranslation(0,0,-4.5f),Matrix4x4.CreateOrthographic(4,4,.1f,50),directLight:false)];
                scene.ReplaceEnvironmentShaders(readers[c]);scene.Submit(frame++,batch,[],new(Vector3.Zero,Vector3.UnitZ,Vector4.UnitW),Matrix4x4.Identity,new(),target);
                renderer.CaptureTarget(target,bytes);renderer.Present();ushort expected=BitConverter.HalfToUInt16Bits((Half)value);
                Pass(Enumerable.Range(0,1024).All(i=>(bytes[i*4]|bytes[i*4+1]<<8)==expected),$"exact HDR measurement transport {value} expected={expected} actual={bytes[0]|bytes[1]<<8}");
            }
        }
        void Observe(SceneGpuDraw[] geometry,SceneGpuDraw[] casters,ResourceLighting lighting,Matrix4x4 lightVp,
            SceneShadowSettings shadow,GpuSkinRequest[] requests,GpuSkinPalette[] palettes,string label,ref ulong next)
        {
            // Real imported geometry/palettes are borrowed only for this bounded call.
            observed.Add(label);
            if(preset is not null){shadow=preset.Settings;if(!shadows)casters=[];}
            foreach((int w,int h) in new[]{(256,256),(255,193)}){
                renderer.Resize((uint)w,(uint)h);byte[] bytes=new byte[w*h*4];var hdr=new Vector3[w*h];
                using var target=renderer.CreateViewTarget((uint)w,(uint)h);
                var settings=new BloomSettings(Levels:3,Threshold:.04f,SoftKnee:.5f,Intensity:.8f,Scatter:.65f);
                var plan=BloomFeature.Build((uint)w,(uint)h,settings,package)!;
                var builder=new UiDisplayListBuilder(renderer);
                // Scissor strictly inside each quad isolates display color/alpha/order from
                // GPU derivative-dependent analytic edge AA (covered by RegisteredUiTests).
                builder.Quad(new(Guid.NewGuid(),new(0,0,80,80),Matrix3x2.Identity,new(8,8,64,64),1,true,-1),white,new(.2f,.4f,.8f,1));
                builder.Quad(new(Guid.NewGuid(),new(0,0,128,96),Matrix3x2.CreateTranslation(40,16),new(60,32,80,64),1,true,-1),white,new(.8f,.3f,.1f,.5f));
                using var ui=builder.Build();byte[]? defaultPixels=null;ulong defaultGeneration=0;
                foreach(int mode in costQuality is null?new[]{0,2}:new[]{0,1,2}){
                    bool custom=mode!=0;
                    RenderPipeline Pipeline(float exposure)=>preset is null?
                        (custom?new ExampleScenePipeline(exposure,0,true):new Scene3DPipeline(exposure,0,512)):
                        CostJointPipeline(preset,mode,exposure);
                    using var scene=new ScenePipelineSession(renderer,Pipeline(1),(uint)w,(uint)h,environmentShaders:official);
                    if(preset is not null){var maps=scene.Plan.Resources.Where(r=>r.Role==RenderRole.SceneShadow).ToArray();
                        Pass(shadows?maps.Length==1&&maps[0].Width==preset.Budget.Resolution&&maps[0].Height==preset.Budget.Resolution:maps.Length==0,"exact selected shadow resources "+costQuality);
                        if(preset.RequiresHighShader)Pass(scene.Plan.RequiredSceneGeometryPixelHash==selected.GeometryPixel.ContentHash,"High exact registered geometry pixel required");}
                    scene.ConfigureEnvironment(environment,config,()=>true);
                    bool Draw(ulong f,bool overlay=false,bool noCasters=false,bool capture=true){
                        // Diagnostic readback explicitly drains outside simulation; warm submit
                        // honours the existing nonblocking palette backpressure instead.
                        if(capture&&requests.Length!=0)renderer.WaitIdle();
                        if(requests.Length!=0&&!renderer.TryUpdateSkins(f,requests,palettes)){
                            if(capture)throw new InvalidOperationException("Diagnostic drain did not free a palette slot; refusing stale image.");
                            return false;
                        }
                        scene.Submit(f,geometry,noCasters?[]:casters,lighting,lightVp,shadow,overlay?null:target);
                        if(overlay)renderer.SubmitUi(ui,f,Vector4.Zero,overlay:true);
                        if(capture){if(overlay)renderer.Capture(bytes);else renderer.CaptureTarget(target,bytes);}renderer.Present();return true;
                    }
                    for(int c=0;c<3;c++){
                        scene.ReplaceEnvironmentShaders(readers[c]);Draw(next++);
                        for(int i=0;i<hdr.Length;i++)hdr[i][c]=(float)BitConverter.UInt16BitsToHalf((ushort)(bytes[i*4]|bytes[i*4+1]<<8));
                    }
                    Pass(hdr.All(v=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z))&&hdr.Any(v=>v.LengthSquared()>.001f),"finite nonempty measured HDR");
                    scene.ReplaceEnvironmentShaders(official);scene.ConfigurePostProcess(plan,programs,()=>true);
                    var filtered=new BloomOracle(w,h,hdr).Bloom(settings);
                    foreach(float exposure in new[]{.25f,1f,4f}){
                        scene.Configure(Pipeline(exposure));Draw(next++,overlay:true);int error=0,maxAt=0;byte[] expectedImage=new byte[bytes.Length];
                        for(int y=0;y<h;y++)for(int x=0;x<w;x++){
                            int i=y*w+x;var color=filtered.Pixels[i];
                            Vector3 rgb=new(ToneOracle(color.X,exposure,SceneToneCurve.AcesFitted)/255f,ToneOracle(color.Y,exposure,SceneToneCurve.AcesFitted)/255f,ToneOracle(color.Z,exposure,SceneToneCurve.AcesFitted)/255f);
                            if(x>=8&&x<72&&y>=8&&y<72)rgb=new(.2f,.4f,.8f);
                            if(x>=60&&x<140&&y>=32&&y<96)rgb=new Vector3(.8f,.3f,.1f)*.5f+rgb*.5f;
                            for(int c=0;c<3;c++){expectedImage[i*4+c]=(byte)MathF.Round(rgb[c]*255);int difference=Math.Abs(bytes[i*4+c]-expectedImage[i*4+c]);if(difference>error){error=difference;maxAt=i*4+c;}}
                            expectedImage[i*4+3]=255;
                            error=Math.Max(error,Math.Abs(bytes[i*4+3]-255));
                        }
                        if(error>3){File.WriteAllBytes(Path.Combine(output,"failed-actual.png"),Png((uint)w,(uint)h,bytes));File.WriteAllBytes(Path.Combine(output,"failed-expected.png"),Png((uint)w,(uint)h,expectedImage));}
                        maxError=Math.Max(maxError,error);Pass(error<=3,$"independent HDR-input Bloom/Tone/UI whole image {label} {w}x{h} custom={custom} EVscale={exposure} error={error} at={maxAt/4%w},{maxAt/4/w} c={maxAt%4} actual={bytes[maxAt]} expected={expectedImage[maxAt]} hdr={hdr[maxAt/4]}");
                        jointImages++;evidence.Add(new{label,w,h,custom,mode,costQuality,exposure,error});
                    }
                    scene.Configure(Pipeline(1));Draw(next++);var baseline=bytes.ToArray();
                    if(!custom){defaultPixels=baseline;defaultGeneration=scene.Key.Value;}else Pass(defaultPixels is not null&&bytes.SequenceEqual(defaultPixels)&&scene.Key.Value!=defaultGeneration,"fresh custom resource identity and default/full pipeline whole-image parity");
                    scene.ConfigurePostProcess(null,null,()=>true);Draw(next++);Pass(ChangedPixels(baseline,bytes)>100,"Bloom actually contributes");scene.ConfigurePostProcess(plan,programs,()=>true);
                    scene.ConfigureEnvironment(null,EnvironmentLightingConfiguration.Off,()=>true);Draw(next++);Pass(ChangedPixels(baseline,bytes)>100,"IBL actually contributes after Bloom");scene.ConfigureEnvironment(environment,config,()=>true);
                    if(casters.Length>0){Draw(next++,noCasters:true);Pass(ChangedPixels(baseline,bytes)>100,"skin shadow actually contributes after Bloom");}
                    ulong pubs=renderer.PostStats.Publications;Reject(()=>scene.ConfigurePostProcess(plan,programs,()=>false));cases++;
                    Draw(next++);Pass(bytes.SequenceEqual(baseline)&&renderer.PostStats.Publications==pubs,"rejected post candidate preserves joint pixels/publication");
                    for(int i=0;i<8;i++)Draw(next++,capture:false);
                    var resources=renderer.ResourceStats;var posts=renderer.PostStats;var envStats=renderer.EnvironmentStats;
                    renderer.WaitIdle();int submitted=0;
                    long allocated=GC.GetAllocatedBytesForCurrentThread(),started=Stopwatch.GetTimestamp();
                    for(int i=0;i<16;i++)if(Draw(next++,capture:false))submitted++;
                    double ms=Stopwatch.GetElapsedTime(started).TotalMilliseconds;allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
                    Pass(submitted>0&&allocated==0&&renderer.ResourceStats.UploadedBytes==resources.UploadedBytes&&renderer.PostStats.Publications==posts.Publications&&renderer.EnvironmentStats.Live==envStats.Live,"warm joint skin/scene/post no managed allocation/immutable upload/publication");
                    evidence.Add(new{label,w,h,custom,allocated,elapsedMs=ms,attempts=16,submitted,busy=16-submitted});
                    if(preset is not null)evidence.Add(MeasureSceneJointCosts(renderer,f=>Draw(f,overlay:true,capture:false),
                        label,w,h,mode,preset,geometry.Length,casters.Length,requests.Length,palettes.Length,ref next));
                    Draw(next++,overlay:true);string imageMode=costQuality is null?custom.ToString():mode.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    File.WriteAllBytes(Path.Combine(output,$"joint-{label}-{w}-{imageMode}.png"),Png((uint)w,(uint)h,bytes));
                }
            }
            renderer.Resize(256,256);
        }
        int inheritedCases=EnvironmentJointImages(plugins,output,renderer,cache,skin,environment,config,ref frame,Observe);cases+=inheritedCases;
        Pass(jointImages==observed.Count*(costQuality is null?12:18)&&observed.Contains("static-fbx")&&observed.Any(s=>s.StartsWith("blender_279_sausage_6100_ascii",StringComparison.Ordinal))&&observed.Any(s=>s.StartsWith("blender_279_sausage_7400_binary",StringComparison.Ordinal)),"static plus both actual FBX encodings/clips joint image coverage");
        Pass(renderer.PostStats.Bindings==0&&renderer.PostStats.ResidentBytes==0&&renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0,"joint scenes and skin/post resources released");
        Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"DX11 API0/0");
        File.WriteAllText(Path.Combine(output,"joint-post-results.json"),JsonSerializer.Serialize(new{cases,jointImages,maxError,costQuality,evidence,apiErrors=0,apiWarnings=0,independentPostOracle=true,independentWholeShadingOracle=false,relocatedProductsAccepted=false,manualAccepted=false,fullPerformanceAccepted=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS {(costQuality is null?"M7.4-D2":"M7.7 joint costs "+costQuality)} {cases} checks / {jointImages} joint images; maxError={maxError}; API0/0; {output}");return 0;
    }
    private static int ChangedPixels(byte[] a,byte[] b){int count=0;for(int i=0;i<a.Length;i+=4)if(Math.Abs(a[i]-b[i])+Math.Abs(a[i+1]-b[i+1])+Math.Abs(a[i+2]-b[i+2])>3)count++;return count;}
}
