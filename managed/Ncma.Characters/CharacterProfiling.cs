using System.Diagnostics;
using Ncma.Physics;
namespace Ncma.Characters;

public readonly record struct CharacterStepProfile(Guid PlaySessionId,Guid WorldId,ulong Tick,
    double PreparationMilliseconds,double ActionMilliseconds,double RootMilliseconds,
    double CombatMilliseconds,double QueryMilliseconds,double CommitObserverMilliseconds,PhysicsCounters Native);

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
        return new(_play.SessionId,_play.Document.World.Identity,_profileTick,_preparationMs,_actionMs,_rootMs,_combatMs,_queryMs,_commitMs,_domain!.Counters);
    }
}
