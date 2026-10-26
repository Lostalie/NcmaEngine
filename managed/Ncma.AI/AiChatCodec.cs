using System.Text;
using System.Text.Json;
using Ncma.Modules;

namespace Ncma.AI;

internal sealed class AiProtocolException(string code) : Exception(code)
{ internal string Code { get; } = code; }

internal static class AiJson
{
    internal static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        _ = new UTF8Encoding(false, true).GetCharCount(bytes.Span);
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        try { Unique(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in value.EnumerateObject()) {
                if (!keys.Add(item.Name)) throw new AiProtocolException("duplicate_json_member");
                Unique(item.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
    }
    internal static void Members(JsonElement value, string[] required, string[]? optional = null)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new AiProtocolException("object_required");
        foreach (var key in required) if (!value.TryGetProperty(key, out _)) throw new AiProtocolException("missing_member");
        foreach (var member in value.EnumerateObject()) if (!required.Contains(member.Name) && !(optional?.Contains(member.Name) ?? false))
            throw new AiProtocolException("unknown_member");
    }
}

public sealed class AiPreparedChatRequest
{
    private readonly byte[] bytes;
    public string Sha256 { get; }
    public int Length => bytes.Length;
    internal AiPreparedChatRequest(byte[] bytes) { this.bytes = bytes; Sha256 = AiContract.Hash(bytes); }
    public byte[] CopyBytes() => (byte[])bytes.Clone();
    public override string ToString() => $"Chat request {Sha256} ({Length} bytes)";
}

public static class AiChatCodec
{
    public const int MaximumTransportBytes = 262144;
    // Pure deterministic encoding; call for full request preview before approval AND again on dispatch.
    public static AiPreparedChatRequest Prepare(ModulePayload payload, ModuleContext context, ModuleOperation operation,
        AiConnectionSnapshot connection)
    {
        ArgumentNullException.ThrowIfNull(payload); ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation); ArgumentNullException.ThrowIfNull(connection);
        context.Validate();
        var p = connection.Profile;
        if (!connection.ValidForDispatch || operation.InputSchema != AiRequestContent.InputSchema ||
            operation.ConfigurationId != p.ConfigurationId || operation.TimeoutMilliseconds > p.TimeoutMilliseconds)
            throw new AiProtocolException("request_configuration_mismatch");
        using var document = AiJson.Parse(payload.CopyBytes()); var root = document.RootElement;
        AiJson.Members(root, ["version", "request", "observation"]);
        if (root.GetProperty("version").GetInt32() != 2) throw new AiProtocolException("request_version");
        var r = root.GetProperty("request");
        AiJson.Members(r, ["version", "schema", "connectionEpoch", "configuration", "instruction", "template", "history",
            "stream", "maximumCalls", "maximumTransportBytes", "maximumProposalBytes", "automaticRetries", "automaticTools",
            "wireContract", "responseFormat", "thinking"]);
        if (r.GetProperty("version").GetInt32() != 2 || r.GetProperty("schema").GetGuid() != AiRequestContent.InputSchema ||
            r.GetProperty("connectionEpoch").GetGuid() != connection.Epoch ||
            r.GetProperty("configuration").GetRawText() != AiRequestContent.DescribeConfiguration(p).GetRawText() ||
            r.GetProperty("history").ValueKind != JsonValueKind.Array || r.GetProperty("history").GetArrayLength() != 0 ||
            r.GetProperty("stream").GetBoolean() || r.GetProperty("automaticTools").GetBoolean() ||
            r.GetProperty("automaticRetries").GetInt32() != 0 || r.GetProperty("maximumCalls").GetInt32() != 1 ||
            r.GetProperty("maximumTransportBytes").GetInt32() != MaximumTransportBytes ||
            r.GetProperty("maximumProposalBytes").GetInt32() != ModulePayload.MaximumBytes ||
            r.GetProperty("wireContract").GetString() != "ncma.chat-completions.1" ||
            r.GetProperty("responseFormat").GetString() != "json_object" ||
            r.GetProperty("thinking").GetString() != (p.ProviderId == "deepseek" ? "disabled" : "provider-default"))
            throw new AiProtocolException("request_policy_mismatch");
        var template = r.GetProperty("template"); AiJson.Members(template, ["id", "version", "text"]);
        AiContract.Token(template.GetProperty("id").GetString()!, 64); AiContract.Token(template.GetProperty("version").GetString()!, 32);
        string text = template.GetProperty("text").GetString()!, instruction = r.GetProperty("instruction").GetString()!;
        AiContract.Text(text, AiRequestContent.MaximumTemplateBytes); AiContract.Text(instruction, AiRequestContent.MaximumUserBytes);
        if (!text.Contains("json", StringComparison.OrdinalIgnoreCase)) throw new AiProtocolException("json_instruction_required");
        var observation = root.GetProperty("observation"); AiJson.Members(observation, ["version", "source", "observations"]);
        if (observation.GetProperty("version").GetInt32() != 1 ||
            observation.GetProperty("source").GetRawText() != JsonSerializer.Serialize(context) ||
            observation.GetProperty("observations").ValueKind != JsonValueKind.Array ||
            observation.GetProperty("observations").GetArrayLength() is < 1 or > 8)
            throw new AiProtocolException("observation_source_mismatch");
        // The exact authorized observations are data in a user message, never tool instructions or hidden reads.
        string user = JsonSerializer.Serialize(new { instruction, observation });
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) {
            writer.WriteStartObject(); writer.WriteString("model", p.Model); writer.WritePropertyName("messages"); writer.WriteStartArray();
            writer.WriteStartObject(); writer.WriteString("role", "system"); writer.WriteString("content", text); writer.WriteEndObject();
            writer.WriteStartObject(); writer.WriteString("role", "user"); writer.WriteString("content", user); writer.WriteEndObject();
            writer.WriteEndArray(); writer.WriteBoolean("stream", false); writer.WriteNumber("max_tokens", p.MaximumOutputTokens);
            writer.WritePropertyName("response_format"); writer.WriteStartObject(); writer.WriteString("type", "json_object"); writer.WriteEndObject();
            if (p.ProviderId == "deepseek") {
                writer.WritePropertyName("thinking"); writer.WriteStartObject(); writer.WriteString("type", "disabled"); writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        if (stream.Length > MaximumTransportBytes) throw new AiProtocolException("request_byte_budget");
        return new(stream.ToArray());
    }

    internal static (ModulePayload Proposal, AiTokenUsage? Usage) Decode(ReadOnlyMemory<byte> bytes)
    {
        var decoded = DecodeCompletion(bytes);
        var proposal = decoded.Content;
        using var parsed = JsonDocument.Parse(proposal.CopyBytes()); var envelope = parsed.RootElement;
        AiJson.Members(envelope, ["version", "capability", "input"]);
        if (envelope.GetProperty("version").GetInt32() != 1 || envelope.GetProperty("input").ValueKind != JsonValueKind.Object ||
            envelope.GetProperty("capability").GetString() is not ("ncma.scene.transaction" or "ncma.scene.delete_object" or
            "ncma.assets.metadata.edit" or "ncma.ui.document.commit" or "ncma.animgraph.transaction" or AiAnimationDraft.Capability or AiUiDraft.Capability or AiMetadataDraft.Capability))
            throw new AiProtocolException("proposal_envelope_denied");
        if (envelope.GetProperty("capability").GetString() == AiAnimationDraft.Capability)
            _ = AiAnimationDraft.Decode(proposal.CopyBytes());
        if (envelope.GetProperty("capability").GetString() == AiUiDraft.Capability)
            _ = AiUiDraft.Decode(proposal.CopyBytes());
        if (envelope.GetProperty("capability").GetString() == AiMetadataDraft.Capability)
            _ = AiMetadataDraft.Decode(proposal.CopyBytes());
        return (proposal, decoded.Usage);
    }
    // Shared envelope validation only. Each caller still validates its own closed content schema.
    internal static (ModulePayload Content, AiTokenUsage? Usage) DecodeCompletion(ReadOnlyMemory<byte> bytes)
    {
        using var doc = AiJson.Parse(bytes); var root = doc.RootElement;
        AiJson.Members(root, ["id", "object", "created", "model", "choices"], ["usage", "system_fingerprint", "service_tier"]);
        AiContract.Text(root.GetProperty("id").GetString()!, 256);
        AiContract.Text(root.GetProperty("model").GetString()!, 256);
        if (root.GetProperty("created").GetInt64() < 0) throw new AiProtocolException("completion_timestamp");
        if (root.GetProperty("object").GetString() != "chat.completion" ||
            root.GetProperty("choices").ValueKind != JsonValueKind.Array || root.GetProperty("choices").GetArrayLength() != 1)
            throw new AiProtocolException("completion_shape");
        var choice = root.GetProperty("choices")[0]; AiJson.Members(choice, ["index", "message", "finish_reason"], ["logprobs"]);
        if (choice.GetProperty("index").GetInt32() != 0 || choice.GetProperty("finish_reason").GetString() != "stop")
            throw new AiProtocolException("completion_not_stopped");
        var message = choice.GetProperty("message"); AiJson.Members(message, ["role", "content"], ["reasoning_content", "tool_calls", "function_call", "refusal"]);
        if (message.GetProperty("role").GetString() != "assistant" || message.GetProperty("content").ValueKind != JsonValueKind.String)
            throw new AiProtocolException("completion_message");
        foreach (string key in new[] { "tool_calls", "function_call", "refusal" })
            if (message.TryGetProperty(key, out var extra) && extra.ValueKind != JsonValueKind.Null) throw new AiProtocolException("completion_action_denied");
        string content = message.GetProperty("content").GetString()!;
        if (content.Length > ModulePayload.MaximumBytes) throw new AiProtocolException("proposal_byte_budget");
        var proposal = new ModulePayload(new UTF8Encoding(false, true).GetBytes(content));
        AiTokenUsage? usage = null;
        if (root.TryGetProperty("usage", out var u) && u.ValueKind != JsonValueKind.Null) {
            AiJson.Members(u, ["prompt_tokens", "completion_tokens", "total_tokens"],
                ["prompt_tokens_details", "completion_tokens_details", "prompt_cache_hit_tokens", "prompt_cache_miss_tokens"]);
            long prompt = u.GetProperty("prompt_tokens").GetInt64(), completion = u.GetProperty("completion_tokens").GetInt64(), total = u.GetProperty("total_tokens").GetInt64();
            if (prompt < 0 || completion < 0 || total < 0 || prompt > long.MaxValue - completion || prompt + completion != total)
                throw new AiProtocolException("usage_invalid");
            usage = new(prompt, completion, total);
        }
        return (proposal, usage);
    }
}

public sealed record AiTokenUsage(long PromptTokens, long CompletionTokens, long TotalTokens);
