using System.Text.Json;
using Ncma.Editor.Services;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Prefabs;

internal static class EditorPrefabTests
{
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Prefab editor assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException) { return; }
        throw new InvalidOperationException("Expected prefab editor rejection.");
    }
    private static ComponentRegistry Registry()
    {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<AssetValue>("game.asset_value", 1,
            """{"type":"object","additionalProperties":false,"required":["asset"],"properties":{"asset":{"type":"string"}}}""", v => v);
        registry.Register<Label>("game.label", 1,
            """{"type":"object","additionalProperties":false,"required":["text"],"properties":{"text":{"type":"string"}}}""", v => v);
        return registry;
    }
    private static PrefabPolicy Policy(ComponentRegistry registry) => new PrefabPolicy(registry)
        .AllowComponent("game.asset_value", 1, [new("/asset", PrefabReferenceRole.Asset, "StaticMesh")])
        .AllowComponent("game.label", 1, []);
    private static (EditorWorkspace Ui, EditorPrefabWorkspace Flow, Guid[] Ids, Guid Asset) Setup(EditorSessionOwner owner)
    {
        var a = owner.Document.World.CreateObject("Spatial");
        var b = owner.Document.World.CreateObject("Empty");
        Guid asset = Guid.NewGuid(); a.Set(TransformData.Identity); a.Set(new AssetValue(asset));
        owner.ActivateEditor(); var ui = new EditorWorkspace(owner);
        return (ui, new(ui, Policy(owner.Document.World.Components)), [a.PersistentId, b.PersistentId], asset);
    }
    private static PrefabDocument Extract(EditorPrefabWorkspace flow, EditorWorkspace ui, Guid[] ids) =>
        flow.Extract(ui.Stamp, ids, Guid.NewGuid(), "Hero", new(1, 2, 3));
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("M3.7 editor exact extraction/placement inspection is read-only and detached", () => {
            using var owner = new EditorSessionOwner("Prefab", activateEditor: false, components: Registry());
            var (ui, flow, ids, asset) = Setup(owner); ui.Select(ui.Stamp, ids[0]);
            byte[] before = owner.Document.CaptureBytes(); var state = owner.Edit!.State;
            var reference = owner.Document.World.FindObject(ids[0]).Reference;
            var prefab = Extract(flow, ui, ids); byte[] bytes = PrefabDocumentCodec.Encode(prefab, Policy(owner.Document.World.Components));
            var plan = flow.InspectPlacement(ui.Stamp, bytes, 1, new(10, 20, 30), d => d == new PrefabDependency(asset, "StaticMesh"));
            Check(plan.Stamp == ui.Stamp && plan.WorldId == owner.Document.World.Identity && plan.TemplateHash.Length == 64);
            Check(plan.CandidateBytes > before.Length && plan.ReservedOperations == 6 && plan.Preview.Objects.Length == 2);
            Check(plan.Preview.Objects.Single(o => o.Name == "Empty").Components.Length == 0);
            var spatial = plan.Preview.Objects.Single(o => o.Name == "Spatial");
            Check(owner.Document.World.Components.Decode<TransformData>(spatial.Components.Single(c => c.TypeId == "ncma.transform")).Position == new System.Numerics.Vector3(9, 18, 27));
            plan.Preview.Objects[0] = plan.Preview.Objects[0] with { Name = "Caller tamper" };
            var second = flow.InspectPlacement(ui.Stamp, bytes, 1, new(0, 0, 0), _ => true);
            Check(second.Preview.Objects.All(o => o.Name != "Caller tamper") && second.Preview.InstanceId != plan.Preview.InstanceId);
            Check(owner.Edit.State == state && before.SequenceEqual(owner.Document.CaptureBytes()) && owner.Document.World.FindObject(ids[0]).Reference == reference);
            Check(!owner.Edit.Describe().Any(d => d.Name.Contains("prefab", StringComparison.OrdinalIgnoreCase)));
        });
        yield return ("M3.7 editor rejects stale/draft/Play/frozen/foreign registry and thread", () => {
            using var owner = new EditorSessionOwner("Prefab", activateEditor: false, components: Registry());
            var (ui, flow, ids, _) = Setup(owner); var old = ui.Stamp;
            ui.BeginDraft(ui.Stamp, ids[0], "Unsubmitted"); Reject(() => Extract(flow, ui, ids)); Check(ui.HasDraft); ui.CancelDraft();
            ui.Transaction(ui.Stamp, "Rename", [new { op = "rename", objectId = ids[0], name = "Changed" }]);
            Reject(() => flow.Extract(old, ids, Guid.NewGuid(), "Hero", new(0, 0, 0)));
            ui.PlayControl(ui.Stamp, "start"); Reject(() => Extract(flow, ui, ids));
            ui.PlayControl(ui.Stamp, "pause"); Reject(() => Extract(flow, ui, ids)); ui.PlayControl(ui.Stamp, "stop");
            owner.Edit!.SetFrozen(true); Reject(() => Extract(flow, ui, ids)); owner.Edit.SetFrozen(false);
            Reject(() => new EditorPrefabWorkspace(ui, Policy(Registry())));
            Task.Run(() => Reject(() => Extract(flow, ui, ids))).GetAwaiter().GetResult();
            owner.Document.World.FindObject(ids[0]).Name = "External change"; Reject(() => Extract(flow, ui, ids));
        });
        yield return ("M3.7 editor dependency/type identity/base version and callback guards", () => {
            using var owner = new EditorSessionOwner("Prefab", activateEditor: false, components: Registry());
            var (ui, flow, ids, _) = Setup(owner); var prefab = Extract(flow, ui, ids);
            byte[] bytes = PrefabDocumentCodec.Encode(prefab, Policy(owner.Document.World.Components)); byte[] before = owner.Document.CaptureBytes();
            Reject(() => flow.InspectPlacement(ui.Stamp, bytes, 2, new(0, 0, 0), _ => true));
            Reject(() => flow.InspectPlacement(ui.Stamp, bytes, 1, new(0, 0, 0), _ => false));
            Reject(() => flow.InspectPlacement(ui.Stamp, bytes, 1, new(0, 0, 0), _ => { owner.Document.World.CreateObject("Callback write"); return true; }));
            Reject(() => flow.InspectPlacement(ui.Stamp, bytes, 1, new(0, 0, 0), _ => { Extract(flow, ui, ids); return true; }));
            Reject(() => flow.InspectPlacement(ui.Stamp, bytes, 1, new(0, 0, 0), _ => { owner.Edit!.SetFrozen(true); return true; })); owner.Edit!.SetFrozen(false);
            var collision = prefab with { AssetId = ids[0] };
            byte[] colliding = PrefabDocumentCodec.Encode(collision, Policy(owner.Document.World.Components));
            Reject(() => flow.InspectPlacement(ui.Stamp, colliding, 1, new(0, 0, 0), _ => true));
            Check(before.SequenceEqual(owner.Document.CaptureBytes()) && owner.Edit.State.UndoCount == 0);
        });
        yield return ("M3.7 editor validates combined scene host composition and object capacity", () => {
            using (var owner = new EditorSessionOwner("Prefab", activateEditor: false, components: Registry(), validateComposition: s => { if (s.Objects.Length > 2) throw new ArgumentException("Host scene budget"); })) {
                var (ui, flow, ids, _) = Setup(owner); var prefab = Extract(flow, ui, ids);
                byte[] bytes = PrefabDocumentCodec.Encode(prefab, Policy(owner.Document.World.Components)); var stamp = ui.Stamp;
                Reject(() => flow.InspectPlacement(stamp, bytes, 1, new(0, 0, 0), _ => true)); Check(ui.Stamp == stamp && owner.Document.World.Count == 2);
            }
            using (var owner = new EditorSessionOwner("Full", activateEditor: false)) {
                for (int i = 0; i < World.MaxObjects; i++) owner.Document.World.CreateObject("Empty");
                owner.ActivateEditor(); var ui = new EditorWorkspace(owner); var flow = new EditorPrefabWorkspace(ui, new(owner.Document.World.Components));
                Guid id = owner.Document.CaptureSnapshot().Objects[0].Id;
                var prefab = Extract(flow, ui, [id]); byte[] bytes = PrefabDocumentCodec.Encode(prefab, new(owner.Document.World.Components)); var stamp = ui.Stamp;
                Reject(() => flow.InspectPlacement(stamp, bytes, 1, new(0, 0, 0), _ => true)); Check(ui.Stamp == stamp && owner.Document.World.Count == World.MaxObjects);
            }
        });
        yield return ("M3.7 editor complete scene byte budget rejects otherwise valid small template", () => {
            using var owner = new EditorSessionOwner("Bytes", activateEditor: false, components: Registry());
            var ids = new List<Guid>(); string text = new('x', 60000);
            for (int i = 0; i < 65; i++) { var obj = owner.Document.World.CreateObject("Large value"); obj.Set(new Label(text)); if (i < 5) ids.Add(obj.PersistentId); }
            owner.ActivateEditor(); var ui = new EditorWorkspace(owner); var flow = new EditorPrefabWorkspace(ui, Policy(owner.Document.World.Components));
            var prefab = Extract(flow, ui, ids.ToArray()); byte[] bytes = PrefabDocumentCodec.Encode(prefab, Policy(owner.Document.World.Components));
            Check(bytes.Length < World.MaxSnapshotBytes && owner.Document.CaptureBytes().Length < World.MaxSnapshotBytes); var stamp = ui.Stamp;
            Reject(() => flow.InspectPlacement(stamp, bytes, 1, new(0, 0, 0), _ => true)); Check(ui.Stamp == stamp && owner.Document.World.Count == 65);
        });
        yield return ("M3.7 editor reserves membership operations instead of splitting publication", () => {
            var registry = ComponentRegistry.CreateDefault();
            const string schema = """{"type":"object","additionalProperties":false,"properties":{}}""";
            registry.Register<C0>("game.c0", 1, schema, v => v); registry.Register<C1>("game.c1", 1, schema, v => v);
            registry.Register<C2>("game.c2", 1, schema, v => v); registry.Register<C3>("game.c3", 1, schema, v => v);
            registry.Register<C4>("game.c4", 1, schema, v => v); registry.Register<C5>("game.c5", 1, schema, v => v);
            registry.Register<C6>("game.c6", 1, schema, v => v);
            using var owner = new EditorSessionOwner("Operations", activateEditor: false, components: registry);
            var ids = new List<Guid>();
            for (int i = 0; i < 16; i++) {
                var obj = owner.Document.World.CreateObject("Values"); ids.Add(obj.PersistentId);
                obj.Set(new C0()); obj.Set(new C1()); obj.Set(new C2()); obj.Set(new C3()); obj.Set(new C4()); obj.Set(new C5()); obj.Set(new C6());
            }
            owner.ActivateEditor(); var ui = new EditorWorkspace(owner); var policy = new PrefabPolicy(registry);
            for (int i = 0; i < 7; i++) policy.AllowComponent("game.c" + i, 1, []);
            var flow = new EditorPrefabWorkspace(ui, policy); var prefab = Extract(flow, ui, ids.ToArray());
            Check(PrefabTemplates.PreviewExpansion(prefab, policy, new(0, 0, 0), []).OperationCount == 128);
            var stamp = ui.Stamp; byte[] bytes = PrefabDocumentCodec.Encode(prefab, policy);
            Reject(() => flow.InspectPlacement(stamp, bytes, 1, new(0, 0, 0), _ => true));
            Check(ui.Stamp == stamp && owner.Document.World.Count == 16 && owner.Edit!.State.UndoCount == 0);
        });
    }
    private readonly record struct AssetValue(Guid Asset) : IComponent;
    private readonly record struct Label(string Text) : IComponent;
    private readonly record struct C0 : IComponent;
    private readonly record struct C1 : IComponent;
    private readonly record struct C2 : IComponent;
    private readonly record struct C3 : IComponent;
    private readonly record struct C4 : IComponent;
    private readonly record struct C5 : IComponent;
    private readonly record struct C6 : IComponent;
}
