using V3 = System.Numerics.Vector3;
using V4 = System.Numerics.Vector4;
using Ncma.Assets;
using Ncma.Characters;
using Ncma.Editor.Services;
using Ncma.Gameplay;
using Ncma.Interop;
using Ncma.Physics;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Scene.Rendering;
using System.Security.Cryptography;

internal static class CharacterEditorChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException) { return; } throw new Exception("Expected rejection"); }
    internal static void Run(string output, string plugins)
    {
        string root = Path.Combine(output, "Character Editor"); Directory.CreateDirectory(Path.Combine(root, "assets"));
        Guid project = Guid.NewGuid(), model = Guid.NewGuid(), mesh = Guid.NewGuid(), slots = Guid.NewGuid(), rig=Guid.NewGuid(), clip=Guid.NewGuid();
        var settings = new ImportSettings(1, 30, true); string sourceHash = new('A', 64);
        ImportVertex Vertex(float x, float y) => new(new(x, y, 0), V3.UnitZ, default, default, new(1,0,0,0));
        var identity=new ImportTransform(V3.Zero,System.Numerics.Quaternion.Identity,V3.One);
        var payload = new MeshPayload(true, 1, [Vertex(-.5f, 0), Vertex(.5f, 0), Vertex(-.5f, 1.8f)], [], [0, 1, 2], [0], [new(0,AssetMatrices.EncodeColumnMajor(System.Numerics.Matrix4x4.Identity))], 1);
        var manifest = new ModelAssetManifest(1, model, false, sourceHash, settings, rig, [new(mesh, slots)], [clip]);
        byte[] bytes = DerivedAssetCodec.Encode([new(model, AssetKind.Character, ModelAssetManifestCodec.Encode(manifest)), new(mesh, AssetKind.SkinnedMesh, ModelPayloadCodec.Encode(payload)), new(slots, AssetKind.MaterialSet, ModelPayloadCodec.Encode(new MaterialSlotsPayload(["Default"]))),
            new(rig,AssetKind.Skeleton,ModelPayloadCodec.Encode(new SkeletonPayload([new("root",-1,identity)]))),
            new(clip,AssetKind.Clip,ModelPayloadCodec.Encode(new ClipPayload(1,new("Root run",1,[new(0,[new(0,identity),new(1,identity with{Position=new(3,0,0)})])]))))]);
        string hash = Convert.ToHexString(SHA256.HashData(bytes)), relative = $"out/assets/{project:N}/{model:N}/1-{hash}.nca", generation = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(generation)!); File.WriteAllBytes(generation, bytes);
        File.WriteAllBytes(Path.Combine(root, "assets/model.fbx.ncmeta"), AssetRecordCodec.Encode(new(1, model, AssetKind.Character, "assets/model.fbx", sourceHash, "ufbx", 1, settings,
            [new(mesh, AssetKind.SkinnedMesh, "mesh/0", "Triangle", false), new(slots, AssetKind.MaterialSet, "materials/0", "Slots", false),new(rig,AssetKind.Skeleton,"rig/0","Rig",false),new(clip,AssetKind.Clip,"clip/0","Run",false)], [], new(1, hash, relative))));
        using var physics = new PhysicsService(plugins, characterSupport: true);
        using var loader = new PluginLoader(); loader.Load(plugins, [new("platform", ModuleKind.Platform, "NcmaPlatform.dll", "NcmaPlatform.dll", 1, 0, []), new("renderer", ModuleKind.Renderer, "NcmaRenderer.dll", "NcmaRenderer.dll", 1, 2, ["platform"])]);
        using var window = new PlatformWindow(loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "K3 character Editor", 320, 240, false);
        using var renderer = new RendererSession(loader.Modules.Single(m => m.Kind == ModuleKind.Renderer), window, 320, 240);
        using var cache = new RenderResourceCache(renderer);
        string kernelPath=Path.Combine(plugins,"NcmaAnimationKernel.dll");using var kernel=new Ncma.Animation.Native.PoseKernel(kernelPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(kernelPath))));
        PreparedSceneAssetLease? preparation=null;
        SceneRenderSession? scene = null; CharacterPlayRuntime? runtime = null; bool failClose = false; int closes = 0;
        using var owner = new EditorSessionOwner("K3 Editor", components: CharacterComponents.Register(RenderComponentRegistry.CreateRegistry()), validateComposition: CharacterComponents.RequireComposition,
            composePlay: p => runtime = CharacterPlayRuntime.Compose(p, physics,preparation), beforePlayStop: () =>
            {
                Check(runtime!.Status.ResourcesOwned, "Derived close precedes solver close");
                if (failClose) throw new IOException("Injected derived close failure");
                scene?.Dispose(); scene = null; closes++;
            });
        var world = owner.Document.World;
        var actor = world.CreateObject("Character"); actor.Set(TransformData.Identity with { Position = new(0, .1f, 0) }); actor.Set(CharacterData.Default); actor.Set(new SkinnedMeshData(model,mesh,rig,slots,true,true,uint.MaxValue));
        actor.Set(new Ncma.Animation.ClipPlaybackData(clip,true,true,1,0));actor.Set(new Ncma.Animation.RootMotionData(0));
        var floor = world.CreateObject("Floor"); floor.Set(TransformData.Identity with { Position = new(0, -.5f, 0) }); floor.Set(new BoxColliderData(20, .5f, 20, 1000, 1, false));
        var wall = world.CreateObject("Wall"); wall.Set(TransformData.Identity with { Position = new(2, 2, 0) }); wall.Set(new BoxColliderData(.2f, 2, 20, 1000, 1, false));
        var camera = world.CreateObject("Follow camera"); camera.Set(TransformData.Identity with { Position = new(0, 3, 6) }); camera.Set(CameraData.Default); camera.Set(new FollowCameraData(actor.PersistentId, 0, 3, 6, 1));
        var light = world.CreateObject("Light"); light.Set(TransformData.Identity); light.Set(DirectionalLightData.Default);
        owner.Edit!.Resynchronize(); owner.PrepareRenderAssets(root, project);
        preparation=owner.RenderAssets;
        var workspace = new EditorWorkspace(owner); byte[] edit = owner.Document.CaptureBytes(); Guid editWorld = world.Identity; ulong frame = 0;
        for (int cycle = 0; cycle < 3; ++cycle)
        {
            workspace.PlayControl(workspace.Stamp, "start"); var play = owner.Play!;
            Check(play.Document.World.Identity != editWorld && owner.Edit.State.Frozen, "Play isolates and freezes Edit");
            var assets = owner.PlayRenderAssets!;
            scene = new(renderer, cache, play.Document.World, assets, play.Document.CaptureSnapshot(), poseKernel:kernel,play: play, interpolateTransforms: true,rootMotion:runtime);
            var held = new ulong[8]; held[1] = 1UL << 4; // D, world +X
            play.SubmitInput(new(play.SessionId, 1, true, held, new ulong[8], new ulong[8]));
            for (int i = 0; i < 120; ++i) Check(play.AdvanceFrame(1d / 60).State == PlayState.Running, "Actual Editor coupled movement");
            var committed = play.Document.World.FindObject(actor.PersistentId).Get<TransformData>();
            Check(committed.Position.X > 1.45f && committed.Position.X < 1.51f && MathF.Abs(committed.Rotation.Y) < 1e-5f, "Editor root motion wall constraint replaces input turn");
            byte[] beforeRender = play.Document.CaptureBytes(); ulong tick = play.Tick;
            play.AdvanceFrame(1d / 120); var interpolated = play.RenderView.Objects.Single(o => o.ObjectId == actor.PersistentId).Transform;
            var follow = CharacterPlayRuntime.FollowView(play, camera.PersistentId, 320, 240);
            Check(scene.Submit(++frame, 320, 240, camera.PersistentId, follow), "Actual scene batch, not reference/clear fallback");
            Check(scene.Costs.GeometryDraws == 1 && scene.View!.Geometry.Single().Model.M41 == interpolated.Position.X, "Geometry uses copied interpolation");
            Check(scene.View!.ShadowCasters.Single().Model == scene.View.Geometry.Single().Model, "Shadow uses the same interpolation as geometry");
            Check(beforeRender.SequenceEqual(play.Document.CaptureBytes()) && play.Tick == tick && runtime!.Status.CommittedSequence == tick, "Render cannot execute solver or write World");
            renderer.Present();
            workspace.PlayControl(workspace.Stamp, "pause"); workspace.PlayControl(workspace.Stamp, "step"); Check(play.Tick == tick + 1, "Editor single step");
            if (cycle == 0)
            {
                failClose = true; Reject(() => workspace.PlayControl(workspace.Stamp, "stop"));
                Check(owner.Play == play && owner.Edit.State.Frozen && physics.Inspect().Worlds == 1 && runtime!.Status.ResourcesOwned, "Close failure retains Play/assets/solver ownership"); failClose = false;
            }
            workspace.PlayControl(workspace.Stamp, "stop");
            Check(owner.Play is null && !owner.Edit.State.Frozen && physics.Inspect().Worlds == 0 && edit.SequenceEqual(owner.Document.CaptureBytes()), "Stop releases solver and preserves Edit");
        }
        Check(closes == 3 && renderer.Stats.ValidationErrors == 0 && renderer.Stats.ValidationWarnings == 0, "Editor actual GPU validation and lifetime");
        Reject(() => { using var blocked = new FileStream(generation, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete); });
        owner.Dispose(); // Edit's preparation intentionally pins its generation until owner close.
        using var unlocked = new FileStream(generation, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
    }
}
