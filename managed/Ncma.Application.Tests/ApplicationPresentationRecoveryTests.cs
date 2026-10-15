using System.Text.Json;
using Ncma.Application;

internal static class ApplicationPresentationRecoveryTests
{
    public static void Run()
    {
        int checks=0;void Pass(bool ok,string label){if(!ok)throw new Exception("Application presentation recovery: "+label);checks++;}
        foreach(var phase in Enum.GetValues<ApplicationFramePhase>()) {
            using var app=new ApplicationLifetime();var pipe=new TestPipeline{Fail=phase};app.Start([pipe]);
            Reject(()=>app.Run(new TestClock{Invalid=phase==ApplicationFramePhase.Clock},pipe,1));
            var fault=app.FrameFailure!.Value;bool allowed=phase is ApplicationFramePhase.Present or ApplicationFramePhase.BeginPresentation;
            Pass(fault.Phase==phase&&app.CanRecoverPresentation==allowed,"exact phase eligibility "+phase);
            var operation=new Recovery(pipe,()=>pipe.Fail=null);
            if(allowed){app.RecoverPresentation(fault,operation);Pass(app.State==ApplicationState.Running&&operation.Calls==1,"presentation restored "+phase);}
            else {Reject(()=>app.RecoverPresentation(fault,operation));Pass(app.State==ApplicationState.Failed&&operation.Calls==0,"non-presentation cannot resume "+phase);}
        }
        using(var app=new ApplicationLifetime()) {
            var pipe=new TestPipeline{Fail=ApplicationFramePhase.Present};app.Start([pipe]);Reject(()=>app.Run(new TestClock(),pipe,2));
            var fault=app.FrameFailure!.Value;var first=app.Failure;var operation=new Recovery(pipe,()=>pipe.Fail=null);
            Reject(()=>app.RecoverPresentation(fault with{Identity=Guid.NewGuid()},operation));
            Task.Run(()=>Reject(()=>app.RecoverPresentation(fault,operation))).GetAwaiter().GetResult();
            Pass(app.RecoveryAttempts==0&&pipe.Advanced.SequenceEqual(new ulong[]{1}),"stale/foreign-thread review has no effects; failed Present already advanced once");
            Reject(()=>app.RecoverPresentation(fault,new Recovery(new TestPipeline(),()=>{})));
            Pass(app.State==ApplicationState.Failed&&app.RecoveryFailure is{Code:"application_presentation_recovery_failed"},"foreign pipeline cannot restore owned loop");
            app.RecoverPresentation(fault,operation);var clock=new TestClock{Value=1000};
            Pass(app.Run(clock,pipe,2)==2&&pipe.Advanced.SequenceEqual(new ulong[]{1,2,3})&&pipe.Frames.All(f=>f.DeltaSeconds<.02),"frame IDs monotonic, failed gameplay step not replayed, recovery wall time excluded");
            Pass(ReferenceEquals(first,app.Failure)&&app.FrameFailure is null&&pipe.Starts==1&&pipe.Closes==0,"same service, first diagnostic retained");
            Reject(()=>app.RecoverPresentation(fault,operation));pipe.Fail=ApplicationFramePhase.Present;Reject(()=>app.Run(new TestClock(),pipe,1));
            var next=app.FrameFailure!.Value;Pass(next.FrameId==4&&next.Identity!=fault.Identity&&ReferenceEquals(first,app.Failure),"new exact fault ticket while retaining first cause");
            Reject(()=>app.RecoverPresentation(fault,operation));app.RecoverPresentation(next,operation);
            app.Dispose();Pass(pipe.Closes==1&&app.State==ApplicationState.Stopped&&ReferenceEquals(first,app.Failure),"normal close after recovery keeps diagnostics");
        }
        foreach(int kind in new[]{0,1,2,3,4}) {
            using var app=new ApplicationLifetime();var pipe=new TestPipeline{Fail=ApplicationFramePhase.Present};app.Start([pipe]);Reject(()=>app.Run(new TestClock(),pipe,1));var fault=app.FrameFailure!.Value;
            var operation=new Recovery(pipe,()=>{if(kind==0)throw new InvalidOperationException("private-recovery-secret");if(kind==1)Reject(app.Dispose);});
            if(kind==2)operation.Ready=false;
            if(kind==3)operation.OnPipeline=()=>Reject(()=>app.Run(new TestClock(),pipe,1));
            if(kind==4)operation.OnReady=()=>Reject(app.Dispose);
            Reject(()=>app.RecoverPresentation(fault,operation));
            Pass(app.State==ApplicationState.Failed&&app.FrameFailure==fault&&app.RetainedServiceCount==1&&pipe.Closes==0,"restore failure/readiness/reentry remains failed "+kind);
            Pass(!JsonSerializer.Serialize(new{app.Failure,app.RecoveryFailure}).Contains("private"),"copied bounded diagnostics "+kind);
            app.RecoverPresentation(fault,new Recovery(pipe,()=>pipe.Fail=null));Pass(app.State==ApplicationState.Running,"explicit new retry after rejection "+kind);
        }
        using(var app=new ApplicationLifetime()) {
            var pipe=new TestPipeline{Fail=ApplicationFramePhase.Present,FailClose=true};app.Start([pipe]);Reject(()=>app.Run(new TestClock(),pipe,1));var fault=app.FrameFailure!.Value;
            Reject(app.Dispose);Reject(()=>app.RecoverPresentation(fault,new Recovery(pipe,()=>{})));
            Pass(!app.CanRecoverPresentation&&app.RetainedServiceCount==1,"terminal close cannot become presentation recovery");pipe.FailClose=false;app.Dispose();
        }
        using(var app=new ApplicationLifetime()) {
            app.Start([]);var unowned=new TestPipeline{Fail=ApplicationFramePhase.Present};Reject(()=>app.Run(new TestClock(),unowned,1));
            Pass(!app.CanRecoverPresentation,"unowned pipeline cannot recover through application authority");
        }
        Console.WriteLine($"PASS application presentation recovery {checks} checks");
    }
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is InvalidOperationException or ArgumentException or AggregateException){return;}throw new Exception("Expected presentation recovery rejection");}
    private sealed class TestClock:IFrameClock {public double Value;public bool Invalid;public double Seconds=>Invalid?double.NaN:Value+=1d/60;}
    private sealed class Recovery(IFramePipeline pipeline,Action restore):IApplicationPresentationRecovery
    {
        public int Calls;public bool Ready=true;public Action? OnPipeline,OnReady;
        public IFramePipeline Pipeline{get{OnPipeline?.Invoke();return pipeline;}}
        public void Restore(){Calls++;restore();}
        public bool IsReady{get{OnReady?.Invoke();return Ready;}}
    }
    private sealed class TestPipeline:IApplicationService,IFramePipeline
    {
        public ApplicationFramePhase? Fail;public bool FailClose;public int Starts,Closes;
        public List<ulong> Advanced=[];public List<FrameContext> Frames=[];
        private void Step(ApplicationFramePhase phase){if(Fail==phase)throw new InvalidOperationException("private-frame-secret");}
        public void Start(){Starts++;}public void Dispose(){if(FailClose)throw new InvalidOperationException("private-close-secret");Closes++;}
        public bool PumpPlatform(){Step(ApplicationFramePhase.Platform);return true;}
        public void ApplyEditorIntents(FrameContext f)=>Step(ApplicationFramePhase.EditorIntents);
        public void PumpAgent(FrameContext f)=>Step(ApplicationFramePhase.Agent);
        public void BeginPresentation(FrameContext f)=>Step(ApplicationFramePhase.BeginPresentation);
        public void SubmitInput(FrameContext f)=>Step(ApplicationFramePhase.Input);
        public void AdvancePlay(FrameContext f){Step(ApplicationFramePhase.Gameplay);Advanced.Add(f.FrameId);Frames.Add(f);}
        public void Present(FrameContext f)=>Step(ApplicationFramePhase.Present);
    }
}
