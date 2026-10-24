using Ncma.Application;
internal static class PresentationStageTests
{
    internal static void Run()
    {
        int count=0;
        void Check(bool ok){count++;if(!ok)throw new Exception("Stage assertion "+count);}
        void Reject(Action action){bool rejected=false;try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){rejected=true;}Check(rejected);}
        Reject(()=>new PresentationStageCapture(0));Reject(()=>new PresentationStageCapture(65));
        var capture=new PresentationStageCapture(2);
        Reject(()=>capture.Phase(PresentationStage.GuiDraw));Reject(()=>capture.End(true));
        Reject(()=>capture.Begin(0,PresentationCall.Present));Reject(()=>capture.Begin(1,(PresentationCall)8));
        capture.Begin(1,PresentationCall.BeginPresentation);
        Reject(()=>capture.Begin(2,PresentationCall.Present));Reject(()=>capture.Capture());
        Reject(()=>capture.Phase((PresentationStage)99));
        capture.Phase(PresentationStage.GuiBegin);capture.Phase(PresentationStage.Control);capture.End(true);
        capture.Begin(1,PresentationCall.Present,true);
        capture.Phase(PresentationStage.GuiDraw);
        byte[] allocation=new byte[2048];GC.KeepAlive(allocation);capture.End(false);
        var report=capture.Capture();
        Check(report.Version==1&&report.TotalCalls==2&&report.CompleteRawCoverage&&!report.PerformanceAccepted);
        Check(report.Calls[1].DiagnosticCaptureRequested&&!report.Calls[1].Completed&&report.Calls[1].OwnerAllocatedBytes>=2048);
        Check(report.Calls[0].Stages[0].Visits==2&&report.Calls[0].Stages[3].Visits==1);
        foreach(var row in report.Calls) {
            Check(row.Stages.Sum(s=>s.WallTicks)==row.EndedTimestamp-row.StartedTimestamp);
            Check(row.Stages.Sum(s=>s.OwnerAllocatedBytes)==row.OwnerAllocatedBytes);
            Check(row.Stages.Select(s=>(int)s.Stage).SequenceEqual(Enumerable.Range(0,13)));
            Check(row.Stages.Where(s=>s.Visits==0).All(s=>s.WallTicks==0&&s.OwnerAllocatedBytes==0));
        }
        report.Calls[0].Stages[0]=default;report.Calls[1]=default;
        Check(capture.Capture().Calls[0].Stages[0].Visits==2&&capture.Capture().Calls[1].FrameHint==1);
        capture.Begin(1,PresentationCall.Present);capture.End(true); // repeated hint is not a unique GPU frame
        Check(capture.Capture() is{TotalCalls:3,OmittedCalls:1,CompleteRawCoverage:false});
        Check(Task.Run(()=>{try{capture.Begin(2,PresentationCall.Present);return false;}catch(InvalidOperationException){return true;}}).Result);
        Check(Task.Run(()=>{try{capture.Capture();return false;}catch(InvalidOperationException){return true;}}).Result);
        var warm=new PresentationStageCapture();
        for(int i=0;i<128;i++){warm.Begin(1,PresentationCall.Present);warm.Phase(PresentationStage.NativePresent);warm.End(true);}
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<256;i++){warm.Begin(1,PresentationCall.Present);warm.Phase(PresentationStage.NativePresent);warm.End(true);}
        Check(GC.GetAllocatedBytesForCurrentThread()==before);
        Check(warm.Capture() is{TotalCalls:384,OmittedCalls:320});
        Console.WriteLine($"PASS E5.D presentation stages {count} checks");
    }
}
