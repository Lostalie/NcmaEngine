using System.Diagnostics;
using Ncma.Application;

internal static class LoopCostTests
{
    internal static void Run()
    {
        int checks=0;
        void Check(bool ok) { checks++;if(!ok)throw new Exception("Loop cost assertion "+checks); }
        void Reject(Action action) { bool rejected=false;try{action();}catch(Exception e)when(e is InvalidOperationException or ArgumentException){rejected=true;}Check(rejected); }
        Reject(()=>new LoopCostCapture(0));Reject(()=>new LoopCostCapture(513));
        var costs=new LoopCostCapture(2);
        Reject(()=>costs.End(0,LoopCostOutcome.Completed));Reject(costs.PresentReturned);
        costs.Begin();Reject(costs.Begin);Reject(()=>costs.Capture());
        Reject(()=>costs.Phase((LoopCostPhase)99));Reject(()=>costs.End(0,(LoopCostOutcome)99));
        costs.Phase(LoopCostPhase.Presentation);costs.PresentReturned();Reject(costs.PresentReturned);
        costs.End(1,LoopCostOutcome.Completed);
        costs.Begin();costs.Phase(LoopCostPhase.Recovery);costs.End(2,LoopCostOutcome.Recovered);
        costs.Begin();costs.PresentReturned();costs.End(3,LoopCostOutcome.Faulted);
        var report=costs.Capture();
        Check(report.TotalIterations==3&&report.OmittedIterations==1&&!report.CompleteRawCoverage&&report.Samples.Length==2);
        Check(report.PresentReturns==2&&report.PresentIntervals==1&&report.MaximumPresentIntervalTicks>=0);
        Check(report.Samples[1].PresentReturnTimestamp is null&&report.Samples[1].Outcome==LoopCostOutcome.Recovered);
        Check(report.TimestampFrequency==Stopwatch.Frequency&&!report.TargetPerformanceAccepted);
        report.Samples[0]=default;Check(costs.Capture().Samples[0].Sequence==1);
        Check(Task.Run(()=>{try{costs.Begin();return false;}catch(InvalidOperationException){return true;}}).Result);

        // A synthetic simulation clock must not become the observed wall clock.
        var loop=new LoopCostCapture();
        using(var app=new ApplicationLifetime()) {
            app.Start([]);
            var pipeline=new Pipeline(loop);
            Check(app.Run(new Clock(),pipeline,3,loop)==3);
            pipeline.Open=false;Check(app.Run(new Clock(),pipeline,1,loop)==0);
            var rows=loop.Capture().Samples;
            Check(rows.Length==4&&rows[3].Outcome==LoopCostOutcome.Stopped&&rows[3].FrameId==0);
            Check(rows.Take(3).All(s=>s.Outcome==LoopCostOutcome.Completed&&s.PresentReturnTimestamp is not null));
            Check(loop.Capture().PresentReturns==3&&loop.Capture().PresentIntervals==2);
            Check(pipeline.Delta==10&&rows.All(s=>s.EndedTimestamp>=s.StartedTimestamp));
            foreach(var row in rows) {
                var p=row.Phases;
                Check(p.Control+p.Platform+p.Intents+p.Agent+p.BeginPresentation+p.Input+p.Simulation+p.Presentation+p.Recovery+p.Pacing==row.EndedTimestamp-row.StartedTimestamp);
            }
        }
        foreach(var fault in new[]{"platform","input","simulation","present","after_present"}) {
            using var app=new ApplicationLifetime();app.Start([]);
            var capture=new LoopCostCapture();
            Reject(()=>app.Run(new Clock(),new Pipeline(capture){Fault=fault},1,capture));
            var row=capture.Capture().Samples.Single();
            Check(row.Outcome==LoopCostOutcome.Faulted&&app.State==ApplicationState.Failed);
            Check(capture.Capture().PresentReturns==(fault=="after_present"?1UL:0));
        }
        // Warm capture, including over-capacity observations, allocates no owner-thread objects.
        var bounded=new LoopCostCapture(1);
        for(int i=0;i<128;i++){bounded.Begin();bounded.Phase(LoopCostPhase.Presentation);bounded.PresentReturned();bounded.End(1,LoopCostOutcome.Completed);}
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<256;i++){bounded.Begin();bounded.Phase(LoopCostPhase.Presentation);bounded.PresentReturned();bounded.End(1,LoopCostOutcome.Completed);}
        Check(GC.GetAllocatedBytesForCurrentThread()==before);
        Check(bounded.Capture() is {TotalIterations:384,OmittedIterations:383,PresentReturns:384,PresentIntervals:383});
        Console.WriteLine($"PASS E5.D loop costs {checks} checks");
    }
    private sealed class Clock:IFrameClock {private double _value;public double Seconds=>_value+=10;}
    private sealed class Pipeline(LoopCostCapture capture):IFramePipeline
    {
        public bool Open=true;
        public string? Fault;
        public double Delta;
        private void Fail(string phase){if(Fault==phase)throw new InvalidOperationException("fixture "+phase);}
        public bool PumpPlatform(){Fail("platform");return Open;}
        public void ApplyEditorIntents(FrameContext frame){Delta=frame.DeltaSeconds;}
        public void PumpAgent(FrameContext frame){}
        public void BeginPresentation(FrameContext frame){}
        public void SubmitInput(FrameContext frame){Fail("input");}
        public void AdvancePlay(FrameContext frame){Fail("simulation");}
        public void Present(FrameContext frame){Fail("present");capture.PresentReturned();Fail("after_present");}
    }
}
