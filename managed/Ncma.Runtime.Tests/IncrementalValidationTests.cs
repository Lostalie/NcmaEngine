using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Runtime;

internal static class IncrementalValidationTests
{
    private readonly record struct TextData(string Text) : IComponent;
    private readonly record struct Tagged<T>(int Value) : IComponent;
    private static void RegisterTagged<T>(ComponentRegistry registry, string id) => registry.Register<Tagged<T>>(id, 1,
        """{"type":"object","required":["value"],"properties":{"value":{"type":"integer"}}}""", v => v);
    private static void SetTagged<T>(World world, Entity entity, int value) => world.Set(entity, new Tagged<T>(value));
    private struct Computed : IComponent
    {
        private int _value;
        internal static int Bias = 0;
        public int Value { [CompilerGenerated] get => _value + Bias; set => _value = value; }
    }
    [JsonConverter(typeof(DynamicConverter))]
    public readonly record struct Dynamic(int Value) : IComponent;
    public sealed class DynamicConverter : JsonConverter<Dynamic>
    {
        internal static string Text = "x";
        internal static Action? OnWrite;
        public override Dynamic Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        { using var document = JsonDocument.ParseValue(ref reader); return new(1); }
        public override void Write(Utf8JsonWriter writer, Dynamic value, JsonSerializerOptions options)
        { OnWrite?.Invoke(); writer.WriteStartObject(); writer.WriteString("text", Text); writer.WriteEndObject(); }
    }
    private static void Check(bool value) { if (!value) throw new Exception("Incremental validation assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Invalid validation candidate accepted.");
    }
    private static World TextWorld(Func<TextData, TextData>? validator = null)
    {
        var registry = new ComponentRegistry();
        registry.Register<TextData>("text", 1, """{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""", validator ?? (v => v));
        return new World("引擎 \"exact\" \\ budget", registry);
    }
    private static void CheckPrepared(World world, Action<WorldSnapshot>? inspect = null)
    {
        int bytes = 0;
        Action commit = world.PrepareStep(snapshot => {
            bytes = SceneJson.EncodeBounded(snapshot, World.MaxSnapshotBytes).Length; inspect?.Invoke(snapshot);
        });
        Check(world.PreparedSnapshotBytes == bytes); commit();
    }
    internal static int Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("internal component traversal preserves sparse membership without iterator allocations", () => {
                var storage = new ComponentStorage(); storage.Set(0, TransformData.Identity);
                storage.Set(0, new TextData("zero")); storage.Set(1, new TextData("one"));
                int Visit(int slot) { int result = 0; foreach (Type type in storage.TypesAt(slot))
                    result += type == typeof(TransformData) ? 1 : type == typeof(TextData) ? 2 : throw new Exception("Unknown type"); return result; }
                Check(Visit(0) == 3 && Visit(1) == 2 && Visit(2) == 0);
                for (int i = 0; i < 100; i++) _ = Visit(0);
                long before = GC.GetAllocatedBytesForCurrentThread(); int sum = 0;
                for (int i = 0; i < 4096; i++) sum += Visit(i % 3);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(sum == 1365 * 5 + 3 && allocated == 0);
                var branch = storage.Fork(); branch.Remove(0, typeof(TransformData));
                int count = 0; foreach (Type type in branch.TypesAt(0)) { Check(type == typeof(TextData)); count++; }
                Check(count == 1 && Visit(0) == 3);
            }),
            ("bounded derived component array preserves all 64 identities across membership changes", () => {
                var registry = new ComponentRegistry(); Type tag = typeof(int);
                var setters = new List<Action<World, Entity, int>>();
                for (int i = 0; i < World.MaxComponentsPerObject; i++) {
                    typeof(IncrementalValidationTests).GetMethod(nameof(RegisterTagged), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                        .MakeGenericMethod(tag).Invoke(null, [registry, "tag." + i.ToString("D2")]);
                    setters.Add(typeof(IncrementalValidationTests).GetMethod(nameof(SetTagged), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                        .MakeGenericMethod(tag).CreateDelegate<Action<World, Entity, int>>());
                    tag = typeof(Tagged<>).MakeGenericType(tag);
                }
                var world = new World("64 components", registry); var entity = world.CreateEntity("all"); var id = world.GetUuid(entity);
                for (int i = 0; i < setters.Count; i++) setters[i](world, entity, i);
                world.BeginStep(); CheckPrepared(world);
                world.BeginStep(); setters[63](world, entity, 999);
                world.StageRemove(id, "tag.00"); world.StageRemove(id, "tag.31");
                CheckPrepared(world, s => {
                    Check(s.Objects[0].Components.Length == 62);
                    foreach (var c in s.Objects[0].Components) {
                        int number = int.Parse(c.TypeId.AsSpan(4));
                        Check(c.Data.GetProperty("value").GetInt32() == (number == 63 ? 999 : number));
                    }
                });
                using var restored = JsonDocument.Parse("{\"value\":777}");
                world.BeginStep(); world.StageAdd(id, new("tag.00", 1, restored.RootElement.Clone()));
                _ = world.PrepareStep(); world.AbortStep();
                world.BeginStep(); CheckPrepared(world, s => Check(s.Objects[0].Components.Length == 62));
                world.BeginStep(); world.StageAdd(id, new("tag.00", 1, restored.RootElement.Clone()));
                CheckPrepared(world, s => Check(s.Objects[0].Components.Length == 63 && s.Objects[0].Components[0].Data.GetProperty("value").GetInt32() == 777));
            }),
            ("lazy validation DTO copies retain ordering budget and observer isolation", () => {
                var registry = ComponentRegistry.CreateDefault();
                registry.Register<TextData>("a.text", 1,
                    """{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""", v => v);
                var world = new World("lazy DTO", registry); var a = world.CreateEntity("first"); var b = world.CreateEntity("second");
                world.Set(a, TransformData.Identity); world.Set(a, new TextData("中文\\\"")); world.Set(b, TransformData.Identity);
                world.FixedStep(() => { }); long copies = world.ValidationWork.CopiedSnapshots;
                world.BeginStep(); world.Set(a, TransformData.Identity with { Position = new(3, 0, 0) });
                _ = world.PrepareStepView(view => Check(view.Objects.Count() == 2));
                Check(world.ValidationWork.CopiedSnapshots == copies);
                byte[]? first = null;
                _ = world.PrepareStep(snapshot => {
                    Check(snapshot.Objects[0].PersistentId == world.GetUuid(a) && snapshot.Objects[1].PersistentId == world.GetUuid(b));
                    Check(snapshot.Objects[0].Components.Select(c => c.TypeId).SequenceEqual(new[] { "a.text", "ncma.transform" }));
                    first = SceneJson.EncodeBounded(snapshot, World.MaxSnapshotBytes);
                    snapshot.Objects[0].Components[0] = snapshot.Objects[0].Components[1];
                    snapshot.Objects[1] = snapshot.Objects[0];
                });
                Action commit = world.PrepareStep(snapshot => Check(first!.AsSpan().SequenceEqual(SceneJson.EncodeBounded(snapshot, World.MaxSnapshotBytes))));
                Check(world.PreparedSnapshotBytes == first!.Length && world.Get<TransformData>(a).Position.X == 0);
                commit(); Check(world.Get<TransformData>(a).Position.X == 3);
                world.BeginStep(); world.Set(a, new TextData("aborted")); _ = world.PrepareStep(); world.AbortStep();
                world.BeginStep(); CheckPrepared(world, snapshot => Check(snapshot.Objects[0].Components[0].Data.GetProperty("text").GetString() == "中文\\\""));
            }),
            ("validated payload is last-write normalized and retains delayed schema rejection", () => {
                int calls = 0; var world = TextWorld(v => { calls++; return new(v.Text.Trim()); });
                var a = world.CreateEntity("A"); world.Set(a, new TextData("original")); world.FixedStep(() => { });
                int before = calls; world.BeginStep(); world.Set(a, new TextData(" first ")); _ = world.PrepareStep();
                world.Set(a, new TextData(" 中文😀\\\"\u0000\ud800 "));
                string? serialized = null; var install = world.PrepareStep(s => serialized = JsonSerializer.Serialize(s, SceneJson.Options));
                Check(world.PreparedSnapshotBytes == System.Text.Encoding.UTF8.GetByteCount(serialized!)); install();
                Check(calls == before + 2 && world.SerializeSnapshot() == serialized);
                var badRegistry = new ComponentRegistry(); badRegistry.Register<TextData>("bad", 1,
                    """{"type":"object","required":["text"],"properties":{"text":{"type":"integer"}}}""", v => v);
                var bad = new World(components: badRegistry); var b = bad.CreateEntity("B"); bad.Set(b, new TextData("before"));
                bad.BeginStep(); bad.Set(b, new TextData("after")); // Schema rejection stays at preparation, not Set.
                Reject(bad.CommitStep); bad.AbortStep(); Check(bad.Get<TextData>(b).Text == "before");
            }),
            ("validated payload branches remain isolated through discard install abort and slot reuse", () => {
                var world = TextWorld(); var a = world.CreateEntity("A"); var id = world.GetUuid(a);
                world.Set(a, new TextData("old")); world.FixedStep(() => { });
                world.BeginStep(); world.Set(a, new TextData("outer"));
                _ = world.PrepareStructuralBatch(() => world.Set(a, new TextData("discarded")), _ => { });
                CheckPrepared(world, s => Check(s.Objects[0].Components[0].Data.GetProperty("text").GetString() == "outer"));
                world.BeginStep(); world.Set(a, new TextData("before installed"));
                world.PrepareStructuralBatch(() => world.Set(a, new TextData("installed")), _ => { })();
                CheckPrepared(world, s => Check(s.Objects[0].Components[0].Data.GetProperty("text").GetString() == "installed"));
                world.BeginStep(); world.Set(a, new TextData("aborted")); _ = world.PrepareStep(); world.AbortStep();
                world.BeginStep(); CheckPrepared(world, s => Check(s.Objects[0].Components[0].Data.GetProperty("text").GetString() == "installed"));
                world.BeginStep(); world.Set(a, new TextData("must not revive")); var next = Guid.NewGuid();
                world.PrepareStructuralBatch(() => { world.StageDestroy(id); world.StageCreate(next, "new");
                    world.StageAdd(next, new("text", 1, world.Components.Encode(new TextData("replacement")))); }, _ => { })();
                var replacement = world.CandidateEntity(next); Check(replacement.Index == a.Index && replacement.Generation != a.Generation);
                CheckPrepared(world, s => Check(s.Objects[0].Components[0].Data.GetProperty("text").GetString() == "replacement"));
            }),
            ("remove then add invalidates a validated payload even within same generation", () => {
                var world = TextWorld(); var a = world.CreateEntity("A"); var id = world.GetUuid(a);
                world.Set(a, new TextData("old")); world.FixedStep(() => { }); world.BeginStep(); world.Set(a, new TextData("stale"));
                world.PrepareStructuralBatch(() => { world.StageRemove(id, "text");
                    world.StageAdd(id, new("text", 1, world.Components.Encode(new TextData("new")))); }, _ => { })();
                CheckPrepared(world, s => Check(s.Objects[0].Components[0].Data.GetProperty("text").GetString() == "new"));
                Check(world.Get<TextData>(a).Text == "new");
            }),
            ("typed candidate reads copy staged values while public reads stay committed", () => {
                var world = new World(); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity);
                var staged = TransformData.Identity with { Position = new(7, 0, 0) };
                world.BeginStep(); world.Set(a, staged);
                World.PreparedStepView? view = null; var commit = world.PrepareStepView(v => view = v);
                Check(view!.TryResolve(world.GetUuid(a), out var resolved) && resolved == a);
                Check(view.TryGet<TransformData>(a, out var copy) && copy == staged);
                copy = copy with { Position = new(999, 0, 0) }; Check(view.TryGet<TransformData>(a, out var again) && again == staged);
                Check(world.Get<TransformData>(a) == TransformData.Identity);
                commit(); Check(world.Get<TransformData>(a) == staged); Reject(() => view.TryGet<TransformData>(a, out _));
            }),
            ("typed candidate reads validate missing components and full identity", () => {
                var world = new World(); var a = world.CreateEntity("A"); var id = world.GetUuid(a);
                var foreign = new World().CreateEntity("A"); world.BeginStep();
                World.PreparedStepView? view = null; _ = world.PrepareStepView(v => view = v);
                Check(!view!.TryGet<TransformData>(a, out _) && !view.TryResolve(Guid.NewGuid(), out _));
                Reject(() => view.TryGet<TransformData>(foreign, out _)); Reject(() => view.TryGet<TransformData>(default, out _));
                world.StageDestroy(id); Reject(() => view.TryResolve(id, out _));
                _ = world.PrepareStepView(v => view = v); Check(!view!.TryResolve(id, out _));
                Reject(() => view.TryGet<TransformData>(a, out _)); world.AbortStep();
            }),
            ("typed candidate reads reject abandoned branches and foreign threads", () => {
                var world = new World(); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity); world.BeginStep();
                World.PreparedStepView? branch = null;
                _ = world.PrepareStructuralBatch(() => world.StageRename(world.GetUuid(a), "branch"), v => branch = v);
                Reject(() => branch!.TryGet<TransformData>(a, out _));
                _ = world.PrepareStepView(v => branch = v);
                Reject(() => Task.Run(() => branch!.TryGet<TransformData>(a, out _)).GetAwaiter().GetResult());
                world.AbortStep(); world.BeginStep(); _ = world.PrepareStepView();
                Reject(() => branch!.TryResolve(world.GetUuid(a), out _)); world.AbortStep();
            }),
            ("counting output equals actual JSON including escapes and exact boundary", () => {
                foreach (string text in new[] { "", "quote\"slash\\", "中文😀<>\u0000\n", new string('x', 65500) }) {
                    var value = new ObjectSnapshot(Guid.NewGuid(), "name", [new("text", 1, JsonSerializer.SerializeToElement(new { text }))]);
                    byte[] encoded = SceneJson.EncodeBounded(value, World.MaxSnapshotBytes);
                    Check(SceneJson.MeasureBounded(value, encoded.Length) == encoded.Length);
                    Reject(() => SceneJson.MeasureBounded(value, encoded.Length - 1));
                }
            }),
            ("eligibility rejects converters and spoofed compiler-generated getters", () => {
                Check(StableComponentEncoding.IsStable(typeof(TransformData)) && StableComponentEncoding.IsStable(typeof(TextData)));
                Check(!StableComponentEncoding.IsStable(typeof(Dynamic)) && !StableComponentEncoding.IsStable(typeof(Computed)));
            }),
            ("composed component depth agrees with full snapshot codec", () => {
                foreach (bool arrays in new[] { false, true })
                foreach (int depth in new[] { 1, 26, 27, 28, 31 }) {
                    string nested = arrays ? new string('[', depth) + "0" + new string(']', depth) :
                        string.Concat(Enumerable.Repeat("{\"v\":", depth)) + "0" + new string('}', depth);
                    using var json = JsonDocument.Parse(nested);
                    var snapshot = new WorldSnapshot(1, "depth", [new(Guid.NewGuid(), "A", [new("test.depth", 1, json.RootElement)])]);
                    if (depth <= 27) {
                        SceneJson.VerifySnapshotComponentDepth(json.RootElement);
                        using var decoded = JsonDocument.Parse(SceneJson.EncodeBounded(snapshot, World.MaxSnapshotBytes), new JsonDocumentOptions { MaxDepth = SceneJson.Options.MaxDepth });
                        Check(decoded.RootElement.ValueKind == JsonValueKind.Object);
                    } else {
                        Reject(() => SceneJson.VerifySnapshotComponentDepth(json.RootElement));
                        bool failed = false;
                        try {
                            using var decoded = JsonDocument.Parse(SceneJson.EncodeBounded(snapshot, World.MaxSnapshotBytes), new JsonDocumentOptions { MaxDepth = SceneJson.Options.MaxDepth });
                        }
                        catch (Exception error) when (error is JsonException or InvalidOperationException) { failed = true; }
                        Check(failed);
                    }
                }
            }),
            ("warm unchanged steps do not encode objects or construct snapshots", () => {
                var world = new World(); for (int i = 0; i < 100; ++i) world.Set(world.CreateEntity("A"), TransformData.Identity);
                world.FixedStep(() => { }); var work = world.ValidationWork;
                Check(work.EncodedObjects == 100 && work.CopiedSnapshots == 0);
                for (int i = 0; i < 20; ++i) world.FixedStep(() => { });
                Check(world.ValidationWork == work && world.Tick == 21);
            }),
            ("one dirty object is encoded once and repeated preflight reuses it", () => {
                var world = new World(); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity);
                for (int i = 0; i < 20; ++i) world.Set(world.CreateEntity("B"), TransformData.Identity);
                world.FixedStep(() => { }); long initial = world.ValidationWork.EncodedObjects;
                world.BeginStep(); world.Set(a, TransformData.Identity with { Position = new(8, 0, 0) });
                _ = world.PrepareStep(); _ = world.PrepareStep();
                Check(world.ValidationWork.EncodedObjects == initial + 1 && world.Get<TransformData>(a).Position.X == 0);
                world.CommitStep(); Check(world.ValidationWork.EncodedObjects == initial + 1 && world.Get<TransformData>(a).Position.X == 8);
            }),
            ("writes always validate even when the component encoding is cacheable", () => {
                int calls = 0; var world = TextWorld(v => { calls++; return v with { Text = v.Text.Trim() }; });
                var a = world.CreateEntity("A"); world.Set(a, new TextData(" start ")); world.FixedStep(() => { });
                int before = calls; world.FixedStep(() => { world.Set(a, new TextData(" one ")); world.Set(a, new TextData(" two ")); });
                Check(calls == before + 2 && world.Get<TextData>(a).Text == "two");
            }),
            ("dirty component does not reencode unchanged peers or renamed values", () => {
                var registry = ComponentRegistry.CreateDefault(); registry.Register<TextData>("text", 1,
                    """{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""", v => v);
                var world = new World(components: registry); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity); world.Set(a, new TextData("old"));
                world.FixedStep(() => { }); long before = world.EncodedComponentCount;
                world.BeginStep(); world.Set(a, new TextData("new")); CheckPrepared(world); Check(world.EncodedComponentCount == before + 1);
                world.BeginStep(); world.StageRename(world.GetUuid(a), "renamed"); CheckPrepared(world); Check(world.EncodedComponentCount == before + 1);
                world.BeginStep(); world.StageRemove(world.GetUuid(a), "text");
                world.StageAdd(world.GetUuid(a), new("text", 1, registry.Encode(new TextData("replacement")))); CheckPrepared(world);
                Check(world.EncodedComponentCount == before + 2 && world.Get<TextData>(a).Text == "replacement");
            }),
            ("snapshot callbacks cannot mutate cached array shells", () => {
                var world = new World(); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity); world.FixedStep(() => { });
                world.BeginStep(); _ = world.PrepareStep(snapshot => { snapshot.Objects[0].Components[0] = null!; snapshot.Objects[0] = null!; });
                CheckPrepared(world, snapshot => Check(snapshot.Objects[0].Components[0].TypeId == "ncma.transform"));
                Check(world.Get<TransformData>(a) == TransformData.Identity);
            }),
            ("prepared view rejects foreign threads writes abort and commit", () => {
                var world = new World(); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity); world.BeginStep();
                World.PreparedStepView? view = null; _ = world.PrepareStepView(v => view = v);
                Check(view is not null && view.Contains(a) && !view.Contains(default));
                var active = view!; Check(active.ComponentBytes(world.GetUuid(a), "ncma.transform") > 0);
                Reject(() => Task.Run(() => active.Bytes).GetAwaiter().GetResult());
                var cursor = active.Objects.GetEnumerator(); Check(cursor.MoveNext()); world.Set(a, TransformData.Identity);
                Reject(() => { _ = active.Bytes; }); Reject(() => cursor.MoveNext());
                _ = world.PrepareStepView(v => view = v); active = view!; world.AbortStep(); Reject(() => active.CopySnapshot());
                world.BeginStep(); var commit = world.PrepareStepView(v => view = v); active = view!; commit(); Reject(() => active.Contains(a));
            }),
            ("discarded structural preparation never installs its encoded root", () => {
                var world = new World(); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity); world.FixedStep(() => { });
                world.BeginStep(); _ = world.PrepareStep();
                _ = world.PrepareStructuralBatch(() => world.StageRename(world.GetUuid(a), "discarded"), _ => { });
                CheckPrepared(world, snapshot => Check(snapshot.Objects[0].Name == "A"));
                world.BeginStep(); world.PrepareStructuralBatch(() => world.StageRename(world.GetUuid(a), "installed"), _ => { })();
                CheckPrepared(world, snapshot => Check(snapshot.Objects[0].Name == "installed"));
            }),
            ("aborted writes do not replace committed encoded data", () => {
                var world = TextWorld(); var a = world.CreateEntity("A"); world.Set(a, new TextData("old")); world.FixedStep(() => { });
                world.BeginStep(); world.Set(a, new TextData("candidate")); _ = world.PrepareStep(); world.AbortStep();
                long before = world.ValidationWork.EncodedObjects; world.BeginStep();
                CheckPrepared(world, snapshot => Check(snapshot.Objects[0].Components[0].Data.GetProperty("text").GetString() == "old"));
                Check(world.ValidationWork.EncodedObjects == before);
            }),
            ("direct edits and restore invalidate encoded roots", () => {
                var world = TextWorld(); var a = world.CreateEntity("A"); world.Set(a, new TextData("old")); world.FixedStep(() => { });
                world.Rename(a, "B"); world.Set(a, new TextData("edit")); world.BeginStep();
                CheckPrepared(world, snapshot => Check(snapshot.Objects[0].Name == "B" && snapshot.Objects[0].Components[0].Data.GetProperty("text").GetString() == "edit"));
                var snapshot = world.CaptureSnapshot(); world.RestoreSnapshot(snapshot with { Name = "restored😀" });
                long before = world.ValidationWork.EncodedObjects; world.BeginStep(); CheckPrepared(world);
                Check(world.ValidationWork.EncodedObjects == before + 1); Reject(() => world.Resolve(a));
            }),
            ("structural batches carry dirty sets without exposing candidate caches", () => {
                var world = new World(); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity); world.FixedStep(() => { });
                var oldId = world.GetUuid(a); var newId = Guid.NewGuid(); world.BeginStep();
                world.PrepareStructuralBatch(() => { world.StageDestroy(oldId); world.StageCreate(newId, "Replacement"); }, _ => { })();
                var replacement = world.CandidateEntity(newId); Check(replacement.Index == a.Index && replacement.Generation > a.Generation);
                world.PrepareStructuralBatch(() => world.StageAdd(newId, new("ncma.transform", 1, world.Components.Encode(TransformData.Identity))), _ => { })();
                CheckPrepared(world, snapshot => Check(snapshot.Objects.Length == 1 && snapshot.Objects[0].PersistentId == newId));
                Reject(() => world.Resolve(a)); Check(world.Has<TransformData>(replacement));
            }),
            ("prepared commits and structural installs reject intervening writes", () => {
                var world = new World(); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity);
                world.BeginStep(); var first = world.PrepareStep(); world.Set(a, TransformData.Identity with { Position = new(3, 0, 0) }); Reject(first);
                var second = world.PrepareStructuralBatch(() => world.StageRename(world.GetUuid(a), "candidate"), _ => { });
                world.Set(a, TransformData.Identity with { Position = new(4, 0, 0) }); Reject(second);
                world.CommitStep(); Check(world.GetName(a) == "A" && world.Get<TransformData>(a).Position.X == 4); Reject(first);
                world.BeginStep(); var aborted = world.PrepareStep(); world.AbortStep(); world.BeginStep(); Reject(aborted); world.AbortStep();
            }),
            ("cold converters reencode on every preparation and obey mutation guard", () => {
                var registry = new ComponentRegistry(); registry.Register<Dynamic>("dynamic", 1,
                    """{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""", v => v);
                DynamicConverter.Text = "x"; DynamicConverter.OnWrite = null;
                var world = new World(components: registry); world.Set(world.CreateEntity("A"), new Dynamic(1)); world.FixedStep(() => { });
                world.BeginStep(); var prepared = world.PrepareStep(); int first = world.PreparedSnapshotBytes;
                DynamicConverter.Text = "longer"; _ = world.PrepareStep(); Check(world.PreparedSnapshotBytes == first + 5);
                DynamicConverter.OnWrite = () => world.CreateEntity("forbidden"); Reject(() => world.PrepareStep());
                DynamicConverter.OnWrite = null; Reject(prepared); Reject(world.CommitStep);
                world.AbortStep(); Check(world.Count == 1); DynamicConverter.Text = "x";
            }),
            ("computed getters cannot reuse stale encodings", () => {
                var registry = new ComponentRegistry(); registry.Register<Computed>("computed", 1,
                    """{"type":"object","required":["value"],"properties":{"value":{"type":"integer"}}}""", v => v);
                Computed.Bias = 0; var world = new World(components: registry); world.Set(world.CreateEntity("A"), new Computed { Value = 1 });
                world.FixedStep(() => { }); world.BeginStep(); _ = world.PrepareStep(); int first = world.PreparedSnapshotBytes;
                Computed.Bias = 1000; _ = world.PrepareStep(); Check(world.PreparedSnapshotBytes == first + 3);
                world.AbortStep(); Computed.Bias = 0;
            }),
            ("cold output retains per-component payload and schema checks", () => {
                var registry = new ComponentRegistry(); registry.Register<Dynamic>("dynamic", 1,
                    """{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""", v => v);
                DynamicConverter.Text = "x"; var world = new World(components: registry); world.Set(world.CreateEntity("A"), new Dynamic(1)); world.FixedStep(() => { });
                world.BeginStep(); DynamicConverter.Text = new string('x', ComponentRegistry.MaxPayloadBytes); Reject(world.CommitStep); world.AbortStep();
                DynamicConverter.Text = "x"; world.FixedStep(() => { });
                var badRegistry = new ComponentRegistry(); badRegistry.Register<TextData>("text", 1,
                    """{"type":"object","required":["text"],"properties":{"text":{"type":"integer"}}}""", v => v);
                var bad = new World(components: badRegistry); bad.Set(bad.CreateEntity("bad"), new TextData("not integer"));
                bad.BeginStep(); Reject(bad.CommitStep); bad.AbortStep(); Check(bad.Tick == 0);
            }),
            ("exact aggregate boundary rejects normalized growth without corrupting cache", () => {
                string? normalized = null; var world = TextWorld(v => normalized is null ? v : new TextData(normalized));
                for (int i = 0; i < 64; ++i) world.Set(world.CreateEntity("Full"), new TextData(new string('x', 65000)));
                var edge = world.CreateEntity("Edge"); world.Set(edge, new TextData(""));
                int remaining = World.MaxSnapshotBytes - SceneJson.EncodeBounded(world.CaptureSnapshot(), World.MaxSnapshotBytes).Length;
                Check(remaining > 0 && remaining < 65000); world.Set(edge, new TextData(new string('x', remaining)));
                world.BeginStep(); CheckPrepared(world); Check(System.Text.Encoding.UTF8.GetByteCount(world.SerializeSnapshot()) == World.MaxSnapshotBytes);
                string before = world.SerializeSnapshot(); normalized = new string('x', remaining + 1);
                world.BeginStep(); world.Set(edge, new TextData("small raw input")); Reject(world.CommitStep); world.AbortStep(); normalized = null;
                Check(world.SerializeSnapshot() == before); world.BeginStep(); CheckPrepared(world);
            }),
            ("random dirty and structural steps equal independently captured JSON", () => {
                var random = new Random(310218); var world = TextWorld();
                for (int i = 0; i < 30; ++i) world.Set(world.CreateEntity("A"), new TextData("v" + i));
                for (int i = 0; i < 160; ++i) {
                    var live = world.GetEntities(); var a = live[random.Next(live.Count)]; string old = world.SerializeSnapshot();
                    world.BeginStep(); world.Set(a, new TextData(new string('界', random.Next(50))));
                    if (i % 3 == 0) world.StageRename(world.GetUuid(a), "changed😀" + i);
                    if (i % 7 == 0) { world.StageDestroy(world.GetUuid(a)); world.StageCreate(Guid.NewGuid(), "new" + i); }
                    string? prepared = null; var commit = world.PrepareStep(s => prepared = JsonSerializer.Serialize(s, SceneJson.Options));
                    Check(world.PreparedSnapshotBytes == System.Text.Encoding.UTF8.GetByteCount(prepared!));
                    if (i % 11 == 0) { world.AbortStep(); Check(world.SerializeSnapshot() == old); }
                    else { commit(); Check(world.SerializeSnapshot() == prepared); }
                    // New empty entities are intentionally kept valid and receive their component off-frame.
                    foreach (var entity in world.GetEntities()) if (!world.Has<TextData>(entity)) world.Set(entity, new TextData("new"));
                }
            }),
        };
        int failures = 0;
        foreach (var test in cases) { try { test.Run(); Console.WriteLine("PASS incremental validation " + test.Name); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL incremental validation " + test.Name + ": " + error); }
            finally { DynamicConverter.OnWrite = null; DynamicConverter.Text = "x"; } }
        Console.WriteLine($"Incremental validation tests: {cases.Length - failures}/{cases.Length} passed."); return failures;
    }
}
