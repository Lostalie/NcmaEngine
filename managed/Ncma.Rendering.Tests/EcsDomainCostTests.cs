using System.Text.Json;
using Ncma.Rendering;

internal static class EcsDomainCostTests
{
    internal static void Run()
    {
        int checks=0;
        void Check(bool ok){if(!ok)throw new Exception("GPU snapshot ledger assertion");checks++;}
        void Reject(Action action){try{action();}catch(InvalidOperationException){checks++;return;}throw new Exception("Expected GPU snapshot rejection");}
        var frames=new HashSet<ulong>{10,11,12,13};
        static AttributedGpuCost Slot(ulong frame,GpuTimingDomain domain,ulong seq=0,ulong origin=0,GpuSampleState state=GpuSampleState.Unavailable)=>
            new(new(new(2,seq,origin,frame,0,state,domain,state==GpuSampleState.Valid?2:0),0,0),
                state!=GpuSampleState.Valid?CostSampleAttribution.NoValidSample:origin==frame?CostSampleAttribution.CurrentSourceFrame:CostSampleAttribution.EarlierSourceFrame);
        static PresentationCostReport Report(ulong frame,ulong seq,ulong origin,GpuSampleState state,ulong resolved,ulong resolvedOrigin,GpuSampleState resolvedState)
        {
            var skin=Slot(frame,GpuTimingDomain.SkinDispatch);var ui=Slot(frame,GpuTimingDomain.UiProduction);
            return new(2,frame,true,false,1,0,default,Slot(frame,GpuTimingDomain.MainSubmissionBeforePresent,seq,origin,state),skin,ui,
                Slot(frame,GpuTimingDomain.MainSubmissionBeforePresent,resolved,resolvedOrigin,resolvedState),skin,ui);
        }
        var baseline=Report(9,0,0,GpuSampleState.Unavailable,0,0,GpuSampleState.Unavailable);
        var first=Report(10,2,10,GpuSampleState.Pending,1,9,GpuSampleState.Valid);
        EcsGpuSnapshotLedger New()=>new(baseline,frames);
        var ledger=New();ledger.Add(first);
        ledger.Add(Report(11,4,11,GpuSampleState.Pending,3,10,GpuSampleState.Invalid));
        ledger.Add(Report(12,4,11,GpuSampleState.Failed,3,10,GpuSampleState.Invalid));
        ledger.Add(Report(13,5,13,GpuSampleState.Valid,5,13,GpuSampleState.Valid));
        using(var json=JsonDocument.Parse(JsonSerializer.Serialize(ledger.Summary()))){
            var root=json.RootElement;var d=root.GetProperty("domains")[0];
            Check(root.GetProperty("resolved").GetArrayLength()==3&&root.GetProperty("duplicateResolvedObservations").GetInt32()==1);
            Check(d.GetProperty("uniqueMeasuredValidSources").GetInt32()==1&&d.GetProperty("uniqueOutsideWindowValidSources").GetInt32()==1&&d.GetProperty("uniqueInvalidResolvedSources").GetInt32()==1);
            Check(d.GetProperty("admissionSequenceGaps").GetInt32()==2&&d.GetProperty("unobservedResolutionSequenceSlots").GetInt32()==2);
            Check(d.GetProperty("unobservedTailSequenceSlots").GetInt32()==0&&!root.GetProperty("completeGpuCoverage").GetBoolean());
            Check(d.GetProperty("currentStates")[2].GetInt32()==2&&d.GetProperty("currentStates")[6].GetInt32()==1);
        }
        Reject(()=>New().Add(first with{Main=null}));
        Reject(()=>New().Add(first with{MainResolved=null}));
        Reject(()=>New().Add(first with{Generation=3}));
        Reject(()=>New().Add(first with{Completed=false}));
        Reject(()=>New().Add(first with{HostFrame=9}));
        var resolved=first.MainResolved!.Value;
        foreach(var broken in new[]{resolved.Sample.Timing with{Generation=3},resolved.Sample.Timing with{Domain=GpuTimingDomain.SkinDispatch},
            resolved.Sample.Timing with{OriginFrame=11},resolved.Sample.Timing with{ObservedFrame=11},
            resolved.Sample.Timing with{Milliseconds=double.NaN},resolved.Sample.Timing with{Milliseconds=-1},
            resolved.Sample.Timing with{State=GpuSampleState.Pending,Milliseconds=0},resolved.Sample.Timing with{Sequence=0},
            resolved.Sample.Timing with{ObservedFrame=8},resolved.Sample.Timing with{State=(GpuSampleState)99},
            resolved.Sample.Timing with{Sequence=3},resolved.Sample.Timing with{SkippedSamples=1}})
            Reject(()=>New().Add(first with{MainResolved=resolved with{Sample=resolved.Sample with{Timing=broken}}}));
        Reject(()=>New().Add(first with{MainResolved=resolved with{Attribution=CostSampleAttribution.CurrentSourceFrame}}));
        Reject(()=>New().Add(first with{MainResolved=resolved with{Sample=resolved.Sample with{Resource=1}}}));
        var ui=first.Ui!.Value;
        Reject(()=>New().Add(first with{Ui=ui with{Sample=ui.Sample with{Resource=1}}}));
        Reject(()=>New().Add(first with{MainResolved=resolved with{Sample=resolved.Sample with{Timing=resolved.Sample.Timing with{Sequence=2}}}}));
        var regression=New();regression.Add(first);
        Reject(()=>regression.Add(first));
        Reject(()=>regression.Add(Report(11,1,10,GpuSampleState.Pending,0,0,GpuSampleState.Unavailable)));
        Reject(()=>regression.Add(Report(11,2,9,GpuSampleState.Pending,1,9,GpuSampleState.Valid)));
        var repeated=Report(11,2,10,GpuSampleState.Pending,1,9,GpuSampleState.Valid);
        var changed=repeated.MainResolved!.Value;
        Reject(()=>regression.Add(repeated with{MainResolved=changed with{Sample=changed.Sample with{Timing=changed.Sample.Timing with{Milliseconds=3}}}}));
        regression.Add(repeated);
        using(var json=JsonDocument.Parse(JsonSerializer.Serialize(regression.Summary()))){
            Check(json.RootElement.GetProperty("duplicateResolvedObservations").GetInt32()==1);
            Check(json.RootElement.GetProperty("domains")[0].GetProperty("unobservedTailSequenceSlots").GetInt32()==1);
        }
        var warm=Report(9,1,8,GpuSampleState.Valid,1,8,GpuSampleState.Valid);
        var warmLedger=new EcsGpuSnapshotLedger(warm,frames);
        var repeatWarm=Report(10,2,10,GpuSampleState.Pending,1,8,GpuSampleState.Valid);
        var conflicting=repeatWarm.MainResolved!.Value;
        Reject(()=>warmLedger.Add(repeatWarm with{MainResolved=conflicting with{Sample=conflicting.Sample with{Timing=conflicting.Sample.Timing with{Milliseconds=4}}}}));
        warmLedger.Add(repeatWarm);
        Reject(()=>new EcsGpuSnapshotLedger(baseline,new HashSet<ulong>{9}));
        Console.WriteLine($"E5-D GPU snapshot ledger: {checks} checks passed; baseline/current/resolved/gaps/conflicts guarded.");
    }
}
