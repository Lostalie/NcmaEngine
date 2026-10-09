using Ncma.Animation;

internal static class MontageIntervalWeightTests
{
    private static void Check(bool value,string detail){if(!value)throw new Exception("M6.8-B2b-1 interval envelope: "+detail);}
    private static void Near(double actual,double expected,string detail)=>Check(Math.Abs(actual-expected)<1e-7,detail+": "+actual+" != "+expected);
    private static AnimationMontagePlayback Playback(AnimationMontageDefinition d)=>new(MontageDataTests.Compile(d),new(Guid.NewGuid(),Guid.NewGuid(),0));
    private static void Play(AnimationMontagePlayback p,AnimationMontageDefinition d)=>p.Request(new(Guid.NewGuid(),p.InstanceId,p.Frame.Context,d.Slots[0].Id,MontageRequestKind.Play,Guid.Empty,d.Slots[0].Priority));
    private static void Step(AnimationMontagePlayback p,double dt){var token=p.Prepare(p.Frame.Context,dt);p.Commit(token,p.Frame.Context with{Tick=p.Frame.Context.Tick+1});}
    private static AnimationMontageDefinition Single(double blendIn,double blendOut)
    {var d=MontageDataTests.Definition();return d with{Slots=[d.Slots[0] with{BlendIn=blendIn,BlendOut=blendOut}],Sections=[d.Sections[0] with{Start=0,End=1,NextSection=Guid.Empty}]};}
    public static void Add(List<(string,Action)> tests)
    {
        tests.Add(("M6.8-B2b-1 incoming saturation and overlapping blend ramps integrate analytically",()=>{
            var d=Single(.1,0);var p=Playback(d);Play(p,d);Step(p,.2);var rows=new MontageInterval[528];Check(p.CopyCommittedIntervals(rows)==1,"One section");Near(rows[0].AverageWeight,.75,"Ramp then plateau mean");Near(rows[0].StepFraction,1,"Whole step");
            d=Single(1,1);p=Playback(d);Play(p,d);Step(p,1);p.CopyCommittedIntervals(rows);Near(rows[0].AverageWeight,.25,"Incoming/outgoing triangle");Check(!p.ReadSlot(d.Slots[0].Id).Active,"Triangle terminal inactive");
            d=Single(0,0);p=Playback(d);Play(p,d);Step(p,1);p.CopyCommittedIntervals(rows);Near(rows[0].AverageWeight,1,"Zero windows stay full weight through terminal interval");
        }));
        tests.Add(("M6.8-B2b-1 cross-section envelope retains all traversals and terminal partial-step coverage",()=>{
            var d=MontageDataTests.Definition();var p=Playback(d);Play(p,d);Step(p,.5);var rows=new MontageInterval[528];Check(p.CopyCommittedIntervals(rows)==2,"Cross Section");
            Near(rows[0].AverageWeight,5d/6,"Incoming integral first Section");Near(rows[0].StepFraction,.6,"First Section fraction");Near(rows[1].AverageWeight,1,"Next terminal Section before outgoing fade");Near(rows[1].StepFraction,.4,"Next Section fraction");
            Step(p,.3);Check(p.CopyCommittedIntervals(rows)==1&&!p.ReadSlot(d.Slots[0].Id).Active,"Final interval survives inactive");Near(rows[0].AverageWeight,.5,"Final outgoing mean, not final Slot zero");Near(rows[0].StepFraction,2d/3,"Terminal leftover time is not invented root travel");
        }));
        tests.Add(("M6.8-B2b-1 single-section integral independent of interval subdivision, not general root performance",()=>{
            var d=Single(.4,.4);var whole=Playback(d);Play(whole,d);Step(whole,1);var rows=new MontageInterval[528];whole.CopyCommittedIntervals(rows);double expected=rows[0].AverageWeight;
            var split=Playback(d);Play(split,d);double integral=0;for(int i=0;i<10;i++){Step(split,.1);int n=split.CopyCommittedIntervals(rows);for(int j=0;j<n;j++)integral+=(rows[j].Current-rows[j].Previous)*rows[j].AverageWeight;}
            Near(integral,expected,"Analytic envelope area");Near(expected,.6,"Ramp/plateau/fade area");
        }));
        tests.Add(("M6.8-B2b-1 candidate Abort retains weighted intervals and cancel emits no motion",()=>{
            var d=Single(.4,.4);var p=Playback(d);Play(p,d);Step(p,.1);var rows=new MontageInterval[528];p.CopyCommittedIntervals(rows);var before=rows[0];
            var token=p.Prepare(p.Frame.Context,.1);p.CopyPreparedIntervals(token,rows);Near(rows[0].AverageWeight,.375,"Prepared second interval");p.Abort(token);p.CopyCommittedIntervals(rows);Check(rows[0]==before,"No partial publication");
            p.Request(new(Guid.NewGuid(),p.InstanceId,p.Frame.Context,d.Slots[0].Id,MontageRequestKind.Cancel,Guid.Empty,10));Step(p,.1);Check(p.CopyCommittedIntervals(rows)==0,"Cancel freezes playhead");Near(p.ReadSlot(d.Slots[0].Id).Weight,.1875,"Cancel visual fade distinct from root interval envelope");
        }));
        tests.Add(("M6.8-B2b-1 jump never invents skipped intervals and root-disabled metadata remains explicit",()=>{
            var d=MontageDataTests.Definition();d=d with{Slots=[d.Slots[0] with{RootMotion=false}]};var p=Playback(d);Play(p,d);Step(p,.05);
            p.Request(new(Guid.NewGuid(),p.InstanceId,p.Frame.Context,d.Slots[0].Id,MontageRequestKind.Jump,d.Sections[1].Id,10));Step(p,.05);var rows=new MontageInterval[528];Check(p.CopyCommittedIntervals(rows)==1&&!rows[0].RootMotion,"Exact authored root opt-out");Near(rows[0].Previous,.4,"Jump begins at explicit next Section, no catchup");Near(rows[0].AverageWeight,.75,"Jump preserves incoming elapsed envelope");
        }));
        tests.Add(("M6.8-B2b-1 weighted32-cycle budget and warmed integral execution allocate0",()=>{
            var d=Single(.4,.4);d=d with{Sections=[d.Sections[0] with{End=.001,NextSection=d.Sections[0].Id}]};var p=Playback(d);Play(p,d);Step(p,.032);var rows=new MontageInterval[528];Check(p.CopyCommittedIntervals(rows)==32,"Exactly32 crossings");double coverage=0;for(int i=0;i<32;i++){Near(rows[i].StepFraction,1d/32,"Cycle fraction");coverage+=rows[i].StepFraction*rows[i].AverageWeight;}Near(coverage,.04,"32ms incoming mean");
            for(int i=0;i<32;i++)Step(p,.032);long start=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1024;i++){Step(p,.032);p.CopyCommittedIntervals(rows);}Check(GC.GetAllocatedBytesForCurrentThread()==start,"Stack-only envelope work");
        }));
        tests.Add(("M6.8-B2b-1 all16 Slots preserve528 weighted intervals without relaxing32 boundaries",()=>{
            var d=Single(0,0);Guid clip=d.Sections[0].ClipId;var slots=new AnimationMontageSlot[16];var sections=new AnimationMontageSection[16];
            for(int i=0;i<16;i++){Guid slot=Guid.NewGuid(),section=Guid.NewGuid();slots[i]=new(slot,"Slot"+i,section,10,true,true,0,0);sections[i]=new(section,"Cycle",slot,clip,0,.001,section);}
            d=d with{Slots=slots,Sections=sections};var p=Playback(d);foreach(var slot in slots)p.Request(new(Guid.NewGuid(),p.InstanceId,p.Frame.Context,slot.Id,MontageRequestKind.Play,Guid.Empty,10));
            Step(p,.0325);var rows=new MontageInterval[528];Check(p.CopyCommittedIntervals(rows)==528,"Full bounded output");
            foreach(var slot in slots){var selected=rows.Where(r=>r.SlotId==slot.Id).ToArray();Check(selected.Length==33&&selected.Count(r=>r.Current==.001)==32,"32 full traversals and one partial, not33 crossings");Near(selected.Sum(r=>r.StepFraction),1,"Exact full-step coverage");Check(selected.All(r=>r.AverageWeight==1),"Unit envelope");}
        }));
    }
}
