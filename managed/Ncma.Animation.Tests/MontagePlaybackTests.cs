using Ncma.Animation;
internal static class MontagePlaybackTests
{
    private static void Check(bool v,string detail=""){if(!v)throw new Exception("M6.8-B1 playback: "+detail);}
    private static void Reject(Action a){try{a();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){return;}throw new Exception("Invalid montage playback accepted.");}
    private static AnimationStepContext Context()=>new(Guid.NewGuid(),Guid.NewGuid(),0);
    private static MontageRequest Request(AnimationMontagePlayback p,AnimationMontageDefinition d,MontageRequestKind kind=MontageRequestKind.Play,Guid section=default,int priority=10)=>new(Guid.NewGuid(),p.InstanceId,p.Frame.Context,d.Slots[0].Id,kind,section,priority);
    private static void Step(AnimationMontagePlayback p,double delta=.1){var t=p.Prepare(p.Frame.Context,delta);p.Commit(t,p.Frame.Context with{Tick=p.Frame.Context.Tick+1});}
    public static void Add(List<(string,Action)> tests)
    {
        tests.Add(("M6.8-B1 exact stamped requests, duplicate payload and priority/noninterruptible outcomes",()=>{
            var d=MontageDataTests.Definition();var p=new AnimationMontagePlayback(MontageDataTests.Compile(d),Context());var r=Request(p,d);Check(p.Request(r)&&!p.Request(r));Reject(()=>p.Request(r with{Priority=11}));Reject(()=>p.Request(r with{InstanceId=Guid.NewGuid()}));Reject(()=>p.Request(r with{Context=r.Context with{WorldId=Guid.NewGuid()}}));Step(p);Reject(()=>p.Request(r));
            Check(p.ReadSlot(d.Slots[0].Id).Active&&Math.Abs(p.ReadSlot(d.Slots[0].Id).Time-.2)<1e-10);p.Request(Request(p,d,priority:9));Step(p);var rows=new MontageReceipt[64];Check(p.CopyCommittedReceipts(rows)==1&&rows[0].Outcome==MontageRequestOutcome.PriorityRejected);
            var locked=d with{Slots=[d.Slots[0] with{Interruptible=false}]};var q=new AnimationMontagePlayback(MontageDataTests.Compile(locked),Context());q.Request(Request(q,locked));Step(q);q.Request(Request(q,locked,priority:255));Step(q);q.CopyCommittedReceipts(rows);Check(rows[0].Outcome==MontageRequestOutcome.NonInterruptibleRejected);
        }));
        tests.Add(("M6.8-B1 Prepare/Abort preserves queued requests and committed frames, exact next commit",()=>{
            var d=MontageDataTests.Definition();var p=new AnimationMontagePlayback(MontageDataTests.Compile(d),Context());p.Request(Request(p,d));var t=p.Prepare(p.Frame.Context,.1);Check(!p.Frame.SnapshotValid&&p.PreparedSlot(t,d.Slots[0].Id).Active);Reject(()=>p.Request(Request(p,d)));Reject(()=>p.ReadSlot(d.Slots[0].Id));Reject(()=>p.Commit(t,p.Frame.Context with{Tick=2}));p.Abort(t);Check(!p.ReadSlot(d.Slots[0].Id).Active&&p.Frame.Context.Tick==0);Step(p);Check(p.ReadSlot(d.Slots[0].Id).Active&&p.Frame.Context.Tick==1);Reject(()=>p.Commit(t,p.Frame.Context));
            var before=p.ReadSlot(d.Slots[0].Id);Reject(()=>p.Prepare(p.Frame.Context,double.NaN));Check(!p.Frame.SnapshotValid);Reject(()=>p.CopyCommittedIntervals(new MontageInterval[528]));Step(p);Check(p.Frame.SnapshotValid&&p.Frame.Context.Tick==2&&p.ReadSlot(d.Slots[0].Id).Time>before.Time);
        }));
        tests.Add(("M6.8-B1 section interval partition, terminal quantum retained, jump/cancel blend windows",()=>{
            var d=MontageDataTests.Definition();var p=new AnimationMontagePlayback(MontageDataTests.Compile(d),Context());p.Request(Request(p,d));Step(p,.5);var intervals=new MontageInterval[528];Check(p.CopyCommittedIntervals(intervals)==2);Check(Math.Abs(intervals[0].Previous-.1)<1e-12&&Math.Abs(intervals[0].Current-.4)<1e-12&&Math.Abs(intervals[1].Previous-.4)<1e-12&&Math.Abs(intervals[1].Current-.6)<1e-12);
            Check(Math.Abs(p.ReadSlot(d.Slots[0].Id).Time-.6)<1e-10&&Math.Abs(p.ReadSlot(d.Slots[0].Id).Weight-1)<1e-5);Step(p,.3);Check(!p.ReadSlot(d.Slots[0].Id).Active&&p.CopyCommittedIntervals(intervals)==1&&intervals[0].Current==.8,"Terminal interval must not disappear when Active becomes false");
            p.Request(Request(p,d));Step(p,.05);Check(Math.Abs(p.ReadSlot(d.Slots[0].Id).Weight-.5)<1e-5);p.Request(Request(p,d,MontageRequestKind.Jump,d.Sections[1].Id));Step(p,.05);Check(Math.Abs(p.ReadSlot(d.Slots[0].Id).Time-.45)<1e-10);
            p.Request(Request(p,d,MontageRequestKind.Cancel));Step(p,.1);Check(p.ReadSlot(d.Slots[0].Id).Cancelling&&Math.Abs(p.ReadSlot(d.Slots[0].Id).Weight-.5)<1e-5&&p.CopyCommittedIntervals(intervals)==0);Step(p,.1);Check(!p.ReadSlot(d.Slots[0].Id).Active);
        }));
        tests.Add(("M6.8-B1 tiny cyclic sections enforce32 crossings with no partial publication",()=>{
            var d=MontageDataTests.Definition();var a=d.Sections[0] with{Start=0,End=.001,NextSection=d.Sections[0].Id};d=d with{Sections=[a],Slots=[d.Slots[0] with{BlendIn=0,BlendOut=0}]};var p=new AnimationMontagePlayback(MontageDataTests.Compile(d),Context());p.Request(Request(p,d));Step(p,.032);var intervals=new MontageInterval[528];Check(p.CopyCommittedIntervals(intervals)==32&&p.Frame.Context.Tick==1);
            Reject(()=>p.Prepare(p.Frame.Context,.033));Check(p.Frame.Context.Tick==1&&!p.Frame.SnapshotValid);Reject(()=>p.CopyCommittedIntervals(intervals));Step(p,.001);Check(p.Frame.Context.Tick==2&&p.CopyCommittedIntervals(intervals)==1);
        }));
        tests.Add(("M6.8-B1 owner isolation, bounded recent IDs without a256 attack lifetime limit and warm allocation0",()=>{
            var d=MontageDataTests.Definition();var p=new AnimationMontagePlayback(MontageDataTests.Compile(d),Context());var first=Request(p,d);p.Request(first);Step(p);for(int i=0;i<1024;i++){p.Request(Request(p,d));Step(p);}Reject(()=>p.Request(first));
            Check(Task.Run(()=>{try{_=p.Frame;return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult());var q=new AnimationMontagePlayback(MontageDataTests.Compile(d),Context());Check(!q.ReadSlot(d.Slots[0].Id).Active&&q.InstanceId!=p.InstanceId);
            long bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1024;i++){p.Request(Request(p,d));Step(p);}Check(GC.GetAllocatedBytesForCurrentThread()==bytes,"Warmed requests and candidates allocate0");
        }));
        tests.Add(("M6.8-B1 queue/receipt/output limits validate whole requests and retain successful step",()=>{
            var d=MontageDataTests.Definition();var p=new AnimationMontagePlayback(MontageDataTests.Compile(d),Context());for(int i=0;i<64;i++)p.Request(Request(p,d));Reject(()=>p.Request(Request(p,d)));Reject(()=>p.Request(Request(p,d,MontageRequestKind.Jump,Guid.NewGuid())));Step(p);Check(p.Frame.ReceiptCount==64);Reject(()=>p.CopyCommittedReceipts(new MontageReceipt[63]));
            var r=Request(p,d);p.Request(r);var t=p.Prepare(p.Frame.Context,.1);Reject(()=>p.CopyPreparedIntervals(t,new MontageInterval[0]));p.Abort(t);
        }));
    }
}
