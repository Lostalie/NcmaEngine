using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Ncma.Asset.Import;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma;
using System.Numerics;
using Vector3 = System.Numerics.Vector3;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Scene;
using Ncma.Editor.Services;

// Trusted process fixtures for lifecycle/IPC faults. Not available in the actual worker or Agent schema.
if (args.Length == 2 && args[0].EndsWith(".worker-fixture", StringComparison.Ordinal))
{
    var request = ImportProtocol.ReadAsync<ImportWorkerRequest>(Console.OpenStandardInput()).GetAwaiter().GetResult();
    string mode = File.ReadAllText(args[0]); var output = Console.OpenStandardOutput();
    if (mode == "crash") return 17;
    if (mode == "hang") { Thread.Sleep(15000); return 0; }
    if (mode == "memory") { byte[] allocation = new byte[128 * 1024 * 1024]; for (int i = 0; i < allocation.Length; i += 4096) allocation[i] = 1; Thread.Sleep(15000); GC.KeepAlive(allocation); return 0; }
    if (mode == "oversize") { output.Write([0, 0, 16, 0]); output.Flush(); Thread.Sleep(15000); return 0; }
    if (mode == "wrong_job") { ImportProtocol.WriteAsync(output, new ImportWorkerMessage(1, Guid.NewGuid(), "Progress", 1, 0, 0, "ok", null, 0)).GetAwaiter().GetResult(); return 0; }
    if (mode == "invalid_model")
    {
        byte[] bytes = new byte[48]; File.WriteAllBytes(request.ResultPath, bytes);
        ImportProtocol.WriteAsync(output, new ImportWorkerMessage(1, request.JobId, "Ready", 4, 0, 0, "ok", Hash(bytes), bytes.Length)).GetAwaiter().GetResult(); return 0;
    }
    if (mode == "late")
    {
        Thread.Sleep(250); ImportProtocol.WriteAsync(output, new ImportWorkerMessage(1, request.JobId, "Ready", 4, 0, 0, "ok", new string('0', 64), 48)).GetAwaiter().GetResult(); return 0;
    }
    return 19;
}
if (args.Length != 3) { Console.Error.WriteLine("Expected repository, native build and configuration."); return 2; }
string repository = Path.GetFullPath(args[0]), native = Path.GetFullPath(args[1]), configuration = args[2];
string kernelPath = Path.Combine(native, "NcmaImportKernel.dll");
string workerAssembly = Path.Combine(repository, "managed", "Ncma.Asset.ImportWorker", "bin", configuration, "net8.0", "Ncma.Asset.ImportWorker.dll");
string dotnet = Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe");
dotnet = Path.GetFullPath(dotnet);
string suiteRoot = Path.Combine(AppContext.BaseDirectory, "import-work", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(suiteRoot);
string ascii = Path.Combine(repository, "tests", "assets", "fbx", "blender_279_sausage_6100_ascii.fbx");
string binary = Path.Combine(repository, "tests", "assets", "fbx", "blender_279_sausage_7400_binary.fbx");
string staticFixture = Path.Combine(repository, "tests", "assets", "fbx", "ncma_static_asymmetric_7400_ascii.fbx");
int serial = 0;
string Root(string source)
{
    string root = Path.Combine(suiteRoot, (++serial).ToString()); Directory.CreateDirectory(Path.Combine(root, "assets"));
    File.Copy(source, Path.Combine(root, "assets", "Hero.fbx")); return root;
}
ImportWorkerLaunch Launch() => new(dotnet, workerAssembly, HashFile(workerAssembly), kernelPath, HashFile(kernelPath));
ImportWorkerLaunch Fake(string mode)
{
    string file = Path.Combine(suiteRoot, Guid.NewGuid().ToString("N") + ".worker-fixture"); File.WriteAllText(file, mode);
    return new(dotnet, typeof(ImportCoordinator).Assembly.Location.Replace("Ncma.Assets.Authoring.dll", "Ncma.Asset.Import.Tests.dll"),
        HashFile(typeof(ImportCoordinator).Assembly.Location.Replace("Ncma.Assets.Authoring.dll", "Ncma.Asset.Import.Tests.dll")), file, HashFile(file));
}
ImportCoordinator Coordinator(string root, ImportWorkerLaunch? launch = null, AssetRevisionClock? clock = null,
    Func<string, bool>? approved = null, Func<bool>? current = null, ImportLimits? limits = null) =>
    new(new(root), Guid.NewGuid(), 1, clock ?? new(), launch ?? Launch(), approved ?? (s => s == "assets/Hero.fbx"), current ?? (() => true), limits);
ImportJobSnapshot Wait(ImportCoordinator c, Guid id, int timeout = 20000)
{
    var watch = Stopwatch.StartNew();
    while (watch.ElapsedMilliseconds < timeout)
    {
        var job = c.Inspect(id); if (job.State is ImportJobState.Ready or ImportJobState.Failed or ImportJobState.Cancelled) return job;
        Thread.Sleep(10);
    }
    throw new InvalidOperationException("Import did not reach a terminal state.");
}
void Check(bool value) { if (!value) throw new InvalidOperationException("Assertion failed."); }
void Reject(Action action)
{
    try { action(); } catch (Exception e) when (e is ArgumentException or IOException or InvalidOperationException or System.Text.Json.JsonException) { return; }
    throw new InvalidOperationException("Expected rejection.");
}
void Drain(ImportCoordinator c) { c.Completion.Wait(TimeSpan.FromSeconds(10)); Check(c.Completion.IsCompletedSuccessfully); }
AssetRecord Record(Guid id, string hash, bool staticOnly = false, int rate = 30) => new(1, id, staticOnly ? AssetKind.StaticMesh : AssetKind.Character,
    "assets/Hero.fbx", hash, "ncma.ufbx", 1, new(1, rate, true), [], [], null);
AssetWriteScope Scope(Guid id, Func<bool>? current = null) => new(["assets/Hero.ncmeta"], ["assets/Hero.fbx"], [id], current ?? (() => true));
CapabilityResult Invoke(EditSession edit, AssetImportCommands commands, AssetRevisionClock clock, Guid ticket, bool confirm = false) =>
    edit.Invoke(new(2, Guid.NewGuid(), edit.SessionId, edit.Revision, AssetImportCommands.CapabilityName,
        JsonSerializer.SerializeToElement(new { ticket, expectedAssetRevision = clock.Revision, projectGeneration = 1, confirmNewIdentities = confirm })),
        new([AssetImportCommands.CapabilityName, "ncma.history.undo", "ncma.history.redo"]));
CapabilityResult History(EditSession edit, bool redo = false) => edit.Invoke(new(2, Guid.NewGuid(), edit.SessionId, edit.Revision,
    redo ? "ncma.history.redo" : "ncma.history.undo", JsonSerializer.SerializeToElement(new { })), new(["ncma.history.undo", "ncma.history.redo", AssetImportCommands.CapabilityName]));
Guid Prepare(AssetProjectAuthoring project, ImportCoordinator c, AssetRecord record)
{
    Guid job = c.Enqueue(record.SourcePath, record.Settings.SampleRate, project.Clock.Revision, 1, record.SourceHash, record.Kind == AssetKind.StaticMesh);
    var result = Wait(c, job); if (result.State != ImportJobState.Ready) throw new InvalidOperationException("Worker failed: " + result.Code);
    Guid ticket = project.Imports.PrepareAsync(c, job, "assets/Hero.ncmeta", record); var watch = Stopwatch.StartNew();
    while (!project.Imports.IsPrepared(ticket) && watch.Elapsed < TimeSpan.FromSeconds(15)) Thread.Sleep(5);
    project.Imports.InspectPrepared(ticket); return ticket;
}
var tests = new (string Name, Action Run)[]
{
    ("Static centimetre asymmetric fixture bakes transforms, UV and tangents; character remains strict", () => {
        using (var strict = new ImportKernel(kernelPath, HashFile(kernelPath))) Reject(() => strict.LoadAndCopy(staticFixture, 30));
        ImportedModel raw; using (var kernel = new ImportKernel(kernelPath, HashFile(kernelPath))) raw = kernel.LoadAndCopy(staticFixture, 30, staticOnly: true);
        Check(Math.Abs(raw.SourceUnitMetres - .01) < .00001); var record = Record(Guid.NewGuid(), HashFile(staticFixture), true);
        var plan = ModelImportPlanner.Build(raw, record, Guid.NewGuid(), true); var manifest = ModelAssetManifestCodec.ValidateBundle(plan.DerivedBytes, plan.Record);
        Check(manifest.Skeleton is null && manifest.Clips.Length == 0); var blocks = DerivedAssetCodec.Decode(plan.DerivedBytes);
        var mesh = ModelPayloadCodec.DecodeMesh(blocks.Single(b => b.AssetId == manifest.Meshes[0].Mesh).Data);
        Check(!mesh.Skinned && mesh.Bindings.Length == 0 && mesh.Tangents.Length == mesh.Vertices.Length);
        var positions = mesh.Vertices.Select(v => v.Position).ToArray();
        Check(positions.Any(p => Vector3.Distance(p, new(1, .5f, .25f)) < .0001f));
        Check(positions.Any(p => Vector3.Distance(p, new(5, .5f, .25f)) < .0001f));
        Check(positions.Any(p => Vector3.Distance(p, new(1, 1.5f, .25f)) < .0001f));
        Check(mesh.Vertices[0].UV == new Vector2(0, 1));
        using var badStatic = new ImportKernel(kernelPath, HashFile(kernelPath)); Reject(() => badStatic.LoadAndCopy(ascii, 30, staticOnly: true));
    }),
    ("Typed model blocks round-trip and reimport preserves exact UUID evidence; changed rig/name conflicts", () => {
        ImportedModel raw; using (var kernel = new ImportKernel(kernelPath, HashFile(kernelPath))) raw = kernel.LoadAndCopy(ascii, 30);
        Guid project = Guid.NewGuid(); var record = Record(Guid.NewGuid(), HashFile(ascii)); var initial = ModelImportPlanner.Build(raw, record, project, false);
        var repeated = ModelImportPlanner.Build(raw, record, project, false, AssetRecordCodec.Decode(AssetRecordCodec.Encode(initial.Record)));
        Check(!repeated.RequiresConfirmation && initial.Record.Subassets.Select(s => s.AssetId).Order().SequenceEqual(repeated.Record.Subassets.Select(s => s.AssetId).Order()));
        foreach (var block in DerivedAssetCodec.Decode(initial.DerivedBytes))
        {
            byte[] encoded = block.Kind switch { AssetKind.SkinnedMesh => ModelPayloadCodec.Encode(ModelPayloadCodec.DecodeMesh(block.Data)), AssetKind.Skeleton => ModelPayloadCodec.Encode(ModelPayloadCodec.DecodeSkeleton(block.Data)),
                AssetKind.Clip => ModelPayloadCodec.Encode(ModelPayloadCodec.DecodeClip(block.Data)), AssetKind.MaterialSet => ModelPayloadCodec.Encode(ModelPayloadCodec.DecodeMaterials(block.Data)), _ => ModelAssetManifestCodec.Encode(ModelAssetManifestCodec.Decode(block.Data)) };
            Check(encoded.SequenceEqual(block.Data));
            if (block.Kind == AssetKind.SkinnedMesh) { Reject(() => ModelPayloadCodec.DecodeMesh(block.Data[..^1])); Reject(() => ModelPayloadCodec.DecodeMesh([.. block.Data, 0])); }
        }
        var renamed = raw with { Meshes = raw.Meshes.Select(m => m with { Name = "Renamed " + m.Name }).ToArray() };
        var conflict = ModelImportPlanner.Build(renamed, record, project, false, initial.Record); Check(conflict.RequiresConfirmation && conflict.Record.Subassets.Any(s => s.Tombstone));
        var bones = (ImportBone[])raw.Bones.Clone(); bones[^1] = bones[^1] with { BindLocal = bones[^1].BindLocal with { Position = bones[^1].BindLocal.Position + new Vector3(.1f, 0, 0) } };
        Check(ModelImportPlanner.Build(raw with { Bones = bones }, record, project, false, initial.Record).RequiresConfirmation);
        Reject(() => ModelImportPlanner.Build(raw with { Meshes = [raw.Meshes[0], raw.Meshes[0]] }, record, project, false));
    }),
    ("RH Z-up metres, multiple static instances and degenerate UV fallback are deterministic", () => {
        string fixture = Path.Combine(repository, "tests/assets/fbx/ncma_static_instances_zup_7400_ascii.fbx");
        ImportedModel raw; using (var kernel = new ImportKernel(kernelPath, HashFile(kernelPath))) raw = kernel.LoadAndCopy(fixture, 30, staticOnly: true);
        Check(raw.Meshes.Length == 2); Guid project = Guid.NewGuid(); var record = Record(Guid.NewGuid(), HashFile(fixture), true);
        var plan = ModelImportPlanner.Build(raw, record, project, true);
        var blocks = DerivedAssetCodec.Decode(plan.DerivedBytes);
        var sub = plan.Record.Subassets.Single(s => s.Kind == AssetKind.StaticMesh && s.Name == "InstanceA");
        var mesh = ModelPayloadCodec.DecodeMesh(blocks.Single(b => b.AssetId == sub.AssetId).Data);
        Check(Vector3.Distance(mesh.Vertices[0].Position, new(1, 3, -2)) < .0001f);
        Check(plan.Diagnostics.Any(d => d.Code == "tangent_uv_fallback"));
        string duplicate = Path.Combine(suiteRoot, "duplicate-source.fbx");
        File.WriteAllText(duplicate, File.ReadAllText(fixture).Replace("Model::InstanceB", "Model::InstanceA", StringComparison.Ordinal));
        using (var kernel = new ImportKernel(kernelPath, HashFile(kernelPath))) Reject(() => kernel.LoadAndCopy(duplicate, 30, staticOnly: true));
        var reordered = ModelImportPlanner.Build(raw with { Meshes = raw.Meshes.Reverse().ToArray() }, record, project, true, plan.Record);
        Check(!reordered.RequiresConfirmation && reordered.Record.Subassets.Select(s => s.AssetId).Order().SequenceEqual(plan.Record.Subassets.Select(s => s.AssetId).Order()));
    }),
    ("Multiple clips retain scoped identities when reordered; topology changes require review", () => {
        ImportedModel raw; using (var kernel = new ImportKernel(kernelPath, HashFile(kernelPath))) raw = kernel.LoadAndCopy(ascii, 30);
        Check(raw.Clips.Length >= 3); // Actual frozen FBX has Base/Spin/Wiggle takes, not only synthetic clip lists.
        raw = raw with { Clips = [.. raw.Clips, raw.Clips[^1] with { Name = "Distinct synthetic clip" }] };
        Guid project = Guid.NewGuid(); var record = Record(Guid.NewGuid(), HashFile(ascii)); var a = ModelImportPlanner.Build(raw, record, project, false);
        var b = ModelImportPlanner.Build(raw with { Clips = raw.Clips.Reverse().ToArray() }, record, project, false, a.Record);
        Check(!b.RequiresConfirmation && a.Record.Subassets.Select(s => s.AssetId).Order().SequenceEqual(b.Record.Subassets.Select(s => s.AssetId).Order()));
        var indices = (uint[])raw.Meshes[0].Indices.Clone(); (indices[0], indices[1]) = (indices[1], indices[0]);
        var changed = raw with { Meshes = [raw.Meshes[0] with { Indices = indices }] };
        Check(ModelImportPlanner.Build(changed, record, project, false, a.Record).RequiresConfirmation);
        Reject(() => ModelImportPlanner.Build(raw with { Clips = [raw.Clips[0], raw.Clips[0]] }, record, project, false));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { ModelImportPlanner.Build(raw, record, project, false, cancellation: cancelled.Token); throw new Exception("Expected cancellation."); } catch (OperationCanceledException) { }
    }),
    ("Five-influence truncation and unweighted rigid fallback report explicit diagnostics", () => {
        string fixture = Path.Combine(repository, "tests/assets/fbx/ncma_skin_weights_7400_ascii.fbx");
        ImportedModel raw; using (var kernel = new ImportKernel(kernelPath, HashFile(kernelPath))) raw = kernel.LoadAndCopy(fixture, 30);
        Check(raw.Warnings.Any(w => w.Contains("strongest four", StringComparison.Ordinal)) && raw.Warnings.Any(w => w.Contains("Unweighted", StringComparison.Ordinal)));
        var vertices = raw.Meshes.Single().Vertices; Check(vertices.Any(v => v.Weights == new Vector4(.25f, .25f, .25f, .25f)) && vertices.Any(v => v.Weights == new Vector4(1, 0, 0, 0)));
        var plan = ModelImportPlanner.Build(raw, Record(Guid.NewGuid(), HashFile(fixture)), Guid.NewGuid(), false);
        Check(plan.Diagnostics.Count(d => d.Code == "fbx_conversion_warning") >= 2); ModelAssetManifestCodec.ValidateBundle(plan.DerivedBytes, plan.Record);
    }),
    ("Actual project restart reimport preserves UUIDs and cold Play needs no FBX", () => {
        string root = Root(ascii); Guid id = Guid.NewGuid(), projectId = Guid.NewGuid(); var record = Record(id, HashFile(ascii));
        var edit = new EditSession(new SceneDocument()); var project = new AssetProjectAuthoring(root, projectId, 1, edit, Scope(id));
        var c = project.CreateImportCoordinator(Launch(), _ => true); Guid ticket = Prepare(project, c, record); Check(Invoke(edit, project.Imports, project.Clock, ticket).Status == "ok");
        var first = AssetRecordCodec.Decode(File.ReadAllBytes(Path.Combine(root, "assets/Hero.ncmeta"))); project.Dispose(); project.Completion.GetAwaiter().GetResult();
        edit = new(new SceneDocument()); project = new(root, projectId, 1, edit, Scope(id)); project.GenerationPreparation.GetAwaiter().GetResult();
        c = project.CreateImportCoordinator(Launch(), _ => true); ticket = Prepare(project, c, record); var again = project.Imports.InspectPrepared(ticket);
        Check(!again.RequiresIdentityConfirmation && first.Subassets.Select(s => s.AssetId).Order().SequenceEqual(again.Record.Subassets.Select(s => s.AssetId).Order()));
        Check(Invoke(edit, project.Imports, project.Clock, ticket).Status == "ok"); project.Dispose(); project.Completion.GetAwaiter().GetResult();
        File.Move(Path.Combine(root, "assets/Hero.fbx"), Path.Combine(root, "assets/Absent.fbx"));
        using var owner = new EditorSessionOwner("Cold derived Play"); owner.ConfigureAssets(root, projectId, 2); owner.Assets!.GenerationPreparation.GetAwaiter().GetResult();
        owner.StartPlay(); Check(owner.Edit!.State.Frozen); owner.StopPlay(); Check(!owner.Edit.State.Frozen);
    }),
    ("Formal Editor import, Play version locks, exact GC and retained pressure budget", () => {
        string root = Root(staticFixture); Guid id = Guid.NewGuid(), projectId = Guid.NewGuid(); var owner = new EditorSessionOwner("Import owner");
        owner.ConfigureAssets(root, projectId, 1, Scope(id)); owner.ConfigureImportTools(Launch(), _ => true);
        var project = owner.Assets!; var c = owner.Imports!; Guid ticket = Prepare(project, c, Record(id, HashFile(staticFixture), true));
        var info = project.Imports.InspectPrepared(ticket); Check(Invoke(owner.Edit!, project.Imports, project.Clock, ticket).Status == "ok");
        Reject(() => owner.StartPlay()); owner.RefreshAssets(true); owner.StartPlay(); Check(owner.Edit!.State.Frozen);
        using (var collector = new DerivedGenerationStore(new(root), projectId)) Reject(() => collector.Collect([info.Record.Generation!.RelativePath], _ => true, new([])));
        byte[] derived = File.ReadAllBytes(Path.Combine(root, info.Record.Generation!.RelativePath));
        owner.StopPlay(); Check(History(owner.Edit!).Status == "ok"); owner.Dispose(); project.Completion.GetAwaiter().GetResult();
        string unknown = info.Record.Generation.RelativePath.Replace("/1-", "/2-", StringComparison.Ordinal); File.WriteAllBytes(Path.Combine(root, unknown), [1]);
        using (var collector = new DerivedGenerationStore(new(root), projectId)) {
            Reject(() => collector.Collect([info.Record.Generation.RelativePath, unknown], _ => true, new([])));
            Check(File.Exists(Path.Combine(root, info.Record.Generation.RelativePath)));
            collector.Collect([info.Record.Generation.RelativePath], _ => true, new([]));
        }
        Check(!File.Exists(Path.Combine(root, info.Record.Generation!.RelativePath)));
        string directory = Path.GetDirectoryName(Path.Combine(root, info.Record.Generation.RelativePath))!;
        for (int i = 0; i < DerivedGenerationStore.MaxGenerations; i++) File.WriteAllBytes(Path.Combine(directory, $"unrecognized-{i}"), [1]);
        using var pressure = new DerivedGenerationStore(new(root), projectId); Reject(() => pressure.Prepare(info.Record, derived));
        Check(!File.Exists(Path.Combine(root, info.Record.Generation.RelativePath)) && File.Exists(Path.Combine(root, unknown)));
    }),
    ("Persistent import uses sole history, cold reload without FBX, Undo/Redo and exact grants", () => {
        string root = Root(ascii); Guid id = Guid.NewGuid(), projectId = Guid.NewGuid(); var record = Record(id, HashFile(ascii)); var edit = new EditSession(new SceneDocument());
        using var project = new AssetProjectAuthoring(root, projectId, 1, edit, Scope(id)); using var c = project.CreateImportCoordinator(Launch(), _ => true);
        Guid ticket = Prepare(project, c, record); var info = project.Imports.InspectPrepared(ticket);
        Check(!File.Exists(Path.Combine(root, "assets", "Hero.ncmeta")) && info.Record.Generation is not null);
        Check(Invoke(edit, project.Imports, project.Clock, ticket).Status == "ok" && edit.State.UndoCount == 1);
        var installed = AssetRecordCodec.Decode(File.ReadAllBytes(Path.Combine(root, "assets", "Hero.ncmeta")));
        byte[] derived = File.ReadAllBytes(Path.Combine(root, installed.Generation!.RelativePath)); ModelAssetManifestCodec.ValidateBundle(derived, installed);
        File.Move(Path.Combine(root, "assets", "Hero.fbx"), Path.Combine(root, "assets", "Absent.fbx")); // No parser used for cold read.
        using (var cold = new DerivedGenerationStore(new(root), projectId)) cold.Prepare(installed, null);
        Check(History(edit).Status == "ok" && !File.Exists(Path.Combine(root, "assets", "Hero.ncmeta")));
        Check(History(edit, true).Status == "ok" && File.Exists(Path.Combine(root, "assets", "Hero.ncmeta")));
        File.WriteAllText(Path.Combine(root, "assets", "Hero.ncmeta"), "external edit"); Check(History(edit).Status != "ok");
        Check(File.ReadAllText(Path.Combine(root, "assets", "Hero.ncmeta")) == "external edit"); Drain(c);
    }),
    ("Reimport changed sampling requires explicit identity confirmation and preserves old Play/history pins", () => {
        string root = Root(ascii); Guid id = Guid.NewGuid(), projectId = Guid.NewGuid(); var edit = new EditSession(new SceneDocument());
        using var project = new AssetProjectAuthoring(root, projectId, 1, edit, Scope(id)); using var c = project.CreateImportCoordinator(Launch(), _ => true);
        Guid ticket = Prepare(project, c, Record(id, HashFile(ascii))); Check(Invoke(edit, project.Imports, project.Clock, ticket).Status == "ok");
        var old = AssetRecordCodec.Decode(File.ReadAllBytes(Path.Combine(root, "assets", "Hero.ncmeta")));
        using var play = project.Imports.Generations.PinForPlay([old]);
        ticket = Prepare(project, c, Record(id, HashFile(ascii), rate: 60)); var next = project.Imports.InspectPrepared(ticket);
        Check(next.RequiresIdentityConfirmation && next.Record.Subassets.Any(s => s.Tombstone));
        Check(Invoke(edit, project.Imports, project.Clock, ticket).Code == "asset_identity_confirmation_required");
        Check(Invoke(edit, project.Imports, project.Clock, ticket, true).Status == "ok");
        Check(File.Exists(Path.Combine(root, old.Generation!.RelativePath)));
        Reject(() => project.Imports.Generations.Collect([old.Generation.RelativePath], _ => true, new AssetCatalog([])));
        Check(History(edit).Status == "ok"); Check(AssetRecordCodec.Decode(File.ReadAllBytes(Path.Combine(root, "assets", "Hero.ncmeta"))).Generation == old.Generation);
        Check(History(edit, true).Status == "ok"); Drain(c);
    }),
    ("Every generation/descriptor publish fault preserves prior version and restart recovers journals", () => {
        foreach (string phase in new[] { "generation_written", "generation_published", "journal_created", "backup:0", "published:0", "metadata_published", "completion_ready", "commit_decided" })
        {
            string root = Root(ascii); Guid id = Guid.NewGuid(), projectId = Guid.NewGuid(); var record = Record(id, HashFile(ascii));
            var edit = new EditSession(new SceneDocument()); bool armed = false;
            var project = new AssetProjectAuthoring(root, projectId, 1, edit, Scope(id), step => { if (armed && step == phase) throw new IOException("Injected " + phase); });
            var c = project.CreateImportCoordinator(Launch(), _ => true); Guid first = Prepare(project, c, record); Check(Invoke(edit, project.Imports, project.Clock, first).Status == "ok");
            byte[] old = File.ReadAllBytes(Path.Combine(root, "assets", "Hero.ncmeta")); armed = true;
            if (phase.StartsWith("generation", StringComparison.Ordinal)) Reject(() => Prepare(project, c, record with { Settings = new(1, 60, true) }));
            else
            {
                Guid ticket = Prepare(project, c, record with { Settings = new(1, 60, true) });
                var result = Invoke(edit, project.Imports, project.Clock, ticket, true); Check(result.Status != "ok");
            }
            project.Dispose(); Drain(c); project.Imports.Completion.ContinueWith(_ => { }).Wait();
            using var restarted = new AssetProjectAuthoring(root, projectId, 1, new EditSession(new SceneDocument()), Scope(id));
            Check(!File.Exists(Path.Combine(root, "assets", "Hero.ncmeta.journal")));
            byte[] now = File.ReadAllBytes(Path.Combine(root, "assets", "Hero.ncmeta"));
            if (phase != "commit_decided") Check(now.SequenceEqual(old)); else Check(AssetRecordCodec.Decode(now).Generation!.Number == 2);
        }
    }),
    ("Interrupted direct publish leaves journal; fresh owner recovers without old in-memory history", () => {
        foreach (string phase in new[] { "journal_created", "backup:0", "published:0", "metadata_published", "completion_ready", "commit_decided" }) {
            string root = Root(ascii); Guid id = Guid.NewGuid(), projectId = Guid.NewGuid(); var record = Record(id, HashFile(ascii));
            var edit = new EditSession(new SceneDocument()); bool armed = false;
            var project = new AssetProjectAuthoring(root, projectId, 1, edit, Scope(id), step => { if (armed && step == phase) throw new IOException("Interrupted " + phase); });
            var c = project.CreateImportCoordinator(Launch(), _ => true); Guid ticket = Prepare(project, c, record); Check(Invoke(edit, project.Imports, project.Clock, ticket).Status == "ok");
            byte[] old = File.ReadAllBytes(Path.Combine(root, "assets/Hero.ncmeta"));
            ticket = Prepare(project, c, record with { Settings = new(1, 60, true) });
            var memento = project.Imports.Prepare(JsonSerializer.SerializeToElement(new { ticket, expectedAssetRevision = project.Clock.Revision, projectGeneration = 1, confirmNewIdentities = true }));
            armed = true;
            Reject(() => { project.Imports.Publish(memento, true); project.Imports.Complete(memento, true); }); // Deliberately no compensation: crash boundary.
            Check(File.Exists(Path.Combine(root, "assets/Hero.ncmeta.journal"))); project.Dispose(); project.Completion.GetAwaiter().GetResult();
            using var recovered = new AssetProjectAuthoring(root, projectId, 1, new(new SceneDocument()), Scope(id));
            byte[] now = File.ReadAllBytes(Path.Combine(root, "assets/Hero.ncmeta"));
            if (phase == "commit_decided") Check(AssetRecordCodec.Decode(now).Generation!.Number == 2); else Check(now.SequenceEqual(old));
            Check(!File.Exists(Path.Combine(root, "assets/Hero.ncmeta.journal")));
        }
    }),
    ("Scope revocation, cancellation/frozen Play, external metadata and GC unknown contents are strict", () => {
        string root = Root(ascii); Guid id = Guid.NewGuid(), projectId = Guid.NewGuid(); var edit = new EditSession(new SceneDocument()); bool granted = true;
        using var project = new AssetProjectAuthoring(root, projectId, 1, edit, Scope(id, () => granted)); using var c = project.CreateImportCoordinator(Launch(), _ => granted);
        Guid ticket = Prepare(project, c, Record(id, HashFile(ascii))); edit.SetFrozen(true); Check(Invoke(edit, project.Imports, project.Clock, ticket).Code == "play_frozen"); edit.SetFrozen(false);
        granted = false; Check(Invoke(edit, project.Imports, project.Clock, ticket).Status == "denied"); granted = true;
        var info = project.Imports.InspectPrepared(ticket); c.Cancel(info.JobId); Check(Invoke(edit, project.Imports, project.Clock, ticket).Status != "ok");
        Check(!File.Exists(Path.Combine(root, "assets", "Hero.ncmeta"))); Drain(c);
        using var collector = new DerivedGenerationStore(new(root), projectId);
        Reject(() => collector.Collect([info.Record.Generation!.RelativePath], _ => true, new AssetCatalog([]))); // History/session pin blocks even another store.
        Reject(() => collector.Collect(["../escape.nca"], _ => true, new AssetCatalog([])));
        Reject(() => collector.Collect([info.Record.Generation!.RelativePath], _ => false, new AssetCatalog([])));
    }),
    ("Validated Editor package launches hashed tool dependencies; Players and tampering are rejected", () => {
        using var index = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repository, $"out/verification/m2-7/{configuration}/packages.json")));
        string editor = index.RootElement.GetProperty("editor").GetString()!;
        Ncma.Application.DeploymentManifest.Validate(editor); var launch = ImportToolDeployment.FromValidatedEditorPackage(editor);
        Check(launch.Dependencies is { Length: > 4 }); string root = Root(staticFixture);
        using (var c = Coordinator(root, launch)) { Guid id = c.Enqueue("assets/Hero.fbx", 30, 0, 1, HashFile(staticFixture), true); Check(Wait(c, id).State == ImportJobState.Ready); Drain(c); }
        foreach (string player in new[] { "playerNull", "playerDx11" }) Reject(() => ImportToolDeployment.FromValidatedEditorPackage(index.RootElement.GetProperty(player).GetString()!));
        var dependencies = (ImportToolFile[])launch.Dependencies!.Clone(); dependencies[0] = dependencies[0] with { Hash = new string('0', 64) };
        using var bad = Coordinator(Root(staticFixture), launch with { Dependencies = dependencies });
        Guid job = bad.Enqueue("assets/Hero.fbx", 30, 0, 1, HashFile(staticFixture), true); Check(Wait(bad, job).State == ImportJobState.Failed); Drain(bad);
    }),
    ("C ABI copied streams survive close and NIM1 binary round trip", () => {
        foreach (string fixture in new[] { ascii, binary })
        {
            ImportedModel model;
            using (var kernel = new ImportKernel(kernelPath, HashFile(kernelPath))) { model = kernel.LoadAndCopy(fixture, 30); Check(kernel.ReadProgress().Phase == 4); }
            byte[] bytes = ImportedModelCodec.Encode(model); var loaded = ImportedModelCodec.Decode(bytes);
            Check(ImportedModelCodec.Encode(loaded).SequenceEqual(bytes)); Check(model.Meshes.Length > 0 && model.Bones.Length > 0 && model.Clips.Length > 0);
            foreach (var clip in loaded.Clips) foreach (var track in clip.Tracks) Check(track.Keys[0].Time == 0 && track.Keys[^1].Time == clip.Duration);
            Reject(() => ImportedModelCodec.Decode(bytes[..^1])); Reject(() => ImportedModelCodec.Decode([.. bytes, 0]));
            byte[] bad = (byte[])bytes.Clone(); BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(32), int.MaxValue); Reject(() => ImportedModelCodec.Decode(bad));
            Reject(() => ImportedModelCodec.Encode(model with { SampleRate = double.NaN }));
            var mesh = model.Meshes[0]; uint[] indices = (uint[])mesh.Indices.Clone(); indices[0] = uint.MaxValue;
            Reject(() => ImportedModelCodec.Encode(model with { Meshes = [mesh with { Indices = indices }] }));
            var firstVertex = mesh.Vertices[0]; var vs = (ImportVertex[])mesh.Vertices.Clone(); vs[0] = firstVertex with { Weights = new(float.NaN, 0, 0, 0) };
            Reject(() => ImportedModelCodec.Encode(model with { Meshes = [mesh with { Vertices = vs }] }));
        }
    }),
    ("Kernel hash, owner thread and pre-load cancellation are strict", () => {
        Reject(() => new ImportKernel(kernelPath, new string('0', 64)));
        using var kernel = new ImportKernel(kernelPath, HashFile(kernelPath));
        Task.Run(() => Reject(() => kernel.LoadAndCopy(ascii, 30))).Wait();
        Task.Run(kernel.Cancel).Wait();
        try { kernel.LoadAndCopy(ascii, 30); throw new InvalidOperationException("Expected cancellation."); } catch (OperationCanceledException) { }
    }),
    ("Closed IPC rejects oversized, unknown, duplicate and missing fields", () => {
        Reject(() => ImportProtocol.ReadAsync<ImportWorkerCancel>(new MemoryStream([0, 0, 1, 0])).GetAwaiter().GetResult());
        foreach (string json in new[] { "{}", "{\"version\":1,\"version\":1}", "{\"version\":1,\"jobId\":\"" + Guid.NewGuid() + "\",\"operation\":\"cancel\",\"extra\":0}" })
        {
            byte[] payload = Encoding.UTF8.GetBytes(json), frame = new byte[payload.Length + 4]; BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length); payload.CopyTo(frame, 4);
            Reject(() => ImportProtocol.ReadAsync<ImportWorkerCancel>(new MemoryStream(frame)).GetAwaiter().GetResult());
        }
        Reject(() => ImportProtocol.Validate(new ImportWorkerMessage(1, Guid.NewGuid(), "Ready", 4, 0, 0, "ok", new string('0', 64), 48), Guid.NewGuid()));
    }),
    ("Real ASCII/binary worker validates manifest, pins source and keeps last metadata untouched", () => {
        foreach (string fixture in new[] { ascii, binary })
        {
            string root = Root(fixture); string descriptor = Path.Combine(root, "assets", "Hero.ncmeta"); File.WriteAllText(descriptor, "last-good-descriptor");
            using var c = Coordinator(root); Guid id = c.Enqueue("assets/Hero.fbx", 30, 0, 1, HashFile(fixture));
            var status = Wait(c, id); Check(status.State == ImportJobState.Ready); var info = c.RequireReady(id, 0, 1);
            Check(info.SourceHash == HashFile(fixture) && info.Meshes > 0 && info.Bones > 0 && info.Clips > 0);
            Check(File.ReadAllText(descriptor) == "last-good-descriptor");
            Reject(() => File.WriteAllText(Path.Combine(root, "assets", "Hero.fbx"), "overwrite"));
            Reject(() => File.Move(Path.Combine(root, "assets", "Hero.fbx"), Path.Combine(root, "assets", "Moved.fbx")));
            Reject(() => Directory.Move(Path.Combine(root, "assets"), Path.Combine(root, "replaced")));
            Drain(c); c.Cancel(id); Reject(() => c.RequireReady(id, 0, 1));
            File.WriteAllText(Path.Combine(root, "assets", "Hero.fbx"), "allowed after cancellation");
        }
    }),
    ("Queue/candidate bounds, queued cancellation and Ready cancellation", () => {
        string root = Root(ascii); using var c = Coordinator(root); Guid[] jobs = new Guid[ImportCoordinator.MaxJobs];
        for (int i = 0; i < jobs.Length; i++) jobs[i] = c.Enqueue("assets/Hero.fbx", 30, 0, 1, HashFile(ascii));
        Reject(() => c.Enqueue("assets/Hero.fbx", 30, 0, 1, HashFile(ascii))); c.Cancel(jobs[^1]);
        for (int i = 0; i < jobs.Length - 1; i++) Check(Wait(c, jobs[i]).State == ImportJobState.Ready);
        Check(c.Pump().Count(j => j.State == ImportJobState.Ready) == 4); Drain(c);
        c.Cancel(jobs[0]); Check(c.Inspect(jobs[0]).State == ImportJobState.Cancelled);
    }),
    ("Revocation, stale revision/generation and source paths deny before/after Ready", () => {
        string root = Root(ascii); bool allowed = true, current = true; var clock = new AssetRevisionClock();
        using var c = Coordinator(root, clock: clock, approved: _ => allowed, current: () => current);
        Reject(() => c.Enqueue("../Hero.fbx", 30, 0, 1, HashFile(ascii))); Reject(() => c.Enqueue("assets/Hero.fbx", 30, 0, 2, HashFile(ascii)));
        allowed = false; Reject(() => c.Enqueue("assets/Hero.fbx", 30, 0, 1, HashFile(ascii))); allowed = true;
        Guid id = c.Enqueue("assets/Hero.fbx", 30, 0, 1, HashFile(ascii)); Check(Wait(c, id).State == ImportJobState.Ready);
        clock.Advance(); Reject(() => c.RequireReady(id, 0, 1)); Check(c.Pump()[0].State == ImportJobState.Cancelled);
        id = c.Enqueue("assets/Hero.fbx", 30, 1, 1, HashFile(ascii)); Check(Wait(c, id).State == ImportJobState.Ready);
        allowed = false; Reject(() => c.RequireReady(id, 1, 1)); c.Pump(); Check(c.Inspect(id).State == ImportJobState.Cancelled);
        current = false; Reject(() => c.Enqueue("assets/Hero.fbx", 30, 1, 1, HashFile(ascii))); Drain(c);
    }),
    ("Worker crash, malformed output/model and integrity failure cannot publish", () => {
        foreach (string mode in new[] { "crash", "wrong_job", "oversize", "invalid_model" })
        {
            string root = Root(ascii); using var c = Coordinator(root, Fake(mode), limits: new(TimeSpan.FromSeconds(3), ImportLimits.Default.MaxPrivateBytes));
            Guid id = c.Enqueue("assets/Hero.fbx", 30, 0, 1, HashFile(ascii)); Check(Wait(c, id).State == ImportJobState.Failed); Reject(() => c.RequireReady(id, 0, 1)); Drain(c);
        }
        using var bad = Coordinator(Root(ascii), Launch() with { WorkerHash = new string('0', 64) });
        Check(Wait(bad, bad.Enqueue("assets/Hero.fbx", 30, 0, 1, HashFile(ascii))).State == ImportJobState.Failed); Drain(bad);
    }),
    ("Timeout/memory watchdog kills only its owned worker and releases source", () => {
        foreach (string mode in new[] { "hang", "memory" })
        {
            string root = Root(ascii); using var c = Coordinator(root, Fake(mode), limits: new(TimeSpan.FromMilliseconds(mode == "hang" ? 500 : 5000), mode == "memory" ? 48L * 1024 * 1024 : ImportLimits.Default.MaxPrivateBytes));
            Guid id = c.Enqueue("assets/Hero.fbx", 30, 0, 1, HashFile(ascii)); var result = Wait(c, id);
            Check(result.State == ImportJobState.Failed && result.Code == (mode == "hang" ? "worker_timeout" : "worker_memory_budget"));
            Drain(c); File.WriteAllText(Path.Combine(root, "assets", "Hero.fbx"), "released");
        }
    }),
    ("Close is nonblocking, queued/late Ready is rejected and leases are released", () => {
        string root = Root(ascii); var c = Coordinator(root, Fake("late"));
        Guid id = c.Enqueue("assets/Hero.fbx", 30, 0, 1, HashFile(ascii)); var watch = Stopwatch.StartNew();
        c.Dispose(); Check(watch.ElapsedMilliseconds < 1000); Drain(c); Reject(() => c.RequireReady(id, 0, 1));
        File.WriteAllText(Path.Combine(root, "assets", "Hero.fbx"), "released");
    }),
    ("Active cancellation, late result after close and source hash changes are rejected", () => {
        foreach (string mode in new[] { "hang", "late" })
        {
            string root = Root(ascii); var c = Coordinator(root, Fake(mode)); Guid id = c.Enqueue("assets/Hero.fbx", 30, 0, 1, HashFile(ascii));
            var watch = Stopwatch.StartNew();
            while (c.Inspect(id).State != ImportJobState.Parsing && watch.Elapsed < TimeSpan.FromSeconds(3)) Thread.Sleep(1);
            Check(c.Inspect(id).State == ImportJobState.Parsing);
            if (mode == "hang") { c.Cancel(id); Check(c.Inspect(id).State == ImportJobState.Cancelled); } else c.Dispose();
            Drain(c); if (mode == "hang") c.Dispose();
            File.WriteAllText(Path.Combine(root, "assets", "Hero.fbx"), "released");
        }
        string changed = Root(ascii); File.WriteAllText(Path.Combine(changed, "assets", "Hero.fbx"), "changed after plan");
        using var stale = Coordinator(changed); Guid job = stale.Enqueue("assets/Hero.fbx", 30, 0, 1, HashFile(ascii));
        var failure = Wait(stale, job); Check(failure.State == ImportJobState.Failed && failure.Code == "source_changed"); Drain(stale);
    }),
    ("Editor-owner pump stays responsive while worker hangs, preview sampling is isolated", () => {
        string root = Root(binary); using var c = Coordinator(root, Fake("hang"), limits: new(TimeSpan.FromSeconds(10), ImportLimits.Default.MaxPrivateBytes));
        Guid id = c.Enqueue("assets/Hero.fbx", 30, 0, 1, HashFile(binary));
        using var preview = new ImportedCharacterResource(Path.Combine(native, "NcmaNative.dll"), binary);
        var sample = new float[preview.SampleFloatCount];
        var watch = Stopwatch.StartNew(); for (int i = 0; i < 100; i++) { c.Pump(); preview.Sample(0, 0, sample); }
        Check(watch.Elapsed < TimeSpan.FromSeconds(5)); c.Cancel(id); Drain(c);
    }),
};
int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception e) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + e); }
}
Console.WriteLine($"Import tests: {tests.Length - failures}/{tests.Length} passed."); return failures == 0 ? 0 : 1;
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
