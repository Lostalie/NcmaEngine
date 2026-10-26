using System.Text;
using System.Text.Json;
using Ncma.AI;

internal static class AnimationDraftTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Animation model contract assertion"); }
    private static void Reject(Action action) {
        try { action(); } catch (Exception e) when (e is ArgumentException or AiProtocolException or JsonException or InvalidOperationException) { return; }
        throw new Exception("Expected animation draft rejection");
    }
    private static byte[] Draft(object input) => JsonSerializer.SerializeToUtf8Bytes(new { version = 1, capability = AiAnimationDraft.Capability, input });
    internal static IEnumerable<(string, Action)> All()
    {
        yield return ("C2 animation draft owns copied operations, explicit graph only", () => {
            Guid graph = Guid.NewGuid(); byte[] bytes = Draft(new { graphId = graph, operations = new[] { new { op = "graph.rename", name = "AI 动画 😀" } } });
            var draft = AiAnimationDraft.Decode(bytes); bytes.AsSpan().Fill(0);
            Check(draft.GraphId == graph && draft.CopyOperations()[0].GetProperty("name").GetString() == "AI 动画 😀");
        });
        yield return ("C2 animation rejects supplied handles paths authority duplicate fields and old commit tags", () => {
            Guid graph = Guid.NewGuid(); object[] ops = [new { op = "graph.rename", name = "draft" }];
            foreach (byte[] bytes in new[] {
                Draft(new { graphId = graph, operations = ops, proposalId = Guid.NewGuid() }),
                Draft(new { graphId = graph, operations = ops, path = "assets/other.ncmaanim" }),
                Draft(new { graphId = graph, operations = ops, approved = true }),
                Draft(new { graphId = Guid.Empty, operations = ops }),
                Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Draft(new { graphId = graph, operations = ops })).Replace("\"version\":1", "\"version\":1,\"version\":1")),
                Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Draft(new { graphId = graph, operations = ops })).Replace(AiAnimationDraft.Capability, "ncma.animgraph.transaction"))
            }) Reject(() => AiAnimationDraft.Decode(bytes));
        });
        yield return ("C2 animation original 64 operation and 48KiB budgets retained", () => {
            Guid graph = Guid.NewGuid();
            _ = AiAnimationDraft.Decode(Draft(new { graphId = graph, operations = Enumerable.Range(0, 64).Select(i => new { op = "graph.rename", name = i.ToString() }).ToArray() }));
            foreach (int count in new[] { 0, 65 }) Reject(() => AiAnimationDraft.Decode(Draft(new { graphId = graph, operations = Enumerable.Range(0, count).Select(i => new { op = "graph.rename", name = "x" }).ToArray() })));
            Reject(() => AiAnimationDraft.Decode(Draft(new { graphId = graph, operations = new[] { new { op = "graph.rename", name = new string('x', 49152) } } })));
            Reject(() => AiAnimationDraft.Decode(Draft(new { graphId = graph, operations = new[] { new { notAnOperation = "x" } } })));
        });
    }
}
