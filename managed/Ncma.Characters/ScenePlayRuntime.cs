using Ncma.Animation;
using Ncma.Gameplay;
using Ncma.Physics;
using Ncma.Scene.Rendering;
namespace Ncma.Characters;

// One application composition lease; derived GPU owners must close before this lease.
public sealed class ScenePlayRuntime : IDisposable
{
    public CharacterPlayRuntime? Characters { get; private set; }
    public SceneAnimatorRuntime? Animators { get; private set; }
    public static ScenePlayRuntime Compose(PlaySession play, PhysicsService physics, PreparedSceneAssetLease? assets)
    {
        var owner = new ScenePlayRuntime();
        try { owner.Characters = CharacterPlayRuntime.Compose(play, physics, assets); owner.Animators = SceneAnimatorRuntime.Compose(play, assets); return owner; }
        catch { owner.Dispose(); throw; }
    }
    public void Dispose()
    {
        Animators?.Dispose(); Animators = null;
        Characters?.Dispose(); Characters = null;
    }
}
