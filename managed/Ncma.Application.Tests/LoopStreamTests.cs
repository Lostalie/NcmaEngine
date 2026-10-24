using System.Text;
using System.Text.Json;
using Ncma.Application;

internal static class LoopStreamTests
{
    internal static void Run()
    {
        int checks=0;
        void Check(bool value) { checks++;if(!value)throw new Exception("Loop stream assertion "+checks); }
        void Reject(Action action) {try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){checks++;return;}throw new Exception("Expected stream rejection");}
        static void Frames(LoopCostCapture capture,int count) {
            for(int i=0;i<count;i++) {
                capture.Begin();capture.Phase(LoopCostPhase.Presentation);
                if(i%3!=0)capture.PresentReturned();
                capture.End((ulong)i,i%7==0?LoopCostOutcome.Faulted:LoopCostOutcome.Completed);
            }
        }
        using var bytes=new MemoryStream();
        var stream=new LoopCostStream(bytes,4096,1000);
        var costs=new LoopCostCapture(stream:stream);
        Reject(()=>new LoopCostCapture(stream:stream));
        Frames(costs,800);
        Check(!stream.Snapshot().CompleteRawCoverage);
        var end=stream.Complete();
        Check(end is {Status:"completed",Produced:800,Accepted:800,Written:800,Unwritten:0,CompleteRawCoverage:true,TargetPerformanceAccepted:false});
        Check(costs.Capture() is {TotalIterations:800,OmittedIterations:288,Stream.CompleteRawCoverage:true});
        Check(end.PublishTicks>=0&&end.PublishAllocatedBytes>=0&&end.WriterWallTicks>0&&end.WriterAllocatedBytes>0);
        string[] lines=Encoding.UTF8.GetString(bytes.ToArray()).Split('\n',StringSplitOptions.RemoveEmptyEntries);
        Check(lines.Length==802&&end.Bytes==bytes.ToArray().Length);
        using(var header=JsonDocument.Parse(lines[0])) {
            Check(header.RootElement.GetProperty("kind").GetString()=="header");
            Check(header.RootElement.GetProperty("runId").GetGuid()==end.RunId);
        }
        var prefix=costs.Capture().Samples;
        for(int i=0;i<800;i++) {
            using var row=JsonDocument.Parse(lines[i+1]);
            var sample=row.RootElement.GetProperty("sample");
            Check(sample.GetProperty("Sequence").GetUInt64()==(ulong)i+1);
            if(i<512)Check(JsonSerializer.Deserialize<LoopCostSample>(sample)==prefix[i]);
        }
        using(var footer=JsonDocument.Parse(lines[^1])) {
            Check(footer.RootElement.GetProperty("kind").GetString()=="sealed");
            Check(footer.RootElement.GetProperty("report").GetProperty("Written").GetUInt64()==800);
        }
        Reject(()=>{costs.Begin();costs.End(0,LoopCostOutcome.Completed);});
        Check(Task.Run(()=>{try{stream.Snapshot();return false;}catch(InvalidOperationException){return true;}}).Result);
        foreach(bool budget in new[]{false,true}) {
            using var output=new GateStream();
            var pressure=new LoopCostStream(output,budget?4096:2,budget?3UL:100UL);
            Check(output.Entered.Wait(2000));
            var capture=new LoopCostCapture(stream:pressure);Frames(capture,10);
            output.Release.Set();
            var report=pressure.Complete();
            Check(report.Status=="completed"&&!report.CompleteRawCoverage);
            Check(report.Produced==10&&report.Written==(budget?3UL:2UL));
            Check(report.QueueDrops==(budget?0UL:8UL)&&report.BudgetDrops==(budget?7UL:0UL));
            Check(report.Accepted+report.QueueDrops+report.BudgetDrops+report.FailureDrops==report.Produced);
        }
        using(var output=new GateStream()) {
            var slow=new LoopCostStream(output,2,10);
            Check(output.Entered.Wait(2000));
            var capture=new LoopCostCapture(stream:slow);Frames(capture,1);
            Check(slow.Complete(TimeSpan.FromMilliseconds(10)) is {Status:"close_timeout",CompleteRawCoverage:false});
            Check(!output.Disposed);
            output.Release.Set();
            Check(slow.Complete() is {Status:"close_timeout",Written:1,CompleteRawCoverage:false});
            Check(output.Disposed);
        }
        using(var output=new FailStream(false)) {
            var failed=new LoopCostStream(output,4,10);
            Check(output.Attempted.Wait(2000));
            Check(SpinWait.SpinUntil(()=>failed.Snapshot().Failure is not null,2000));
            var capture=new LoopCostCapture(stream:failed);Frames(capture,3);
            Check(failed.Complete() is {Status:"failed",Produced:3,Written:0,FailureDrops:3,CompleteRawCoverage:false});
        }
        var closeFailure=new LoopCostStream(new FailStream(true),4,10);
        Check(closeFailure.Complete() is {Status:"failed",Failure:"loop_stream_close_failed",CompleteRawCoverage:false});
        string directory=Path.Combine(Path.GetTempPath(),"ncma-loop-stream-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);string path=Path.Combine(directory,"evidence.ncloop");
        Reject(()=>LoopCostStream.ValidateTarget("relative.ncloop"));
        Reject(()=>LoopCostStream.ValidateTarget(Path.Combine(directory,"absent","evidence.ncloop")));
        Reject(()=>LoopCostStream.ValidateTarget(Path.Combine(directory,"wrong.json")));
        var file=LoopCostStream.Open(path);
        Check(file.Complete() is {Status:"completed",Written:0,CompleteRawCoverage:true});
        byte[] original=File.ReadAllBytes(path);
        Reject(()=>LoopCostStream.Open(path));
        Check(original.SequenceEqual(File.ReadAllBytes(path)));
        Console.WriteLine($"PASS E5.D full loop stream {checks} checks; 800 ordered raw rows beyond prefix; bounded pressure/budget; IO/close failure; sticky timeout; no target performance acceptance");
    }
    private sealed class GateStream : MemoryStream
    {
        internal readonly ManualResetEventSlim Entered=new(false),Release=new(false);
        internal bool Disposed;
        public override void Write(byte[] buffer,int offset,int count) {Entered.Set();if(!Release.Wait(5000))throw new IOException("test gate timeout");base.Write(buffer,offset,count);}
        public override void Write(ReadOnlySpan<byte> buffer) {Entered.Set();if(!Release.Wait(5000))throw new IOException("test gate timeout");base.Write(buffer);}
        protected override void Dispose(bool disposing){Disposed=true;base.Dispose(disposing);}
    }
    private sealed class FailStream(bool onClose) : MemoryStream
    {
        internal readonly ManualResetEventSlim Attempted=new(false);
        public override void Write(byte[] buffer,int offset,int count){Attempted.Set();if(!onClose)throw new IOException("test write failure");base.Write(buffer,offset,count);}
        public override void Write(ReadOnlySpan<byte> buffer){Attempted.Set();if(!onClose)throw new IOException("test write failure");base.Write(buffer);}
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(onClose)throw new IOException("test close failure");}
    }
}
