using System.Text.Json;
using Ncma.Modules;

namespace Ncma.AI;

// Model-only result, not an editor capability. No paths, records, revisions or grants.
public sealed record AiMetadataDraft(Guid AssetId, int Version, int SampleRate, bool GenerateTangents)
{
    public const string Capability = "ncma.ai.metadata.settings.draft";
    public static Guid OutputSchema { get; } = new("ef704084-3001-4001-8001-000000000005");
    public static AiMetadataDraft Decode(ReadOnlySpan<byte> bytes)
    {
        var payload = new ModulePayload(bytes);
        using var doc = AiJson.Parse(payload.CopyBytes()); var root = doc.RootElement;
        AiJson.Members(root, ["version", "capability", "input"]);
        if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("capability").GetString() != Capability)
            throw new AiProtocolException("metadata_draft_envelope");
        var input = root.GetProperty("input"); AiJson.Members(input, ["assetId", "settings"]);
        var asset = input.GetProperty("assetId");
        if (asset.ValueKind != JsonValueKind.String || !Guid.TryParseExact(asset.GetString(), "D", out var id) || id == Guid.Empty || asset.GetString() != id.ToString("D"))
            throw new AiProtocolException("metadata_draft_asset");
        var settings = input.GetProperty("settings"); AiJson.Members(settings, ["version", "sampleRate", "generateTangents"]);
        if (settings.GetProperty("version").GetInt32() != 1 || !settings.GetProperty("sampleRate").TryGetInt32(out int rate) || rate is < 1 or > 120 ||
            settings.GetProperty("generateTangents").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new AiProtocolException("metadata_draft_settings");
        return new(id, 1, rate, settings.GetProperty("generateTangents").GetBoolean());
    }
}
