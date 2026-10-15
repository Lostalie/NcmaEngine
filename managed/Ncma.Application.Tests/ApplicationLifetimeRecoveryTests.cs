using Ncma.Application;
using System.Text.Json;

internal static class ApplicationLifetimeRecoveryTests
{
    public static void Run()
    {
        int checks=0;void Pass(bool condition,string message){Check(condition,message);checks++;}
        var calls=new List<string>();var app=new ApplicationLifetime();
        var first=new RecoverableService("first",calls);var last=new RecoverableService("last",calls){RejectClose=true};
        app.Start([first,last]);Reject(app.Dispose);
        Pass(calls.SequenceEqual(new[]{"first.start","last.start","last.close"}),"failed close must retain earlier dependencies, not close them");
        Pass(app.RetainedServiceCount==2&&app.ShutdownAttempts==1&&app.ShutdownFailure is{Code:"application_shutdown_failed"},"bounded owner and close status");
        var primary=app.Failure;Reject(app.Dispose);
        Pass(last.CloseAttempts==2&&first.CloseAttempts==0,"retry must revisit retained owner without touching predecessors");
        Pass(ReferenceEquals(primary,app.Failure)&&ReferenceEquals(primary,app.ShutdownFailure),"retry must preserve original failure identity");
        Task.Run(()=>Reject(app.Dispose)).GetAwaiter().GetResult();
        Pass(app.ShutdownAttempts==2&&app.RetainedServiceCount==2,"foreign thread cannot close retained services");
        Reject(()=>app.Start([]));Reject(()=>app.Run(new TestClock(),new TestPipeline(),1));
        Pass(app.ShutdownAttempts==2,"failed lifetime cannot restart or execute frames");
        last.RejectClose=false;app.Dispose();app.Dispose();
        Pass(last.CloseAttempts==3&&first.CloseAttempts==1&&app.RetainedServiceCount==0&&app.RetainedServiceId is null,"successful retry drains retained chain exactly once");
        Pass(app.State==ApplicationState.Failed&&ReferenceEquals(app.Failure,primary)&&app.ShutdownAttempts==3,"drain preserves fault and repeated close is a no-op");
        foreach(bool initialization in new[]{true,false}) {
            calls=[];app=new();first=new("base",calls);last=new("failed",calls){RejectStart=initialization,RejectClose=true};
            if(initialization)Reject(()=>app.Start([first,last,new RecoverableService("never",calls)]));
            else {app.Start([first,last]);Reject(()=>app.Run(new TestClock(),new TestPipeline{FailPresent=true},1));}
            primary=app.Failure;
            Pass(primary?.Code==(initialization?"application_initialize_failed":"application_frame_failed"),"exact primary phase");
            Pass(!calls.Contains("never.start")&&app.RetainedServiceCount==2,"partial startup or failed frame retains the dependency chain");
            Reject(app.Dispose);var closing=app.ShutdownFailure;Reject(app.Dispose);
            Pass(ReferenceEquals(primary,app.Failure)&&ReferenceEquals(closing,app.ShutdownFailure)&&primary?.Correlation==closing?.Correlation,"separate shutdown evidence cannot overwrite primary phase/correlation");
            Pass(!JsonSerializer.Serialize(new{app.Failure,app.ShutdownFailure}).Contains("private"),"diagnostics retain no raw callback text");
            last.RejectClose=false;app.Dispose();
            Pass(first.CloseAttempts==1&&app.RetainedServiceCount==0&&ReferenceEquals(primary,app.Failure),"successful explicit retry preserves primary failure and drains dependencies");
        }
        calls=[];app=new();first=new("first",calls){RejectClose=true};last=new("last",calls);
        app.Start([first,last]);Reject(app.Dispose);first.RejectClose=false;app.Dispose();
        Pass(first.CloseAttempts==2&&last.CloseAttempts==1,"already released successors never close twice");
        calls=[];app=new();first=new("first",calls);
        first.OnClose=()=>{Reject(app.Dispose);Reject(()=>app.Start([]));Reject(()=>app.Run(new TestClock(),new TestPipeline(),1));};
        app.Start([first]);app.Dispose();
        Pass(first.CloseAttempts==1&&app.State==ApplicationState.Stopped&&app.Failure is null,"caught close reentry never mutates ownership or starts another frame");
        app=new();Reject(()=>app.Start(Enumerable.Range(0,65).Select(_=>(IApplicationService)new RecoverableService("unused",[])).ToArray()));
        Pass(app.RetainedServiceCount==0&&app.State==ApplicationState.Created,"service budget rejects before ownership");
        first=new("duplicate",[]);Reject(()=>app.Start([first,first]));
        Pass(app.RetainedServiceCount==0&&app.State==ApplicationState.Created,"duplicate owner rejects before ownership");
        app.Dispose();
        Console.WriteLine($"PASS M7.6 application lifetime {checks} checks; retained chain/primary diagnostics/explicit retry");
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception("M7.6 lifecycle: "+message);}
    private static void Reject(Action action)
    {try{action();}catch(Exception error)when(error is ArgumentException or InvalidOperationException or AggregateException){return;}throw new Exception("Expected lifecycle rejection");}
    private sealed class RecoverableService(string id,List<string> calls):IApplicationService
    {
        public bool RejectClose;
        public bool RejectStart;
        public Action? OnClose;
        public int CloseAttempts;
        public void Start(){calls.Add(id+".start");if(RejectStart)throw new InvalidOperationException("private-initialize-cause");}
        public void Dispose(){CloseAttempts++;calls.Add(id+".close");OnClose?.Invoke();if(RejectClose)throw new InvalidOperationException("private-close-cause");}
    }
    private sealed class TestClock:IFrameClock {private double _time;public double Seconds=>_time+=1d/60;}
    private sealed class TestPipeline:IFramePipeline
    {
        public bool FailPresent;
        public bool PumpPlatform()=>true;
        public void ApplyEditorIntents(FrameContext frame){}
        public void PumpAgent(FrameContext frame){}
        public void BeginPresentation(FrameContext frame){}
        public void SubmitInput(FrameContext frame){}
        public void AdvancePlay(FrameContext frame){}
        public void Present(FrameContext frame){if(FailPresent)throw new InvalidOperationException("private-frame-cause");}
    }
}
