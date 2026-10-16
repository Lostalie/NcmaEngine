using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static int PostModuleTests(string plugins, string output)
    {
        output = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        int cases = 0;
        void Pass(bool ok, string label) { Check(ok, "M7.4-C2 modules " + label); cases++; }
        void Bad(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException or PluginException) { cases++; return; } throw new Exception("C2 expected rejection."); }
        foreach(var reference in new[]{new PostProcessLibraryReference(0,new string('a',64)),new(1,""),new(1,new string('x',64))})Bad(()=>reference.Validate());
        Pass(new PostProcessLibraryReference(1,new string('a',64)).Validate().IndexHash==new string('A',64),"project reference canonical external hash");
        foreach(string json in new[]{"{}","{\"version\":1}","{\"version\":1,\"indexHash\":\""+new string('a',64)+"\",\"path\":\"ignored\"}"})
            Bad(()=>JsonSerializer.Deserialize<PostProcessLibraryReference>(json,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        var defaults = PostProcessModules.Official(); var bloom = PostProcessModules.Bloom(new(Levels: 2));
        var settings = PostProcessData.Off with { Enabled = true, StackJson = PostProcessData.EncodeStack([bloom]) };
        var normalized = defaults.Validate(settings);
        Pass(normalized == defaults.Validate(normalized), "canonical idempotence");
        string saved = normalized.StackJson; bloom.Parameters[0] = new("bad", 99);
        var copy = PostProcessData.DecodeStack(saved); copy[0].Parameters[0] = new("changed", 1);
        Pass(defaults.Validate(normalized).StackJson == saved, "caller arrays cannot mutate settings");
        Pass(PostProcessData.EncodeStack([]) == "[]", "canonical empty stack");
        var finite = PostProcessModules.Bloom(new());
        foreach (var invalid in new[] { settings with { Version = 0 }, settings with { ToneModule = Guid.Empty }, settings with { ToneVersion = 0 },
            settings with { ExposureEv = float.NaN }, settings with { ExposureEv = -6.01f }, settings with { ExposureEv = 4.01f },
            settings with { StackJson = new string(' ', PostProcessData.MaxStackBytes + 1) } }) Bad(() => PostProcessData.Validate(invalid));
        foreach (var text in new[] { "null", "{}", "[{}]", "[{\"moduleId\":\"" + finite.ModuleId + "\",\"version\":1,\"version\":1,\"parameters\":[]}]",
            PostProcessData.EncodeStack([finite]).Replace("\"version\":1", "\"version\":1,\"source\":\"not permitted\"", StringComparison.Ordinal) }) Bad(() => PostProcessData.DecodeStack(text));
        foreach (var value in new[] { finite with { ModuleId = Guid.Empty }, finite with { Version = 0 }, finite with { Parameters = [new("value", float.PositiveInfinity)] },
            finite with { Parameters = [new("0name", 1)] }, finite with { Parameters = [new("v", 1), new("v", 2)] }, finite with { Parameters = Enumerable.Range(0,17).Select(i=>new PostParameterInput("v"+i,1)).ToArray() } }) Bad(() => PostProcessData.EncodeStack([value]));
        Bad(() => PostProcessData.EncodeStack(Enumerable.Repeat(finite, 9))); Bad(() => PostProcessData.EncodeStack([finite, finite]));
        Bad(() => defaults.Validate(settings with { ToneModule = Guid.NewGuid() })); Bad(() => defaults.Validate(settings with { ToneVersion = 2 }));
        Bad(() => defaults.Validate(settings with { StackJson = PostProcessData.EncodeStack([finite with { Version = 2 }]) }));
        foreach (var parameters in new[] { new[] { new PostParameterInput("levels", 2) }, finite.Parameters.Select(p => p.Name == "levels" ? p with { Value = 1.5f } : p).ToArray(),
            finite.Parameters.Select(p => p.Name == "intensity" ? p with { Value = 5 } : p).ToArray(), finite.Parameters.Select(p => p.Name == "scatter" ? p with { Name = "unknown" } : p).ToArray() })
            Bad(() => defaults.Validate(settings with { StackJson = PostProcessData.EncodeStack([finite with { Parameters = parameters }]) }));
        Pass(defaults.Build(settings with { Enabled = false }, 64, 64, _ => throw new Exception("Off resolved resources")) is null, "Off skips callbacks/program resolution");
        Bad(() => defaults.Build(settings, 0, 64, _ => throw new Exception("bad size resolved resources")));
        var registry = ComponentRegistry.CreateDefault(); PostProcessData.Register(registry);
        var document = new SceneDocument("Post policy", registry, scene => _ = ScenePostProcessState.Read(scene));
        global::Ncma.Runtime.Entity obj = document.World.CreateEntity("Settings"); document.World.Set(obj, normalized);
        byte[] bytes = document.CaptureBytes(); var cloneRegistry = ComponentRegistry.CreateDefault(); PostProcessData.Register(cloneRegistry);
        var clone = new SceneDocument("Copy", cloneRegistry, scene => _ = ScenePostProcessState.Read(scene)); clone.RestoreBytes(bytes);
        Pass(ScenePostProcessState.Read(clone.CaptureSnapshot()) == normalized && ScenePostProcessState.Read(clone.World, clone.World.Identity) == normalized, "scene roundtrip and flat value");
        Bad(() => ScenePostProcessState.Read(clone.World, Guid.NewGuid()));
        using (SceneEnvironmentState.BeginRead(clone.World, clone.World.Identity)) Bad(() => clone.World.CreateEntity("forbidden"));
        clone.World.Set(clone.World.CreateEntity("Duplicate Off"), PostProcessData.Off); Bad(() => ScenePostProcessState.Read(clone.World, clone.World.Identity)); Bad(() => clone.ValidateAuthoring());
        Exception? owner = null; var thread = new Thread(() => { try { defaults.Validate(settings); } catch(Exception e) { owner=e; } }); thread.Start(); thread.Join(); Pass(owner is InvalidOperationException, "module owner thread");

        var repo = new DirectoryInfo(Path.GetFullPath(plugins)); while (!File.Exists(Path.Combine(repo.FullName,"Build.bat"))) repo=repo.Parent??throw new Exception("Repo missing");
        using var library = RuntimePostProcessLibrary.LoadDeployment(Path.Combine(repo.FullName,"out/bin"));
        var package = library.Program(PostProcessLibrarySelection.BloomModule);
        Pass(!package.GpuValidated && !ProcessHasGraphics(), "CPU-only policy/library path before plugin load");
        var official = defaults.Build(settings, 64, 64, _ => package)!;
        Pass(official.Plan.PassCount > 0 && official.Plan.ProgramHash == official.Programs.ContentHash && !official.Programs.GpuValidated, "official public composition");
        var merged = PostProcessComposition.Programs([package,package]);
        Pass(merged.CopyPage().Length == 5 && merged.CopyBytes().SequenceEqual(PostProcessComposition.Programs([package]).CopyBytes()), "duplicate identical programs coalesced deterministically");
        Bad(() => PostProcessComposition.Programs([])); Bad(() => PostProcessComposition.Programs(Enumerable.Repeat(package,9)));
        byte[] altered=package.CopyBytes(); altered[52]^=1; // first vertex author hash, still a valid CPU package but conflicting immutable identity
        var collision=PostProcessPackage.Preflight(altered,Convert.ToHexString(SHA256.HashData(altered))); Bad(()=>PostProcessComposition.Programs([package,collision]));
        var p = BloomFeature.Build(64,64,new(Levels:2),package)!;
        var chained=PostProcessComposition.Chain(64,64,[(p,package),(p,package)],merged);
        Pass(chained.PassCount==2*p.PassCount && chained.CopyPasses()[p.PassCount].Input.Value!=0, "second Feature consumes previous HDR, not scene HDR again");
        Bad(()=>PostProcessComposition.Chain(63,64,[(p,package)],merged)); Bad(()=>PostProcessComposition.Chain(64,64,[(p,collision)],merged));
        var deep=BloomFeature.Build(64,64,new(Levels:6),package)!; Bad(()=>PostProcessComposition.Chain(64,64,[(deep,package),(deep,package)],merged));
        var customId=Guid.NewGuid(); var secondId=Guid.NewGuid(); int builds=0;
        PostFeatureRegistration Module(Guid id,int order,Guid[] dependencies)=>new(id,1,order,[new("gain",0,4)],dependencies,(w,h,parameters,programs)=>{builds++;return BloomFeature.Build(w,h,new(Levels:1,Threshold:0,Intensity:parameters["gain"]),programs);});
        var first=Module(customId,10,[]); var second=Module(secondId,20,[customId]);
        var modules=new PostProcessModules([new(PostProcessLibrarySelection.AcesModule,1)],[first,second]);
        var user=settings with { StackJson=PostProcessData.EncodeStack([new(customId,1,[new("gain",.5f)]),new(secondId,1,[new("gain",.5f)])]) };
        var userGraph=modules.Build(user,64,64,_=>package)!; Pass(builds==2,"trusted custom callbacks through same builder");
        Bad(()=>modules.Validate(user with{StackJson=PostProcessData.EncodeStack(PostProcessData.DecodeStack(user.StackJson).Reverse())}));
        Bad(()=>modules.Validate(user with{StackJson=PostProcessData.EncodeStack([new(secondId,1,[new("gain",1)])])}));
        first.Parameters[0]=new("mutated",0,1);second.Requires[0]=Guid.NewGuid();Pass(modules.Validate(user)==user,"registration arrays owned");
        Bad(()=>new PostProcessModules([new(PostProcessLibrarySelection.AcesModule,1)],[Module(customId,10,[secondId]),Module(secondId,10,[customId])]));
        Bad(()=>new PostProcessModules([new(PostProcessLibrarySelection.AcesModule,1)],[Module(customId,10,[Guid.NewGuid()])]));
        PostProcessModules? reentry=null;reentry=new([new(PostProcessLibrarySelection.AcesModule,1)],[Module(customId,10,[]) with{Build=(w,h,v,b)=>{reentry!.Validate(PostProcessData.Off);return null;}}]);
        Bad(()=>reentry.Build(user with{StackJson=PostProcessData.EncodeStack([new(customId,1,[new("gain",1)])])},64,64,_=>package));
        Pass(reentry.Validate(PostProcessData.Off)==PostProcessData.Off,"failed callback clears build guard");
        Bad(()=>modules.Build(user,64,64,_=>throw new ArgumentException("resolver refused"))); Pass(modules.Validate(user)==user,"failed resolver preserves catalog");

        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Post modules",64,64,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,64,64);using var target=renderer.CreateViewTarget(64,64);
        var prepared=PreparedPostProcessPrograms.Prepare(renderer,userGraph.Programs,()=>true);
        using var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false),64,64);scene.ConfigurePostProcess(userGraph.Plan,prepared,()=>true);
        byte[] image=new byte[64*64*4];scene.Submit(1,[],[],new(Vector3.Zero,Vector3.UnitZ,Vector4.UnitW),Matrix4x4.Identity,new(),target,clear:new(.25f,.125f,.0625f,1));renderer.CaptureTarget(target,image);renderer.Present();
        int Expected(float value){double v=(float)(Half)((float)(Half)(value*1.5f)*1.5f);v=Math.Clamp(v*(2.51*v+.03)/(v*(2.43*v+.59)+.14),0,1);return (int)Math.Round((v<=.0031308?12.92*v:1.055*Math.Pow(v,1/2.4)-.055)*255);}
        int[] expected=[Expected(.25f),Expected(.125f),Expected(.0625f),255];int error=image.Select((v,i)=>Math.Abs(v-expected[i%4])).Max();Pass(error<=1,"actual chained user graph full image vs independent scalar/Half oracle");
        Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"API0/0");
        File.WriteAllText(Path.Combine(output,"post-module-results.json"),JsonSerializer.Serialize(new{cases,maxError=error,formalHosts=false,manualAcceptance=false}));Console.WriteLine($"PASS M7.4-C2 policy/composition: {cases} checks; maxError={error}; API0/0; {output}");return 0;
    }
    private static bool ProcessHasGraphics()=>System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>().Any(m=>m.ModuleName is "NcmaRenderer.dll" or "NcmaPlatform.dll" or "d3dcompiler_47.dll");
}
