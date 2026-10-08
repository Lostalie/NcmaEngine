namespace Ncma.Animation;

public enum MontageRequestKind { Play, Cancel, Jump }
public enum MontageRequestOutcome { Started, Cancelled, Jumped, PriorityRejected, NonInterruptibleRejected, NotPlaying }
public readonly record struct MontageRequest(Guid RequestId,Guid InstanceId,AnimationStepContext Context,Guid SlotId,
    MontageRequestKind Kind,Guid SectionId,int Priority);
public readonly record struct MontageEvaluationToken(Guid InstanceId,ulong Sequence,ulong SourceTick);
public readonly record struct MontageInterval(Guid SlotId,Guid SectionId,Guid ClipId,double Previous,double Current,bool RootMotion);
public readonly record struct MontageReceipt(Guid RequestId,Guid SlotId,MontageRequestOutcome Outcome);
public readonly record struct MontageSlotFrame(Guid SlotId,Guid SectionId,Guid ClipId,double Time,float Weight,bool Active,
    bool Cancelling,int Priority,ulong PlaybackGeneration);
public readonly record struct MontagePlaybackFrame(Guid InstanceId,Guid AssetId,AnimationStepContext Context,ulong Sequence,
    bool SnapshotValid,int IntervalCount,int ReceiptCount);

