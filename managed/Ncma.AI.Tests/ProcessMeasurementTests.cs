using System.Text;
using Ncma.Application;

internal static class ProcessMeasurementTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Process measurement assertion"); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; } throw new Exception("Must reject process measurement"); }
    private sealed class Clock : TimeProvider, IFrameClock
    {
        internal long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
        public double Seconds => Now / 1000d;
    }
    private sealed class Pipeline(Clock clock) : IFramePipeline
    {
        internal readonly List<string> Calls = [];
        internal bool Stop, Fail; internal Action? DuringPresent;
        public bool PumpPlatform() { Calls.Add("platform"); return !Stop; }
        public void ApplyEditorIntents(FrameContext f) => Calls.Add("intents");
        public void PumpAgent(FrameContext f) => Calls.Add("agent");
        public void BeginPresentation(FrameContext f) => Calls.Add("begin");
        public void SubmitInput(FrameContext f) => Calls.Add("input");
        public void AdvancePlay(FrameContext f) => Calls.Add("play");
        public void Present(FrameContext f) { Calls.Add("present"); DuringPresent?.Invoke(); clock.Now += 1000; if (Fail) throw Failure; }
        internal readonly Exception Failure = new InvalidOperationException("ORIGINAL_BUSINESS_FAILURE");
    }
    private static ProcessMeasurementIdentity Identity() => new(Guid.NewGuid(), new('A',64), new('B',64), new('C',64), new('D',64), MeasurementConfiguration.Release, MeasurementVariant.AiExtensions, MeasurementHost.Headless);
    private static ProcessSeriesCounters Good() => new(1,2,3,4,5,6,7,8,9,10,new(0,0,0),new(0,0,0),ProcessSeriesFailure.None);
    private static ProcessMeasurement Create(ProcessMeasurementIdentity id, Clock clock, Func<ProcessSeriesCounters>? read = null)
        => new(id,TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(10),16,clock,read ?? Good);
    private static ProcessMeasurementReport Report()
    {
        var clock=new Clock(); var m=Create(Identity(),clock); using var app=new ApplicationLifetime(); app.Start([]);
        app.Run(clock,new Pipeline(clock),2,measurement:m); return m.Seal();
    }
    internal static IEnumerable<(string,Action)> Cases()
    {
        yield return ("M8.7-E2C2 identity plan is explicit labels not provenance",()=>{
            var id=Identity(); var clock=new Clock(); int reads=0;
            _=Create(id,clock,()=>{reads++;return Good();}); Check(reads==0);
            foreach(var invalid in new[]{id with{RunId=Guid.Empty},id with{SourceSha256="path/secret"},id with{ProductManifestSha256=new('c',64)},id with{Host=(MeasurementHost)99}}) Reject(()=>Create(invalid,clock));
            var report=Create(id,clock).Seal(); Check(!report.BoundToLoop&&report.Iterations==0&&report.Series.Samples.Length==0);
            _=ProcessMeasurementEvidence.DecodeExpected(ProcessMeasurementEvidence.Encode(report),id);
        });
        yield return ("M8.7-E2C2 original loop order default off explicit post iteration sampling",()=>{
            var clock=new Clock(); var pipeline=new Pipeline(clock); int reads=0;
            using var app=new ApplicationLifetime(); app.Start([]); app.Run(clock,pipeline,1);
            var m=Create(Identity(),clock,()=>{Check(pipeline.Calls[^1]=="present");reads++;return Good();});
            Check(app.Run(clock,pipeline,2,measurement:m)==2&&reads==2&&app.LastFrameId==3);
            var report=m.Seal(); Check(report.Iterations==2&&report.CompletedIterations==2&&report.Series.MissingDueSlots==1);
            Check(pipeline.Calls.SequenceEqual(Enumerable.Range(0,3).SelectMany(_=>new[]{"platform","intents","agent","begin","input","play","present"})));
            _=ProcessMeasurementEvidence.Decode(ProcessMeasurementEvidence.Encode(report));
        });
        yield return ("M8.7-E2C2 stopped and failed iterations never replace original lifecycle result",()=>{
            foreach(bool fail in new[]{false,true}) {
                var clock=new Clock(); var pipeline=new Pipeline(clock){Stop=!fail,Fail=fail};var m=Create(Identity(),clock,()=>throw new IOException("SECRET_COUNTER"));
                using var app=new ApplicationLifetime();app.Start([]);
                try { Check(app.Run(clock,pipeline,1,measurement:m)==0&&!fail); }
                catch(Exception error) {Check(fail&&ReferenceEquals(error,pipeline.Failure));}
                var report=m.Seal();Check(report.Iterations==1&&report.StoppedIterations==(fail?0:1)&&report.FaultedIterations==(fail?1:0)&&!report.Series.AllCountersAvailable);
                Check(app.State==(fail?ApplicationState.Failed:ApplicationState.Running));
                string bytes=Encoding.UTF8.GetString(ProcessMeasurementEvidence.Encode(report));Check(!bytes.Contains("SECRET")&&!bytes.Contains("ORIGINAL_BUSINESS"));
            }
        });
        yield return ("M8.7-E2C2 one lifetime pipeline owner and seal boundaries",()=>{
            var clock=new Clock();var pipeline=new Pipeline(clock);var m=Create(Identity(),clock);using var app=new ApplicationLifetime();app.Start([]);
            pipeline.DuringPresent=()=>Reject(()=>m.Seal());app.Run(clock,pipeline,1,measurement:m);
            Task.Run(()=>Reject(()=>m.Seal())).GetAwaiter().GetResult();
            using var other=new ApplicationLifetime();other.Start([]);Reject(()=>other.Run(clock,pipeline,1,measurement:m));Check(other.State==ApplicationState.Running);
            Reject(()=>app.Run(clock,new Pipeline(clock),1,measurement:m));Check(app.State==ApplicationState.Running);
            app.Run(clock,pipeline,1,measurement:m);var report=m.Seal();Check(report.CompletedIterations==2);
            Reject(()=>m.Seal());Reject(()=>app.Run(clock,pipeline,1,measurement:m));Check(app.State==ApplicationState.Running);
        });
        yield return ("M8.7-E2C2 strict raw and envelope roundtrip binds expected labels",()=>{
            var report=Report();byte[] bytes=ProcessMeasurementEvidence.Encode(report);
            var copy=ProcessMeasurementEvidence.DecodeExpected(bytes,report.Identity);Check(copy.Iterations==2&&!copy.MemoryAccepted&&!copy.PerformanceAccepted);
            foreach(var mismatch in new[]{report.Identity with{RunId=Guid.NewGuid()},report.Identity with{SourceSha256=new('E',64)},report.Identity with{CaptureManifestSha256=new('E',64)},report.Identity with{ProductManifestSha256=new('E',64)},report.Identity with{WorkloadSha256=new('E',64)},report.Identity with{Configuration=MeasurementConfiguration.Debug},report.Identity with{Variant=MeasurementVariant.Default},report.Identity with{Host=MeasurementHost.Editor}})
                Reject(()=>ProcessMeasurementEvidence.DecodeExpected(bytes,mismatch));
            byte[] raw=ProcessSeriesEvidence.Encode(report.Series);Check(raw.SequenceEqual(ProcessSeriesEvidence.Encode(ProcessSeriesEvidence.Decode(raw))));
        });
        yield return ("M8.7-E2C2 reject forged totals identities and derived claims",()=>{
            var report=Report();Reject(()=>ProcessMeasurementEvidence.Encode(report with{Iterations=3}));Reject(()=>ProcessMeasurementEvidence.Encode(report with{BoundToLoop=false}));
            Reject(()=>ProcessMeasurementEvidence.Encode(report with{ObserverFailures=2}));Reject(()=>ProcessMeasurementEvidence.Encode(report with{Identity=report.Identity with{RunId=Guid.NewGuid()}}));
            string json=Encoding.UTF8.GetString(ProcessMeasurementEvidence.Encode(report));
            string allocation=report.Series.Samples[0].ObserverAllocationValid?"true":"false", inverted=allocation=="true"?"false":"true";
            foreach(string bad in new[]{json.Replace("\"PerformanceAccepted\":false","\"PerformanceAccepted\":true"),json.Replace("\"Version\":1","\"Version\":1,\"Version\":1"),json.Replace("\"Iterations\":2","\"Iterations\":2,\"Unknown\":0"),json+" ",json.Replace("\"ObserverAllocationValid\":"+allocation,"\"ObserverAllocationValid\":"+inverted)}) Reject(()=>ProcessMeasurementEvidence.Decode(Encoding.UTF8.GetBytes(bad)));
        });
        yield return ("M8.7-E2C2 byte shape scalar duplicate array and nesting budgets",()=>{
            foreach(string bad in new[]{"", "null", "{}", "{\"Samples\":[1]}", "{\"Samples\":[],\"Samples\":[]}", "{\"Samples\":[],\"X\":\""+new string('x',1025)+"\"}", "{\"Samples\":["+string.Join(',',Enumerable.Repeat("{}",8193))+"]}","[[[[[[[[[[]]]]]]]]]]"}) Reject(()=>ProcessSeriesEvidence.Decode(Encoding.UTF8.GetBytes(bad)));
            var report=Report();byte[] bytes=ProcessMeasurementEvidence.Encode(report);
            Reject(()=>ProcessMeasurementEvidence.Decode(bytes,bytes.Length-1));Reject(()=>ProcessMeasurementEvidence.Encode(report,1));
            Reject(()=>ProcessMeasurementEvidence.Decode(bytes,ProcessSeriesEvidence.MaximumBytes+1));
        });
        yield return ("M8.7-E2C2 diagnostics outside loop cost no close or business stop authority",()=>{
            var clock=new Clock();var pipeline=new Pipeline(clock);var m=Create(Identity(),clock);using var app=new ApplicationLifetime();app.Start([]);
            var costs=new LoopCostCapture();app.Run(clock,pipeline,1,costs,measurement:m);var report=m.Seal();
            app.Run(clock,pipeline,1);Check(app.State==ApplicationState.Running&&app.LastFrameId==2&&report.Iterations==1);
            Check(report.Series.Samples.Length==1&&!report.Series.CompleteCadenceCoverage);_ = ProcessMeasurementEvidence.Encode(report);
        });
    }
}
