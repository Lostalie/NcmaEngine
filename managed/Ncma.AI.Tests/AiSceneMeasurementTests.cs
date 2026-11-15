using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.AI;
using Ncma.Application;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;
using Ncma.Runtime;
using Ncma.Scene;

internal static partial class AiSceneMeasurementTests
{
    private static void Check(bool value) { if (!value) throw new Exception("AI scene input assertion"); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is ArgumentException or IOException or InvalidOperationException or JsonException) { return; }
        throw new Exception("AI scene input must reject");
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static AiMeasurementRecipient Recipient() => new("deepseek", AiDeployment.LocalLoopback,
        "http://127.0.0.1:8000/v1/chat/completions", "fixture-model", 2048, 60000, 2);
    private static byte[] Scene()
    {
        var document = new SceneDocument("Controlled scene input");
        var id = document.CreateObject("Observed"); document.SetComponent(id, TransformData.Identity);
        return document.CaptureBytes();
    }
    private sealed class Fixture
    {
        internal readonly MeasurementInputsTests.Fixture Original = new();
        internal readonly string Workload;
        internal readonly byte[] Bytes;
        internal readonly AiSceneMeasurementRequest Request;
        internal Fixture(byte[]? scene = null, string scenario = "controlled-fixture", string name = AiSceneMeasurementRequest.SceneFileName, bool extra = false)
        {
            Workload = Path.Combine(Original.Root, "scene-workload"); Directory.CreateDirectory(Workload);
            Bytes = scene ?? Scene(); File.WriteAllBytes(Path.Combine(Workload, name), Bytes);
            var files = new List<object> { new { path = name, size = Bytes.Length, sha256 = Hash(Bytes) } };
            if (extra) { byte[] other = Encoding.UTF8.GetBytes("not executable; evidence only"); File.WriteAllBytes(Path.Combine(Workload, "other.dll"), other);
                files.Add(new { path = "other.dll", size = other.Length, sha256 = Hash(other) }); }
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, kind = "ncma.measurement-workload.v1", scenario, files });
            File.WriteAllBytes(Path.Combine(Workload, MeasurementInputLease.WorkloadManifestName), manifest);
            Request = new(1, new(1, Original.Identity with { WorkloadSha256 = Hash(manifest) }, 3000, 1000, 3, 120,
                Original.Capture, Original.Product, Workload), Hash(Bytes), Recipient());
        }
        internal MeasurementInputLease Lease() => Request.Inputs.OpenInputs();
        // Like the original fixture, preserve every positive and negative input; never launch synthetic EXEs.
    }
    internal static IEnumerable<(string, Action)> Cases()
    {
        foreach (var test in CandidateEvidenceCases()) yield return test;
        yield return ("M8.7-E2E7 canonical secret-free request binds original plan scene and recipient", () => {
            var f = new Fixture(); byte[] bytes = AiSceneMeasurementRequest.Encode(f.Request);
            Check(AiSceneMeasurementRequest.Decode(bytes) == f.Request);
            string json = Encoding.UTF8.GetString(bytes);
            Check(!json.Contains("Credential") && !json.Contains("ConfigurationId") && !json.Contains("humanApproved"));
            foreach (var bad in new[] { f.Request with { Version = 2 }, f.Request with { SceneSha256 = new('a', 64) },
                f.Request with { Inputs = f.Request.Inputs with { Identity = f.Request.Inputs.Identity with { Variant = MeasurementVariant.AiExtensions } } },
                f.Request with { Inputs = f.Request.Inputs with { Identity = f.Request.Inputs.Identity with { Variant = MeasurementVariant.Default } } } })
                Reject(() => AiSceneMeasurementRequest.Encode(bad));
        });
        yield return ("M8.7-E2E7 unknown duplicate omitted noncanonical and overbudget requests rejected", () => {
            var f = new Fixture(); string json = Encoding.UTF8.GetString(AiSceneMeasurementRequest.Encode(f.Request));
            foreach (string bad in new[] { "null", "{}", json + " ", json.Replace("\"Version\":1", "\"Version\":1,\"Version\":1"),
                json.Replace("\"Recipient\":", "\"humanApproved\":true,\"Recipient\":"), json.Replace("\"SceneSha256\":", "\"Unknown\":"), "{\"secret\": " })
                Reject(() => AiSceneMeasurementRequest.Decode(Encoding.UTF8.GetBytes(bad)));
            Reject(() => AiSceneMeasurementRequest.Decode(new byte[MeasurementCandidateRequest.MaximumBytes + 1]));
            Reject(() => AiSceneMeasurementRequest.Encode(f.Request with { Recipient = null! }));
            Reject(() => AiSceneMeasurementRequest.Encode(f.Request with { Inputs = null! }));
        });
        yield return ("M8.7-E2E7 original provider endpoint and model restrictions with bounded attempts", () => {
            var r = Recipient(); _ = r.Limits();
            _ = (r with { Deployment = AiDeployment.RemoteHttps, Endpoint = AiProvider.DeepSeek.RemoteEndpoint }).Limits();
            foreach (var bad in new[] { r with { ProviderId = "unregistered" }, r with { Endpoint = "http://localhost:8000/chat" },
                r with { Endpoint = "http://127.0.0.1/chat?key=secret" }, r with { Endpoint = "http://127.0.0.1:80/chat" },
                r with { Deployment = AiDeployment.RemoteHttps, Endpoint = "https://example.test/chat" }, r with { Model = "model\nsecret" },
                r with { MaximumOutputTokens = 4097 }, r with { TimeoutMilliseconds = 60001 }, r with { MaximumAttempts = 0 }, r with { MaximumAttempts = 33 } })
                Reject(() => bad.Limits());
            Check(r.Limits().Fingerprint == r.Limits().Fingerprint && r.Limits().Fingerprint != (r with { MaximumAttempts = 3 }).Limits().Fingerprint);
        });
        yield return ("M8.7-E2E7 local reader uses external hash original bound and no fallback", () => {
            var f = new Fixture(); byte[] bytes = AiSceneMeasurementRequest.Encode(f.Request); string path = Path.Combine(f.Original.Root, "request.json");
            File.WriteAllBytes(path, bytes); Check(AiSceneMeasurementRequest.Read(path, Hash(bytes)) == f.Request);
            Reject(() => AiSceneMeasurementRequest.Read(path, new('0', 64)));
            Reject(() => AiSceneMeasurementRequest.Read(Path.Combine(f.Original.Root, "absent.json"), Hash(bytes)));
            Reject(() => AiSceneMeasurementRequest.Read("relative.json", Hash(bytes)));
            Check(bytes.SequenceEqual(File.ReadAllBytes(path)));
        });
        yield return ("M8.7-E2E7 actual lease roots not matching labels bind detached same-handle scene", () => {
            var f = new Fixture(); using var lease = f.Lease(); Reject(() => f.Request.Prepare(lease));
            lease.Prepare(); var prepared = f.Request.Prepare(lease);
            Check(prepared.ObjectCount == 1 && prepared.Inputs.CheckSequence > 1 && prepared.Inputs.WorkloadFiles == 1 && prepared.CopySceneBytes().SequenceEqual(f.Bytes));
            Check(prepared.RequestSha256 == Hash(AiSceneMeasurementRequest.Encode(f.Request)) && prepared.PolicySha256 == f.Request.Recipient.Limits().Fingerprint);
            Check(!prepared.LaunchAuthorized && !prepared.ModelCallAuthorized && !prepared.WorkloadExecuted && !prepared.PerformanceAccepted);
            byte[] copy = prepared.CopySceneBytes(); copy[0] = 0; Check(prepared.CopySceneBytes()[0] != 0);
            var wrongPaths = f.Request with { Inputs = f.Request.Inputs with { WorkloadDirectory = f.Original.Workload } };
            Reject(() => wrongPaths.Prepare(lease));
            Reject(() => (f.Request with { Inputs = f.Request.Inputs with { MaximumFrames = 121 } }).Prepare(lease));
            Reject(() => (f.Request with { SceneSha256 = new('0', 64) }).Prepare(lease));
            lease.Dispose(); Check(prepared.CopySceneBytes().SequenceEqual(f.Bytes)); Reject(() => f.Request.Prepare(lease));
        });
        yield return ("M8.7-E2E7 exact single controlled scene rejects alternate names extras and open world", () => {
            foreach (var f in new[] { new Fixture(name: "other.ncmascene"), new Fixture(extra: true), new Fixture(scenario: "open-world-exploration"), new Fixture(scenario: "editor-idle") }) {
                using var lease = f.Lease(); lease.Prepare(); Reject(() => f.Request.Prepare(lease));
            }
        });
        yield return ("M8.7-E2E7 empty excessive and executable scene content never loads", () => {
            var snapshot = SceneDocumentCodec.Decode(Scene()); var row = snapshot.Objects[0];
            foreach (var bad in new[] { snapshot with { Objects = [] },
                snapshot with { Objects = Enumerable.Range(0, 4097).Select(_ => row with { Id = Guid.NewGuid() }).ToArray() },
                snapshot with { Objects = [row with { Behaviours = [new(Guid.NewGuid(), "Fixture.NoExecute", true, [])] }] },
                snapshot with { Objects = [row with { Components = [new("fixture.external", 1, JsonSerializer.SerializeToElement(new { path = "never.dll" }))] }] } }) {
                // Intentionally bypass the encoder's metadata guard for malformed-input construction.
                var f = new Fixture(JsonSerializer.SerializeToUtf8Bytes(bad, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                using var lease = f.Lease(); lease.Prepare(); Reject(() => f.Request.Prepare(lease));
            }
        });
        yield return ("M8.7-E2E7 original Transform validation and scene JSON remain mandatory", () => {
            var snapshot = SceneDocumentCodec.Decode(Scene()); var row = snapshot.Objects[0];
            byte[] invalidTransform = SceneDocumentCodec.Encode(snapshot with { Objects = [row with {
                Components = [new("ncma.transform", 1, JsonSerializer.SerializeToElement(new { unexpected = true }))] }] });
            foreach (byte[] bad in new[] { invalidTransform, Encoding.UTF8.GetBytes("{\"bad\":true}"), new byte[AiSceneMeasurementRequest.MaximumSceneBytes + 1] }) {
                var f = new Fixture(bad); using var lease = f.Lease(); lease.Prepare(); Reject(() => f.Request.Prepare(lease));
            }
        });
        yield return ("M8.7-E2E7 owner readonly pin remains held no execution or repeated pin consumption", () => {
            var f = new Fixture(); using var lease = f.Lease(); lease.Prepare();
            Task.Run(() => Reject(() => f.Request.Prepare(lease))).GetAwaiter().GetResult();
            for (int i = 0; i < 4; ++i) Check(f.Request.Prepare(lease).CopySceneBytes().SequenceEqual(f.Bytes));
            Reject(() => { using var writer = new FileStream(Path.Combine(f.Workload, AiSceneMeasurementRequest.SceneFileName), FileMode.Open, FileAccess.Write, FileShare.ReadWrite); });
            Reject(() => lease.ReadSingleWorkload("../escape", f.Request.SceneSha256, 1024));
            Reject(() => lease.ReadSingleWorkload(AiSceneMeasurementRequest.SceneFileName, f.Request.SceneSha256, 4 * 1024 * 1024 + 1));
            Check(lease.State == MeasurementInputsState.Ready);
        });
        yield return ("M8.7-E2E7 output isolation uses original noncreating CreateNew preflight", () => {
            var f = new Fixture(); string outputRoot = Path.Combine(Path.GetTempPath(), "ncma-ai-scene-output-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputRoot); string output = Path.Combine(outputRoot, "session.ncobserve");
            f.Request.ValidateOutput(output); Check(!File.Exists(output));
            foreach (string root in new[] { f.Workload, f.Original.Product, f.Original.Root }) Reject(() => f.Request.ValidateOutput(Path.Combine(root, "run.ncobserve")));
            File.WriteAllText(output, "retained collision evidence"); Reject(() => f.Request.ValidateOutput(output));
            Check(File.ReadAllText(output) == "retained collision evidence");
        });
    }
}
