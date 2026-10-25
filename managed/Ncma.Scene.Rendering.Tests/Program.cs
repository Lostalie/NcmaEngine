using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static partial class Program
{
    private static readonly Guid MeshId = Guid.NewGuid(), SetId = Guid.NewGuid();
    private static readonly string Hash = new('A', 64);
    private static readonly StaticMeshData Mesh = new(MeshId, SetId, true, true, uint.MaxValue);
    private static SceneAssetInfo MeshInfo => new(MeshId, AssetKind.StaticMesh, 1, Hash, 2, BoundsMin: new(-.5f), BoundsMax: new(.5f));
    private static SceneAssetInfo SetInfo => new(SetId, AssetKind.MaterialSet, 1, Hash, 2);
    private static PreparedSceneAssets Assets => new([MeshInfo, SetInfo]);
    private static SceneDocument Document() => new("Scene rendering", global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module), SceneRenderValidation.RequireComposition);
    private static Guid Geometry(SceneDocument doc, Vector3? position = null)
    { Guid obj = doc.CreateObject("Geometry"); doc.SetComponent(obj, TransformData.Identity with { Position = position ?? new(0, 0, -3) }); doc.SetComponent(obj, Mesh); return obj; }
    private static Guid Camera(SceneDocument doc)
    { Guid obj = doc.CreateObject("Camera"); doc.SetComponent(obj, TransformData.Identity); doc.SetComponent(obj, CameraData.Default); return obj; }
    private static void Light(SceneDocument doc)
    { Guid obj = doc.CreateObject("Light"); doc.SetComponent(obj, TransformData.Identity); doc.SetComponent(obj, DirectionalLightData.Default); }
    private static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException) { return; } throw new Exception("Invalid operation accepted"); }
    private static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value);
    private static CapabilityPermissions Permissions => new(["ncma.scene.transaction", "ncma.history.undo", "ncma.history.redo"]);
    private static CapabilityResult Invoke(EditSession edit, string capability, object input) => edit.Invoke(new(EditSession.ContractVersion,
        Guid.NewGuid(), edit.SessionId, edit.Revision, capability, Element(input)), Permissions);
    private static void AtomicReject(SceneDocument doc, SceneDocumentSnapshot candidate)
    {
        var bytes = doc.CaptureBytes(); ulong revision = doc.Revision; Guid identity = doc.SessionId;
        Reject(() => doc.RestoreSnapshot(candidate));
        Check(bytes.AsSpan().SequenceEqual(doc.CaptureBytes()) && revision == doc.Revision && identity == doc.SessionId, "Rejected candidate changed the authoring document.");
    }
    private static string OutputRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Build.bat"))) root = root.Parent;
        if (root is null) throw new Exception("Repository not found.");
        var path = Path.Combine(root.FullName, "out/verification/m3-4"); Directory.CreateDirectory(path); return path;
    }
    public static int Main(string[] args)
    {
        var tests = new List<(string Name, Action Run)> {
            ("registry-shared-values", () => { var r = global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module); Check(r.Describe().Count == 14 && r.Describe().Count(c => c.TypeId != EnvironmentLightingData.TypeId && c.TypeId != PostProcessData.TypeId&&c.TypeId!=SceneUiData.TypeId&&c.TypeId!=SceneShadowData.TypeId) == 10);Check(r.Decode<SceneUiData>(new(SceneUiData.TypeId,1,r.Encode(SceneUiData.Off)))==SceneUiData.Off); Check(r.Decode<PostProcessData>(new(PostProcessData.TypeId,1,r.Encode(PostProcessData.Off)))==PostProcessData.Off); Check(r.Decode<StaticMeshData>(new(StaticMeshData.TypeId, 1, r.Encode(Mesh))) == Mesh); Check(r.Decode<Ncma.Animation.RootMotionData>(new(Ncma.Animation.RootMotionData.TypeId,1,r.Encode(new Ncma.Animation.RootMotionData(0))))==new Ncma.Animation.RootMotionData(0)); Reject(() => r.Decode<CameraData>(new(StaticMeshData.TypeId, 1, r.Encode(Mesh)))); }),
            ("strict-schema-and-version", () => { var d = Document(); Guid g = Geometry(d); var snapshot = d.CaptureSnapshot(); foreach (var c in snapshot.Objects[0].Components) {
                AtomicReject(d, snapshot with { Objects = [snapshot.Objects[0] with { Components = [c with { Version = 2 }] }] }); }
                var component = snapshot.Objects[0].Components.Single(c => c.TypeId == StaticMeshData.TypeId);
                using var extra = JsonDocument.Parse(component.Data.GetRawText()[..^1] + ",\"pointer\":42}");
                AtomicReject(d, snapshot with { Objects = [snapshot.Objects[0] with { Components = [component with { Data = extra.RootElement.Clone() }] }] });
                using var omitted = JsonDocument.Parse("{\"meshId\":\"" + MeshId + "\"}");
                AtomicReject(d, snapshot with { Objects = [snapshot.Objects[0] with { Components = [component with { Data = omitted.RootElement.Clone() }] }] }); _ = g; }),
            ("complete-document-roundtrip", () => { var d = Document(); Geometry(d); Camera(d); Light(d); Guid logic = d.CreateObject("Logic"); var copy = Document(); copy.RestoreBytes(d.CaptureBytes()); Check(d.CaptureBytes().AsSpan().SequenceEqual(copy.CaptureBytes())); Check(!copy.HasComponent<TransformData>(logic)); }),
            ("save-and-restart", () => { var d = Document(); Geometry(d); string path = Path.Combine(OutputRoot(), Guid.NewGuid() + ".ncmascene"); SceneDocumentFiles.Save(d, path); var copy = Document(); SceneDocumentFiles.Load(copy, path); Check(d.CaptureBytes().AsSpan().SequenceEqual(copy.CaptureBytes())); }),
            ("required-transform-atomic", () => { var d = Document(); Geometry(d); var s = d.CaptureSnapshot(); AtomicReject(d, s with { Objects = [s.Objects[0] with { Components = s.Objects[0].Components.Where(c => c.TypeId != "ncma.transform").ToArray() }] }); }),
            ("optional-transform-unchanged", () => { var d = Document(); Guid obj = d.CreateObject("Logic"); d.SetComponent(obj, TransformData.Identity with { Scale = new(-1, 0, 1) }); d.ValidateAuthoring(); Check(d.GetComponent<TransformData>(obj).Scale.X == -1); }),
            ("render-negative-scale-rejected", () => { var d = Document(); Guid obj = Geometry(d); var s = d.CaptureSnapshot(); var t = new ComponentSnapshot("ncma.transform", 1, d.Components.Encode(TransformData.Identity with { Scale = new(-1, 1, 1) })); AtomicReject(d, s with { Objects = [s.Objects[0] with { Components = [t, s.Objects[0].Components.Single(c => c.TypeId == StaticMeshData.TypeId)] }] }); _ = obj; }),
            ("invalid-camera", () => { foreach (var camera in new[] { CameraData.Default with { Far = .01f }, CameraData.Default with { Near = 0 }, CameraData.Default with { VerticalFovRadians = float.NaN }, CameraData.Default with { ViewportWidth = 2 } }) Reject(() => CameraData.Validate(camera)); }),
            ("invalid-light-and-override", () => { Reject(() => DirectionalLightData.Validate(DirectionalLightData.Default with { IsPrimary = false })); Reject(() => MaterialOverrideData.Validate(new(Guid.Empty, false, 0, .5f))); Reject(() => MaterialOverrideData.Validate(new(Guid.NewGuid(), true, 0, 0))); }),
            ("primary-light-uniqueness", () => { var d = Document(); Light(d); Light(d); Reject(d.ValidateAuthoring); }),
            ("mutually-exclusive-geometry", () => { var d = Document(); Guid obj = Geometry(d); d.SetComponent(obj, new SkinnedMeshData(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SetId, true, true, 1)); Reject(d.ValidateAuthoring); }),
            ("orphan-material-override", () => { var d = Document(); Guid obj = d.CreateObject("Override"); d.SetComponent(obj, new MaterialOverrideData(Guid.NewGuid(), false, 0, .5f)); Reject(d.ValidateAuthoring); }),
            ("asset-kind-and-material-slots", () => { var d = Document(); Geometry(d); var s = d.CaptureSnapshot(); Reject(() => SceneRenderValidation.Inspect(s, new([MeshInfo with { Kind = AssetKind.Texture }, SetInfo]))); Reject(() => SceneRenderValidation.Inspect(s, new([MeshInfo, SetInfo with { MaterialSlots = 1 }]))); Check(SceneRenderValidation.Inspect(s, Assets, true).Count == 0); }),
            ("missing-asset-preserves-uuid", () => { var d = Document(); Geometry(d); var bytes = d.CaptureBytes(); Check(SceneRenderValidation.Inspect(d.CaptureSnapshot()).Count == 2); Reject(() => SceneRenderValidation.Inspect(d.CaptureSnapshot(), strictMissing: true)); Check(bytes.AsSpan().SequenceEqual(d.CaptureBytes())); }),
            ("skinned-rig-identity", () => { var d = Document(); Guid obj = d.CreateObject("Skin"); d.SetComponent(obj, TransformData.Identity); var skin = new SkinnedMeshData(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SetId, true, true, 1); d.SetComponent(obj, skin);
                SceneAssetInfo Info(Guid id, AssetKind kind) => new(id, kind, 1, Hash, 2, SkeletonId:skin.SkeletonId,CharacterId:skin.CharacterId);
                var resources = new PreparedSceneAssets([Info(skin.CharacterId, AssetKind.Character), Info(skin.SkeletonId, AssetKind.Skeleton), Info(skin.MeshId, AssetKind.SkinnedMesh) with { SkeletonId = skin.SkeletonId, CharacterId = skin.CharacterId }, SetInfo]);
                Check(SceneRenderValidation.Inspect(d.CaptureSnapshot(), resources, true).Count == 0);
                Reject(() => SceneRenderValidation.Inspect(d.CaptureSnapshot(), new([Info(skin.CharacterId, AssetKind.Character), Info(skin.SkeletonId, AssetKind.Skeleton), Info(skin.MeshId, AssetKind.SkinnedMesh) with{SkeletonId=Guid.Empty,CharacterId=Guid.Empty}, SetInfo]), true)); }),
            ("clip-exact-model-generation", () => {
                var d=Document();Guid obj=d.CreateObject("Animation");d.SetComponent(obj, TransformData.Identity);var skin=new SkinnedMeshData(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),SetId,true,true,1);d.SetComponent(obj, skin);
                var clip=new Ncma.Animation.ClipPlaybackData(Guid.NewGuid(),true,true,1,0);d.SetComponent(obj, clip);
                SceneAssetInfo Info(Guid id,AssetKind kind)=>new(id,kind,1,Hash,2,skin.SkeletonId,skin.CharacterId);
                var baseAssets=new[]{Info(skin.CharacterId,AssetKind.Character),Info(skin.SkeletonId,AssetKind.Skeleton),Info(skin.MeshId,AssetKind.SkinnedMesh),SetInfo};var valid=Info(clip.ClipId,AssetKind.Clip);
                Check(SceneRenderValidation.Inspect(d.CaptureSnapshot(),new(baseAssets.Append(valid)),true).Count==0);
                foreach(var bad in new[]{valid with{Generation=2},valid with{CharacterId=Guid.NewGuid()},valid with{SkeletonId=Guid.NewGuid()},valid with{Kind=AssetKind.Material}})
                    Reject(()=>SceneRenderValidation.Inspect(d.CaptureSnapshot(),new(baseAssets.Append(bad)),true));
                d.RemoveComponent<SkinnedMeshData>(obj);Reject(d.ValidateAuthoring);
            }),
            ("transaction-remove-transform-rejected", () => { var d = Document(); Guid obj = Geometry(d); var edit = new EditSession(d); var bytes = d.CaptureBytes(); var result = Invoke(edit, "ncma.scene.transaction", new { operations = new[] { new { op = "remove_component", objectId = obj, typeId = "ncma.transform" } } }); Check(result.Status == "error" && !result.Changed && edit.State.UndoCount == 0 && bytes.AsSpan().SequenceEqual(d.CaptureBytes())); }),
            ("transaction-whole-candidate-undo-redo", () => { var d = Document(); var edit = new EditSession(d); var r = d.Components; Guid id = Guid.NewGuid(); object[] ops = [new { op = "create", objectId = id, name = "Mesh" }, new { op = "set_component", objectId = id, typeId = StaticMeshData.TypeId, version = 1, data = r.Encode(Mesh) }, new { op = "set_component", objectId = id, typeId = "ncma.transform", version = 1, data = r.Encode(TransformData.Identity) }]; Check(Invoke(edit, "ncma.scene.transaction", new { operations = ops }).Changed); var bytes = d.CaptureBytes(); Check(Invoke(edit, "ncma.history.undo", new { }).Changed && d.ObjectCount == 0); Check(Invoke(edit, "ncma.history.redo", new { }).Changed && bytes.AsSpan().SequenceEqual(d.CaptureBytes())); }),
            ("validator-no-mutation-or-dto-rewrite", () => { SceneDocument? doc = null; bool armed = false; doc = new("Policy", global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module), s => { s.Objects[0] = s.Objects[0] with { Name = "Rewritten" }; if (armed) doc!.CreateObject("Forbidden"); }); Guid id = Geometry(doc); var s = doc.CaptureSnapshot(); doc.RestoreSnapshot(s); Check(doc.GetObjectName(id) == "Geometry"); armed = true; AtomicReject(doc, s); }),
            ("save-incomplete-authoring-preserves-file", () => { var d = Document(); Guid g = Geometry(d); string path = Path.Combine(OutputRoot(), Guid.NewGuid() + ".ncmascene"); SceneDocumentFiles.Save(d, path); var bytes = File.ReadAllBytes(path); d.RemoveComponent<TransformData>(g); Reject(() => SceneDocumentFiles.Save(d, path)); Check(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path))); }),
            ("isolated-clone-preserves-policy", () => { var d = Document(); Geometry(d); var clone = d.CreateIsolatedCopy(); Check(clone.SessionId != d.SessionId); clone.RemoveComponent<TransformData>(clone.GetObjectIds()[0]); Reject(clone.ValidateAuthoring); Check(d.HasComponent<TransformData>(d.GetObjectIds()[0])); }),
            ("clone-validator-cannot-mutate-source", () => { SceneDocument? d = null; d = new("Source", global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module), _ => d!.CreateObject("Forbidden")); Geometry(d); var bytes = d.CaptureBytes(); Reject(() => d.CreateIsolatedCopy()); Check(bytes.AsSpan().SequenceEqual(d.CaptureBytes())); }),
            ("editor-play-stop-isolation", () => { var f=new AssetFixture(); using var owner = new EditorSessionOwner("Isolated", components: global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module), validateComposition: SceneRenderValidation.RequireComposition); Guid id=Geometry(owner.Document);owner.Document.SetComponent(id,new StaticMeshData(f.Mesh,f.Set,true,true,uint.MaxValue));owner.PrepareRenderAssets(f.Root,f.Project); var bytes = owner.Document.CaptureBytes(); var play = owner.StartPlay(); Check(play.Scene.World.Identity != Guid.Empty); global::Ncma.Runtime.Entity g = play.Scene.World.GetEntities()[0]; play.Scene.World.Set(g, play.Scene.World.Get<TransformData>(g) with { Position = new(10, 0, 0) }); owner.StopPlay(); Check(bytes.AsSpan().SequenceEqual(owner.Document.CaptureBytes())); }),
            ("empty-no-camera-no-draw", () => { var d = Runtime(); var v = new RenderSceneExtractor(d.World).Extract(Assets, Guid.Empty, 800, 600); Check(v.Geometry.Count == 0 && v.ShadowCasters.Count == 0 && v.Diagnostics.Single().Code == "camera_selection_required"); }),
            ("non-spatial-no-draw", () => { var d = Runtime(); d.World.CreateEntity("Logic"); Guid camera = Camera(d); Check(new RenderSceneExtractor(d.World).Extract(Assets, camera, 800, 600).Geometry.Count == 0); }),
            ("explicit-camera-selection", () => { var d = Runtime(); Geometry(d); Camera(d); var second = Camera(d); var e = new RenderSceneExtractor(d.World); Check(e.Extract(Assets, Guid.Empty, 800, 600).Camera is null); Check(e.Extract(Assets, second, 800, 600).Camera!.Value.ObjectId == second); }),
            ("invalid-first-primary-never-selects-second", () => { var d = Runtime(); Light(d); d.World.Remove<TransformData>(d.World.GetEntities()[0]); Light(d); var view = new RenderSceneExtractor(d.World).Extract(Assets, Guid.Empty, 800, 600); Check(view.PrimaryLight is null && view.Diagnostics.Any(x => x.Code == "multiple_primary_lights")); }),
            ("frustum-and-independent-shadow-set", () => { var d = Runtime(); global::Ncma.Runtime.Entity front = Geometry(d); global::Ncma.Runtime.Entity outside = Geometry(d, new(20, 0, -3)); Geometry(d, new(0, 0, 3)); Guid camera = Camera(d); Light(d); var v = new RenderSceneExtractor(d.World).Extract(Assets, camera, 800, 600); Check(v.Geometry.Count == 1 && v.Geometry[0].ObjectId == d.World.GetUuid(front) && v.ShadowCasters.Count == 3 && v.ShadowCasters.Any(x => x.ObjectId == d.World.GetUuid(outside))); }),
            ("orthographic-and-near-far-clipping", () => { var d = Runtime(); Geometry(d); Geometry(d, new(0, 0, -30)); Guid camera = Camera(d); d.World.Set(d.World.FindEntity(camera), CameraData.Default with { Projection = CameraProjection.Orthographic, Far = 10 }); Check(new RenderSceneExtractor(d.World).Extract(Assets, camera, 800, 600).Geometry.Count == 1); }),
            ("layers-not-reused-for-shadow-culling", () => { var d = Runtime(); global::Ncma.Runtime.Entity g = Geometry(d); d.World.Set(g, Mesh with { LayerMask = 2 }); Guid camera = Camera(d); d.World.Set(d.World.FindEntity(camera), CameraData.Default with { LayerMask = 1 }); Light(d); var v = new RenderSceneExtractor(d.World).Extract(Assets, camera, 800, 600); Check(v.Geometry.Count == 0 && v.ShadowCasters.Count == 1); }),
            ("nonuniform-scale-copied-matrix", () => { var d = Runtime(); global::Ncma.Runtime.Entity g = Geometry(d); d.World.Set(g, d.World.Get<TransformData>(g) with { Scale = new(2, 3, 4) }); Guid camera = Camera(d); var v = new RenderSceneExtractor(d.World).Extract(Assets, camera, 800, 600); Check(v.Geometry[0].Model.M11 == 2 && v.Geometry[0].Model.M22 == 3 && v.Geometry[0].Model.M33 == 4 && v.Geometry[0].Model.M43 == -3); }),
            ("world-mutation-and-restore-invalidation", () => { var d = Runtime(); global::Ncma.Runtime.Entity g = Geometry(d); var id = d.World.GetUuid(g); Guid camera = Camera(d); var e = new RenderSceneExtractor(d.World); var a = Assets; var s = d.CaptureCheckpoint(); var v = e.Extract(a, camera, 800, 600); d.World.Set(g, d.World.Get<TransformData>(g) with { Position = new(20, 0, -3) }); var changed = e.Extract(a, camera, 800, 600); Check(changed.Geometry.Count == 0 && v.Geometry.Count == 1); d.RestoreCheckpoint(s, d.Revision); var restored = e.Extract(a, camera, 800, 600); Check(restored.WorldId != v.WorldId && restored.Geometry[0].ObjectId == id && restored.FrameIdentity != v.FrameIdentity); Reject(() => _ = d.World.GetName(g)); }),
            ("asset-generation-invalidation", () => { var d = Runtime(); Geometry(d); Guid camera = Camera(d); var e = new RenderSceneExtractor(d.World); var before = e.Extract(Assets, camera, 800, 600); var after = e.Extract(new([MeshInfo with { Generation = 2, ContentHash = new('B', 64) }, SetInfo]), camera, 800, 600); Check(before.Geometry[0].Mesh.Generation == 1 && after.Geometry[0].Mesh.Generation == 2 && before.FrameIdentity != after.FrameIdentity); }),
            ("invalid-gameplay-data-diagnostic-no-rollback", () => { var d = Runtime(); global::Ncma.Runtime.Entity g = Geometry(d); Guid camera = Camera(d); var runner = new WorldRunner(d.World); runner.AddSystem(new ActionSystem(w => w.Set(w.FindEntity(d.World.GetUuid(g)), TransformData.Identity with { Scale = new(-1, 1, 1) }))); runner.Advance(1d / 60); ulong tick = d.World.Tick; var v = new RenderSceneExtractor(d.World).Extract(Assets, camera, 800, 600); Check(tick == 1 && d.World.Tick == tick && v.Geometry.Count == 0 && v.Diagnostics.Any(x => x.Code == "render_data_invalid") && !runner.IsFaulted); }),
            ("cached-extraction-zero-allocations", CachedAllocationTest),
            ("wrong-thread-and-safe-boundary", () => { var d = Runtime(); Guid camera = Camera(d); var e = new RenderSceneExtractor(d.World); var a = Assets; _ = e.Extract(a, camera, 800, 600); var error = Task.Run(() => { try { e.Extract(a, camera, 800, 600); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult(); Check(error); var runner = new WorldRunner(d.World); runner.AddSystem(new ActionSystem(_ => Reject(() => e.Extract(a, camera, 800, 600)))); runner.Advance(1d / 60); }),
            ("metadata-and-target-negative-inputs", () => { Reject(() => new PreparedSceneAssets([MeshInfo, MeshInfo])); Reject(() => new PreparedSceneAssets([MeshInfo with { BoundsMax = new(float.NaN) }])); var d = Runtime(); var e = new RenderSceneExtractor(d.World); Reject(() => e.Extract(Assets, Guid.Empty, 0, 600)); Reject(() => e.Extract(Assets, Guid.Empty, 20000, 600)); Reject(() => e.Extract(Assets, Guid.Empty, 800, 600, new(Guid.Empty, default, Vector3.Zero, CameraData.Default))); }),
            ("skin-requires-prepared-gpu-session", () => { var d = Runtime(); global::Ncma.Runtime.Entity o = d.World.CreateEntity("Skin"); d.World.Set(o, new SkinnedMeshData(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SetId, true, true, 1)); var v = new RenderSceneExtractor(d.World).Extract(Assets, Guid.Empty, 800, 600); Check(v.Geometry.Count == 0 && v.Diagnostics.Any(x => x.Code == "skinning_unprepared")); })
        };
        RegisterAssetTests(tests);
        RegisterGeneratedDomainTests(tests);
        RegisterAssetPolicyTests(tests);
        RegisterBakeDependencyTests(tests);
        RegisterPackageTests(tests);
        RegisterEnvironmentAssetTests(tests);
        RegisterShadowSelectionTests(tests);
        RegisterStartupEntityTests(tests);
        RegisterRenderQueryTests(tests);
        tests.Add(("M3.7-A explicit character/rig/mesh/material/clip template references",PrefabRenderReferences));
        tests.Add(("M3.7-A render template composition preflight preserves source",PrefabRenderComposition));
        foreach (int count in new[] { 1, 256, 4096 }) tests.Add(($"bounded-{count}-instances", () => BatchTest(count)));
        if (args.Length != 0) {
            if (!args.SequenceEqual(new[] { "--asset-policy-tests" })) return 2;
            tests = tests.Where(t => t.Name.StartsWith("asset policy:", StringComparison.Ordinal)).ToList();
        }
        return CaseHarness.Run("Scene rendering foundation (GPU/G4 acceptance separate)", tests);
    }
    private static void BatchTest(int count)
    {
        var d = Runtime(); for (int i = 0; i < count; i++) Geometry(d);
        var browser = RenderSceneExtractor.CreateCamera(Guid.Empty, TransformData.Identity, CameraData.Default, 800, 600);
        var v = new RenderSceneExtractor(d.World).Extract(Assets, Guid.Empty, 800, 600, browser);
        Check(v.Geometry.Count == count && v.ObjectIndices.Count == count && v.Geometry.Select(x => x.FrameIndex).Distinct().Count() == count);
        Check(v.Geometry.All(x => ReferenceEquals(x.Mesh, v.Geometry[0].Mesh)), "Instances did not share immutable prepared mesh metadata.");
    }
    private static void CachedAllocationTest()
    {
        var d = Runtime(); for (int i = 0; i < 256; i++) Geometry(d); Guid camera = Camera(d);
        var e = new RenderSceneExtractor(d.World); var a = Assets; var first = e.Extract(a, camera, 800, 600);
        for (int i = 0; i < 512; i++) _ = e.Extract(a, camera, 800, 600);
        long start = GC.GetAllocatedBytesForCurrentThread(); long time = Stopwatch.GetTimestamp();
        for (int i = 0; i < 4096; i++) if (!ReferenceEquals(first, e.Extract(a, camera, 800, 600))) throw new Exception("Cache miss without changes.");
        double ms = Stopwatch.GetElapsedTime(time).TotalMilliseconds; long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Check(allocated == 0, "Cached extraction allocated: " + allocated);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        File.WriteAllText(Path.Combine(OutputRoot(), configuration + "-scene-foundation.json"), JsonSerializer.Serialize(new {
            objects = 256, cacheHits = 4096, allocatedBytes = allocated, elapsedMs = ms, gpuSceneRendering = false, g4Accepted = false }));
    }
    private sealed class ActionSystem(Action<World> action) : IWorldSystem
    { public void FixedUpdate(World world, double fixedDeltaSeconds) => action(world); }
}
