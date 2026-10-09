using System.Numerics;
using Ncma.Animation;

namespace Ncma.Characters;
using Vector3=System.Numerics.Vector3;

internal readonly record struct MontageRootContribution(Guid SlotId,RootMotionDelta Delta,float Coverage);

// B2b-1 numerical preparation only. No World, resource pin, scheduler, solver or movement write.
// The future graph consumer must mix the remaining (1-Coverage) base intent before sole Movement/Jolt.
internal sealed class MontageRootMotionRecipe
{
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private readonly AnimationMontageProgram _program;
    private readonly AnimationMontageSlot[] _slots;
    private readonly Dictionary<Guid,int> _slotIndices;
    private readonly Dictionary<Guid,RootMotionTrack> _tracks;
    private readonly MontageRootContribution[] _candidate;
    private readonly double[] _fractions;
    private readonly int[] _counts;
    private readonly int[] _boundaries;
    private readonly MontageInterval[] _last;
    internal MontageRootMotionRecipe(AnimationMontageProgram program,IReadOnlyDictionary<Guid,RootMotionTrack> tracks)
    {
        ArgumentNullException.ThrowIfNull(program);ArgumentNullException.ThrowIfNull(tracks);_program=program;
        var definition=program.CopyDefinition();_slots=definition.Slots;_slotIndices=_slots.Select((s,i)=>(s.Id,i)).ToDictionary(x=>x.Id,x=>x.i);
        var required=definition.Sections.Select(s=>s.ClipId).ToHashSet();
        if(tracks.Count>128||!required.SetEquals(tracks.Keys))throw new ArgumentException("Exact bounded Montage root Clip set required.");
        _tracks=new(tracks.Count);int root=-1,keys=0;
        foreach(var pair in tracks){if(pair.Value is null||pair.Value.Duration!=program.ClipDuration(pair.Key)||root>=0&&root!=pair.Value.RootIndex)throw new ArgumentException("Exact prepared duration and canonical root required.");
            root=pair.Value.RootIndex;keys=checked(keys+pair.Value.KeyCount);if(keys>262144)throw new ArgumentException("Montage root key budget.");_tracks.Add(pair.Key,pair.Value);}
        _candidate=new MontageRootContribution[_slots.Length];_fractions=new double[_slots.Length];_counts=new int[_slots.Length];_boundaries=new int[_slots.Length];_last=new MontageInterval[_slots.Length];
    }
    internal int Evaluate(ReadOnlySpan<MontageInterval> intervals,double fixedDelta,Span<MontageRootContribution> destination)
    {
        if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Montage root owner thread required.");
        if(intervals.Length>AnimationMontagePlayback.MaxIntervals||destination.Length<_slots.Length||!double.IsFinite(fixedDelta)||fixedDelta is <.001 or >1)throw new ArgumentException("Bounded root intervals/delta/output required.");
        Array.Clear(_fractions);Array.Clear(_counts);Array.Clear(_boundaries);for(int i=0;i<_slots.Length;i++)_candidate[i]=new(_slots[i].Id,default,0);
        int previousSlot=-1;
        foreach(var interval in intervals){
            if(!_slotIndices.TryGetValue(interval.SlotId,out int slot)||slot<previousSlot)throw new ArgumentException("Ordered exact Slot intervals required.");previousSlot=slot;
            var section=_program.RequireSection(interval.SectionId);var policy=_slots[slot];
            if(section.SlotId!=interval.SlotId||section.ClipId!=interval.ClipId||policy.RootMotion!=interval.RootMotion||
                !double.IsFinite(interval.Previous)||!double.IsFinite(interval.Current)||interval.Previous<section.Start||interval.Current>section.End||interval.Current<=interval.Previous||
                !float.IsFinite(interval.AverageWeight)||interval.AverageWeight is <0 or >1||!double.IsFinite(interval.StepFraction)||interval.StepFraction is <=0 or >1||
                Math.Abs(interval.StepFraction-(interval.Current-interval.Previous)/fixedDelta)>1e-10)
                throw new ArgumentException("Exact Section interval/policy/envelope/fraction required.");
            if(_counts[slot]>0){var last=_last[slot];var before=_program.RequireSection(last.SectionId);
                if(before.NextSection!=section.Id||last.Current!=before.End||interval.Previous!=section.Start)throw new ArgumentException("Complete consecutive Section traversals required.");}
            if(++_counts[slot]>33||(_fractions[slot]+=interval.StepFraction)>1+1e-10)throw new ArgumentException("Per-Slot interval/time budget exceeded.");_last[slot]=interval;
            if(interval.Current==section.End&&++_boundaries[slot]>AnimationMontagePlayback.MaxCrossings)throw new ArgumentException("At most32 actual Section boundaries per Slot.");
            // All rows, including root-disabled Slots, validate the exact track before any output is copied.
            var delta=_tracks[interval.ClipId].Extract(new(interval.Previous,interval.Current),false);
            if(!policy.RootMotion)continue;
            var old=_candidate[slot];float weight=interval.AverageWeight;
            Vector3 translation=old.Delta.Translation+Vector3.Transform(delta.Translation*weight,Quaternion.CreateFromAxisAngle(Vector3.UnitY,old.Delta.Yaw));
            float yaw=MathF.IEEERemainder(old.Delta.Yaw+delta.Yaw*weight,2*MathF.PI);
            float coverage=old.Coverage+(float)(interval.StepFraction*weight);
            if(!float.IsFinite(translation.LengthSquared())||translation.LengthSquared()>1e6f||Math.Abs(translation.Y)>1e-6f||!float.IsFinite(yaw)||coverage>1+1e-6f)throw new ArgumentException("Bounded Montage root contribution required.");
            _candidate[slot]=new(interval.SlotId,new(translation,yaw),Math.Min(1,coverage));
        }
        _candidate.CopyTo(destination);return _slots.Length;
    }
}
