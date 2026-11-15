using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.AI;
using Ncma.Application;
using Ncma.Editor.Services;
using Ncma.Scene;

namespace Ncma.Editor.Modules;

// Request data only: no key/reference, configuration read, callback, grant or model dispatch.
// Provider support here follows the existing built-in DeepSeek UI; custom registration is not implicit.
public sealed record AiMeasurementRecipient(string ProviderId, AiDeployment Deployment, string Endpoint,
    string Model, int MaximumOutputTokens, int TimeoutMilliseconds, int MaximumAttempts)
{
    public AiWorkspaceMeasurementLimits Limits()
    {
        if (ProviderId != AiProvider.DeepSeek.Id) throw new ArgumentException("ai_measurement_provider_unsupported");
        // Private comparison-only placeholders satisfy the ORIGINAL profile validator. Never exposed,
        // installed as configuration, looked up in a secret store or passed to a transport.
        var placeholder = new Guid("00000000-0000-0000-0000-000000000001");
        var profile = new AiConnectionProfile(placeholder, 1, AiProvider.DeepSeek, Deployment, Endpoint, Model,
            Deployment == AiDeployment.RemoteHttps ? new(placeholder, 1) : null, MaximumOutputTokens, TimeoutMilliseconds);
        if (Endpoint != profile.Endpoint) throw new ArgumentException("ai_measurement_endpoint_noncanonical");
        return new(profile, MaximumAttempts);
    }
}

public sealed record AiSceneMeasurementRequest(int Version, MeasurementCandidateRequest Inputs,
    string SceneSha256, AiMeasurementRecipient Recipient)
{
    public const string SceneFileName = "scene.ncmascene";
    public const int MaximumSceneBytes = 4 * 1024 * 1024;
    public const int MaximumObjects = 4096;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static void Require(bool value) { if (!value) throw new ArgumentException("ai_scene_measurement_request"); }
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Inputs); ArgumentNullException.ThrowIfNull(Recipient);
        Inputs.Validate();
        Require(Version == 1 && Inputs.Identity.Variant == MeasurementVariant.Ai && IsHash(SceneSha256));
        _ = Recipient.Limits();
    }
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    public static byte[] Encode(AiSceneMeasurementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); request.Validate();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(request, Options);
        Require(bytes.Length <= MeasurementCandidateRequest.MaximumBytes); return bytes;
    }
    public static AiSceneMeasurementRequest Decode(ReadOnlySpan<byte> bytes)
    {
        Require(bytes.Length is > 0 and <= MeasurementCandidateRequest.MaximumBytes);
        try {
            var result = JsonSerializer.Deserialize<AiSceneMeasurementRequest>(bytes, Options) ?? throw new ArgumentException("ai_scene_measurement_null");
            Require(bytes.SequenceEqual(Encode(result))); return result;
        } catch (JsonException) { throw new ArgumentException("ai_scene_measurement_json"); }
    }
    public static AiSceneMeasurementRequest Read(string path, string expectedSha256)
        => Decode(MeasurementCandidateRequest.ReadLocalBytes(path, expectedSha256));

    public PreparedAiSceneMeasurement Prepare(MeasurementInputLease inputs)
    {
        Validate(); ArgumentNullException.ThrowIfNull(inputs);
        _ = inputs.RequireCurrent(Inputs); // Exact actual lease roots AND plan, not a caller-made receipt.
        byte[] scene = inputs.ReadSingleWorkload(SceneFileName, SceneSha256, MaximumSceneBytes);
        var snapshot = SceneDocumentCodec.Decode(scene);
        Require(snapshot.Objects.Length is > 0 and <= MaximumObjects);
        Require(snapshot.Objects.All(o => o.Behaviours.Length == 0 &&
            o.Components.All(c => c is not null && c.TypeId == "ncma.transform" && c.Version == 1)));
        // Original generated component decode/validation, no custom registry or executable loader.
        var validation = new SceneDocument(); validation.RestoreSnapshot(snapshot);
        var receipt = inputs.RequireCurrent(Inputs);
        return new(this, receipt, scene, snapshot.Objects.Length);
    }
    public void ValidateOutput(string path) { Validate(); Inputs.ValidateOutput(path); }
}

// Detached reviewed input bytes, not a live lease, document, permission or freshness certificate.
public sealed class PreparedAiSceneMeasurement
{
    private readonly byte[] scene;
    public AiSceneMeasurementRequest Request { get; }
    public MeasurementInputsReceipt Inputs { get; }
    public int ObjectCount { get; }
    public string RequestSha256 { get; }
    public string PolicySha256 { get; }
    public bool LaunchAuthorized => false;
    public bool ModelCallAuthorized => false;
    public bool WorkloadExecuted => false;
    public bool PerformanceAccepted => false;
    internal PreparedAiSceneMeasurement(AiSceneMeasurementRequest request, MeasurementInputsReceipt inputs, byte[] bytes, int objectCount)
    {
        Request = request; Inputs = inputs; scene = bytes.ToArray(); ObjectCount = objectCount;
        RequestSha256 = Convert.ToHexString(SHA256.HashData(AiSceneMeasurementRequest.Encode(request)));
        PolicySha256 = request.Recipient.Limits().Fingerprint;
    }
    public byte[] CopySceneBytes() => scene.ToArray();
}
