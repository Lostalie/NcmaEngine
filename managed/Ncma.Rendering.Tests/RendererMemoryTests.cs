using System.Text.Json;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int RendererMemoryTests(string plugins,string output,bool unsupported=false)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        int checks=0;void Pass(bool ok,string why){Check(ok,"Renderer memory: "+why);checks++;}
        var capture=new RendererMemoryCapture();
        capture.Observe(RendererMemoryCheckpoint.BeforeStartup,null);
        Pass(capture.Capture().Samples.Single() is {State:RendererMemoryState.NoRenderer,Ledger:null,FailureType:null},"no renderer is not zero");
        Reject(()=>capture.Observe((RendererMemoryCheckpoint)999,null));
        Pass(capture.Capture().TotalObservations==1,"invalid checkpoint no mutation");
        Pass(Task.Run(()=>{try{capture.Observe(RendererMemoryCheckpoint.RuntimeReady,null);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult(),"capture owner thread");
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Renderer memory",64,64,false);
        using(var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,64,64,pureUi:true)) {
            using(var image=renderer.CreateUiImage(1,1,[255,255,255,255])) {
                capture.Observe(RendererMemoryCheckpoint.RuntimeReady,renderer);
                var row=capture.Capture().Samples[1];
                if(unsupported)Pass(row is {State:RendererMemoryState.Unsupported,Ledger:null,NativeResult:PluginResult.AbiMismatch},"actual unsupported native query remains missing, not zero");
                else {
                    Pass(row is {State:RendererMemoryState.Available,FailureType:null,Ledger:{PureUi:true}}&&row.Ledger.Domains[5].LogicalBytes==4,"actual explicit logical UI bytes");
                    row.Ledger!.Domains[5]=default;
                    Pass(capture.Capture().Samples[1].Ledger!.Domains[5].LogicalBytes==4,"nested report copy isolation");
                }
            }
            renderer.Dispose();capture.Observe(RendererMemoryCheckpoint.BeforeRendererClose,renderer);
            Pass(capture.Capture().Samples[2] is {State:RendererMemoryState.ReadFailed,Ledger:null,FailureType:nameof(ObjectDisposedException)},"closed renderer read failure is not successful zero");
        }
        capture.Observe(RendererMemoryCheckpoint.AfterShutdown,null);
        var report=capture.Capture();Pass(report.Samples[3].State==RendererMemoryState.NoRenderer&&!report.MemoryAccepted&&!report.CompleteLifetimeCoverage,"after close no lifetime/VRAM claim");
        var bounded=new RendererMemoryCapture();
        for(int i=0;i<35;i++)bounded.Observe(RendererMemoryCheckpoint.BeforeGraphicsRetirement,null);
        var prefix=bounded.Capture();Pass(prefix.TotalObservations==35&&prefix.OmittedObservations==3&&prefix.Samples.Length==32&&!prefix.CompleteRawCoverage,"fixed prefix retains omissions");
        Pass(prefix.Samples.Select(s=>s.Sequence).SequenceEqual(Enumerable.Range(1,32).Select(i=>(ulong)i)),"ordered bounded sequence");
        File.WriteAllText(Path.Combine(output,"renderer-memory.json"),JsonSerializer.Serialize(new{checks,unsupported,report,prefix},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS renderer memory {checks} checks; unsupported={unsupported}; report={output}");return 0;
    }
}
