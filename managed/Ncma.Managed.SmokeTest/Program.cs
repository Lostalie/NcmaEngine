using Ncma;

using SceneWorld world = new("ManagedSmokeTest");
Node root = world.CreateNode("Root");
Node camera = world.CreateNode("Camera", root);
Transform transform = camera.LocalTransform;
transform.Position = new Vector3(1.0f, 2.0f, 3.0f);
camera.LocalTransform = transform;

if (camera.LocalTransform.Position.Y != 2.0f)
    throw new InvalidOperationException("Managed/native transform round-trip failed.");

Console.WriteLine($"Ncma managed/native smoke test passed (ABI node {camera.Id}).");

using ActionAnimationSession animation = new();
animation.TriggerAction("Attack");
animation.Step(0.2);
using (var state = System.Text.Json.JsonDocument.Parse(animation.InspectJson()))
{
    if (state.RootElement.GetProperty("state").GetString() != "Attack" ||
        !state.RootElement.GetProperty("hit_window").GetBoolean())
        throw new InvalidOperationException("Managed/native animation and notify smoke test failed.");
}
animation.Undo();
using (var state = System.Text.Json.JsonDocument.Parse(animation.InspectJson()))
{
    if (state.RootElement.GetProperty("time").GetDouble() != 0)
        throw new InvalidOperationException("Managed/native animation undo failed.");
}
animation.Redo();
animation.Reset();
Console.WriteLine("Ncma managed/native animation smoke test passed (animation ABI 1).");
