namespace Ncma.Animation;

public readonly record struct AnimationCacheLifetime(Guid NodeId,int PreparedOrder,int LastConsumer,int ConsumerCount);
public readonly record struct AnimationCacheStatistics(ulong Tick,int Requests,int Hits);

// Bounded row-lifetime allocator for prepared dense recipes. Structural layouts are reused until topology changes;
// time/weight/parameters are not cache keys. No graph compilation/IO/allocations inside numeric evaluation.
public sealed class AnimationPoseScratchLayout
{
    private readonly int[] _slots,_last,_owners,_a,_b,_c;private readonly AnimationPoseOperation[] _operations;
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private int _count,_output=-1;public int Slots {get;private set;}
    public AnimationPoseScratchLayout(int maximumInstructions)
    {
        if(maximumInstructions is <1 or >AnimationGraphCodec.MaxPlanInstructions)throw new ArgumentException("Bounded lifetime table.");
        _slots=new int[maximumInstructions];_last=new int[maximumInstructions];_owners=new int[maximumInstructions];_a=new int[maximumInstructions];_b=new int[maximumInstructions];_c=new int[maximumInstructions];_operations=new AnimationPoseOperation[maximumInstructions];
    }
    public void Prepare(ReadOnlySpan<AnimationPoseInstruction> plan,int output)
    {
        Verify();
        AnimationPoseRecipe.Validate(plan,output);if(plan.Length>_slots.Length)throw new ArgumentException("Scratch row capacity.");
        bool same=plan.Length==_count&&output==_output;for(int i=0;same&&i<plan.Length;i++){var r=plan[i];same=_a[i]==r.SourceA&&_b[i]==r.SourceB&&_c[i]==r.SourceC&&_operations[i]==r.Operation;}if(same)return;
        for(int i=0;i<plan.Length;i++)_last[i]=i;
        for(int i=0;i<plan.Length;i++){var r=plan[i];if(r.SourceA>=0)_last[r.SourceA]=i;if(r.SourceB>=0)_last[r.SourceB]=i;if(r.SourceC>=0)_last[r.SourceC]=i;}
        _last[output]=plan.Length;Array.Fill(_owners,-1);int used=0;
        for(int i=0;i<plan.Length;i++){
            int slot=0;while(slot<used&&_last[_owners[slot]]>=i)slot++;if(slot==used)used++;_slots[i]=slot;_owners[slot]=i;
            _a[i]=plan[i].SourceA;_b[i]=plan[i].SourceB;_c[i]=plan[i].SourceC;_operations[i]=plan[i].Operation;
        }
        _count=plan.Length;_output=output;Slots=used;
    }
    public int Slot(int row){Verify();return row>=0&&row<_count?_slots[row]:throw new ArgumentException("Prepared scratch row required.");}
    private void Verify(){if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Scratch layout owner thread required.");}
}
