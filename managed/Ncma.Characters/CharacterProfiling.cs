using System.Diagnostics;
using Ncma.Physics;
namespace Ncma.Characters;

public readonly record struct CharacterStepProfile(Guid PlaySessionId,Guid WorldId,ulong Tick,
    double PreparationMilliseconds,double ActionMilliseconds,double RootMilliseconds,
    double CombatMilliseconds,double QueryMilliseconds,double CommitObserverMilliseconds,CharacterNativeCostCounters Native);

// Owned managed observation with properties that survive default JSON serialization.
// Do not expose the mutable interop layout or globally enable native-field serialization.
public readonly record struct CharacterNativeCostCounters(PhysicsDimension Dimension,uint State,uint LiveWorlds,
    ulong LiveBodies,ulong Sequence,ulong Batches,ulong CopiedBytes,ulong Errors,ulong LiveJobs,double LastStepMilliseconds)
{
    internal static CharacterNativeCostCounters Copy(PhysicsCounters value)=>new(value.Dimension,value.State,value.LiveWorlds,
        value.LiveBodies,value.Sequence,value.Batches,value.CopiedBytes,value.Errors,value.LiveJobs,value.LastStepMilliseconds);
}

public sealed partial class CharacterPlayRuntime
{
    // Trusted opt-in instrumentation only. No IO, waits, history, Agent grant or native handles.
    // Timings are nested scopes, not additive total cost; incomplete/faulted steps cannot be read.
    private readonly bool _profileEnabled;
    private double _preparationMs,_actionMs,_rootMs,_combatMs,_queryMs,_commitMs;
    private ulong _profileTick;
    private long ProfileTime=>_profileEnabled?Stopwatch.GetTimestamp():0;
    private double ProfileElapsed(long start)=>_profileEnabled?Stopwatch.GetElapsedTime(start).TotalMilliseconds:0;
    public CharacterStepProfile ReadStepProfile()
    {
        VerifyRootPresentation();
        if(!_profileEnabled||_profileTick!=_play.Tick||_profileTick==0)throw new InvalidOperationException("No committed opt-in profile for this tick.");
        return new(_play.SessionId,_play.Scene.World.Identity,_profileTick,_preparationMs,_actionMs,_rootMs,_combatMs,_queryMs,_commitMs,CharacterNativeCostCounters.Copy(_domain!.Counters));
    }
}
