using Ncma.Assets;
using Ncma.Runtime;
using Ncma.Scene.Rendering;

internal static partial class Program
{
    private static SkinnedMeshData QuerySkin => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SetId, true, true, uint.MaxValue);
    private static void RegisterRenderQueryTests(List<(string Name, Action Run)> tests)
    {
        tests.Add(("E4-C optional unregistered settings and geometry stay empty", () => {
            var w = new World(); var reader = new SceneComponentReader<SceneShadowData>(w);
            Check(reader.ReadSingle(w.Identity, out var value) == 0 && value == default);
            Check(SceneShadowState.Read(reader, w.Identity) is null);
            Check(ScenePostProcessState.Read(w, w.Identity) == PostProcessData.Off);
            Check(SceneEnvironmentState.Read(w, w.Identity) == EnvironmentLightingData.Off);
            var extractor = new RenderSceneExtractor(w);
            Check(extractor.Extract(Assets, Guid.Empty, 800, 600).Geometry.Count == 0);
        }));
        tests.Add(("E4-C singleton counts retain duplicate Off rejection", () => {
            var w = Document().World;
            var env = new SceneComponentReader<EnvironmentLightingData>(w);
            var post = new SceneComponentReader<PostProcessData>(w);
            var shadow = new SceneComponentReader<SceneShadowData>(w);
            var a = w.CreateEntity("settings");
            w.Set(a, EnvironmentLightingData.Off); w.Set(a, PostProcessData.Off); w.Set(a, new SceneShadowData(1, SceneShadowQuality.Off));
            Check(SceneEnvironmentState.Read(env, w.Identity) == EnvironmentLightingData.Off);
            Check(ScenePostProcessState.Read(post, w.Identity) == PostProcessData.Off);
            Check(SceneShadowState.Read(shadow, w.Identity)?.Quality == SceneShadowQuality.Off);
            var b = w.CreateEntity("duplicate");
            w.Set(b, EnvironmentLightingData.Off); w.Set(b, PostProcessData.Off); w.Set(b, new SceneShadowData(1, SceneShadowQuality.Off));
            Check(shadow.ReadSingle(w.Identity, out var none) == 2 && none == default);
            Reject(() => SceneEnvironmentState.Read(env, w.Identity));
            Reject(() => ScenePostProcessState.Read(post, w.Identity));
            Reject(() => SceneShadowState.Read(shadow, w.Identity));
            w.Destroy(a); Check(shadow.ReadSingle(w.Identity, out _) == 1);
            w.Destroy(b); Check(SceneShadowState.Read(shadow, w.Identity) is null);
        }));
        tests.Add(("E4-C singleton values are not cached by membership", () => {
            var w = Document().World; var e = w.CreateEntity("settings"); w.Set(e, SceneShadowData.Default);
            var reader = new SceneComponentReader<SceneShadowData>(w); Check(reader.ReadSingle(w.Identity, out var before) == 1);
            ulong membership = w.MembershipVersion;
            w.Set(e, new SceneShadowData(1, SceneShadowQuality.High));
            Check(w.MembershipVersion == membership && SceneShadowState.Read(reader, w.Identity)?.Quality == SceneShadowQuality.High);
            Check(before == SceneShadowData.Default);
        }));
        tests.Add(("E4-C reader rejects old resource identity before rebuilding", () => {
            var w = Document().World; var e = w.CreateEntity("settings"); w.Set(e, SceneShadowData.Default);
            var reader = new SceneComponentReader<SceneShadowData>(w); Guid old = w.Identity;
            Check(reader.ReadSingle(old, out _) == 1); var snapshot = w.CaptureSnapshot(); w.RestoreSnapshot(snapshot);
            Reject(() => reader.ReadSingle(old, out _));
            Check(reader.ReadSingle(w.Identity, out var value) == 1 && value == SceneShadowData.Default);
            var other = Document().World; Reject(() => reader.ReadSingle(other.Identity, out _));
        }));
        tests.Add(("E4-C reader owner and committed boundary remain checked", () => {
            var w = Document().World; var reader = new SceneComponentReader<SceneShadowData>(w); Guid identity = w.Identity;
            Check(reader.ReadSingle(identity, out _) == 0);
            Task.Run(() => Reject(() => reader.ReadSingle(identity, out _))).GetAwaiter().GetResult();
            var runner = new WorldRunner(w);
            runner.AddSystem(new ActionSystem(world => Reject(() => reader.ReadSingle(identity, out _))));
            runner.Advance(1d / 60); Check(!runner.IsFaulted);
        }));
        tests.Add(("E4-C query capacity grows without truncating 4096 members", () => {
            var w = new World(); var reader = new SceneComponentReader<TransformData>(w);
            Check(reader.ReadSingle(w.Identity, out _) == 0);
            for (int i = 1; i <= World.MaxObjects; i++) {
                var e = w.CreateEntity("member"); w.Set(e, TransformData.Identity);
                if (i <= 34 || (i & (i - 1)) == 0 || i == World.MaxObjects)
                    Check(reader.ReadSingle(w.Identity, out _) == i);
            }
        }));
        tests.Add(("E4-C warm single-value reads allocate no query plan", () => {
            var w = new World(); var e = w.CreateEntity("member"); w.Set(e, TransformData.Identity);
            var reader = new SceneComponentReader<TransformData>(w); Guid identity = w.Identity;
            for (int i = 0; i < 512; i++) reader.ReadSingle(identity, out _);
            long readStart = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4096; i++) Check(reader.ReadSingle(identity, out var value) == 1 && value.Scale.X == 1);
            long readBytes = GC.GetAllocatedBytesForCurrentThread() - readStart;
            long equalityStart = GC.GetAllocatedBytesForCurrentThread();
            Check(TransformData.Identity == TransformData.Identity);
            long equalityBytes = GC.GetAllocatedBytesForCurrentThread() - equalityStart;
            // Warm the entire measured expression, including generated record equality comparers.
            for (int i = 0; i < 512; i++) Check(reader.ReadSingle(identity, out var value) == 1 && value == TransformData.Identity);
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4096; i++) Check(reader.ReadSingle(identity, out var value) == 1 && value == TransformData.Identity);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
            Console.WriteLine($"E4-C allocation isolation: reader={readBytes}; first record equality={equalityBytes}; warm full expression={bytes}");
            Check(readBytes == 0 && bytes == 0); // Reader only, not whole-frame/performance acceptance.
        }));
        tests.Add(("E4-C geometry union preserves creation order and malformed members", () => {
            var w = Document().World; var query = new SceneGeometryQuery(w);
            var a = w.CreateEntity("skin", Guid.Parse("00000003-0000-0000-0000-000000000000")); w.Set(a, QuerySkin);
            var b = w.CreateEntity("static", Guid.Parse("00000001-0000-0000-0000-000000000000")); w.Set(b, Mesh);
            var c = w.CreateEntity("conflict", Guid.Parse("00000002-0000-0000-0000-000000000000")); w.Set(c, Mesh); w.Set(c, QuerySkin);
            Check(query.Read(w.Identity).SequenceEqual(new[] { a, b, c }));
            w.Destroy(b); var replacement = w.CreateEntity("reused slot"); w.Set(replacement, Mesh);
            Check(query.Read(w.Identity).SequenceEqual(new[] { a, c, replacement }));
        }));
        tests.Add(("E4-C mixed geometry structural oracle and restore", () => {
            var w = Document().World; var query = new SceneGeometryQuery(w); var random = new Random(41020);
            for (int step = 0; step < 240; step++) {
                var all = w.GetEntities();
                if (all.Count == 0 || all.Count < 80 && random.Next(3) == 0) w.CreateEntity("geometry");
                else { var e = all[random.Next(all.Count)]; switch (random.Next(5)) {
                    case 0: w.Destroy(e); break;
                    case 1: w.Set(e, Mesh); break;
                    case 2: w.Set(e, QuerySkin); break;
                    case 3: if (w.Has<StaticMeshData>(e)) w.Remove<StaticMeshData>(e); break;
                    case 4: if (w.Has<SkinnedMeshData>(e)) w.Remove<SkinnedMeshData>(e); break;
                }}
                if (step % 31 == 0) { Guid old = w.Identity; w.RestoreSnapshot(w.CaptureSnapshot()); Reject(() => { query.Read(old); }); }
                Check(query.Read(w.Identity).SequenceEqual(w.GetEntities().Where(e => w.Has<StaticMeshData>(e) || w.Has<SkinnedMeshData>(e)).ToArray()));
            }
        }));
        tests.Add(("E4-C mixed geometry 4096 union does not duplicate or truncate", () => {
            var w = Document().World; var query = new SceneGeometryQuery(w); var skin = QuerySkin;
            Check(query.Read(w.Identity).Length == 0);
            for (int i = 0; i < World.MaxObjects; i++) {
                var e = w.CreateEntity("mixed geometry");
                if (i % 3 != 0) w.Set(e, Mesh);
                if (i % 3 != 1) w.Set(e, skin);
                if ((i & (i + 1)) == 0) Check(query.Read(w.Identity).Length == i + 1);
            }
            Check(query.Read(w.Identity).SequenceEqual(w.GetEntities().ToArray()));
        }));
        tests.Add(("E4-C geometry cache is membership-only and warm allocation-free", () => {
            var d = Document(); var e = Geometry(d); var query = new SceneGeometryQuery(d.World); Guid identity = d.World.Identity;
            for (int i = 0; i < 512; i++) { query.Read(identity); }
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4096; i++) Check(query.Read(identity).Length == 1);
            Check(GC.GetAllocatedBytesForCurrentThread() == start);
            ulong membership = d.World.MembershipVersion;
            d.World.Set(e, Mesh with { Visible = false }); Check(d.World.MembershipVersion == membership && query.Read(identity)[0] == e);
            Check(new RenderSceneExtractor(d.World).Extract(Assets, Camera(d), 800, 600).Geometry.Count == 0);
        }));
        tests.Add(("E4-C extraction retains diagnostic order and invalid light handling", () => {
            var d = Document(); var w = d.World;
            var missing = w.CreateEntity("no transform"); w.Set(missing, Mesh);
            var conflict = w.CreateEntity("conflict"); w.Set(conflict, Mesh); w.Set(conflict, QuerySkin);
            var skin = w.CreateEntity("unprepared"); w.Set(skin, QuerySkin);
            var light = w.CreateEntity("invalid primary"); w.Set(light, DirectionalLightData.Default); Light(d);
            var view = new RenderSceneExtractor(w).Extract(Assets, Guid.NewGuid(), 800, 600);
            Check(view.PrimaryLight is null && view.Geometry.Count == 0 && view.ShadowCasters.Count == 0);
            Check(view.Diagnostics.Select(x => x.Code).SequenceEqual(new[] { "camera_missing", "light_invalid", "multiple_primary_lights", "transform_missing", "geometry_conflict", "skinning_unprepared" }));
        }));
        tests.Add(("E4-C frame indices follow creation order after deletion and recreation", () => {
            var d = Document(); var a = Geometry(d); var b = Geometry(d); var c = Geometry(d);
            Guid camera = Camera(d); Light(d); var assets = Assets; var extractor = new RenderSceneExtractor(d.World);
            Check(extractor.Extract(assets, camera, 800, 600).Geometry.Select(x => x.ObjectId).SequenceEqual(new[] { a, b, c }.Select(d.World.GetUuid)));
            d.World.Destroy(a); var fresh = Geometry(d);
            var view = extractor.Extract(assets, camera, 800, 600);
            Check(view.Geometry.Select(x => x.ObjectId).SequenceEqual(new[] { b, c, fresh }.Select(d.World.GetUuid)));
            Check(view.Geometry.Select(x => x.FrameIndex).SequenceEqual(new[] { 0, 1, 2 }));
            Check(view.ShadowCasters.Select(x => x.ObjectId).SequenceEqual(view.Geometry.Select(x => x.ObjectId)));
        }));
    }
}
