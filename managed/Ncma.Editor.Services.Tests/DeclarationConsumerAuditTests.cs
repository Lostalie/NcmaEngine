using System.Text;
using System.Text.Json;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Scripting;

internal static unsafe class DeclarationConsumerAuditTests
{
    private readonly record struct Plain(float Value) : IComponent;
    private static void Check(bool ok) { if (!ok) throw new Exception("Declaration consumer audit assertion"); }
    private static string Label(EditorPresenter v, GuiItem i) => Encoding.UTF8.GetString(v.Text.Slice((int)i.LabelOffset, (int)i.LabelLength));
    private static GuiItem Item(EditorPresenter v, string label) => v.Items.ToArray().Single(i => Label(v, i) == label);
    private static GuiEvent Event(EditorPresenter v, GuiItem i, uint phase, int length = 0, double value = 0) => new() {
        Kind = i.Kind, WidgetHigh = i.WidgetHigh, WidgetLow = i.WidgetLow, Phase = phase, Value = value, TextLength = (uint)length,
        Frame = v.Frame.Frame, ViewGeneration = v.Frame.ViewGeneration, DocumentGeneration = v.Frame.DocumentGeneration, Revision = v.Frame.Revision
    };
    private static void Enter(EditorPresenter v, string label, string text) {
        var i = Item(v, label); var bytes = Encoding.UTF8.GetBytes(text);
        v.Apply([Event(v, i, 1), Event(v, i, 3, bytes.Length)], bytes);
    }
    private static ComponentRegistry Registry() { var r = global::Ncma.Runtime.CompositionRegistry.Build(); CameraData.Generated.Register(r); DirectionalLightData.Generated.Register(r); return r; }
    internal static IEnumerable<(string, Action)> Cases(string root)
    {
        yield return ("E6-B3 audit scalar Inspector AI declarations serialized values and history agree", () => {
            using var owner = new EditorSessionOwner("audit", activateEditor: false, components: Registry());
            var id = owner.Document.CreateObject("camera"); owner.Document.SetComponent(id, CameraData.Default); owner.Document.SetComponent(id, DirectionalLightData.Default);
            owner.ActivateEditor(); var workspace = new EditorWorkspace(owner); workspace.Select(workspace.Stamp, id);
            var view = new EditorPresenter(workspace, null); view.Build(1, 1280, 720);
            var edit = owner.Edit!; var result = edit.Invoke(new(EditSession.ContractVersion, Guid.NewGuid(), edit.SessionId, edit.Revision,
                "ncma.engine.component_types", JsonSerializer.SerializeToElement(new { typeId = CameraData.TypeId })));
            Check(result.Status == "ok"); var metadata = owner.Document.Components.Describe(CameraData.TypeId).Metadata!;
            foreach (var field in result.Data.GetProperty("fieldPage").GetProperty("items").EnumerateArray()) {
                var declared = metadata.Field(field.GetProperty("fieldId").GetString()!);
                Check(Item(view, CameraData.TypeId + " / " + declared.MemberPath).Kind == (uint)GuiItemKind.Text);
                Check(field.GetProperty("jsonPath").GetString() == declared.JsonPath && !field.TryGetProperty("defaultValue", out _));
            }
            int undo = edit.State.UndoCount; Enter(view, CameraData.TypeId + " / LayerMask", "2147483648");
            Check(owner.Document.GetComponent<CameraData>(id).LayerMask == 2147483648u && edit.State.UndoCount == undo + 1);
            view.Build(2, 1280, 720); Enter(view, CameraData.TypeId + " / Far", "5000");
            Check(owner.Document.GetComponent<CameraData>(id).Far == 5000);
            view.Build(3, 1280, 720); byte[] before = owner.Document.CaptureBytes(); Enter(view, CameraData.TypeId + " / Projection", "1.5");
            Check(before.SequenceEqual(owner.Document.CaptureBytes()));
            view.Build(4, 1280, 720); var checkbox = Item(view, DirectionalLightData.TypeId + " / CastShadow");
            view.Apply([Event(view, checkbox, 1), Event(view, checkbox, 3, value: 0)], []);
            Check(!owner.Document.GetComponent<DirectionalLightData>(id).CastShadow);
            workspace.History(workspace.Stamp, false); Check(owner.Document.GetComponent<DirectionalLightData>(id).CastShadow);
            var copy = new SceneDocument(components: Registry()); copy.RestoreBytes(owner.Document.CaptureBytes());
            Check(copy.CaptureBytes().SequenceEqual(owner.Document.CaptureBytes()) && owner.Play is null && owner.Endpoint is null);
        });
        yield return ("E6-B3 audit unannotated JSON fallback and declared readonly fields do not invent controls", () => {
            var r = global::Ncma.Runtime.CompositionRegistry.Build(); var original = Registry().Describe(CameraData.TypeId);
            r.Register(CameraData.TypeId, 1, original.Schema.GetRawText(), CameraData.Validate,
                fields: CameraData.Generated.DescribeFields().Select(f => f.FieldId == "near" ? f with { Flags = ComponentFieldFlags.Persistent | ComponentFieldFlags.RuntimeRead } : f).ToArray(), defaultFactory: () => CameraData.Default);
            r.Register<Plain>("audit.plain", 1, """{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"number"}}}""", v => v);
            using var owner = new EditorSessionOwner("audit", activateEditor: false, components: r); var id = owner.Document.CreateObject("camera");
            owner.Document.SetComponent(id, CameraData.Default); owner.Document.SetComponent(id, new Plain(4)); owner.ActivateEditor();
            var w = new EditorWorkspace(owner); w.Select(w.Stamp, id); var v = new EditorPresenter(w, null); v.Build(1, 1280, 720);
            Check(Item(v, CameraData.TypeId + " / Near").Enabled == 0 && Item(v, "Component JSON").Enabled == 1);
            var far = Item(v, CameraData.TypeId + " / Far"); var stale = Event(v, far, 3, 4); owner.Edit!.SetFrozen(true); v.Build(2, 1280, 720);
            var bytes = owner.Document.CaptureBytes(); v.Apply([stale], Encoding.UTF8.GetBytes("9000"));
            Check(bytes.SequenceEqual(owner.Document.CaptureBytes()) && Item(v, CameraData.TypeId + " / Far").Enabled == 0);
        });
        yield return ("E6-B3 audit Export Inspector uses catalog labels exact numeric contract and original history", () => {
            using var catalog = new ScriptCatalogService(); using (var c = catalog.LoadCandidate(Path.Combine(root, "out/managed/Ncma.Gameplay.Sample.dll"))) catalog.CommitCandidate(c);
            using var owner = new EditorSessionOwner("exports", catalog, activateEditor: false); var id = owner.Document.CreateObject("script"); var type = catalog.Snapshot.Types.Single();
            owner.Document.SetBindings(id, [new(Guid.NewGuid(), type.TypeName, true, type.Exports.Select(e => new ExportData(e.Name, (ExportKind)e.Kind, e.DefaultValue)).ToArray())]);
            owner.ActivateEditor(); var w = new EditorWorkspace(owner); w.Select(w.Stamp, id); var v = new EditorPresenter(w, null); v.Build(1, 1280, 720);
            Check(Item(v, "Rotation / Speed").Kind == (uint)GuiItemKind.Text && Item(v, "Rotation / Clockwise").Kind == (uint)GuiItemKind.Checkbox);
            var edit = owner.Edit!; var query = edit.Invoke(new(EditSession.ContractVersion, Guid.NewGuid(), edit.SessionId, edit.Revision,
                "ncma.engine.behaviour_types", JsonSerializer.SerializeToElement(new { })));
            Check(query.Status == "ok" && query.Data.GetProperty("catalogGeneration").GetGuid() == catalog.Snapshot.Generation);
            var remote = query.Data.GetProperty("items").EnumerateArray().Single();
            foreach (var declared in remote.GetProperty("exports").EnumerateArray()) {
                var original = type.Exports.Single(e => e.Name == declared.GetProperty("name").GetString());
                Check(declared.GetProperty("kind").GetUInt32() == original.Kind && declared.GetProperty("defaultValue").GetDouble() == original.DefaultValue);
                Check(Item(v, declared.GetProperty("category").GetString() + " / " + declared.GetProperty("displayName").GetString()).Enabled != 0);
            }
            Check(!query.Data.GetRawText().Contains("ElapsedSeconds"));
            int undo = owner.Edit!.State.UndoCount; Enter(v, "Rotation / Multiplier", "2147483647");
            Check(owner.Document.CaptureSnapshot().Objects.Single().Behaviours.Single().Exports.Single(e => e.Name == "Multiplier").Value == int.MaxValue && owner.Edit.State.UndoCount == undo + 1);
            v.Build(2, 1280, 720); var before = owner.Document.CaptureBytes(); Enter(v, "Rotation / Multiplier", "1.5"); Check(before.SequenceEqual(owner.Document.CaptureBytes()));
            v.Build(3, 1280, 720); Enter(v, "Rotation / Speed", "5000");
            Check(owner.Document.CaptureSnapshot().Objects.Single().Behaviours.Single().Exports.Single(e => e.Name == "DegreesPerSecond").Value == 5000);
            w.History(w.Stamp, false); Check(before.SequenceEqual(owner.Document.CaptureBytes()));
            var copy = new SceneDocument(); copy.RestoreBytes(before); Check(before.SequenceEqual(copy.CaptureBytes()));
        });
        yield return ("E6-B3 audit catalog replacement before next frame cancels old Export and attach actions", () => {
            string path = Path.Combine(root, "out/managed/Ncma.Gameplay.Sample.dll");
            using var catalog = new ScriptCatalogService(); using (var c = catalog.LoadCandidate(path)) catalog.CommitCandidate(c);
            using var owner = new EditorSessionOwner("catalog", catalog, activateEditor: false); var id = owner.Document.CreateObject("script"); var type = catalog.Snapshot.Types.Single();
            owner.Document.SetBindings(id, [new(Guid.NewGuid(), type.TypeName, true, [new("Multiplier", ExportKind.Integer, 1)])]); owner.ActivateEditor();
            var w = new EditorWorkspace(owner); w.Select(w.Stamp, id); var v = new EditorPresenter(w, null); v.Build(1, 1280, 720);
            var field = Item(v, "Rotation / Multiplier"); v.Apply([Event(v, field, 1)], []);
            var oldEdit = Event(v, field, 3, 1); var oldAttach = Event(v, Item(v, "Attach " + type.TypeName), 3); byte[] before = owner.Document.CaptureBytes();
            using (var c = catalog.LoadCandidate(path)) catalog.CommitCandidate(c);
            v.Apply([oldEdit, oldAttach], Encoding.UTF8.GetBytes("9"));
            Check(before.SequenceEqual(owner.Document.CaptureBytes()) && !w.HasDraft);
            owner.RefreshCatalog(); v.Build(2, 1280, 720); Enter(v, "Rotation / Multiplier", "9");
            Check(owner.Document.CaptureSnapshot().Objects.Single().Behaviours.Single().Exports.Single().Value == 9);
            v.Build(3, 1280, 720); var staleAttach = Event(v, Item(v, "Attach " + type.TypeName), 3); before = owner.Document.CaptureBytes();
            using (var c = catalog.LoadCandidate(path)) catalog.CommitCandidate(c);
            owner.RefreshCatalog(); v.Apply([staleAttach], []); Check(before.SequenceEqual(owner.Document.CaptureBytes()));
            catalog.Clear(); owner.RefreshCatalog(); v.Build(4, 1280, 720);
            Check(v.Items.ToArray().Any(i => Label(v, i) == "Multiplier (Export declaration unavailable)"));
            Check(!v.Items.ToArray().Any(i => Label(v, i) == "Rotation / Multiplier"));
        });
    }
}
