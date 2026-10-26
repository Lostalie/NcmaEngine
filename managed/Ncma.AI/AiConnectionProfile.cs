using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ncma.AI;

public enum AiDeployment { LocalLoopback = 1, RemoteHttps = 2 }
public enum AiProtocol { ChatCompletionsV1 = 1 }

// Trusted composition data, never a model-provided provider registration or executable factory.
public sealed class AiProvider
{
    public static AiProvider DeepSeek { get; } = new("deepseek", AiProtocol.ChatCompletionsV1,
        "https://api.deepseek.com/chat/completions");
    public string Id { get; }
    public AiProtocol Protocol { get; }
    public string RemoteEndpoint { get; }
    public AiProvider(string id, AiProtocol protocol, string remoteEndpoint)
    {
        AiContract.Token(id, 64);
        if (protocol != AiProtocol.ChatCompletionsV1) throw new ArgumentException("ai_protocol_unsupported");
        Id = id; Protocol = protocol;
        RemoteEndpoint = AiContract.Endpoint(remoteEndpoint, AiDeployment.RemoteHttps);
    }
}

// Reference only: no key value, environment lookup, persistence or network access in this layer.
public sealed record AiCredentialReference(Guid Id, ulong Generation);

public sealed class AiConnectionProfile
{
    public Guid ConfigurationId { get; }
    public ulong Generation { get; }
    public string ProviderId { get; }
    public AiProtocol Protocol { get; }
    public AiDeployment Deployment { get; }
    public string Endpoint { get; }
    public string Model { get; }
    public AiCredentialReference? Credential { get; }
    public int MaximumOutputTokens { get; }
    public int TimeoutMilliseconds { get; }
    public string Fingerprint { get; }

    public AiConnectionProfile(Guid configurationId, ulong generation, AiProvider provider, AiDeployment deployment,
        string endpoint, string model, AiCredentialReference? credential, int maximumOutputTokens = 2048,
        int timeoutMilliseconds = 60_000)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (configurationId == Guid.Empty || generation == 0 || maximumOutputTokens is < 1 or > 4096 ||
            timeoutMilliseconds is < 1 or > 60_000) throw new ArgumentException("ai_configuration_invalid");
        AiContract.Token(model, 128, model: true);
        Endpoint = AiContract.Endpoint(endpoint, deployment);
        if (deployment == AiDeployment.RemoteHttps && Endpoint != provider.RemoteEndpoint)
            throw new ArgumentException("ai_provider_endpoint_mismatch");
        if (credential is not null && (credential.Id == Guid.Empty || credential.Generation == 0) ||
            deployment == AiDeployment.RemoteHttps && credential is null)
            throw new ArgumentException("ai_credential_reference_required");
        ConfigurationId = configurationId; Generation = generation; ProviderId = provider.Id;
        Protocol = provider.Protocol; Deployment = deployment; Model = model; Credential = credential;
        MaximumOutputTokens = maximumOutputTokens; TimeoutMilliseconds = timeoutMilliseconds;
        Fingerprint = AiContract.Hash(JsonSerializer.SerializeToUtf8Bytes(new {
            version = 1, ConfigurationId, Generation, ProviderId, Protocol, Deployment, Endpoint, Model,
            Credential, MaximumOutputTokens, TimeoutMilliseconds }));
    }
    public override string ToString() => $"AI profile {ConfigurationId} generation {Generation}";
}

internal static class AiContract
{
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static void Token(string value, int max, bool model = false)
    {
        if (string.IsNullOrEmpty(value) || value.Length > max || !value.All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' || model && c is '/' or ':'))
            throw new ArgumentException("ai_identifier_invalid");
    }
    internal static void Text(string value, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumBytes ||
            new UTF8Encoding(false, true).GetByteCount(value) > maximumBytes)
            throw new ArgumentException("ai_text_budget");
    }
    internal static string Endpoint(string value, AiDeployment deployment)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 2048 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ||
            value.Contains('\\') || value.Contains('%') || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.Port == 0 ||
            uri.AbsolutePath.Contains("//", StringComparison.Ordinal) ||
            value.Contains("/../", StringComparison.Ordinal) || value.Contains("/./", StringComparison.Ordinal))
            throw new ArgumentException("ai_endpoint_invalid");
        string host = uri.Host.Trim('[', ']');
        bool literal = IPAddress.TryParse(host, out var ip);
        bool loopback = literal && IPAddress.IsLoopback(ip!);
        if (deployment == AiDeployment.LocalLoopback) {
            if (!loopback || uri.Scheme is not ("http" or "https")) throw new ArgumentException("ai_loopback_required");
        }
        else if (deployment == AiDeployment.RemoteHttps) {
            if (uri.Scheme != "https" || loopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("ai_remote_https_required");
        }
        else throw new ArgumentException("ai_deployment_invalid");
        return uri.AbsoluteUri;
    }
}
