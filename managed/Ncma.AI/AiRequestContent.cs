using System.Text.Json;
using Ncma.Modules;

namespace Ncma.AI;

// Complete logical send content including wire policy; this is NOT a network approval by itself.
public sealed class AiRequestContent
{
    public static Guid InputSchema { get; } = new("ef704084-1001-4001-8001-000000000002");
    public const int MaximumUserBytes = 8192;
    public const int MaximumTemplateBytes = 8192;
    private readonly ModulePayload content;
    public AiConnectionSnapshot Connection { get; }
    public string Fingerprint => content.Sha256;
    public AiRequestContent(AiConnectionSnapshot connection, string instruction, string templateId,
        string templateVersion, string templateText)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!connection.Owner.IsCurrent(connection)) throw new InvalidOperationException("ai_configuration_stale");
        AiContract.Text(instruction, MaximumUserBytes); AiContract.Text(templateText, MaximumTemplateBytes);
        AiContract.Token(templateId, 64); AiContract.Token(templateVersion, 32);
        if (!templateText.Contains("json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("ai_json_instruction_required");
        Connection = connection;
        var p = connection.Profile;
        // No arbitrary options dictionary, hidden chat history, paths, tools, or credential values.
        content = new(JsonSerializer.SerializeToUtf8Bytes(new {
            version = 2, schema = InputSchema, connectionEpoch = connection.Epoch,
            configuration = DescribeConfiguration(p),
            instruction, template = new { id = templateId, version = templateVersion, text = templateText },
            history = Array.Empty<object>(), stream = false, maximumCalls = 1,
            maximumTransportBytes = 262144, maximumProposalBytes = ModulePayload.MaximumBytes,
            automaticRetries = 0, automaticTools = false,
            wireContract = "ncma.chat-completions.1", responseFormat = "json_object",
            thinking = p.ProviderId == "deepseek" ? "disabled" : "provider-default"
        }));
    }
    public byte[] CopyBytes() => content.CopyBytes();
    public bool IsCurrent() => Connection.Owner.IsCurrent(Connection);
    public override string ToString() => $"AI request content {Fingerprint}";
    internal static JsonElement DescribeConfiguration(AiConnectionProfile p) => JsonSerializer.SerializeToElement(new {
        p.ConfigurationId, p.Generation, p.ProviderId, p.Protocol, p.Deployment, p.Endpoint, p.Model,
        p.Credential, p.MaximumOutputTokens, p.TimeoutMilliseconds, p.Fingerprint });
}
