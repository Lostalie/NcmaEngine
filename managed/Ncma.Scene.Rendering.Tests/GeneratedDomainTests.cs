using System.Text.Json;
using Ncma.Characters;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static partial class Program
{
    private sealed class GeneratedReadObserver(Action action) : ICommittedStepObserver
    { public void StepCommitted(World world, double delta) => action(); }
    private static ComponentRegistry GeneratedDomainRegistry() => global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Characters.Generated.ComponentModule.Module);
    private static void RegisterGeneratedDomainTests(List<(string Name, Action Run)> tests)
    {
        tests.Add(("E6-B3 generated domain schemas retain original ordered closed fields and defaults", () => {
            var r = GeneratedDomainRegistry();
            void Contract<T>(string id, T value, string[] names, string[] kinds, int flags) where T : struct, IComponent {
                // Independent pre-migration schema recipe, not the generated declaration/schema constant.
                string schema = JsonSerializer.Serialize(new { type = "object", additionalProperties = false, required = names,
                    properties = names.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => new { type = kinds[p.i] }) });
                var descriptor = r.Describe(id); var m = descriptor.Metadata!;
                Check(descriptor.Version == 1 && descriptor.Schema.GetRawText() == schema);
                Check(m.Fields.Count == names.Length && m.Fields.All(f => (int)f.Flags == flags));
                foreach (var name in names) Check(m.Field(name).JsonPath == name && m.ReadSerializedField(name, r.Encode(value)).GetRawText() == m.Field(name).DefaultValue.GetRawText());
                Check(r.Decode<T>(r.DefaultSnapshot(id, 1)).Equals(value));
                Reject(() => r.DefaultSnapshot(id, 2));
            }
            Contract(CharacterData.TypeId, CharacterData.Default,
                ["radius", "halfHeight", "footOffsetY", "maximumSlope", "stepHeight", "floorDistance", "speed", "gravity", "jumpSpeed", "turnSpeed", "category", "mask", "controlled"],
                ["number", "number", "number", "number", "number", "number", "number", "number", "number", "number", "integer", "integer", "boolean"], 7);
            Contract(CameraData.TypeId, CameraData.Default,
                ["projection", "verticalFovRadians", "orthographicHeight", "near", "far", "viewportX", "viewportY", "viewportWidth", "viewportHeight", "layerMask"],
                ["integer", "number", "number", "number", "number", "number", "number", "number", "number", "integer"], 15);
            Contract(DirectionalLightData.TypeId, DirectionalLightData.Default,
                ["red", "green", "blue", "intensity", "isPrimary", "castShadow", "layerMask"],
                ["number", "number", "number", "number", "boolean", "boolean", "integer"], 15);
            Check(r.Describe(FollowCameraData.TypeId).Metadata is null && r.Describe(StaticMeshData.TypeId).Metadata is null);
            Reject(() => r.DefaultSnapshot(FollowCameraData.TypeId, 1));
            Reject(() => CameraData.Generated.Register(r));
        }));
        tests.Add(("E6-B3 typed reads writes retain validation readonly thread foreign and generation guards", () => {
            var w = new World(components: GeneratedDomainRegistry()); var e = w.CreateEntity("generated");
            CharacterData.Generated.Write(w, e, CharacterData.Default);
            CameraData.Generated.Write(w, e, CameraData.Default);
            DirectionalLightData.Generated.Write(w, e, DirectionalLightData.Default);
            Check(CharacterData.Generated.Read(w, e) == CharacterData.Default && CameraData.Generated.Read(w, e) == CameraData.Default && DirectionalLightData.Generated.Read(w, e) == DirectionalLightData.Default);
            var query = CameraData.Generated.Query(w); Check(query.Count == 1); var revision = w.Revision;
            Reject(() => CharacterData.Generated.Write(w, e, CharacterData.Default with { Speed = float.NaN }));
            Reject(() => CameraData.Generated.Write(w, e, CameraData.Default with { Far = 0 }));
            Reject(() => DirectionalLightData.Generated.Write(w, e, DirectionalLightData.Default with { IsPrimary = false }));
            Check(w.Revision == revision);
            bool observed = false; var readRunner = new WorldRunner(w);
            readRunner.AddCommittedObserver(new GeneratedReadObserver(() => {
                Reject(() => CharacterData.Generated.Write(w, e, CharacterData.Default));
                Reject(() => CameraData.Generated.Write(w, e, CameraData.Default));
                Reject(() => DirectionalLightData.Generated.Write(w, e, DirectionalLightData.Default));
                observed = true;
            }));
            readRunner.Advance(1d / 60);
            Check(observed && w.Tick == 1 && CharacterData.Generated.Read(w, e) == CharacterData.Default && CameraData.Generated.Read(w, e) == CameraData.Default && DirectionalLightData.Generated.Read(w, e) == DirectionalLightData.Default);
            Reject(() => CameraData.Generated.Read(new World(components: GeneratedDomainRegistry()), e));
            Check(Task.Run(() => { try { CharacterData.Generated.Read(w, e); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
            w.Destroy(e); Check(query.Count == 0); Reject(() => CameraData.Generated.Read(w, e));
        }));
        tests.Add(("E6-B3 generated schedule uses original graph context and commit", () => {
            var w = new World(components: GeneratedDomainRegistry()); var e = w.CreateEntity("camera"); CameraData.Generated.Write(w, e, CameraData.Default);
            var access = CameraData.Generated.WriteAccess(w.Components); Check(access == ScheduleAccess.Write<CameraData>(w.Components));
            Check(CharacterData.Generated.ReadAccess(w.Components) == ScheduleAccess.Read<CharacterData>(w.Components));
            Check(DirectionalLightData.Generated.ReadAccess(w.Components) == ScheduleAccess.Read<DirectionalLightData>(w.Components));
            var task = new ScheduleTask("camera", 0, ScheduleAffinity.OwnerThread, [access], [], []);
            Reject(() => ScheduleGraph.Compile(w.Components, [task, task with { Id = "later", Phase = 1, Access = [CameraData.Generated.ReadAccess(w.Components)] }]));
            var runner = new WorldRunner(w); ScheduleContext? old = null;
            using var executor = new ScheduleExecutor(w, ScheduleGraph.Compile(w.Components, [task]), [new("camera", c => {
                old = c; CameraData.Generated.WriteScheduled(c, 0, CameraData.Generated.ReadScheduled(c, 0) with { Far = 500 });
            })], [CameraData.Generated.Channel()]);
            runner.ConfigureSchedule(executor);
            try { runner.Advance(1d / 60); Check(w.Tick == 1 && w.Get<CameraData>(e).Far == 500); }
            finally { runner.ClearSchedule(); }
            Check(old is not null); Reject(() => CameraData.Generated.ReadScheduled(old!, 0));
        }));
        tests.Add(("E6-B3 generated character registration still rejects runtime attachment atomically", () => {
            var w = new World(components: GeneratedDomainRegistry()); var e = w.CreateEntity("empty"); var id = w.GetUuid(e);
            var task = new ScheduleTask("attach", 0, ScheduleAffinity.OwnerThread, [CharacterData.Generated.WriteAccess(w.Components), ScheduleAccess.StructuralWrite()], [], []);
            var runner = new WorldRunner(w);
            using var executor = new ScheduleExecutor(w, ScheduleGraph.Compile(w.Components, [task]), [new("attach", c => c.Add(id, CharacterData.Default))], [CharacterData.Generated.Channel()]);
            runner.ConfigureSchedule(executor);
            try { Reject(() => runner.Advance(1d / 60)); Check(w.Tick == 0 && !w.Has<CharacterData>(e) && runner.IsFaulted); }
            finally { runner.ClearSchedule(); }
        }));
        tests.Add(("E6-B3 generated registration preserves author bytes bake isolation and actual render extraction", () => {
            var d = new SceneDocument("generated", GeneratedDomainRegistry(), CharacterComponents.RequireComposition);
            var actor = d.CreateObject("actor"); d.SetComponent(actor, TransformData.Identity); d.SetComponent(actor, CharacterData.Default);
            var camera = Camera(d); Light(d); byte[] before = d.CaptureBytes();
            var copy = new SceneDocument("copy", GeneratedDomainRegistry(), CharacterComponents.RequireComposition); copy.RestoreBytes(before);
            Check(before.SequenceEqual(copy.CaptureBytes()));
            var runtime = d.BakeRuntime(d.Revision).Image.Instantiate(GeneratedDomainRegistry());
            Check(CharacterData.Generated.Read(runtime.World, runtime.World.FindEntity(actor)) == CharacterData.Default);
            CameraData.Generated.Write(runtime.World, runtime.World.FindEntity(camera), CameraData.Default with { Far = 500 });
            var view = new RenderSceneExtractor(runtime.World).Extract(Assets, camera, 800, 600);
            Check(view.Camera!.Value.Data.Far == 500 && view.PrimaryLight!.Value.Data == DirectionalLightData.Default);
            Check(before.SequenceEqual(d.CaptureBytes()) && d.GetComponent<CameraData>(camera).Far == 1000);
        }));
    }
}
