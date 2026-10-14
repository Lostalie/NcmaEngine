using Ncma;

namespace Ncma.Gameplay.UiFixture;

// Test-only script assembly, copied into a bounded test project and loaded by the real catalog.
// It has only the public gameplay SDK reference; no editor, UI, renderer or native access.
public sealed class HudProbe : Behaviour
{
    protected override void OnCreate()
    { if (Context.UiInput.Count != 0) throw new InvalidOperationException("UI leaked into initialization."); }
    protected override void OnFixedUpdate(double deltaSeconds)
    {
        var transform = GameObject.LocalTransform;
        for (int i = 0; i < Context.UiInput.Count; i++) {
            var action = Context.UiInput[i];
            if (action.Kind == GameplayUiActionKind.Activate) transform.Position.X += 1;
            else if (action.Kind == GameplayUiActionKind.ValueChanged) transform.Position.Y = action.Value;
            else if (action.Kind == GameplayUiActionKind.TextChanged) transform.Position.Z = action.Text.Length;
        }
        if (Context.Input.Pressed(384) || Context.Input.Pressed(32)) transform.Position.Z += 1000;
        GameObject.LocalTransform = transform;
    }
    protected override void OnUpdate(double deltaSeconds)
    { if (Context.UiInput.Count != 0) throw new InvalidOperationException("UI repeated in display update."); }
}
