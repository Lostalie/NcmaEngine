using Ncma;

namespace Ncma.Gameplay.UiFixture;

// Test-only public SDK consumer. Private progress proves that graphics recovery does not
// recreate Behaviour instances or replay OnCreate/OnEnable/fixed updates.
public sealed class RecoveryProbe:Behaviour
{
    private int _ticks,_creates;
    protected override void OnCreate(){_creates++;}
    protected override void OnEnable(){var t=GameObject.LocalTransform;t.Position.Z+=_creates;t.Scale.Y+=1;GameObject.LocalTransform=t;}
    protected override void OnFixedUpdate(double deltaSeconds){var t=GameObject.LocalTransform;t.Position.X=++_ticks;GameObject.LocalTransform=t;}
}
