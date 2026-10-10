using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static partial class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLinkW(string name, string existing, nint security);
    // Independent closed NCE fixture: exercises CPU persistence, never loads a renderer/Cook.
    private static EnvironmentPackage EnvironmentFixturePackage(Guid id, ulong generation)
    {
        var settings = new EnvironmentCookSettings(2, 2, 2, 64);
        byte[] bytes = new byte[EnvironmentPackage.HeaderBytes + settings.FloatCount * 4];
        uint[] header = [0x3145434E, 1, 1, 160, 4, 2, 2, 2, 2, 64, 2, (uint)settings.FloatCount];
        for (int i = 0; i < header.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), header[i]);
        id.TryWriteBytes(bytes.AsSpan(48)); Guid.NewGuid().TryWriteBytes(bytes.AsSpan(64));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(80), generation);
        SHA256.HashData("trusted decoded test source"u8).CopyTo(bytes, 88);
        for (int i = 0; i < settings.FloatCount; i++) {
            float value = i >= settings.FloatCount - 8 ? (i % 2 == 0 ? .5f : .1f) : i % 4 == 3 ? 1 : i < 96 ? .18f * MathF.PI : .18f;
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(160 + i * 4), value);
        }
        SHA256.HashData(bytes.AsSpan(160)).CopyTo(bytes, 120);
        return EnvironmentPackage.Decode(bytes, Convert.ToHexString(SHA256.HashData(bytes)));
    }
    private sealed class EnvironmentFixture
    {
        internal readonly string Root = Path.Combine(OutputRoot(), "environment-c1", Guid.NewGuid().ToString("N"));
        internal readonly Guid Project = Guid.NewGuid(), Id = Guid.NewGuid();
        internal EnvironmentAssetDescriptor Descriptor = null!;
        internal EnvironmentPackage Package = null!;
        internal string FilePath => Path.Combine(Root, Descriptor.Path);
        internal string MetadataPath => Path.Combine(Root, "assets/environment.ncenv");
        internal EnvironmentLightingData Configuration => new(1, Id, Package.Generation, Package.ContentHash, 1, 0, true);
        internal EnvironmentFixture() { Directory.CreateDirectory(Path.Combine(Root, "assets")); Write(1); }
        internal void Write(ulong generation)
        {
            Package = EnvironmentFixturePackage(Id, generation); Descriptor = EnvironmentAssetDescriptor.FromPackage(Project, Package);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllBytes(FilePath, Package.CopyBytes());
            SaveDescriptor(Descriptor);
        }
        internal void SaveDescriptor(EnvironmentAssetDescriptor descriptor) => File.WriteAllBytes(MetadataPath, descriptor.Encode());
        internal SceneDocument Scene()
        { var doc = Document(); doc.World.CreateObject("Environment").Set(Configuration); return doc; }
        internal RuntimeAssetSnapshot Prepare(bool strict = true) => RuntimeAssetLoader.Prepare(Root, Project, [Ref(Id, AssetKind.Environment)], strict);
    }
    private static RuntimeEnvironmentAsset EnvironmentAsset(RuntimeAssetLease lease, Guid id) => (RuntimeEnvironmentAsset)lease.Require(id, AssetKind.Environment);
    private static void RegisterEnvironmentAssetTests(List<(string Name, Action Run)> tests)
    {
        tests.AddRange(new (string, Action)[] {
            ("M7.3-C1 descriptor independent closed roundtrip", () => {
                var f = new EnvironmentFixture(); Check(EnvironmentAssetDescriptor.Decode(f.Descriptor.Encode()) == f.Descriptor);
                byte[] bytes = f.Package.CopyBytes(); bytes[0] = 0; Check(f.Package.CopyBytes()[0] == 0x4E && !f.Package.GpuValidated);
                Check((int)AssetKind.Environment == 11 && (int)AssetKind.AnimationGraph == 10 && (int)AssetKind.OverrideSet == 9);
                AssetReject(() => DerivedAssetCodec.Encode([new(f.Id, AssetKind.Environment, f.Package.CopyBytes())]));
            }),
            ("M7.3-C1 descriptor closed required duplicate and byte budgets", () => {
                var f = new EnvironmentFixture(); string json = Encoding.UTF8.GetString(f.Descriptor.Encode());
                foreach (string bad in new[] { "null", "[]", "{}", "{\"version\":1," + json[1..], json[..^1] + ",\"gpuHandle\":42}" })
                    AssetReject(() => EnvironmentAssetDescriptor.Decode(Encoding.UTF8.GetBytes(bad)));
                foreach (string field in new[] { "version", "projectId", "assetId", "generation", "contentHash", "path" }) {
                    var n = JsonNode.Parse(json)!.AsObject(); n.Remove(field); AssetReject(() => EnvironmentAssetDescriptor.Decode(Encoding.UTF8.GetBytes(n.ToJsonString())));
                }
                AssetReject(() => EnvironmentAssetDescriptor.Decode(new byte[EnvironmentAssetDescriptor.MaxBytes + 1]));
            }),
            ("M7.3-C1 descriptor canonical identity paths and hashes", () => {
                var f = new EnvironmentFixture();
                foreach (var d in new[] { f.Descriptor with { Version = 2 }, f.Descriptor with { ProjectId = Guid.Empty }, f.Descriptor with { AssetId = Guid.Empty },
                    f.Descriptor with { Generation = 0 }, f.Descriptor with { ContentHash = f.Descriptor.ContentHash.ToLowerInvariant() },
                    f.Descriptor with { Path = f.Descriptor.Path.Replace("/1-", "/01-") }, f.Descriptor with { Path = "assets/../bad.nce" },
                    f.Descriptor with { Path = f.Descriptor.Path.ToUpperInvariant() }, f.Descriptor with { Path = f.Descriptor.Path + ":stream" },
                    f.Descriptor with { Path = "F:/absolute.nce" } }) AssetReject(() => d.Encode());
            }),
            ("M7.3-C1 runtime exact pinned environment without source GPU or Cook", () => {
                var f = new EnvironmentFixture(); using var owner = f.Prepare(); using var lease = owner.AcquireLease();
                var asset = EnvironmentAsset(lease, f.Id); Check(asset.Generation == 1 && asset.ContentHash == f.Package.ContentHash && lease.PinnedGenerations == 1 && Locked(f.FilePath));
                Check(lease.List().Count == 1 && lease.Diagnostics.Count == 0 && !asset.Package.GpuValidated);
                Check(!System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>().Any(m => m.ModuleName is "NcmaRenderer.dll" or "NcmaPlatform.dll"));
            }),
            ("M7.3-C1 scene independent nonspatial configuration and exact reference", () => {
                var f = new EnvironmentFixture(); var doc = f.Scene(); doc.ValidateAuthoring();
                Check(doc.World.Count == 1 && !doc.World.GetObjects()[0].Has<Ncma.Runtime.TransformData>());
                Check(SceneAssetPreparation.HasEnabledEnvironment(doc.CaptureSnapshot()) && SceneAssetPreparation.References(doc.CaptureSnapshot()).SequenceEqual(new[] { Ref(f.Id, AssetKind.Environment) }));
                using var prepared = SceneAssetPreparation.Prepare(f.Root, f.Project, doc.CaptureSnapshot(), true);
                Check(SceneRenderValidation.Inspect(doc.CaptureSnapshot(), prepared.Metadata, true).Count == 0);
            }),
            ("M7.3-C1 Off and empty scenes have no asset IO", () => {
                var doc = Document(); doc.World.CreateObject("Off").Set(EnvironmentLightingData.Off);
                Check(SceneAssetPreparation.References(doc.CaptureSnapshot()).Length == 0 && !SceneAssetPreparation.HasEnabledEnvironment(doc.CaptureSnapshot()));
                using var prepared = SceneAssetPreparation.Prepare(Path.Combine(OutputRoot(), "missing-" + Guid.NewGuid()), Guid.NewGuid(), doc.CaptureSnapshot(), true);
                Check(prepared.Assets.List().Count == 0 && prepared.Assets.PinnedGenerations == 0);
            }),
            ("M7.3-C1 environment schema and scalar restrictions", () => {
                var f = new EnvironmentFixture(); var r = RenderComponentRegistry.CreateRegistry(); var c = new Ncma.Runtime.ComponentSnapshot(EnvironmentLightingData.TypeId, 1, r.Encode(f.Configuration));
                foreach (string field in new[] { "version", "assetId", "generation", "contentHash", "strength", "rotationRadians", "enabled" }) {
                    var node = JsonNode.Parse(c.Data.GetRawText())!.AsObject(); node.Remove(field);
                    AssetReject(() => r.Decode<EnvironmentLightingData>(c with { Data = JsonSerializer.SerializeToElement(node) }));
                }
                AssetReject(() => r.Decode<EnvironmentLightingData>(c with { Version = 2 }));
                var extra = JsonNode.Parse(c.Data.GetRawText())!; extra["nativeHandle"] = 4; AssetReject(() => r.Decode<EnvironmentLightingData>(c with { Data = JsonSerializer.SerializeToElement(extra) }));
                foreach (var bad in new[] { f.Configuration with { Version = 2 }, f.Configuration with { AssetId = Guid.Empty }, f.Configuration with { Generation = 0 },
                    f.Configuration with { Generation = ulong.MaxValue }, f.Configuration with { ContentHash = null! }, f.Configuration with { ContentHash = "" },
                    f.Configuration with { Strength = float.NaN }, f.Configuration with { Strength = -1 }, f.Configuration with { Strength = 17 },
                    f.Configuration with { RotationRadians = float.PositiveInfinity }, f.Configuration with { RotationRadians = 4 },
                    f.Configuration with { Enabled = false }, EnvironmentLightingData.Off with { Strength = 1 } }) AssetReject(() => r.Encode(bad));
            }),
            ("M7.3-C1 scene whole-candidate duplicate rejected including Off", () => {
                var f = new EnvironmentFixture(); var d = f.Scene(); var s = d.CaptureSnapshot();
                AtomicReject(d, s with { Objects = [s.Objects[0], s.Objects[0] with { Id = Guid.NewGuid() }] });
                var r = d.World.Components; AtomicReject(d, s with { Objects = [s.Objects[0], s.Objects[0] with { Id = Guid.NewGuid(), Components = [new(EnvironmentLightingData.TypeId, 1, r.Encode(EnvironmentLightingData.Off))] }] });
                Check(d.World.Count == 1);
            }),
            ("M7.3-C1 present wrong generation hash or kind never fallback", () => {
                var f = new EnvironmentFixture(); var d = f.Scene();
                foreach (var wrong in new[] { new SceneAssetInfo(f.Id, AssetKind.Environment, 2, f.Package.ContentHash),
                    new SceneAssetInfo(f.Id, AssetKind.Environment, 1, new('A', 64)), new SceneAssetInfo(f.Id, AssetKind.Texture, 1, f.Package.ContentHash) }) {
                    AssetReject(() => SceneRenderValidation.Inspect(d.CaptureSnapshot(), new([wrong]), false));
                    AssetReject(() => SceneRenderValidation.Inspect(d.CaptureSnapshot(), new([wrong]), true));
                }
                Check(SceneRenderValidation.Inspect(d.CaptureSnapshot(), PreparedSceneAssets.Empty).Single().Code == "asset_missing");
                AssetReject(() => SceneRenderValidation.Inspect(d.CaptureSnapshot(), PreparedSceneAssets.Empty, true));
            }),
            ("M7.3-C1 scene snapshot save load preserves exact configuration", () => {
                var f = new EnvironmentFixture(); var d = f.Scene(); byte[] bytes = d.CaptureBytes(); var other = Document(); other.RestoreSnapshot(SceneDocumentCodec.Decode(bytes));
                Check(bytes.SequenceEqual(other.CaptureBytes()) && other.World.GetObjects()[0].Get<EnvironmentLightingData>() == f.Configuration);
                string path = Path.Combine(f.Root, "environment.ncmascene"); SceneDocumentFiles.Save(d, path); var read = Document(); SceneDocumentFiles.Load(read, path); Check(bytes.SequenceEqual(read.CaptureBytes()));
            }),
            ("M7.3-C1 descriptor foreign project duplicate and author collision", () => {
                var f = new EnvironmentFixture(); AssetReject(() => RuntimeAssetLoader.Prepare(f.Root, Guid.NewGuid(), [Ref(f.Id, AssetKind.Environment)]));
                File.WriteAllBytes(Path.Combine(f.Root, "assets/duplicate.ncenv"), f.Descriptor.Encode()); AssetReject(() => f.Prepare()); Check(!Locked(f.FilePath));
                var g = new EnvironmentFixture(); File.WriteAllBytes(Path.Combine(g.Root, "assets/collision.ncmaterial"), MaterialCodec.Encode(MaterialDefinition.Default(g.Id))); AssetReject(() => g.Prepare()); Check(!Locked(g.FilePath));
            }),
            ("M7.3-C1 file hash and header generation failure releases pins", () => {
                var f = new EnvironmentFixture(); byte[] bytes = f.Package.CopyBytes(); bytes[^1] ^= 1; File.WriteAllBytes(f.FilePath, bytes); AssetReject(() => f.Prepare(false)); Check(!Locked(f.FilePath));
                var g = new EnvironmentFixture(); var wrong = EnvironmentFixturePackage(g.Id, 2); var descriptor = EnvironmentAssetDescriptor.FromPackage(g.Project, wrong) with { Generation = 1, Path = $"out/assets/{g.Project:N}/{g.Id:N}/1-{wrong.ContentHash}.nce" };
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(g.Root, descriptor.Path))!); File.WriteAllBytes(Path.Combine(g.Root, descriptor.Path), wrong.CopyBytes()); g.SaveDescriptor(descriptor);
                AssetReject(() => g.Prepare(false)); Check(!Locked(Path.Combine(g.Root, descriptor.Path)));
            }),
            ("M7.3-C1 independently hashed wrong asset header fails", () => {
                var f = new EnvironmentFixture(); var wrong = EnvironmentFixturePackage(Guid.NewGuid(), 1);
                var d = f.Descriptor with { ContentHash = wrong.ContentHash, Path = $"out/assets/{f.Project:N}/{f.Id:N}/1-{wrong.ContentHash}.nce" };
                File.WriteAllBytes(Path.Combine(f.Root, d.Path), wrong.CopyBytes()); f.SaveDescriptor(d); AssetReject(() => f.Prepare()); Check(!Locked(Path.Combine(f.Root, d.Path)));
            }),
            ("M7.3-C1 exact casing hardlinks and write rename rejection", () => {
                var f = new EnvironmentFixture(); using (var owner = f.Prepare()) { AssetReject(() => File.Open(f.FilePath, FileMode.Open, FileAccess.Write).Dispose()); AssetReject(() => File.Move(f.FilePath, f.FilePath + ".moved")); }
                Check(!Locked(f.FilePath)); Check(CreateHardLinkW(f.FilePath + ".link", f.FilePath, 0)); AssetReject(() => f.Prepare()); Check(!Locked(f.FilePath));
                var g = new EnvironmentFixture(); File.Move(g.FilePath, Path.ChangeExtension(g.FilePath, ".NCE")); AssetReject(() => g.Prepare());
            }),
            ("M7.3-C1 cancellation request budget and owner thread", () => {
                var f = new EnvironmentFixture(); using var stop = new CancellationTokenSource(); stop.Cancel();
                AssetReject(() => RuntimeAssetLoader.Prepare(f.Root, f.Project, [Ref(f.Id, AssetKind.Environment)], cancellation: stop.Token)); Check(!Locked(f.FilePath));
                AssetReject(() => RuntimeAssetLoader.Prepare(f.Root, f.Project, Enumerable.Repeat(Ref(f.Id, AssetKind.Environment), 4097)));
                using var owner = f.Prepare(); using var lease = owner.AcquireLease(); Check(Task.Run(() => { try { lease.List(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
            }),
            ("M7.3-C1 reparse fixture rejects without traversing user data", () => {
                var f = new EnvironmentFixture(); string target = Path.Combine(f.Root, "owned-link-target"); Directory.CreateDirectory(target);
                string link = Path.Combine(f.Root, "assets/linked");
                var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string arg in new[] { "/d", "/c", "mklink", "/J", Path.GetFullPath(link), Path.GetFullPath(target) }) start.ArgumentList.Add(arg);
                using var process = System.Diagnostics.Process.Start(start) ?? throw new Exception("Cannot create owned junction fixture.");
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                if (!process.WaitForExit(5000) || process.ExitCode != 0) throw new Exception(output);
                AssetReject(() => f.Prepare()); Check(!Locked(f.FilePath) && Directory.Exists(target)); // Retained, never recursively cleaned.
            }),
            ("M7.3-C1 oversize generation rejected before retaining data", () => {
                var f = new EnvironmentFixture(); File.WriteAllBytes(f.FilePath, new byte[EnvironmentPackage.MaxBytes + 1]);
                AssetReject(() => f.Prepare(false)); Check(!Locked(f.FilePath));
            }),
            ("M7.3-C1 only requested environment payload loaded", () => {
                var f = new EnvironmentFixture(); Guid unused = Guid.NewGuid(); var p = EnvironmentFixturePackage(unused, 1); var descriptor = EnvironmentAssetDescriptor.FromPackage(f.Project, p);
                File.WriteAllBytes(Path.Combine(f.Root, "assets/unused.ncenv"), descriptor.Encode()); // deliberately no generation file
                using var owner = f.Prepare(); using var lease = owner.AcquireLease(); Check(lease.List().Count == 1 && lease.PinnedGenerations == 1);
            }),
            ("M7.3-C1 immutable old new generations coexist until final lease", () => {
                var f = new EnvironmentFixture(); var owner = f.Prepare(); var old = owner.AcquireLease(); var play = old.AcquireLease(); string path = f.FilePath; var package = f.Package;
                owner.Dispose(); old.Dispose(); f.Write(2); using (var nextOwner = f.Prepare()) using (var next = nextOwner.AcquireLease()) {
                    Check(EnvironmentAsset(play, f.Id).Package == package || EnvironmentAsset(play, f.Id).ContentHash == package.ContentHash);
                    Check(EnvironmentAsset(next, f.Id).Generation == 2 && next.Identity != play.Identity && Locked(path) && Locked(f.FilePath));
                }
                Check(!Locked(f.FilePath) && Locked(path)); play.Dispose(); Check(!Locked(path)); AssetReject(() => play.List());
            }),
            ("M7.3-C1 source-free relocated NCP exact generation and immutable bytes", () => {
                var f = new EnvironmentFixture(); var scene = f.Scene(); byte[] bytes = SceneAssetPreparation.CreateRuntimePackage(f.Root, f.Project, scene.CaptureSnapshot(), []);
                var index = RuntimeAssetPackage.Inspect(bytes, f.Project); Check(index.Assets.Length == 1 && index.Assets[0].Encoding == "environment" && index.Assets[0].Generation == 1);
                string moved = Path.Combine(OutputRoot(), "environment-moved-" + Guid.NewGuid()); Directory.CreateDirectory(Path.Combine(moved, "assets")); string pack = Path.Combine(moved, "assets/game.ncpak"); File.WriteAllBytes(pack, bytes);
                using (var prepared = SceneAssetPreparation.Prepare(moved, f.Project, scene.CaptureSnapshot(), true, "assets/game.ncpak")) {
                    var p = EnvironmentAsset(prepared.Assets, f.Id).Package; byte[] copy = p.CopyBytes(); copy[0] ^= 1;
                    Check(p.ContentHash == f.Package.ContentHash && p.CopyBytes().SequenceEqual(f.Package.CopyBytes()) && Locked(pack));
                }
                Check(!Locked(pack) && !Directory.Exists(Path.Combine(moved, "out")) && Directory.GetFiles(Path.Combine(moved, "assets")).Length == 1);
            }),
            ("M7.3-C1 runtime package identity kind role generation rejects", () => {
                var f = new EnvironmentFixture(); byte[] bytes = SceneAssetPreparation.CreateRuntimePackage(f.Root, f.Project, f.Scene().CaptureSnapshot(), []);
                foreach (string field in new[] { "generation", "assetId", "kind", "modelId", "skeletonId", "encoding" }) AssetReject(() => RuntimeAssetPackage.Inspect(ChangeIndex(bytes, n => {
                    var e = n["assets"]![0]!; e[field] = field == "generation" ? JsonValue.Create(2) : JsonValue.Create(field == "kind" ? "texture" : field == "encoding" ? "texture" : Guid.NewGuid().ToString("D"));
                }), f.Project));
                byte[] bad = (byte[])bytes.Clone(); bad[^1] ^= 1; AssetReject(() => RuntimeAssetPackage.Inspect(bad, f.Project));
                AssetReject(() => RuntimeAssetPackage.Inspect(bytes, Guid.NewGuid()));
            }),
            ("M7.3-C1 package absence never falls back to valid author files", () => {
                var f = new EnvironmentFixture(); AssetReject(() => SceneAssetPreparation.Prepare(f.Root, f.Project, f.Scene().CaptureSnapshot(), false, "assets/missing.ncpak")); Check(!Locked(f.FilePath));
                var d = f.Scene(); d.World.GetObjects()[0].Set(f.Configuration with { Generation = 2 }); AssetReject(() => SceneAssetPreparation.CreateRuntimePackage(f.Root, f.Project, d.CaptureSnapshot(), [])); Check(!Locked(f.FilePath));
            }),
            ("M7.3-C1 mixed model and environment closure omit unused payload", () => {
                var f = new AssetFixture(); var e = new EnvironmentFixture(); var d = EnvironmentAssetDescriptor.FromPackage(f.Project, e.Package);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(f.Root, d.Path))!); File.WriteAllBytes(Path.Combine(f.Root, d.Path), e.Package.CopyBytes());
                File.WriteAllBytes(Path.Combine(f.Root, "assets/environment.ncenv"), d.Encode()); var scene = f.Scene(); scene.World.CreateObject("Environment").Set(e.Configuration);
                var index = RuntimeAssetPackage.Inspect(SceneAssetPreparation.CreateRuntimePackage(f.Root, f.Project, scene.CaptureSnapshot(), []), f.Project);
                Check(index.Assets.Length == 4 && index.Assets.Count(a => a.Kind == AssetKind.Environment) == 1 && index.Assets.Count(a => a.Encoding == "environment") == 1);
                File.WriteAllBytes(Path.Combine(f.Root, "assets/environment.ncenv"), (d with { AssetId = f.Mesh, Path = $"out/assets/{f.Project:N}/{f.Mesh:N}/1-{d.ContentHash}.nce" }).Encode());
                AssetReject(() => RuntimeAssetLoader.Prepare(f.Root, f.Project, [f.Reference])); Check(!Locked(f.GenerationPath));
            }),
            ("M7.3-C1 Editor failed refresh preserves old publication", () => {
                var f = new EnvironmentFixture(); using var editor = new EditorSessionOwner("Environment", components: RenderComponentRegistry.CreateRegistry(), validateComposition: SceneRenderValidation.RequireComposition);
                editor.Document.RestoreSnapshot(f.Scene().CaptureSnapshot()); editor.PrepareRenderAssets(f.Root, f.Project); Guid identity = editor.RenderAssets!.Assets.Identity; string oldPath = f.FilePath;
                f.Write(2); AssetReject(editor.RefreshRenderAssets); Check(editor.RenderAssets.Assets.Identity == identity && EnvironmentAsset(editor.RenderAssets.Assets, f.Id).Generation == 1 && Locked(oldPath) && !Locked(f.FilePath));
            }),
            ("M7.3-C1 single command history permissions scope and stale session", EnvironmentCommandTests),
            ("M7.3-C1 Play leases isolate configuration and freeze editor", () => {
                var f = new EnvironmentFixture(); using var editor = new EditorSessionOwner("Environment", components: RenderComponentRegistry.CreateRegistry(), validateComposition: SceneRenderValidation.RequireComposition);
                editor.Document.RestoreSnapshot(f.Scene().CaptureSnapshot()); editor.Edit!.Resynchronize(); editor.PrepareRenderAssets(f.Root, f.Project); byte[] original = editor.Document.CaptureBytes(); Guid id = editor.Document.World.GetObjects()[0].PersistentId;
                var play = editor.StartPlay(); Check(editor.Edit.Invoke(RenderingEditorAdapter.EnvironmentRequest(editor.Edit, id, f.Configuration with { Strength = .5f }, editor.Edit.Revision), Permissions).Code == "play_frozen");
                play.Document.World.FindObject(id).Set(f.Configuration with { Strength = .25f }); Check(editor.PlayRenderAssets!.Assets.Identity == editor.RenderAssets!.Assets.Identity && original.SequenceEqual(editor.Document.CaptureBytes()));
                editor.StopPlay(); Check(original.SequenceEqual(editor.Document.CaptureBytes()) && !editor.Edit.State.Frozen);
            }),
            ("M7.3-C1 32 package pin borrow close cycles", () => {
                var f = new EnvironmentFixture(); string path = Path.Combine(f.Root, "assets/game.ncpak"); File.WriteAllBytes(path, SceneAssetPreparation.CreateRuntimePackage(f.Root, f.Project, f.Scene().CaptureSnapshot(), []));
                for (int i = 0; i < 32; i++) { var owner = RuntimeAssetPackage.Prepare(f.Root, "assets/game.ncpak", f.Project, [Ref(f.Id, AssetKind.Environment)]); using (var lease = owner.AcquireLease()) { owner.Dispose(); Check(Locked(path) && EnvironmentAsset(lease, f.Id).Generation == 1); } Check(!Locked(path)); }
            }),
            ("M7.3-C1 Player active environment explicitly pending before gameplay", () => {
                var f = new AssetFixture(); var e = new EnvironmentFixture(); var descriptor = EnvironmentAssetDescriptor.FromPackage(f.Project, e.Package);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(f.Root, descriptor.Path))!); File.WriteAllBytes(Path.Combine(f.Root, descriptor.Path), e.Package.CopyBytes()); File.WriteAllBytes(Path.Combine(f.Root, "assets/environment.ncenv"), descriptor.Encode());
                var scene = f.Scene(); scene.World.CreateObject("Environment").Set(e.Configuration); var options = f.Player(scene);
                var report = Ncma.Player.App.PlayerRunner.Run(options, pluginRoot: Path.Combine(f.Root, "missing-plugins"), visible: false);
                Check(report.ExitCode == 7 && report.Reason == "feature_unimplemented" && report.Tick == 0 && report.RenderedFrames == 0 && report.Modules.Length == 0 && report.ShutdownErrors.Length == 0);
                Check(!Locked(Path.Combine(f.Root, descriptor.Path)) && !Locked(f.GenerationPath));
                byte[] bad = e.Package.CopyBytes(); bad[^1] ^= 1; File.WriteAllBytes(Path.Combine(f.Root, descriptor.Path), bad);
                var rejected = Ncma.Player.App.PlayerRunner.Run(options with { Report = Path.Combine(f.Root, "corrupt-environment-report.json") }, pluginRoot: Path.Combine(f.Root, "missing-plugins"), visible: false);
                Check(rejected.ExitCode == 3 && rejected.Reason == "dependency_failed" && rejected.Tick == 0 && rejected.Modules.Length == 0 && rejected.RenderedFrames == 0);
                Check(!Locked(Path.Combine(f.Root, descriptor.Path)) && !Locked(f.GenerationPath));
            }),
            ("M7.3-C1 Editor apphost active environment rejected before plugins", () => {
                var f = new AssetFixture(); var e = new EnvironmentFixture(); var scene = f.Scene(); scene.World.CreateObject("Environment").Set(e.Configuration); var options = f.Player(scene);
                var repository = new DirectoryInfo(AppContext.BaseDirectory); while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Build.bat"))) repository = repository.Parent;
                string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
                string exe = Path.Combine(repository!.FullName, "managed/Ncma.Editor.App/bin", configuration, "net8.0/NcmaEngine.exe");
                var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = f.Root };
                foreach (string arg in new[] { "--project", options.Project!, "--plugins", Path.Combine(f.Root, "missing-plugins"), "--frames", "1" }) start.ArgumentList.Add(arg);
                using var process = System.Diagnostics.Process.Start(start) ?? throw new Exception("Cannot start formal Editor fixture.");
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000)) throw new Exception("Formal Editor pending preflight timed out.");
                string text = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
                Check(process.ExitCode == 4 && text.Contains("environment_host_integration_pending", StringComparison.Ordinal) && !Directory.Exists(Path.Combine(f.Root, "out/user")));
            })
        });
    }
    private static void EnvironmentCommandTests()
    {
        var f = new EnvironmentFixture(); var doc = f.Scene(); var edit = new EditSession(doc); Guid id = doc.World.GetObjects()[0].PersistentId;
        byte[] before = doc.CaptureBytes(); var request = RenderingEditorAdapter.EnvironmentRequest(edit, id, f.Configuration with { Strength = .25f, RotationRadians = .5f }, edit.Revision);
        Check(edit.Invoke(request).Code == "permission_denied" && before.SequenceEqual(doc.CaptureBytes()));
        Check(edit.Invoke(request, new(["ncma.scene.transaction"], objectScope: [Guid.NewGuid()], componentScope: [EnvironmentLightingData.TypeId])).Status == "denied");
        Check(edit.Invoke(request with { SessionId = Guid.NewGuid() }, Permissions).Code == "session_mismatch");
        Check(edit.Invoke(request with { ExpectedRevision = edit.Revision + 1 }, Permissions).Code == "revision_conflict");
        Check(edit.State.UndoCount == 0 && before.SequenceEqual(doc.CaptureBytes()));
        var result = edit.Invoke(request, Permissions); Check(result.Changed && edit.State.UndoCount == 1); byte[] after = doc.CaptureBytes(); var oldObject = doc.World.FindObject(id);
        Check(edit.Invoke(request, Permissions).Replayed && edit.State.UndoCount == 1);
        var undo = new CapabilityRequest(EditSession.ContractVersion, Guid.NewGuid(), edit.SessionId, edit.Revision, "ncma.history.undo", Element(new { }));
        Check(edit.Invoke(undo, new(["ncma.history.undo"])).Code == "permission_denied" && after.SequenceEqual(doc.CaptureBytes()));
        Check(edit.Invoke(undo, Permissions).Changed && before.SequenceEqual(doc.CaptureBytes())); AssetReject(() => _ = oldObject.Name);
        Check(Invoke(edit, "ncma.history.redo", new { }).Changed && after.SequenceEqual(doc.CaptureBytes()) && doc.World.FindObject(id).Get<EnvironmentLightingData>().Strength == .25f);
    }
}
