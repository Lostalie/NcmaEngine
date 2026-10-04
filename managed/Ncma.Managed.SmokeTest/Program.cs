using Ncma;
using System.Runtime.InteropServices;

if (typeof(Behaviour).Assembly.GetType("Ncma.Node") != null || typeof(Behaviour).GetProperty("Node") != null)
    throw new InvalidOperationException("Legacy scene Node API must not be exposed.");

using SceneWorld world = new("ManagedSmokeTest");
GameObject root = world.CreateObject("Root");
GameObject camera = world.CreateObject("Camera");
if (typeof(GameObject).GetProperty("LogicLanguage") != null || typeof(GameObject).Assembly.GetType("Ncma.GameplayLanguage") != null)
    throw new InvalidOperationException("Python gameplay language selection must not be exposed.");
if (root.HasTransform || camera.HasTransform) throw new InvalidOperationException("GameObjects must be empty containers.");
root.LocalTransform = Transform.Identity;
camera.LocalTransform = Transform.Identity;
if (Native.GetAbiVersion() != 2) throw new InvalidOperationException("Expected native plugin ABI 2 without World exports.");
Transform transform = camera.LocalTransform;
transform.Position = new Vector3(1.0f, 2.0f, 3.0f);
camera.LocalTransform = transform;

if (camera.LocalTransform.Position.Y != 2.0f)
    throw new InvalidOperationException("Managed/native transform round-trip failed.");

if (Marshal.SizeOf<ObjectReference>() != 24 || Marshal.SizeOf<TransformWrite>() != 64 ||
    Marshal.SizeOf<GameplaySignal>() != 88 || Marshal.OffsetOf<GameplaySignal>("Value").ToInt32() != 56)
    throw new InvalidOperationException("World access ABI layout mismatch.");
if (world.FindObject(camera.PersistentId).Id != camera.Id)
    throw new InvalidOperationException("Persistent object lookup failed.");
ObjectReference[] references = [root.Reference, camera.Reference];
Transform[] values = new Transform[2];
world.ReadTransforms(references, values);
Transform changed = values[1]; changed.Position.X = 42;
TransformWrite[] writes = [new TransformWrite(camera, changed)];
world.BeginPhase();
world.WriteTransforms(writes);
world.ReadTransforms(references, values);
if (values[1].Position.X == 42) throw new InvalidOperationException("Uncommitted batch became visible.");
world.SendSignal(root, camera, 17, 12.5);
GameplaySignal[] signals = new GameplaySignal[2];
if (world.ReceiveSignals(camera, signals) != 0) throw new InvalidOperationException("Uncommitted signal delivered.");
world.CommitPhase();
world.ReadTransforms(references, values);
if (values[1].Position.X != 42 || world.ReceiveSignals(camera, signals) != 1 ||
    signals[0].Source.Id != root.Id || signals[0].Code != 17 || signals[0].Value != 12.5 || signals[0].Sequence != 1)
    throw new InvalidOperationException("Managed/native batch or signal round-trip failed.");
using (SceneWorld foreign = new("Foreign"))
{
    ObjectReference[] wrong = [foreign.CreateObject("Foreign").Reference];
    try { world.ReadTransforms(wrong, values.AsSpan(0, 1)); throw new Exception("Foreign reference accepted."); }
    catch (ArgumentException) { }
}
bool threadRejected = Task.Run(() =>
{
    try { _ = camera.LocalTransform; return false; }
    catch (InvalidOperationException) { return true; }
}).GetAwaiter().GetResult();
if (!threadRejected) throw new InvalidOperationException("Cross-thread World access accepted.");

if (typeof(GameObject).GetMethod("SetParent") != null ||
    typeof(SceneWorld).GetMethod("CreateObject")!.GetParameters().Length != 1 ||
    typeof(GameObject).GetMethod("Destroy")!.GetParameters().Length != 0)
    throw new InvalidOperationException("Scene API must expose flat objects without hierarchy controls.");
if (!root.Destroy() || camera.LocalTransform.Position.Y != 2.0f)
    throw new InvalidOperationException("Deleting one flat object must not delete another.");
try { world.ReadTransforms(references, values); throw new Exception("Deleted reference accepted."); }
catch (ArgumentException) { }

ManagedWorldAccessTests.Run();
Console.WriteLine($"Ncma managed scene smoke test passed (GameObject {camera.Id}; no native World API).");

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
Console.WriteLine("Ncma managed/native animation smoke test passed (animation ABI 2; managed clock/history).");
