using Ncma.Animation;
using Ncma.Gameplay;
using Ncma.Physics;
using Ncma.Scene.Rendering;
namespace Ncma.Characters;

// One application composition lease; derived GPU owners must close before this lease.
public sealed class ScenePlayRuntime : IDisposable
{
    private readonly PlaySession _play;
    private readonly bool _profile;
    private bool _disposed;
    private ScenePlayRuntime(PlaySession play, bool profile) { _play = play; _profile = profile; }
    public CharacterPlayRuntime? Characters { get; private set; }
    public SceneAnimatorRuntime? Animators { get; private set; }
    public static ScenePlayRuntime Compose(PlaySession play, PhysicsService physics, PreparedSceneAssetLease? assets,bool interruptTransitions=false, bool profile=false)
    {
        ArgumentNullException.ThrowIfNull(play);
        if (profile) play.EnableSimulationCosts();
        var owner = new ScenePlayRuntime(play, profile);
        try { owner.Animators = SceneAnimatorRuntime.Compose(play, assets,interruptTransitions); owner.Characters = CharacterPlayRuntime.Compose(play, physics, assets, profile:profile, animators: owner.Animators); return owner; }
        catch { owner.Dispose(); throw; }
    }
    public SceneSimulationCostReport? ReadSimulationCosts()
    {
        _play.Scene.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_profile || _play.LastSimulationCosts is not { } advance) return null;
        CharacterStepProfile? character = null;
        if (advance.Completed && advance.CommittedSteps != 0 && Characters is not null) {
            character = Characters.ReadStepProfile();
            if (character.Value.PlaySessionId != advance.SessionId || character.Value.WorldId != advance.WorldId || character.Value.Tick != advance.TickAfter)
                throw new InvalidOperationException("Character costs do not belong to the last committed simulation advance.");
        }
        return new(advance, character);
    }
    public void Dispose()
    {
        Animators?.Dispose(); Animators = null;
        Characters?.Dispose(); Characters = null;
        _disposed = true;
    }
}

public readonly record struct SceneSimulationCostReport(SimulationCostReport Advance, CharacterStepProfile? LastCharacterStep)
{
    public string CharacterScope => "last committed fixed step only; nested timings, not a sum over all quanta; native counters are cumulative";
    public bool FullPerformanceAccepted => false;
}
