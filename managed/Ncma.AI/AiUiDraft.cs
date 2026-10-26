using System.Text;
using System.Text.Json;
using Ncma.Modules;

namespace Ncma.AI;

// Model-only result tag, not a registered editor/MCP capability. The original UI codec and
// workspace validate semantics/resources/actions; the model cannot select file/proposal/grants.
public sealed class AiUiDraft
{
    public const string Capability = "ncma.ai.ui.draft";
    public const int MaximumDefinitionBytes = 48 * 1024;
    public static Guid OutputSchema { get; } = new("ef704084-3001-4001-8001-000000000004");
    public Guid DocumentId { get; }
    private readonly byte[] definition;
    public byte[] CopyDefinition() => (byte[])definition.Clone();
    private AiUiDraft(Guid id, byte[] definition) { DocumentId = id; this.definition = definition; }
    public static AiUiDraft Decode(ReadOnlySpan<byte> bytes)
    {
        var payload = new ModulePayload(bytes);
        using var doc = AiJson.Parse(payload.CopyBytes()); var root = doc.RootElement;
        AiJson.Members(root, ["version", "capability", "input"]);
        if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("capability").GetString() != Capability)
            throw new AiProtocolException("ui_draft_envelope");
        var input = root.GetProperty("input"); AiJson.Members(input, ["documentId", "definition"]);
        if (input.GetProperty("documentId").ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(input.GetProperty("documentId").GetString(), "D", out var id) || id == Guid.Empty ||
            input.GetProperty("documentId").GetString() != id.ToString("D"))
            throw new AiProtocolException("ui_draft_document");
        var value = input.GetProperty("definition");
        byte[] definition = Encoding.UTF8.GetBytes(value.GetRawText());
        if (value.ValueKind != JsonValueKind.Object || definition.Length > MaximumDefinitionBytes)
            throw new AiProtocolException("ui_draft_budget");
        return new(id, definition);
    }
}
