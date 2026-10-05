using System.Numerics;
using System.Security.Cryptography;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Editor.Services;
using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Runtime;

internal static partial class Program
{
    private sealed class AssetFixture
    {
        internal readonly string Root = Path.Combine(OutputRoot(), "asset-tests", Guid.NewGuid().ToString("N"));
        internal readonly Guid Project = Guid.NewGuid(), Model = Guid.NewGuid(), Mesh = Guid.NewGuid(), Set = Guid.NewGuid();
        internal AssetRecord Record = null!;
        internal string GenerationPath => Path.Combine(Root, Record.Generation!.RelativePath);
        internal AssetRef Reference => Ref(Mesh, AssetKind.StaticMesh);
        internal AssetFixture() { Directory.CreateDirectory(Path.Combine(Root, "assets")); WriteModel(1); }
        internal void Descriptor(AssetRecord record)
        { Record = record; File.WriteAllBytes(Path.Combine(Root, "assets/Model.fbx.ncmeta"), AssetRecordCodec.Encode(record)); }
        internal void WriteModel(ulong generation, float size = 1)
        {
            var settings = new ImportSettings(1, 30, true);
            var manifest = new ModelAssetManifest(1, Model, true, Hash, settings, null, [new(Mesh, Set)], []);
            ImportVertex V(Vector3 p, Vector2 uv) => new(p, Vector3.UnitZ, uv, default, Vector4.Zero);
            var mesh = new MeshPayload(false, 0, [V(Vector3.Zero, Vector2.Zero), V(new(size, 0, 0), Vector2.UnitX), V(new(0, size, 0), Vector2.UnitY)],
                [new(1, 0, 0, 1), new(1, 0, 0, 1), new(1, 0, 0, 1)], [0, 1, 2], [0], [], 1);
            var bytes = DerivedAssetCodec.Encode([new(Model, AssetKind.StaticMesh, ModelAssetManifestCodec.Encode(manifest)),
                new(Mesh, AssetKind.StaticMesh, ModelPayloadCodec.Encode(mesh)), new(Set, AssetKind.MaterialSet, ModelPayloadCodec.Encode(new MaterialSlotsPayload(["Imported"]))) ]);
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            string path = $"out/assets/{Project:N}/{Model:N}/{generation}-{hash}.nca";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Root, path))!); File.WriteAllBytes(Path.Combine(Root, path), bytes);
            Descriptor(new(1, Model, AssetKind.StaticMesh, "assets/Model.fbx", Hash, "ufbx", 1, settings,
                [new(Mesh, AssetKind.StaticMesh, "mesh/0", "Triangle", false), new(Set, AssetKind.MaterialSet, "materials/0", "Slots", false)], [], new(generation, hash, path)));
        }
        internal Guid Texture(TextureSemantic semantic)
        {
            Guid id = Guid.NewGuid(); byte[] payload = TextureData.Prepare(1, 1, semantic, new byte[] { 128, 128, 255, 255 }).Encode();
            byte[] bytes = DerivedAssetCodec.Encode([new(id, AssetKind.Texture, payload)]); string hash = Convert.ToHexString(SHA256.HashData(bytes));
            string path = $"out/assets/{Project:N}/{id:N}/1-{hash}.nca";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Root, path))!); File.WriteAllBytes(Path.Combine(Root, path), bytes);
            var record = new AssetRecord(1, id, AssetKind.Texture, "assets/" + id + ".png", Hash, "texture", 1, new(1, 30, true), [], [], new(1, hash, path));
            File.WriteAllBytes(Path.Combine(Root, "assets/" + id + ".png.ncmeta"), AssetRecordCodec.Encode(record)); return id;
        }
        internal SceneDocument Scene()
        { var doc = Document(); var obj = Geometry(doc); obj.Set(new StaticMeshData(Mesh, Set, true, true, uint.MaxValue)); Camera(doc); return doc; }
        internal RuntimeAssetSnapshot Prepare(bool strict = true, params AssetRef[] references) => RuntimeAssetLoader.Prepare(Root, Project, references.Length == 0 ? [Reference] : references, strict);
        internal Ncma.Player.App.PlayerOptions Player(SceneDocument doc)
        {
            SceneDocumentFiles.Save(doc, Path.Combine(Root, "start.ncmascene"));
            // Intentionally invalid gameplay image: asset/feature preflight must finish before CLR assembly load.
            File.WriteAllBytes(Path.Combine(Root, "gameplay.dll"), [0]);
            string project = Path.Combine(Root, "test.ncmaproject");
            File.WriteAllBytes(project, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new Ncma.Application.ProjectConfiguration(1, Project, "Asset preflight", "start.ncmascene", "gameplay.dll", "Direct3D11", []),
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
            return Ncma.Player.App.PlayerOptions.Parse(["--project", project, "--ticks", "1", "--report", Path.Combine(Root, "report.json")]);
        }
    }
    private static void AssetReject(Action action)
    { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or System.Text.Json.JsonException or OperationCanceledException) { return; } throw new Exception("Asset request unexpectedly succeeded."); }
    private static bool Locked(string path)
    { try { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return false; } catch (IOException) { return true; } }
    private static AssetRef Ref(Guid id, AssetKind kind) => new(new(id), kind);
    private static void RegisterAssetTests(List<(string Name, Action Run)> tests)
    {
        tests.AddRange(new (string, Action)[] {
            ("runtime-derived-without-source-import", () => { var f = new AssetFixture(); using var owner = f.Prepare(); using var lease = owner.AcquireLease(); var mesh = lease.RequireMesh(f.Mesh, AssetKind.StaticMesh);
                Check(!File.Exists(Path.Combine(f.Root, "assets/Model.fbx")) && mesh.Generation == 1 && mesh.BoundsMax == new Vector3(1, 1, 0) && lease.PinnedGenerations == 1 && lease.List().Count == 3); }),
            ("runtime-immutable-mesh-copy", () => { var f = new AssetFixture(); using var owner = f.Prepare(); using var lease = owner.AcquireLease(); var mesh = lease.RequireMesh(f.Mesh, AssetKind.StaticMesh); var copy = mesh.CopyPayload(); copy.Indices[0] = 2; copy.Vertices[0] = copy.Vertices[1]; Check(mesh.CopyPayload().Indices[0] == 0 && mesh.CopyPayload().Vertices[0].Position == Vector3.Zero); }),
            ("runtime-imported-slot-is-not-pbr-uuid", () => { var f = new AssetFixture(); using var owner = f.Prepare(); using var lease = owner.AcquireLease(); var set = (RuntimeMaterialSetAsset)lease.Require(f.Set, AssetKind.MaterialSet); Check(set.ImportedSlotsOnly && set.MaterialAt(0) == Guid.Empty && lease.Diagnostics.Single().Code == "imported_material_slots_only"); var names = set.CopyImportedNames(); names[0] = "Changed"; Check(set.CopyImportedNames()[0] == "Imported"); AssetReject(() => set.CopyDefinition()); }),
            ("runtime-scene-metadata-and-extraction", () => { var f = new AssetFixture(); var doc = f.Scene(); using var lease = SceneAssetPreparation.Prepare(f.Root, f.Project, doc.CaptureSnapshot(), true); Check(SceneRenderValidation.Inspect(doc.CaptureSnapshot(), lease.Metadata, true).Count == 0); var camera = doc.World.GetObjects().Single(o => o.Has<CameraData>()).PersistentId; var view = new RenderSceneExtractor(doc.World).Extract(lease.Metadata, camera, 800, 600); Check(view.Geometry.Count == 1 && view.Geometry[0].Mesh.Generation == 1); }),
            ("runtime-root-is-not-first-mesh", () => { var f = new AssetFixture(); var doc = f.Scene(); doc.World.GetObjects()[0].Set(new StaticMeshData(f.Model, f.Set, true, true, 1)); AssetReject(() => SceneAssetPreparation.Prepare(f.Root, f.Project, doc.CaptureSnapshot(), false)); Check(!Locked(f.GenerationPath)); }),
            ("runtime-typed-kind-strict-even-in-editor", () => { var f = new AssetFixture(); AssetReject(() => f.Prepare(false, Ref(f.Set, AssetKind.StaticMesh))); Check(!Locked(f.GenerationPath)); }),
            ("runtime-empty-null-no-file-io", () => { using var owner = RuntimeAssetLoader.Prepare(Path.Combine(OutputRoot(), Guid.NewGuid().ToString("N")), Guid.NewGuid(), []); using var lease = owner.AcquireLease(); Check(lease.PinnedGenerations == 0 && lease.List().Count == 0 && lease.Diagnostics.Count == 0); }),
            ("runtime-missing-resource-editor-vs-strict", () => { var f = new AssetFixture(); AssetRef missing = new(new(Guid.NewGuid()), AssetKind.StaticMesh); AssetReject(() => f.Prepare(true, missing)); using var owner = f.Prepare(false, missing); using var lease = owner.AcquireLease(); Check(lease.Diagnostics.Single().Code == "asset_missing" && lease.PinnedGenerations == 0); }),
            ("runtime-missing-generation-preserves-reference", () => { var f = new AssetFixture(); f.Descriptor(f.Record with { Generation = f.Record.Generation! with { Number = 2, RelativePath = f.Record.Generation.RelativePath.Replace("/1-", "/2-") } }); AssetReject(() => f.Prepare()); using var owner = f.Prepare(false); using var lease = owner.AcquireLease(); Check(lease.Diagnostics.Single().Code == "asset_generation_missing" && !lease.TryResolve(f.Mesh, AssetKind.StaticMesh, out _)); }),
            ("runtime-foreign-project-and-canonical-path", () => { var f = new AssetFixture(); AssetReject(() => RuntimeAssetLoader.Prepare(f.Root, Guid.NewGuid(), [f.Reference])); f.Descriptor(f.Record with { Generation = f.Record.Generation! with { RelativePath = f.Record.Generation.RelativePath.Replace(f.Model.ToString("N"), Guid.NewGuid().ToString("N")) } }); AssetReject(() => f.Prepare()); }),
            ("runtime-file-hash-rejection-releases-pins", () => { var f = new AssetFixture(); using (var stream = new FileStream(f.GenerationPath, FileMode.Open, FileAccess.ReadWrite)) { stream.Position = stream.Length - 1; int value = stream.ReadByte(); stream.Position--; stream.WriteByte((byte)(value ^ 1)); } AssetReject(() => f.Prepare(false)); Check(!Locked(f.GenerationPath)); }),
            ("runtime-block-checksum-rejection", () => { var f = new AssetFixture(); byte[] bytes = File.ReadAllBytes(f.GenerationPath); bytes[^1] ^= 1; string hash = Convert.ToHexString(SHA256.HashData(bytes)); string path = f.Record.Generation!.RelativePath.Replace(f.Record.Generation.ContentHash, hash); File.WriteAllBytes(Path.Combine(f.Root, path), bytes); f.Descriptor(f.Record with { Generation = new(1, hash, path) }); AssetReject(() => f.Prepare()); Check(!Locked(f.GenerationPath)); }),
            ("runtime-owner-close-retains-play-lease", () => { var f = new AssetFixture(); var owner = f.Prepare(); var lease = owner.AcquireLease(); var play = lease.AcquireLease(); owner.Dispose(); lease.Dispose(); Check(Locked(f.GenerationPath) && play.RequireMesh(f.Mesh, AssetKind.StaticMesh).Generation == 1); play.Dispose(); Check(!Locked(f.GenerationPath)); AssetReject(() => play.List()); }),
            ("runtime-new-and-old-generation-coexist", () => { var f = new AssetFixture(); using var oldOwner = f.Prepare(); using var old = oldOwner.AcquireLease(); string oldPath = f.GenerationPath; f.WriteModel(2, 2); using var nextOwner = f.Prepare(); using var next = nextOwner.AcquireLease(); Check(old.RequireMesh(f.Mesh, AssetKind.StaticMesh).BoundsMax.X == 1 && next.RequireMesh(f.Mesh, AssetKind.StaticMesh).BoundsMax.X == 2 && old.Identity != next.Identity && Locked(oldPath) && Locked(f.GenerationPath)); }),
            ("runtime-budget-cancellation-and-owner-thread", () => { var f = new AssetFixture(); AssetReject(() => RuntimeAssetLoader.Prepare(f.Root, f.Project, Enumerable.Repeat(f.Reference, 4097))); using var cancel = new CancellationTokenSource(); cancel.Cancel(); AssetReject(() => RuntimeAssetLoader.Prepare(f.Root, f.Project, [f.Reference], cancellation: cancel.Token)); Check(!Locked(f.GenerationPath)); using var owner = f.Prepare(); using var lease = owner.AcquireLease(); Check(Task.Run(() => { try { lease.List(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult()); }),
            ("runtime-recovery-journal-and-duplicate-uuid", () => { var f = new AssetFixture(); File.WriteAllText(Path.Combine(f.Root, "assets/unfinished.journal"), "pending"); AssetReject(() => f.Prepare()); var second = new AssetFixture(); File.WriteAllBytes(Path.Combine(second.Root, "assets/Duplicate.ncmeta"), AssetRecordCodec.Encode(second.Record)); AssetReject(() => second.Prepare()); }),
            ("runtime-tombstone-not-resolved", () => { var f = new AssetFixture(); f.Descriptor(f.Record with { Subassets = f.Record.Subassets.Select(s => s.AssetId == f.Mesh ? s with { Tombstone = true } : s).ToArray() }); AssetReject(() => f.Prepare()); using var owner = f.Prepare(false); using var lease = owner.AcquireLease(); Check(lease.List().Count == 0 && lease.Diagnostics.Count == 1); }),
            ("runtime-authored-material-closure-and-content-version", () => { var f = new AssetFixture(); Guid texture = f.Texture(TextureSemantic.Color), material = Guid.NewGuid(), set = Guid.NewGuid(); string path = Path.Combine(f.Root, "assets/PBR.ncmaterial"); var definition = MaterialDefinition.Default(material) with { BaseTexture = texture }; File.WriteAllBytes(path, MaterialCodec.Encode(definition)); File.WriteAllBytes(Path.Combine(f.Root, "assets/PBR.ncmatset"), MaterialCodec.Encode(new MaterialSetDefinition(1, set, [material])));
                using var oldOwner = f.Prepare(true, Ref(set, AssetKind.MaterialSet)); using var old = oldOwner.AcquireLease(); var slots = (RuntimeMaterialSetAsset)old.Require(set, AssetKind.MaterialSet); var copied = slots.CopyDefinition(); copied.Materials[0] = Guid.NewGuid(); Check(slots.MaterialAt(0) == material && !slots.ImportedSlotsOnly && ((RuntimeTextureAsset)old.Require(texture, AssetKind.Texture)).Data.Srgb);
                File.WriteAllBytes(path, MaterialCodec.Encode(definition with { Metallic = 1 })); using var nextOwner = f.Prepare(true, Ref(set, AssetKind.MaterialSet)); using var next = nextOwner.AcquireLease(); Check(old.Require(material, AssetKind.Material).Generation != next.Require(material, AssetKind.Material).Generation && ((RuntimeMaterialAsset)old.Require(material, AssetKind.Material)).Definition.Metallic == 0); }),
            ("runtime-wrong-texture-semantic-rejected", () => { var f = new AssetFixture(); Guid texture = f.Texture(TextureSemantic.Color), material = Guid.NewGuid(); File.WriteAllBytes(Path.Combine(f.Root, "assets/Bad.ncmaterial"), MaterialCodec.Encode(MaterialDefinition.Default(material) with { NormalTexture = texture })); AssetReject(() => f.Prepare(false, Ref(material, AssetKind.Material))); }),
            ("runtime-authored-derived-uuid-collision", () => { var f = new AssetFixture(); File.WriteAllBytes(Path.Combine(f.Root, "assets/Collision.ncmaterial"), MaterialCodec.Encode(MaterialDefinition.Default(f.Mesh))); AssetReject(() => f.Prepare()); }),
            ("runtime-malformed-author-document-rejected", () => { var f = new AssetFixture(); File.WriteAllText(Path.Combine(f.Root, "assets/Bad.ncmaterial"), "{\"version\":1,\"pointer\":42}"); AssetReject(() => f.Prepare()); }),
            ("editor-refresh-failure-preserves-resource-state", () => { var f = new AssetFixture(); using var editor = new EditorSessionOwner("Assets", components: RenderComponentRegistry.CreateRegistry(), validateComposition: SceneRenderValidation.RequireComposition); editor.Document.RestoreSnapshot(f.Scene().CaptureSnapshot()); editor.PrepareRenderAssets(f.Root, f.Project); var identity = editor.RenderAssets!.Assets.Identity; f.Descriptor(f.Record with { Generation = f.Record.Generation! with { RelativePath = f.Record.Generation.RelativePath.Replace(f.Model.ToString("N"), Guid.NewGuid().ToString("N")) } }); AssetReject(editor.RefreshRenderAssets); Check(editor.RenderAssets.Assets.Identity == identity && editor.RenderAssets.Assets.RequireMesh(f.Mesh, AssetKind.StaticMesh).Generation == 1); }),
            ("editor-play-resource-version-isolated-from-refresh", () => { var f = new AssetFixture(); using var editor = new EditorSessionOwner("Assets", components: RenderComponentRegistry.CreateRegistry(), validateComposition: SceneRenderValidation.RequireComposition); editor.Document.RestoreSnapshot(f.Scene().CaptureSnapshot()); editor.PrepareRenderAssets(f.Root, f.Project); string oldPath = f.GenerationPath; editor.StartPlay(); f.WriteModel(2, 2); editor.RefreshRenderAssets(); Check(editor.RenderAssets!.Assets.RequireMesh(f.Mesh, AssetKind.StaticMesh).Generation == 2 && editor.PlayRenderAssets!.Assets.RequireMesh(f.Mesh, AssetKind.StaticMesh).Generation == 1 && Locked(oldPath)); editor.Play!.Document.ValidateAuthoring(); editor.StopPlay(); Check(!Locked(oldPath) && editor.PlayRenderAssets is null); }),
            ("catalog-wrong-kind-rejected-before-authoring", () => { var f = new AssetFixture(); var doc = f.Scene(); var obj = doc.World.GetObjects()[0]; obj.Set(new StaticMeshData(f.Set, f.Set, true, true, 1)); AssetReject(() => SceneRenderValidation.Inspect(doc.CaptureSnapshot(), catalog: new AssetCatalog([f.Record]))); }),
            ("player-missing-resource-preflight-no-gpu", () => { var f = new AssetFixture(); var doc = f.Scene(); doc.World.GetObjects()[0].Set(new StaticMeshData(Guid.NewGuid(), f.Set, true, true, 1)); var options = f.Player(doc); var report = Ncma.Player.App.PlayerRunner.Run(options, pluginRoot: Path.Combine(f.Root, "missing-plugins"), visible: false); Check(report.ExitCode == 3 && report.Reason == "dependency_failed" && report.Modules.Length == 0 && report.Tick == 0 && report.RenderedFrames == 0 && !Locked(f.GenerationPath)); }),
            ("player-real-scene-never-falls-back-to-reference-cube", () => { var f = new AssetFixture(); var options = f.Player(f.Scene()); var report = Ncma.Player.App.PlayerRunner.Run(options, pluginRoot: Path.Combine(f.Root, "missing-plugins"), visible: false); Check(report.ExitCode == 7 && report.Reason == "feature_unimplemented" && report.Modules.Length == 0 && report.Tick == 0 && report.RenderedFrames == 0 && report.ShutdownErrors.Length == 0 && !Locked(f.GenerationPath)); }),
            ("player-corrupt-metadata-is-dependency-failure", () => { var f = new AssetFixture(); var options = f.Player(f.Scene()); File.WriteAllText(Path.Combine(f.Root, "assets/Model.fbx.ncmeta"), "{"); var report = Ncma.Player.App.PlayerRunner.Run(options, pluginRoot: Path.Combine(f.Root, "missing-plugins"), visible: false); Check(report.ExitCode == 3 && report.Reason == "dependency_failed" && report.Modules.Length == 0 && report.Tick == 0); })
        });
    }
}