// Cooperating C# playback state only: a host must drive it with the SAME successful fixed-step context.
// No scheduler, World, clock source, native pose/solver, callbacks, IO or Agent authority.
public sealed class AnimationMontagePlayback
{
    public const int MaxRequests=64,MaxRequestIds=256,MaxCrossings=32,MaxIntervals=16*33;
    private struct State {internal int Section,Priority;internal double Time,Elapsed,CancelElapsed;internal float Weight,CancelWeight;internal bool Active,Cancelling;internal ulong Generation;}
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private readonly AnimationMontageProgram _program;private readonly AnimationMontageSlot[] _slots;
    private readonly AnimationMontageSection[] _sections;private readonly int[] _next,_entries;
    private readonly Dictionary<Guid,int> _slotIndices,_sectionIndices;
    private readonly State[] _committed,_candidate;
    private readonly MontageRequest[] _requests=new MontageRequest[MaxRequests];
    private readonly Dictionary<Guid,MontageRequest> _ids=new(MaxRequestIds);
    private readonly Guid[] _idRing=new Guid[MaxRequestIds];private int _idPosition;
    private readonly MontageInterval[] _intervals=new MontageInterval[MaxIntervals],_publishedIntervals=new MontageInterval[MaxIntervals];
    private readonly MontageReceipt[] _receipts=new MontageReceipt[MaxRequests],_publishedReceipts=new MontageReceipt[MaxRequests];
    private int _requestCount,_intervalCount,_receiptCount,_publishedIntervalCount,_publishedReceiptCount;
    private readonly Guid _identity=Guid.NewGuid();private AnimationStepContext _context;
    private ulong _attempt,_sequence;private bool _prepared,_rejected;
    public AnimationMontagePlayback(AnimationMontageProgram program,AnimationStepContext context)
    {
        _program=program??throw new ArgumentNullException(nameof(program));RequireContext(context);_context=context;
        var d=program.CopyDefinition();_slots=d.Slots;_sections=d.Sections;
        _slotIndices=_slots.Select((s,i)=>(s.Id,i)).ToDictionary(x=>x.Id,x=>x.i);_sectionIndices=_sections.Select((s,i)=>(s.Id,i)).ToDictionary(x=>x.Id,x=>x.i);
        _next=_sections.Select(s=>s.NextSection==Guid.Empty?-1:_sectionIndices[s.NextSection]).ToArray();_entries=_slots.Select(s=>_sectionIndices[s.EntrySection]).ToArray();
        _committed=new State[_slots.Length];_candidate=new State[_slots.Length];
        for(int i=0;i<_slots.Length;i++)_committed[i]=new(){Section=_entries[i],Time=_sections[_entries[i]].Start,Priority=_slots[i].Priority};
    }
    private void Verify(){if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Montage owner thread required.");}
    private static void RequireContext(AnimationStepContext context){if(context.SessionId==Guid.Empty||context.WorldId==Guid.Empty)throw new ArgumentException("Exact successful fixed-step context required.");}
    public Guid InstanceId{get{Verify();return _identity;}}
    public bool Request(MontageRequest request)
    {
        Verify();if(_prepared)throw new InvalidOperationException("No requests in a prepared quantum.");
        if(request.InstanceId!=_identity||request.Context!=_context||request.RequestId==Guid.Empty||!Enum.IsDefined(request.Kind)||
            !_slotIndices.TryGetValue(request.SlotId,out int slot)||request.Priority is <0 or >255||
            (request.Kind==MontageRequestKind.Cancel?request.SectionId!=Guid.Empty:request.SectionId!=Guid.Empty&&(!_sectionIndices.TryGetValue(request.SectionId,out int section)||_sections[section].SlotId!=_slots[slot].Id))||
            request.Kind==MontageRequestKind.Jump&&request.SectionId==Guid.Empty)throw new ArgumentException("Exact stamped typed Slot request required.");
        // Repeated IDs never regain authority at another tick or instance; complete payload must match.
        if(_ids.TryGetValue(request.RequestId,out var old)){if(old!=request)throw new ArgumentException("Montage request ID reused.");return false;}
        if(_requestCount>=MaxRequests)throw new ArgumentException("Bounded montage request queue.");
        // Bounded RECENT-ID cache, not a lifetime gameplay limit. Exact instance/tick checks precede lookup.
        Guid retired=_idRing[_idPosition];if(retired!=Guid.Empty)_ids.Remove(retired);_idRing[_idPosition]=request.RequestId;_idPosition=(_idPosition+1)%MaxRequestIds;
        _ids.Add(request.RequestId,request);_requests[_requestCount++]=request;return true;
    }
    public MontageEvaluationToken Prepare(AnimationStepContext context,double fixedDelta)
    {
        Verify();RequireContext(context);if(context!=_context||_prepared||_attempt==ulong.MaxValue||context.Tick==ulong.MaxValue)throw new InvalidOperationException("Exact next montage candidate required.");
        _attempt++;_intervalCount=_receiptCount=0;_committed.CopyTo(_candidate,0);_rejected=false;
        try{
            AnimationGraphCodec.Scalar(fixedDelta,.001,1);
            for(int i=0;i<_requestCount;i++)Apply(_requests[i]);
            for(int i=0;i<_slots.Length;i++)Advance(i,fixedDelta);
            _prepared=true;return new(_identity,_attempt,_context.Tick);
        }catch{_rejected=true;throw;}
    }
    private void Apply(MontageRequest r)
    {
        int i=_slotIndices[r.SlotId];ref var state=ref _candidate[i];var slot=_slots[i];MontageRequestOutcome outcome;
        switch(r.Kind){
            case MontageRequestKind.Play:
                if(r.Priority<slot.Priority||state.Active&&r.Priority<state.Priority){outcome=MontageRequestOutcome.PriorityRejected;break;}
                if(state.Active&&!slot.Interruptible){outcome=MontageRequestOutcome.NonInterruptibleRejected;break;}
                if(state.Generation==ulong.MaxValue)throw new ArgumentException("Playback generation overflow.");
                int section=r.SectionId==Guid.Empty?_entries[i]:_sectionIndices[r.SectionId];state=new(){Section=section,Time=_sections[section].Start,Priority=r.Priority,Active=true,Generation=state.Generation+1};outcome=MontageRequestOutcome.Started;break;
            case MontageRequestKind.Cancel:
                if(!state.Active){outcome=MontageRequestOutcome.NotPlaying;break;}
                if(!state.Cancelling){state.Cancelling=true;state.CancelElapsed=0;state.CancelWeight=state.Weight;}
                outcome=MontageRequestOutcome.Cancelled;break;
            case MontageRequestKind.Jump:
                if(!state.Active||state.Cancelling){outcome=MontageRequestOutcome.NotPlaying;break;}
                state.Section=_sectionIndices[r.SectionId];state.Time=_sections[state.Section].Start;outcome=MontageRequestOutcome.Jumped;break;
            default:throw new ArgumentException("Closed montage request kind.");
        }
        _receipts[_receiptCount++]=new(r.RequestId,r.SlotId,outcome);
    }
    private void Advance(int index,double delta)
    {
        ref var state=ref _candidate[index];if(!state.Active)return;var slot=_slots[index];
        if(state.Cancelling){state.CancelElapsed+=delta;state.Weight=slot.BlendOut==0?0:state.CancelWeight*(float)Math.Max(0,1-state.CancelElapsed/slot.BlendOut);if(state.Weight==0){state.Active=false;state.Cancelling=false;}return;}
        double elapsed=state.Elapsed+delta;if(!double.IsFinite(elapsed)||elapsed<=state.Elapsed)throw new ArgumentException("Montage elapsed precision/overflow.");state.Elapsed=elapsed;
        double consumed=0,compensation=0;int crossings=0;
        while(consumed<delta){var section=_sections[state.Section];double available=section.End-state.Time;double advance=Math.Min(delta-consumed,available);double next=state.Time+advance;
            if(!double.IsFinite(next)||advance>0&&next<=state.Time)throw new ArgumentException("Section time precision loss.");
            if(advance>0){if(_intervalCount>=MaxIntervals)throw new ArgumentException("Montage interval budget.");_intervals[_intervalCount++]=new(slot.Id,section.Id,section.ClipId,state.Time,next,slot.RootMotion);state.Time=next;
                // Compensated accumulation avoids inventing a33rd tiny crossing from repeated subtraction.
                double corrected=advance-compensation,total=consumed+corrected;compensation=(total-consumed)-corrected;consumed=total;}
            if(state.Time<section.End)break;
            if(++crossings>MaxCrossings)throw new ArgumentException("At most32 Section boundaries per Slot/quantum.");
            int successor=_next[state.Section];if(successor<0){state.Active=false;state.Weight=0;return;}state.Section=successor;state.Time=_sections[successor].Start;
        }
        float incoming=slot.BlendIn==0?1:(float)Math.Min(1,state.Elapsed/slot.BlendIn);double untilEnd=_next[state.Section]<0?_sections[state.Section].End-state.Time:double.PositiveInfinity;
        float outgoing=slot.BlendOut==0?1:(float)Math.Min(1,untilEnd/slot.BlendOut);state.Weight=Math.Min(incoming,outgoing);
    }
    private void Token(MontageEvaluationToken t){Verify();if(!_prepared||t.InstanceId!=_identity||t.Sequence!=_attempt||t.SourceTick!=_context.Tick)throw new InvalidOperationException("Exact prepared montage token required.");}
    public void Commit(MontageEvaluationToken token,AnimationStepContext context)
    {
        Token(token);if(context.SessionId!=_context.SessionId||context.WorldId!=_context.WorldId||context.Tick!=_context.Tick+1)throw new InvalidOperationException("Montage commits only with the next successful host fixed tick.");
        _candidate.CopyTo(_committed,0);Array.Copy(_intervals,_publishedIntervals,_intervalCount);Array.Copy(_receipts,_publishedReceipts,_receiptCount);_publishedIntervalCount=_intervalCount;_publishedReceiptCount=_receiptCount;
        _context=context;_sequence=token.Sequence;_prepared=false;_requestCount=0;_rejected=false;
    }
    public void Abort(MontageEvaluationToken token){Token(token);_prepared=false;_rejected=false;}
    public MontagePlaybackFrame Frame{get{Verify();return new(_identity,_program.AssetId,_context,_sequence,!_prepared&&!_rejected,_publishedIntervalCount,_publishedReceiptCount);}}
    public MontageSlotFrame ReadSlot(Guid id){Verify();if(_prepared||_rejected||!_slotIndices.TryGetValue(id,out int i))throw new InvalidOperationException("Current successful montage Slot required.");return Slot(i,_committed[i]);}
    public MontageSlotFrame PreparedSlot(MontageEvaluationToken token,Guid id){Token(token);if(!_slotIndices.TryGetValue(id,out int i))throw new ArgumentException("Exact Slot UUID.");return Slot(i,_candidate[i]);}
    private MontageSlotFrame Slot(int i,State s)=>new(_slots[i].Id,_sections[s.Section].Id,_sections[s.Section].ClipId,s.Time,s.Weight,s.Active,s.Cancelling,s.Priority,s.Generation);
    public int CopyPreparedIntervals(MontageEvaluationToken token,Span<MontageInterval> destination){Token(token);if(destination.Length<_intervalCount)throw new ArgumentException("Interval output capacity.");_intervals.AsSpan(0,_intervalCount).CopyTo(destination);return _intervalCount;}
    public int CopyCommittedIntervals(Span<MontageInterval> destination){Verify();if(!Frame.SnapshotValid||destination.Length<_publishedIntervalCount)throw new InvalidOperationException("Current committed interval capacity required.");_publishedIntervals.AsSpan(0,_publishedIntervalCount).CopyTo(destination);return _publishedIntervalCount;}
    public int CopyCommittedReceipts(Span<MontageReceipt> destination){Verify();if(!Frame.SnapshotValid||destination.Length<_publishedReceiptCount)throw new InvalidOperationException("Current committed receipt capacity required.");_publishedReceipts.AsSpan(0,_publishedReceiptCount).CopyTo(destination);return _publishedReceiptCount;}
}
