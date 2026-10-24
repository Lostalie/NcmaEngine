using Ncma.Application;

internal static class FramePacerTests
{
    internal static void Run()
    {
        int count=0;
        void Check(bool ok){count++;if(!ok)throw new Exception("Pacing assertion "+count);}
        void Reject(Action action){bool rejected=false;try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){rejected=true;}Check(rejected);}
        Reject(()=>new FramePacer(0));Reject(()=>new FramePacer(241));
        Reject(()=>new FramePacer(30,(Action<uint>)null!));
        uint requested=0;
        var precision=new FramePacer(1,(uint us)=>{requested=us;throw new InvalidOperationException("wait failure");});
        Check(precision.Wait());
        long? original=precision.LastAdmissionTimestamp;
        Reject(()=>precision.Wait());
        Check(requested is >=1 and <=8000&&precision.LastAdmissionTimestamp==original);
        Check(!precision.Wait(()=>true)); // failed wait clears reentry guard, no admission.
        Reject(()=>new FramePacer(30,new Clock{Rate=0}));
        var clock=new Clock();var pacer=new FramePacer(30,clock);
        Check(pacer.MinimumIntervalTicks==34&&pacer.LastAdmissionTimestamp is null);
        Check(pacer.Wait()&&clock.Waits==0&&pacer.LastAdmissionTimestamp==0);
        Check(pacer.Wait()&&clock.Now==34&&pacer.LastAdmissionTimestamp==34);
        clock.Now=1000;int waits=clock.Waits;
        Check(pacer.Wait()&&clock.Waits==waits&&pacer.LastAdmissionTimestamp==1000);
        Check(pacer.Wait()&&clock.Now==1034); // no catch-up burst after a stall
        Check(!pacer.Wait(()=>true)&&pacer.LastAdmissionTimestamp==1034);
        Check(pacer.Wait()&&clock.Now==1068); // cancellation does not consume an admission
        int polls=0;Check(!pacer.Wait(()=>++polls==2)&&pacer.LastAdmissionTimestamp==1068);
        Check(pacer.Wait()&&pacer.LastAdmissionTimestamp>=1102);
        Check(Task.Run(()=>{try{pacer.Wait();return false;}catch(InvalidOperationException){return true;}}).Result);
        Reject(()=>pacer.Wait(()=>{try{pacer.Wait();}catch(InvalidOperationException){}return false;}));
        clock.Now=-1;Reject(()=>pacer.Wait());
        var backwards=new Clock{Now=50};var invalid=new FramePacer(30,backwards);invalid.Wait();
        backwards.Now=49;Reject(()=>invalid.Wait());
        var edge=new FramePacer(1,new Clock{Rate=long.MaxValue,Now=long.MaxValue});
        Check(edge.MinimumIntervalTicks==long.MaxValue&&edge.Wait());

        // Same synthetic simulation clock/deltas with and without actual pacing policy.
        // Neither the pacer nor LoopCostCapture is allowed to overwrite FrameContext delta.
        using(var app=new ApplicationLifetime()){
            app.Start([]);var pipeline=new Pipeline();var time=new SimulationClock();
            var fake=new FramePacer(30,new Clock());
            Check(app.Run(time,pipeline,3,pacer:fake)==3&&pipeline.Deltas.SequenceEqual(new[]{.125,.125,.125}));
        }
        var observer=new LoopCostCapture(frameRateLimit:30);
        using(var app=new ApplicationLifetime()){
            app.Start([]);var pipeline=new Pipeline();
            Reject(()=>app.Run(new SimulationClock(),pipeline,1,observer));
            Check(app.State==ApplicationState.Running&&app.LastFrameId==0);
            Check(app.Run(new SimulationClock(),pipeline,3,observer,new FramePacer(30))==3);
        }
        var report=observer.Capture();
        Check(report.Version==2&&report.FrameRateLimit==30&&report.Samples.All(s=>s.PacingAdmissionTimestamp is not null&&s.Phases.Pacing>=0));
        long interval=1+(report.TimestampFrequency-1)/30;
        for(int i=1;i<report.Samples.Length;i++)
            Check(report.Samples[i].PacingAdmissionTimestamp-report.Samples[i-1].PacingAdmissionTimestamp>=interval);
        Reject(()=>new LoopCostCapture(frameRateLimit:0));
        var disabled=new LoopCostCapture();disabled.Begin();
        Reject(()=>disabled.PacingAdmitted(0));disabled.End(0,LoopCostOutcome.Completed);
        Check(disabled.Capture().Samples.Single().PacingAdmissionTimestamp is null);
        // Warm admission itself is allocation-free; this says nothing about the entire host.
        var allocationClock=new Clock();var allocationPacer=new FramePacer(30,allocationClock);
        for(int i=0;i<128;i++)allocationPacer.Wait();
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<256;i++)allocationPacer.Wait();
        Check(GC.GetAllocatedBytesForCurrentThread()==before);
        Console.WriteLine($"PASS E5.D frame pacing {count} checks");
    }
    private sealed class Clock:IFramePacingClock
    {
        internal long Now,Rate=1000;internal int Waits;
        public long Frequency=>Rate;
        public long Timestamp=>Now;
        public void WaitSlice(long remainingTicks){Now+=Math.Min(8,remainingTicks);Waits++;}
    }
    private sealed class SimulationClock:IFrameClock
    {
        private double _time;public double Seconds=>_time+=.125;
    }
    private sealed class Pipeline:IFramePipeline
    {
        internal readonly List<double> Deltas=[];
        public bool PumpPlatform()=>true;
        public void ApplyEditorIntents(FrameContext f){}
        public void PumpAgent(FrameContext f){}
        public void BeginPresentation(FrameContext f){}
        public void SubmitInput(FrameContext f){}
        public void AdvancePlay(FrameContext f){Deltas.Add(f.DeltaSeconds);}
        public void Present(FrameContext f){}
    }
}
