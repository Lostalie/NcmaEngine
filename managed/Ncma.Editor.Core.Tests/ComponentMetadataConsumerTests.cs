using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Runtime;
using Ncma.Scene;

internal static class ComponentMetadataConsumerTests
{
    private readonly record struct Extra(float Value) : IComponent;
    private static void Check(bool value) { if (!value) throw new Exception("E6-B3 component metadata consumer assertion"); }
    private static CapabilityRequest Request(EditSession edit, object input, string capability = "ncma.engine.component_types") =>
        new(EditSession.ContractVersion, Guid.NewGuid(), edit.SessionId, edit.Revision, capability, JsonSerializer.SerializeToElement(input));
    private static void Reject(Action action)
    { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; } throw new Exception("Invalid metadata access accepted"); }
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("E6-B3 declared metadata pages match original registry and exclude default/scene values", () => {
            var registry = new ComponentRegistry();
            registry.Register("ncma.transform", 1, global::Ncma.Runtime.CompositionRegistry.Build().Describe("ncma.transform").Schema.GetRawText(), TransformData.Validate,
                fields: TransformData.Generated.DescribeFields(), defaultFactory: () => TransformData.Identity with { Position = new(7654321, 0, 0) });
            var document = new SceneDocument(components: registry); document.CreateObject("PRIVATE_SCENE_VALUE");
            var edit = new EditSession(document); var before = document.CaptureBytes(); var state = edit.State;
            var ids = new List<string>();
            for (int offset = 0; offset < 13; offset += 5) {
                var result = edit.Invoke(Request(edit, new { typeId = "ncma.transform", offset, limit = 5 })); Check(result.Status == "ok" && !result.Changed);
                var page = result.Data.GetProperty("fieldPage");
                Check(page.GetProperty("available").GetBoolean() && page.GetProperty("totalCount").GetInt32() == 13);
                Check(!page.GetProperty("defaultsIncluded").GetBoolean() && !page.GetProperty("usageIsAuthority").GetBoolean());
                Check(page.GetProperty("complete").GetBoolean() == (offset == 10));
                foreach (var row in page.GetProperty("items").EnumerateArray()) {
                    string id = row.GetProperty("fieldId").GetString()!; ids.Add(id);
                    Check(row.GetProperty("jsonPath").GetString() == registry.Describe("ncma.transform").Metadata!.Field(id).JsonPath);
                    Check(row.EnumerateObject().Count() == 7 && !row.TryGetProperty("defaultValue", out _));
                }
                Check(!result.Data.GetRawText().Contains("7654321") && !result.Data.GetRawText().Contains("PRIVATE_SCENE_VALUE"));
            }
            Check(ids.SequenceEqual(registry.Describe("ncma.transform").Metadata!.Fields.Select(f => f.FieldId)));
            Check(before.SequenceEqual(document.CaptureBytes()) && edit.State == state && edit.Describe().Count == 10);
        });
        yield return ("E6-B3 field queries reject invalid inputs stale envelopes and never confer write permissions", () => {
            var document = new SceneDocument(); var edit = new EditSession(document); byte[] before = document.CaptureBytes();
            foreach (object input in new object[] { new { offset = 1 }, new { typeId = "missing" }, new { typeId = "ncma.transform", limit = 33 },
                new { typeId = "ncma.transform", offset = -1 }, new { typeId = "ncma.transform", defaults = true }, new { typeId = 123 } })
                Check(edit.Invoke(Request(edit, input)).Status == "error");
            var empty = edit.Invoke(Request(edit, new { typeId = "ncma.transform", offset = int.MaxValue }));
            Check(empty.Status == "ok" && empty.Data.GetProperty("fieldPage").GetProperty("returnedCount").GetInt32() == 0);
            var stale = Request(edit, new { typeId = "ncma.transform" });
            Check(edit.Invoke(stale with { SessionId = Guid.NewGuid() }).Status == "conflict");
            Check(edit.Invoke(stale with { ExpectedRevision = edit.Revision + 1 }).Status == "conflict");
            Check(edit.Invoke(Request(edit, new { operations = new[] { new { op = "create", objectId = Guid.NewGuid(), name = "Denied" } } }, "ncma.scene.transaction")).Status == "denied");
            Check(before.SequenceEqual(document.CaptureBytes()) && edit.State.UndoCount == 0);
        });
        yield return ("E6-B3 declared default serializes through original transaction roundtrip and history", () => {
            var document = new SceneDocument(); Guid id = document.CreateObject("Object"); var edit = new EditSession(document);
            var registry = document.Components; var initial = registry.DefaultSnapshot("ncma.transform", 1);
            var input = new { operations = new[] { new { op = "set_component", objectId = id, typeId = initial.TypeId, version = initial.Version, data = initial.Data } } };
            var permissions = new CapabilityPermissions(["ncma.scene.transaction", "ncma.history.undo", "ncma.history.redo"]);
            Check(edit.Invoke(Request(edit, input, "ncma.scene.transaction")).Status == "denied");
            Check(edit.Invoke(Request(edit, input, "ncma.scene.transaction"), permissions).Status == "ok");
            var bytes = document.CaptureBytes(); var copy = new SceneDocument(components: registry); copy.RestoreBytes(bytes);
            Check(bytes.SequenceEqual(copy.CaptureBytes()) && copy.GetComponent<TransformData>(id) == TransformData.Identity);
            Check(edit.Invoke(Request(edit, new { }, "ncma.history.undo"), permissions).Status == "ok" && !document.HasComponent<TransformData>(id));
            Check(edit.Invoke(Request(edit, new { }, "ncma.history.redo"), permissions).Status == "ok" && bytes.SequenceEqual(document.CaptureBytes()));
            var metadata = registry.Describe("ncma.transform").Metadata!;
            Check(metadata.ReadSerializedField("scale.x", initial.Data).GetDouble() == 1);
            Reject(() => metadata.ReadSerializedField("not-a-field", initial.Data));
            Reject(() => metadata.ReadSerializedField("scale.x", JsonSerializer.SerializeToElement(new { })));
            Reject(() => registry.DefaultSnapshot("ncma.transform", 2));
        });
        yield return ("E6-B3 missing declarations are explicit rather than guessed IDs or defaults", () => {
            var registry = global::Ncma.Runtime.CompositionRegistry.Build();
            registry.Register<Extra>("plain", 1, """{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"number"}}}""", v => v);
            var edit = new EditSession(new SceneDocument(components: registry));
            var result = edit.Invoke(Request(edit, new { typeId = "plain" }));
            Check(result.Status == "ok" && !result.Data.GetProperty("fieldPage").GetProperty("available").GetBoolean());
            Check(result.Data.GetProperty("fieldPage").GetProperty("items").GetArrayLength() == 0);
            Reject(() => registry.DefaultSnapshot("plain", 1));
            Check(!registry.TryDescribe("unknown", out _) && registry.TryDescribe("plain", out var found) && found.Metadata is null);
            var schema = edit.Describe().Single(c => c.Name == "ncma.engine.component_types");
            Check(schema.Risk == MutationRisk.ReadOnly && schema.InputSchema.GetProperty("properties").GetProperty("limit").GetProperty("maximum").GetInt32() == 32);
            Check(schema.OutputSchema.GetRawText().Contains("defaultsIncluded"));
        });
    }
}
