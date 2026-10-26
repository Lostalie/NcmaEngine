using System.Text.Json;
using Ncma.Modules;

namespace Ncma.AI;

// A model result tag, NOT an editor capability. It carries no prepared handle, path, grant,
// resource approval or execution instruction. Semantic validation belongs to the original editor.
public sealed class AiAnimationDraft
{
    public const string Capability = "ncma.ai.animgraph.draft";
    public const int MaximumOperations = 64;
    public const int MaximumOperationBytes = 48 * 1024;
    public static Guid OutputSchema { get; } = new("ef704084-3001-4001-8001-000000000003");
    public Guid GraphId { get; }
    private readonly JsonElement operations;
    public JsonElement CopyOperations() => operations.Clone();
    private AiAnimationDraft(Guid graph, JsonElement operations) { GraphId = graph; this.operations = operations.Clone(); }
    public static AiAnimationDraft Decode(ReadOnlySpan<byte> bytes)
    {
        var payload = new ModulePayload(bytes);
        using var doc = AiJson.Parse(payload.CopyBytes()); var root = doc.RootElement;
        AiJson.Members(root, ["version", "capability", "input"]);
        if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("capability").GetString() != Capability)
            throw new AiProtocolException("animation_draft_envelope");
        var input = root.GetProperty("input"); AiJson.Members(input, ["graphId", "operations"]);
        if (input.GetProperty("graphId").ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(input.GetProperty("graphId").GetString(), "D", out var graph) || graph == Guid.Empty ||
            input.GetProperty("graphId").GetString() != graph.ToString("D"))
            throw new AiProtocolException("animation_draft_graph");
        var ops = input.GetProperty("operations");
        if (ops.ValueKind != JsonValueKind.Array || ops.GetArrayLength() is < 1 or > MaximumOperations ||
            System.Text.Encoding.UTF8.GetByteCount(ops.GetRawText()) > MaximumOperationBytes)
            throw new AiProtocolException("animation_draft_budget");
        foreach (var op in ops.EnumerateArray())
            if (op.ValueKind != JsonValueKind.Object || !op.TryGetProperty("op", out var name) || name.ValueKind != JsonValueKind.String)
                throw new AiProtocolException("animation_draft_operation");
        return new(graph, ops);
    }
}
