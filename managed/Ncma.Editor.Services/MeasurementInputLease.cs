using System.Text.Json;
using Ncma.Application;

namespace Ncma.Editor.Services;

// Windows trusted-host readonly input preparation. No directory creation, file writes, source capture,
// restore, process start, sampling, AI access or automatic retry. Failed pins remain owned until Dispose.
public sealed class MeasurementInputLease : IDisposable
{
    public const string WorkloadManifestName = "measurement-workload.json";
    public const int MaximumProductFiles = 1024, MaximumWorkloadFiles = 512;
    public const long MaximumPayloadBytes = 512L * 1024 * 1024, MaximumFileBytes = 64L * 1024 * 1024;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly MeasurementInputsPlan plan;
    private readonly string capturePath, productRoot, workloadRoot;
    private readonly WorkflowPhysicalPins pins = new(2048, 4096);
    private WorkflowEvidenceFile[] product = [], workload = [];
    private MeasurementScenario scenario;
    private MeasurementInputsState state;
    private ulong checks;
    private long payloadBytes;
    public MeasurementInputsState State { get { Owner(); return state; } }
    public MeasurementInputLease(MeasurementInputsPlan plan, string captureManifestPath, string productDirectory, string workloadDirectory)
    {
        ArgumentNullException.ThrowIfNull(plan); this.plan = plan;
        capturePath = WorkflowPhysicalPins.Absolute(captureManifestPath);
        productRoot = WorkflowPhysicalPins.Absolute(productDirectory); workloadRoot = WorkflowPhysicalPins.Absolute(workloadDirectory);
        static bool Inside(string parent, string child) => child.Equals(parent, StringComparison.OrdinalIgnoreCase) || child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (Path.GetPathRoot(productRoot) == productRoot || Path.GetPathRoot(workloadRoot) == workloadRoot || Inside(productRoot, workloadRoot) || Inside(workloadRoot, productRoot) ||
            Inside(productRoot, capturePath) || Inside(workloadRoot, capturePath)) throw new IOException("measurement_input_roots_overlap");
    }
    private void Owner()
    { if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("measurement_input_owner"); }
    private JsonDocument Manifest(string path, string expectedHash)
    {
        var observed = pins.Observe(path, 4 * 1024 * 1024);
        if (observed.Sha256 != expectedHash) throw new IOException("measurement_manifest_changed");
        byte[] bytes = pins.ReadFile(path, expectedHash, observed.Bytes, 4 * 1024 * 1024);
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 }); }
        catch (JsonException) { throw new IOException("measurement_manifest_json"); }
        try { Unique(document.RootElement); return document; } catch { document.Dispose(); throw; }
    }
    private static void Require(bool value)
    { if (!value) throw new IOException("measurement_manifest_contract"); }
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) { Require(names.Add(property.Name)); Unique(property.Value); }
        } else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) Unique(child);
    }
    private static void Fields(JsonElement value, params string[] names)
    {
        Require(value.ValueKind == JsonValueKind.Object);
        Require(value.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(names));
    }
    private static WorkflowEvidenceFile[] Files(JsonElement value, int maximum, string manifestName)
    {
        Require(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= maximum);
        var files = new List<WorkflowEvidenceFile>();
        foreach (var file in value.EnumerateArray()) {
            Fields(file, "path", "size", "sha256");
            files.Add(new(file.GetProperty("path").GetString()!, file.GetProperty("size").GetInt64(), file.GetProperty("sha256").GetString()!));
        }
        var result = WorkflowEvidenceRules.Files(files);
        Require(!result.Any(f => f.Path.Equals(manifestName, StringComparison.OrdinalIgnoreCase) || f.Bytes > MaximumFileBytes));
        return result;
    }
    private void Tree(string root, WorkflowEvidenceFile[] files, string manifest)
    {
        var expected = files.Select(f => f.Path).Append(manifest).ToHashSet(StringComparer.Ordinal);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in expected) for (int i = path.IndexOf('/'); i >= 0; i = path.IndexOf('/', i + 1)) directories.Add(path[..i]);
        Require(directories.Count <= 2048);
        var actual = new HashSet<string>(StringComparer.Ordinal); var seenDirectories = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(); pending.Push(root); pins.Directory(root);
        while (pending.Count > 0) {
            string directory = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory)) {
                string relative = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("measurement_input_reparse");
                if ((attributes & FileAttributes.Directory) != 0) {
                    Require(directories.Contains(relative) && seenDirectories.Add(relative)); pins.Directory(entry); pending.Push(entry);
                } else Require(expected.Contains(relative) && actual.Add(relative));
            }
        }
        Require(expected.SetEquals(actual) && directories.SetEquals(seenDirectories));
    }
    private void Payload(string root, WorkflowEvidenceFile[] files)
    {
        foreach (var file in files) {
            var observed = pins.Observe(Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar)), MaximumFileBytes, allowEmpty: true);
            if (observed.Bytes != file.Bytes || observed.Sha256 != file.Sha256) throw new IOException("measurement_payload_changed");
        }
    }
    public MeasurementInputsReceipt Prepare()
    {
        Owner(); if (state != MeasurementInputsState.Created) throw new InvalidOperationException("measurement_inputs_single_use");
        state = MeasurementInputsState.Faulted; // Every failed/partial preparation is sticky until explicit close.
        using var capture = Manifest(capturePath, plan.Identity.CaptureManifestSha256);
        var source = capture.RootElement;
        Require(source.GetProperty("schemaVersion").GetInt32() == 1 && source.GetProperty("kind").GetString() == "ncma.captured-build.v1" &&
            source.GetProperty("sourceInputsSha256").GetString() == plan.Identity.SourceSha256);
        using var productManifest = Manifest(Path.Combine(productRoot, DeploymentManifest.FileName), plan.Identity.ProductManifestSha256);
        var packaged = productManifest.RootElement;
        bool ai = plan.Identity.Variant is MeasurementVariant.Ai or MeasurementVariant.AiExtensions;
        bool extensions = plan.Identity.Variant is MeasurementVariant.Extensions or MeasurementVariant.AiExtensions;
        Require(packaged.GetProperty("sourceInputsSha256").GetString() == plan.Identity.SourceSha256 &&
            packaged.GetProperty("captureManifestSha256").GetString() == plan.Identity.CaptureManifestSha256 &&
            packaged.GetProperty("configuration").GetString() == plan.Identity.Configuration.ToString() &&
            packaged.GetProperty("aiEnabled").GetBoolean() == ai && packaged.GetProperty("extensionsEnabled").GetBoolean() == extensions);
        product = Files(packaged.GetProperty("files"), MaximumProductFiles, DeploymentManifest.FileName);
        foreach (string required in new[] { "NcmaEngine.exe", "NcmaEngine.dll", "NcmaEngine.deps.json", "NcmaEngine.runtimeconfig.json" }) Require(product.Any(f => f.Path == required && f.Bytes > 0));
        using var workloadManifest = Manifest(Path.Combine(workloadRoot, WorkloadManifestName), plan.Identity.WorkloadSha256);
        var load = workloadManifest.RootElement; Fields(load, "schemaVersion", "kind", "scenario", "files");
        Require(load.GetProperty("schemaVersion").GetInt32() == 1 && load.GetProperty("kind").GetString() == "ncma.measurement-workload.v1");
        scenario = load.GetProperty("scenario").GetString() switch {
            "editor-idle" => MeasurementScenario.EditorIdle, "open-world-exploration" => MeasurementScenario.OpenWorldExploration,
            "controlled-fixture" => MeasurementScenario.ControlledFixture, _ => throw new IOException("measurement_workload_scenario") };
        workload = Files(load.GetProperty("files"), MaximumWorkloadFiles, WorkloadManifestName);
        Require(scenario == MeasurementScenario.EditorIdle || workload.Length > 0);
        payloadBytes = product.Sum(f => f.Bytes) + workload.Sum(f => f.Bytes); Require(payloadBytes <= MaximumPayloadBytes);
        Tree(productRoot, product, DeploymentManifest.FileName); Tree(workloadRoot, workload, WorkloadManifestName);
        Payload(productRoot, product); Payload(workloadRoot, workload);
        DeploymentManifest.Validate(productRoot); // Original package consistency rules, while exact declared files are pinned.
        state = MeasurementInputsState.Ready; return RequireCurrent();
    }
    public MeasurementInputsReceipt RequireCurrent()
    {
        Owner(); if (state != MeasurementInputsState.Ready) throw new InvalidOperationException("measurement_inputs_not_ready");
        try {
            pins.VerifyObserved(); Tree(productRoot, product, DeploymentManifest.FileName); Tree(workloadRoot, workload, WorkloadManifestName);
            return new(plan.Fingerprint, plan.Identity, checked(++checks), scenario, product.Length, workload.Length, payloadBytes,
                WorkflowEvidenceRules.Digest(product), WorkflowEvidenceRules.Digest(workload));
        } catch { state = MeasurementInputsState.Faulted; throw; }
    }
    public void Dispose()
    {
        Owner(); if (state == MeasurementInputsState.Closed) return;
        state = MeasurementInputsState.Faulted; // A partial close can never remain Ready.
        pins.Dispose(); state = MeasurementInputsState.Closed;
    }
    // A matching label/receipt alone does not bind the caller's paths to this actual lease.
    public MeasurementInputsReceipt RequireCurrent(MeasurementCandidateRequest request)
    {
        Owner(); ArgumentNullException.ThrowIfNull(request); request.Validate();
        if (request.Plan().Fingerprint != plan.Fingerprint || request.Identity != plan.Identity ||
            !capturePath.Equals(WorkflowPhysicalPins.Absolute(request.CaptureManifestPath), StringComparison.OrdinalIgnoreCase) ||
            !productRoot.Equals(WorkflowPhysicalPins.Absolute(request.ProductDirectory), StringComparison.OrdinalIgnoreCase) ||
            !workloadRoot.Equals(WorkflowPhysicalPins.Absolute(request.WorkloadDirectory), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("measurement_request_lease_mismatch");
        return RequireCurrent();
    }
    // Deliberately single-file controlled workload, not a general project reader/loader.
    public byte[] ReadSingleWorkload(string relativePath, string expectedSha256, int maximumBytes)
    {
        Owner(); WorkflowEvidenceRules.Hash(expectedSha256);
        if (maximumBytes is < 1 or > 4 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _ = RequireCurrent();
        if (scenario != MeasurementScenario.ControlledFixture || workload.Length != 1 ||
            workload[0].Path != relativePath || workload[0].Sha256 != expectedSha256 || workload[0].Bytes < 1 || workload[0].Bytes > maximumBytes)
            throw new ArgumentException("measurement_single_workload_required");
        try {
            using var output = new MemoryStream(checked((int)workload[0].Bytes));
            pins.CopyObserved(Path.Combine(workloadRoot, workload[0].Path.Replace('/', Path.DirectorySeparatorChar)),
                expectedSha256, workload[0].Bytes, output, new byte[65536]);
            byte[] bytes = output.ToArray();
            _ = RequireCurrent(); return bytes; // Original retained handle; never a path-based second read.
        } catch { state = MeasurementInputsState.Faulted; throw; }
    }
    public MeasurementEntryBinding BindCurrentEditor()
    {
        _ = RequireCurrent();
        string exe=WorkflowPhysicalPins.Absolute(Environment.ProcessPath ?? throw new IOException("measurement_process_path_missing"));
        string entry=WorkflowPhysicalPins.Absolute(System.Reflection.Assembly.GetEntryAssembly()?.Location ?? throw new IOException("measurement_entry_missing"));
        if(!exe.Equals(Path.Combine(productRoot,"NcmaEngine.exe"),StringComparison.OrdinalIgnoreCase) ||
           !entry.Equals(Path.Combine(productRoot,"NcmaEngine.dll"),StringComparison.OrdinalIgnoreCase) ||
           !WorkflowPhysicalPins.Absolute(AppContext.BaseDirectory).Equals(productRoot,StringComparison.OrdinalIgnoreCase))
            throw new IOException("measurement_independent_editor_required");
        // Those exact paths were validated and their file handles remain retained in this lease.
        return new(Environment.ProcessId,product.Single(f=>f.Path=="NcmaEngine.exe").Sha256,
            product.Single(f=>f.Path=="NcmaEngine.dll").Sha256);
    }
}

public sealed record MeasurementEntryBinding(int ProcessId,string ExecutableSha256,string EntryAssemblySha256)
{
    public bool CurrentEntryPathsMatched => true;
    public bool LoadedMemoryBytesVerified => false;
    public bool FullRuntimeClosureVerified => false;
    public string Scope => "OS current executable path, managed entry location and application base match retained candidate files; not loaded memory/native/runtime/font/driver closure or source-build provenance";
}
