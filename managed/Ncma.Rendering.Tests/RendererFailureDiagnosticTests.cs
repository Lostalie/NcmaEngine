using System.Text.Json;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int RendererFailureDiagnosticTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        int checks=0;void Pass(bool ok,string label){Check(ok,"M7.6 GPU diagnostic: "+label);++checks;}
        NativeFailureDiagnostic? copied=null;RenderWindowFault? hostCopy=null;
        using(var loader=new PluginLoader())
        {
            loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());
            var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Copied GPU diagnostics",64,64,false);
            Pass(RendererSession.ReadStartupFailure(native) is null,"Empty module has no invented startup failure");
            try{using var invalid=new RendererSession(native,window,64,64,backend:2);throw new Exception("Unsupported backend accepted");}
            catch(PluginException e){Pass(e.Result==PluginResult.UnsupportedFeature&&e.NativeDiagnostic is null,"Pre-admission rejection keeps original typed exception and no stale GPU diagnostic");}
            using(var renderer=new RendererSession(native,window,64,64,pureUi:true))
            {
                Pass(renderer.ReadFirstFailure() is null,"Healthy device diagnostic is empty");
                Pass(Task.Run(()=>{try{_ = renderer.ReadFirstFailure();return false;}catch(PluginException e){return e.Result==PluginResult.WrongThread;}}).GetAwaiter().GetResult(),"Foreign thread cannot read live native diagnostics");
                var frame=RenderFrame.Reference(53,64,64);var graph=new ClearPipeline(.2f,.3f,.4f).Build(64,64).Compile(renderer.Capabilities);
                renderer.Submit(graph,null,frame);renderer.Capture(new byte[64*64*4]);
                Pass(renderer.ReadFirstFailure() is null,"Ordinary active-frame read does not fabricate failure");
                renderer.AbandonFrameForShutdown(53);copied=renderer.ReadFirstFailure();
                Pass(copied is{Backend:1,Frame:53,Operation:(uint)GpuFailureOperation.ExplicitAbandon,Result:PluginResult.InternalError,NativeCode:null,DeviceReason:0},"Explicit terminal abandonment has numeric identity but no fabricated HRESULT");
                var surfaceState=renderer.ReadFrameState();
                Pass(copied?.Generation==surfaceState.Generation,"Diagnostic bound to exact native generation");
                try{renderer.Present();throw new Exception("Abandoned renderer presented");}
                catch(PluginException e){Pass(e.Result==PluginResult.InternalError&&renderer.ReadFirstFailure()==copied,"Later operation error preserves immutable first diagnostic");}
                Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"Actual healthy device API0/0, not physical removal");
            }
            Pass(copied is{Frame:53,NativeCode:null},"Copied value survives device retirement");
            using(var fresh=new RendererSession(native,window,64,64,pureUi:true))
            {
                Pass(fresh.ReadFirstFailure() is null&&fresh.ReadFrameState().Generation!=copied?.Generation,"New device never inherits old diagnostic history");
                var surface=new RendererWindowSurface(fresh);
                var graph=new ClearPipeline(.2f,.3f,.4f).Build(64,64).Compile(fresh.Capabilities);
                fresh.Submit(graph,null,RenderFrame.Reference(61,64,64));fresh.AbandonFrameForShutdown(61);
                try{fresh.Present();}catch(PluginException e){surface.RecordFailure(e);}
                hostCopy=surface.Status.Failure;
                Pass(hostCopy?.FirstNativeFailure is{Frame:61,NativeCode:null,Operation:(uint)GpuFailureOperation.ExplicitAbandon},"Host retains typed value before any device shutdown/rebuild");
                surface.RecordFailure(new InvalidOperationException("private-callback-path-secret"));
                Pass(surface.Status.Failure==hostCopy&&!JsonSerializer.Serialize(hostCopy).Contains("secret"),"Secondary host failure cannot replace or inject arbitrary diagnostic text");
                surface.BeginShutdown();fresh.Dispose();surface.CompleteShutdown();
                Pass(surface.Status.Failure==hostCopy,"Copied host diagnostic survives ordinary close");
                Reject(()=>fresh.ReadFirstFailure());
            }
        }
        Pass(hostCopy?.FirstNativeFailure is{Frame:61}&&copied is{Frame:53},"Copied independent values survive module unload");
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{checks,copied,hostCopy,physicalDeviceLoss=false}));
        Console.WriteLine($"PASS M7.6 copied GPU diagnostics {checks} checks; {output}");return 0;
    }
}
