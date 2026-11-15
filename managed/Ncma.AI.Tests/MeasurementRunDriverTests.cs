using Ncma.Application;
using Ncma.Editor.Services;

internal static class MeasurementRunDriverTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Measurement driver assertion"); }
    private static Exception Reject(Action action)
    { try { action(); } catch (Exception e) { return e; } throw new Exception("Measurement driver must reject"); }
    private sealed class Clock : TimeProvider, IFrameClock
    {
        internal long Now; internal bool Fail;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Fail ? throw new IOException("PRIVATE_CLOCK") : Now;
        public double Seconds => Now / 1000d;
    }
    private sealed class Pipeline : IFramePipeline, IApplicationService
    {
        internal bool Stop, FailClose;
        internal int Presents, Closes;
        internal Action? During;
        internal readonly Exception CloseError = new IOException("PRIVATE_CLOSE");
        public void Start() { }
        public bool PumpPlatform() => !Stop;
        public void ApplyEditorIntents(FrameContext f) { }
        public void PumpAgent(FrameContext f) { }
        public void BeginPresentation(FrameContext f) { }
        public void SubmitInput(FrameContext f) { }
        public void AdvancePlay(FrameContext f) { }
        public void Present(FrameContext f) { During?.Invoke(); Presents++; }
        public void Dispose() { Closes++; if (FailClose) throw CloseError; }
    }
    private static MeasurementInputsPlan Plan(MeasurementInputsTests.Fixture f, ulong frames = 2) => new(f.Identity,3000,1000,3,frames);
    internal static IEnumerable<(string,Action)> Cases()
    {
        yield return ("M8.7-E2E2 window duration is cooperative and single use",()=>{
            var clock=new Clock();var window=new ApplicationRunWindow(1000,clock);var p=new Pipeline();
            p.During=()=>clock.Now+=400;using var app=new ApplicationLifetime();app.Start([p]);
            Check(app.Run(clock,p,20,window:window)==3&&p.Presents==3&&window.Stop==ApplicationRunStop.Duration);
            Check(app.State==ApplicationState.Running);Reject(()=>app.Run(clock,p,1,window:window));
            Check(app.State==ApplicationState.Running); // Reuse rejection does not change original host state.
        });
        yield return ("M8.7-E2E2 window stop owner clock fault and original null path",()=>{
            foreach(bool failure in new[]{false,true}) {
                var clock=new Clock();var window=new ApplicationRunWindow(1000,clock);var p=new Pipeline();
                using var app=new ApplicationLifetime();app.Start([p]);
                p.During=()=>{if(failure)clock.Fail=true;else window.RequestStop();};
                if(failure){var error=Reject(()=>app.Run(clock,p,10,window:window));Check(error is IOException&&app.FrameFailure?.Phase==ApplicationFramePhase.Clock&&window.Stop==ApplicationRunStop.ClockFailure);}
                else Check(app.Run(clock,p,10,window:window)==1&&window.Stop==ApplicationRunStop.Requested);
                Task.Run(()=>Check(Reject(window.RequestStop) is InvalidOperationException)).GetAwaiter().GetResult();
            }
            var normal=new Pipeline();using var plain=new ApplicationLifetime();plain.Start([normal]);
            Check(plain.Run(new Clock(),normal,2)==2); // Default loop unchanged.
        });
        yield return ("M8.7-E2E2 window backwards clock and bounds reject",()=>{
            Reject(()=>new ApplicationRunWindow(0));Reject(()=>new ApplicationRunWindow(3600001));
            var clock=new Clock{Now=10};var window=new ApplicationRunWindow(1000,clock);var p=new Pipeline{During=()=>clock.Now=9};
            using var app=new ApplicationLifetime();app.Start([p]);Reject(()=>app.Run(clock,p,3,window:window));
            Check(window.Stop==ApplicationRunStop.ClockFailure&&app.State==ApplicationState.Failed);
        });
        yield return ("M8.7-E2E2 driver frames pre post pins seal and original shutdown",()=>{
            var f=new MeasurementInputsTests.Fixture();var plan=Plan(f);using var inputs=f.Lease(plan);inputs.Prepare();
            var driver=new MeasurementRunDriver(plan,inputs);var p=new Pipeline();using var app=new ApplicationLifetime();app.Start([p]);
            var status=driver.RunAndClose(app,new Clock(),p);
            Check(status.State==MeasurementRunState.Closed&&status.End==MeasurementRunEnd.Frames&&status.CompletedFrames==2&&p.Closes==1);
            Check(status.InputsCheckedBeforeRun&&status.InputsCheckedAfterRun&&!status.ApplicationRetained&&!status.InputsRetained&&inputs.State==MeasurementInputsState.Closed);
            Check(!status.ExecutedProductVerified&&!status.WorkloadApproved&&!status.PerformanceAccepted);
            var report=driver.ProcessReport!;Check(report.CompletedIterations==2&&report.BoundToLoop);
            report.Series.Samples[0]=default;Check(driver.ProcessReport!.Series.Samples[0].Counters is not null);
            Reject(()=>driver.RunAndClose(app,new Clock(),p));Reject(()=>driver.RetryClose());
        });
        yield return ("M8.7-E2E2 driver requested stop and platform stop stay distinct",()=>{
            foreach(bool request in new[]{false,true}) {
                var f=new MeasurementInputsTests.Fixture();var plan=Plan(f);using var inputs=f.Lease(plan);inputs.Prepare();
                var driver=new MeasurementRunDriver(plan,inputs);var p=new Pipeline{Stop=!request};using var app=new ApplicationLifetime();app.Start([p]);
                if(request)driver.RequestStop();var status=driver.RunAndClose(app,new Clock(),p);
                Check(status.End==(request?MeasurementRunEnd.Requested:MeasurementRunEnd.Platform)&&status.CompletedFrames==0&&p.Closes==1);
                Check(driver.ProcessReport!.Iterations==(request?0:1));
            }
        });
        yield return ("M8.7-E2E2 driver original fault preserved and completed prefix retained",()=>{
            var f=new MeasurementInputsTests.Fixture();var plan=Plan(f,3);using var inputs=f.Lease(plan);inputs.Prepare();
            var driver=new MeasurementRunDriver(plan,inputs);var p=new Pipeline();var original=new InvalidOperationException("PRIVATE_BUSINESS");
            p.During=()=>{if(p.Presents==1)throw original;};using var app=new ApplicationLifetime();app.Start([p]);
            Check(ReferenceEquals(Reject(()=>driver.RunAndClose(app,new Clock(),p)),original));
            Check(driver.Status.End==MeasurementRunEnd.Fault&&driver.Status.State==MeasurementRunState.Closed&&driver.Status.CompletedFrames==1);
            Check(driver.ProcessReport!.FaultedIterations==1&&driver.ProcessReport.CompletedIterations==1&&p.Closes==1);
            Check(driver.Status.FailureType==nameof(InvalidOperationException));
        });
        yield return ("M8.7-E2E2 driver close failure retains pins until explicit retry",()=>{
            var f=new MeasurementInputsTests.Fixture();var plan=Plan(f);using var inputs=f.Lease(plan);inputs.Prepare();
            var driver=new MeasurementRunDriver(plan,inputs);var p=new Pipeline{FailClose=true};using var app=new ApplicationLifetime();app.Start([p]);
            var error=Reject(()=>driver.RunAndClose(app,new Clock(),p));Check(error is AggregateException);
            for(int i=0;i<3;i++)Check(driver.Status is {State:MeasurementRunState.Retained,ApplicationRetained:true,InputsRetained:true,CloseAttempts:1}&&driver.ProcessReport is not null);
            Check(p.Closes==1&&inputs.State==MeasurementInputsState.Ready);
            Check(Reject(()=>{using var stream=new FileStream(f.Capture,FileMode.Open,FileAccess.Write,FileShare.ReadWrite);}) is IOException);
            p.FailClose=false;var done=driver.RetryClose();Check(done.State==MeasurementRunState.Closed&&done.CloseAttempts==2&&p.Closes==2&&inputs.State==MeasurementInputsState.Closed);
        });
        yield return ("M8.7-E2E2 driver post input drift fails without replacing run or deleting evidence",()=>{
            var f=new MeasurementInputsTests.Fixture();var plan=Plan(f,1);using var inputs=f.Lease(plan);inputs.Prepare();
            var driver=new MeasurementRunDriver(plan,inputs);string extra=Path.Combine(f.Workload,"late.txt");
            var p=new Pipeline{During=()=>File.WriteAllText(extra,"retained evidence")};using var app=new ApplicationLifetime();app.Start([p]);
            Reject(()=>driver.RunAndClose(app,new Clock(),p));Check(driver.Status is {InputsCheckedAfterRun:false,CompletedFrames:1,State:MeasurementRunState.Closed});
            Check(File.Exists(extra)&&p.Closes==1&&driver.ProcessReport is not null);
        });
        yield return ("M8.7-E2E2 driver mismatch preflight never acquires close ownership",()=>{
            var f=new MeasurementInputsTests.Fixture();var plan=Plan(f);using var inputs=f.Lease(plan);inputs.Prepare();
            var driver=new MeasurementRunDriver(Plan(f,3),inputs);var p=new Pipeline();using var app=new ApplicationLifetime();app.Start([p]);
            Reject(()=>driver.RunAndClose(app,new Clock(),p));Check(driver.Status.State==MeasurementRunState.Rejected&&!driver.Status.InputsRetained&&p.Closes==0);
            Check(app.State==ApplicationState.Running&&inputs.State==MeasurementInputsState.Ready&&driver.ProcessReport is null);
        });
        yield return ("M8.7-E2E2 driver owner reentry and stop during frame do not create new loops",()=>{
            var f=new MeasurementInputsTests.Fixture();var plan=Plan(f);using var inputs=f.Lease(plan);inputs.Prepare();
            var driver=new MeasurementRunDriver(plan,inputs);var p=new Pipeline();using var app=new ApplicationLifetime();app.Start([p]);
            Task.Run(()=>Check(Reject(driver.RequestStop) is InvalidOperationException)).GetAwaiter().GetResult();
            var nested=new MeasurementRunDriver(plan,inputs);
            p.During=()=>{
                Reject(()=>nested.RunAndClose(app,new Clock(),p));
                Check(nested.Status is {State:MeasurementRunState.Rejected,ApplicationRetained:false,InputsRetained:false}&&p.Closes==0);
                Reject(()=>driver.RunAndClose(app,new Clock(),p));Reject(()=>driver.RetryClose());driver.RequestStop();
            };
            var status=driver.RunAndClose(app,new Clock(),p);Check(status.End==MeasurementRunEnd.Requested&&status.CompletedFrames==1&&p.Closes==1);
        });
    }
}
