using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
internal static unsafe partial class Program
{
    private static int EnvironmentGpuTests(string root,string output)
    {
        Directory.CreateDirectory(output);int cases=0;bool allowed=true;double maximumError=0;
        void Pass(bool value,string message){Check(value,"M7.3-B1 "+message);cases++;}
        void Bad(Action action){Reject(action);cases++;}
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(root),Specs().Take(2).ToArray());
        var module=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
        using var cook=new EnvironmentCookService(module,()=>true);
        var source=HdrEnvironmentSource.Neutral(Guid.NewGuid());
        var original=cook.Cook(Guid.NewGuid(),1,source,new(8,4,8,64));
        // Independent deliberately distinguishable texels, not a constant/symmetric cook oracle.
        // A host-approved rehash is format integrity, NOT a trusted cook/signature credential.
        byte[] bytes=original.CopyBytes();int count=original.Settings.FloatCount;
        int diffuse=(int)(6*original.Settings.IrradianceSize*original.Settings.IrradianceSize*4),end=count-(int)(original.Settings.LutSize*original.Settings.LutSize*2);
        for(int i=0;i<count;i++){float value=i<end&&i%4==3?1:i<diffuse?70000+i*.125f:i<end?i*.03125f:(i-end)*.01f;
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(160+i*4),value);}
        SHA256.HashData(bytes.AsSpan(160)).CopyTo(bytes,120);
        var distinct=EnvironmentPackage.Decode(bytes,Convert.ToHexString(SHA256.HashData(bytes)));
        var expected=distinct.CopyValues();expected[0]=0;Pass(distinct.CopyValues()[0]==70000,"CopyValues owned snapshot");expected=distinct.CopyValues();
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Environment GPU resources (not scene IBL)",64,64,false);
        ulong baseLive=module.Status.LiveResources;
        using(var renderer=new RendererSession(module,window,64,64)){
            var off=renderer.EnvironmentStats;Pass(off.Live==0&&off.ResidentBytes==0&&off.Publications==0&&off.BudgetBytes==32ul*1024*1024,"Off does not allocate environment resources");
            Bad(()=>renderer.CreateEnvironment(distinct,()=>false));Pass(renderer.EnvironmentStats.Publications==0,"approval rejection before any GPU resources");
            Bad(()=>renderer.CreateEnvironment(distinct,()=>{Bad(renderer.Dispose);throw new InvalidOperationException("revoked");}));
            Pass(renderer.EnvironmentStats.Live==0,"renderer cannot unload inside approval");
            using(var resource=renderer.CreateEnvironment(distinct,()=>allowed)){
                Pass(ReferenceEquals(resource.Package,distinct)&&!resource.Package.GpuValidated,"immutable package metadata, no persistent GPU credential");
                Pass(renderer.EnvironmentStats.Live==1&&renderer.EnvironmentStats.ResidentBytes==(ulong)count*4&&renderer.EnvironmentStats.UploadedBytes==(ulong)count*4,"complete GPU resource accounting");
                Pass(module.Status.LiveResources==baseLive+2,"module counts renderer and environment token");Bad(renderer.Dispose);Bad(()=>module.Dispose());
                var actual=resource.CaptureForDiagnostics(()=>true);
                for(int i=0;i<count;i++)maximumError=Math.Max(maximumError,Math.Abs(actual[i]-expected[i]));
                Pass(maximumError==0,"every actual GPU face/mip/LUT texel exact, independent distinct values");
                Pass(actual[0]==70000&&actual[0]>65504,"RGBA32F physical irradiance no half-float clipping/gamma");
                actual[0]=0;Pass(resource.CaptureForDiagnostics(()=>true)[0]==70000,"GPU capture caller copy does not mutate resource");
                var before=renderer.EnvironmentStats;allowed=false;Bad(()=>resource.Replace(original,()=>allowed));Bad(()=>resource.CaptureForDiagnostics(()=>allowed));allowed=true;
                Pass(ReferenceEquals(resource.Package,distinct)&&renderer.EnvironmentStats.Publications==before.Publications&&renderer.EnvironmentStats.Captures==before.Captures,"revocation retains metadata and GPU counters");
                Task.Run(()=>Bad(()=>resource.Replace(original,()=>true))).GetAwaiter().GetResult();
                Task.Run(()=>Bad(()=>resource.CaptureForDiagnostics(()=>true))).GetAwaiter().GetResult();
                Task.Run(()=>Bad(resource.Dispose)).GetAwaiter().GetResult();
                Pass(renderer.EnvironmentStats.Live==1&&ReferenceEquals(resource.Package,distinct),"wrong thread retains live resource");
                resource.Replace(original,()=>{Bad(()=>renderer.CreateEnvironment(original,()=>true));Bad(()=>resource.Replace(original,()=>true));Bad(resource.Dispose);Bad(renderer.Dispose);return true;});
                Pass(ReferenceEquals(resource.Package,original)&&resource.CaptureForDiagnostics(()=>true).SequenceEqual(original.CopyValues()),"complete replacement after reentry rejection");
                var replaced=renderer.EnvironmentStats;Pass(replaced.Live==1&&replaced.Publications==2&&replaced.Retirements==1,"stable token replacement counters");
                renderer.Resize(96,80);Pass(resource.CaptureForDiagnostics(()=>true).SequenceEqual(original.CopyValues()),"actual resize retains environment cube/mips/LUT");
                var graph=new ClearPipeline(0,0,0).Build(96,80).Compile(renderer.Capabilities);
                renderer.Submit(graph,null,RenderFrame.Reference(1,96,80));
                Bad(()=>resource.Replace(distinct,()=>true));Bad(()=>resource.CaptureForDiagnostics(()=>true));Bad(resource.Dispose);renderer.Present();
                Pass(ReferenceEquals(resource.Package,original)&&renderer.EnvironmentStats.Publications==2,"active frame failures retain old resource");
                Pass(resource.CaptureForDiagnostics(()=>true).SequenceEqual(original.CopyValues()),"present restores off-frame diagnostic boundary");
                resource.Replace(distinct,()=>true);Pass(resource.CaptureForDiagnostics(()=>true).SequenceEqual(expected),"second distinct replacement actual GPU content");
                var stable=renderer.EnvironmentStats;
                for(int i=0;i<1024;i++)_ = renderer.EnvironmentStats;
                Pass(renderer.EnvironmentStats.Publications==stable.Publications&&renderer.EnvironmentStats.UploadedBytes==stable.UploadedBytes&&renderer.EnvironmentStats.Captures==stable.Captures,"1024 statistics reads no GPU upload/publication/readback");
                Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"resource/resize/boundary GPU API0/0, NOT scene IBL");
            }
            Pass(renderer.EnvironmentStats.Live==0&&renderer.EnvironmentStats.ResidentBytes==0,"explicit cleanup zero resident bytes");
            var closed=renderer.CreateEnvironment(original,()=>true);closed.Dispose();closed.Dispose();Bad(()=>closed.Replace(distinct,()=>true));Bad(()=>closed.CaptureForDiagnostics(()=>true));Bad(()=>_ = closed.Package);
            var budget=new List<GpuEnvironmentResource>();try{
                for(int i=0;i<8;i++)budget.Add(renderer.CreateEnvironment(original,()=>true));
                var s=renderer.EnvironmentStats;Bad(()=>renderer.CreateEnvironment(original,()=>true));
                Pass(renderer.EnvironmentStats.Live==8&&renderer.EnvironmentStats.Publications==s.Publications,"ninth resource budget before GPU allocation");
                budget[7].Replace(distinct,()=>true);Pass(budget[7].CaptureForDiagnostics(()=>true).SequenceEqual(expected)&&renderer.EnvironmentStats.Live==8,"at-limit replacement allowed");
            }finally{foreach(var item in budget)item.Dispose();}
            var maximum=cook.Cook(Guid.NewGuid(),1,source,new(64,16,64,64));
            using(var maxResource=renderer.CreateEnvironment(maximum,()=>true)){
                Pass(maxResource.CaptureForDiagnostics(()=>true).SequenceEqual(maximum.CopyValues()),"maximum GPU cube64/full7mips/irr16/LUT64 exact");
                Pass(renderer.EnvironmentStats.ResidentBytes==(ulong)maximum.Settings.FloatCount*4,"maximum resource accounting");
            }
            var minimal=cook.Cook(Guid.NewGuid(),1,source,new(2,2,2,64));
            using(var minResource=renderer.CreateEnvironment(minimal,()=>true))Pass(minResource.CaptureForDiagnostics(()=>true).SequenceEqual(minimal.CopyValues()),"minimum GPU layout exact");
            Pass(renderer.EnvironmentStats.Live==0&&renderer.EnvironmentStats.ResidentBytes==0&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"all budget resources drained API0/0");
        }
        Pass(module.Status.LiveResources==baseLive,"renderer/environment lifetime drained");
        using(var ui=new RendererSession(module,window,64,64,pureUi:true)){
            var stats=ui.EnvironmentStats;Bad(()=>ui.CreateEnvironment(original,()=>true));
            Pass(ui.EnvironmentStats.Live==0&&ui.EnvironmentStats.Publications==stats.Publications&&ui.UiStats.ResidentBytes==0,"real pureUI allocates no environment resources");
            Pass(ui.Stats.ValidationErrors==0&&ui.Stats.ValidationWarnings==0,"pureUI exclusion API0/0");
        }
        using(var next=new RendererSession(module,window,64,64)){
            Pass(next.EnvironmentStats.Live==0&&next.EnvironmentStats.Publications==0,"new renderer generation has isolated counters");
            using var resource=next.CreateEnvironment(distinct,()=>true);Pass(resource.CaptureForDiagnostics(()=>true).SequenceEqual(expected),"new renderer generation real upload");
        }
        File.WriteAllText(Path.Combine(output,"environment-gpu.json"),JsonSerializer.Serialize(new{schema=1,cases,maximumError,query=14,api=1,actualGpuReadback=true,sceneIblImplemented=false,formalHostIntegrated=false,manualHdrAcceptance=false}));
        Console.WriteLine($"PASS M7.3-B1 {cases} environment GPU resource cases; all texel error={maximumError}; scene IBL pending B2/B3");return 0;
    }
}
