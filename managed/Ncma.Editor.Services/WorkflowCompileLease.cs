using System.Xml;
using System.Xml.Linq;

namespace Ncma.Editor.Services;

// Fixed trusted engine build; never restores. Borrows a live successful restore,
// not a path supplied by a model or a deserialized success receipt. SDK/package
// build logic is trusted code, NOT an arbitrary-code or network sandbox.
internal sealed class WorkflowCompileLease : IWorkflowBuildLease
{
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly WorkflowRestoreProduct restore;
    private IDisposable? borrowed;
    private readonly WorkflowCaptureBridge capture;
    private readonly WorkflowRestoreGraph graph;
    private readonly WorkflowPackageClosure packages;
    private readonly WorkflowPhysicalPins pins = new(512, 1024);
    private readonly WorkflowBuildOutput output;
    private readonly WorkflowJobProcess process;
    private WorkflowToolchain? tools;
    private WorkflowCompiledProduct? product;
    internal WorkflowBuildOutput Output => output;
    private bool begun, prepared, closed;
    public Guid LeaseId { get; } = Guid.NewGuid();
    public WorkflowVerificationTask Task { get; }
    public WorkflowBuildEnvironment Environment { get; }
    internal bool Ready => !closed && prepared && capture.Snapshot().Ready;
    public string PreparationFingerprint {
        get { RequireCurrent(); return WorkflowEvidenceRules.Digest(new { task = Task.Fingerprint, environment = Environment.Fingerprint,
            restore = restore.Fingerprint, graph = graph.Fingerprint, packages = packages.Fingerprint,
            tools = tools!.Fingerprint, capture = capture.ReadPreparedFacts() }); }
    }
    internal WorkflowCompileLease(WorkflowRestoreProduct restore)
    {
        this.restore = restore; Task = restore.CreateCompileTask(); Environment = restore.Environment;
        capture = new(Task, Environment); graph = new(Task, Environment, restore.Origin.Binding.JobId);
        packages = new(Task, graph, [restore.PackagesRoot]); output = new(this); process = new(Task, Environment);
        borrowed = restore.Borrow(); // last fallible acquisition; host owns lease before Begin
    }
    private void Owner() { if (System.Environment.CurrentManagedThreadId != thread) throw new IOException("workflow_compile_thread"); }
    internal void BeginPreparation()
    {
        Owner(); if (begun || closed) throw new IOException("workflow_compile_single_use"); begun = true;
        tools = new(Environment.ToolPath, Task.Binding.ToolSha256, Task.Binding.ToolVersion);
        graph.Prepare(); packages.Prepare(); VerifyGeneratedImports();
        prepared = true; capture.BeginPreparation();
    }
    private void VerifyGeneratedImports()
    {
        var declared = Task.CopyInputs().ToDictionary(f => f.Path, StringComparer.Ordinal);
        var packageFiles = restore.CopyPackages().Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in graph.CopyMappings()) {
            string name = Path.GetFileNameWithoutExtension(mapping.Project);
            foreach (string suffix in new[] { ".csproj.nuget.g.props", ".csproj.nuget.g.targets" }) {
                string leaf = name + suffix;
                var file = declared["restore/" + restore.Origin.Binding.JobId.ToString("N") + "/obj/" + name + "/" + leaf];
                byte[] bytes = pins.ReadFile(Path.Combine(mapping.RestoreDirectory, leaf), file.Sha256, file.Bytes, 1048576);
                using var stream = new MemoryStream(bytes, false);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1048576 });
                var document = XDocument.Load(reader);
                foreach (var import in document.Descendants().Where(e => e.Name.LocalName == "Import")) {
                    string path = (string?)import.Attribute("Project") ?? throw new IOException("workflow_generated_import_missing");
                    path = path.Replace("$(NuGetPackageRoot)", restore.PackagesRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        .Replace("$(MSBuildThisFileDirectory)", mapping.RestoreDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal);
                    path = WorkflowBuildCommand.CommandPath(Path.GetFullPath(path, mapping.RestoreDirectory));
                    if (!path.StartsWith(restore.PackagesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        !packageFiles.Contains(Path.GetRelativePath(restore.PackagesRoot, path).Replace('\\', '/')))
                        throw new IOException("workflow_generated_import_outside_retained_packages");
                }
            }
        }
    }
    internal WorkflowCaptureBridgeStatus CaptureStatus() { Owner(); return capture.Snapshot(); }
    internal WorkflowCompiledProduct SealProduct(WorkflowBuildStatus status)
    {
        Owner(); if (product is not null) throw new IOException("workflow_compiled_seal_single_use");
        _ = WorkflowObservedEvidence.Outcome(Task, status, Observe()); // early observation does not consume sealing
        product = new(this); _ = product.Prepare(status); return product;
    }
    internal void CancelPreparation() { Owner(); capture.RequestClose(); }
    public void RequireCurrent()
    {
        Owner(); if (!Ready || borrowed is null) throw new IOException("workflow_compile_not_ready");
        restore.RequireCurrent(); _ = tools!.CopyFiles(); _ = graph.CopyMappings(); _ = packages.CopyFiles(); _ = capture.ReadPreparedFacts();
    }
    public void Start(WorkflowBuildStartPermit permit)
    {
        RequireCurrent(); output.Reserve(permit);
        byte[] mapping = graph.CopyMappingProps();
        using (var file = new FileStream(output.Command.RestoreMappingPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) {
            WorkflowPhysicalPins.ValidateHandle(file.SafeFileHandle, false); file.Write(mapping); file.Flush(true);
        }
        pins.File(output.Command.RestoreMappingPath, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(mapping)), mapping.Length);
        process.Begin(output, tools!, capture);
    }
    public void RequestStop() { Owner(); process.RequestStop(); }
    public WorkflowBuildObservation Observe()
    {
        Owner(); var state = process.Observe();
        if (state.BuildStarted && state.ExecutionDrained && state.FailureCode.Length == 0) {
            try { restore.RequireCurrent(); }
            catch (Exception error) when (error is not OutOfMemoryException) { return state with { FailureCode = error.GetType().Name }; }
        }
        return state;
    }
    public void Close()
    {
        Owner(); if (closed) return;
        if (!Observe().ExecutionDrained) throw new IOException("workflow_compile_not_drained");
        product?.Dispose();
        output.Close(); process.CloseDrained();
        if (!capture.TryClose()) throw new IOException("workflow_compile_capture_closing");
        pins.Dispose(); packages.Dispose(); graph.Dispose(); tools?.Dispose(); borrowed?.Dispose(); borrowed = null; closed = true;
    }
}
