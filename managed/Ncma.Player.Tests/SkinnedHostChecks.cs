using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Asset.Import;
using Ncma.Player.App;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Characters;

internal static class SkinnedHostChecks
{
    internal static string[] Run(string repository, string output, string plugins)
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        string native = Directory.GetParent(Directory.GetParent(plugins)!.FullName)!.FullName;
        foreach (string fixture in new[] { "blender_279_sausage_6100_ascii.fbx", "blender_279_sausage_7400_binary.fbx" })
        {
            string root = Path.Combine(output, "FBX Player " + fixture); Directory.CreateDirectory(Path.Combine(root, "assets"));
            string path = Path.Combine(native, "NcmaImportKernel.dll"), source = Path.Combine(repository, "tests/assets/fbx", fixture);
            ModelImportPlan plan; Guid project = Guid.NewGuid(), model = Guid.NewGuid();
            using (var importer = new ImportKernel(path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
            {
                var imported = importer.LoadAndCopy(source, 30);
                plan = ModelImportPlanner.Build(imported, new(1, model, AssetKind.Character, "assets/model.fbx", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))), "ufbx", 1, new(1, 30, true), [], [], null), project, false);
            }
            // Only derived NCA and metadata saved. Source FBX/import DLL is NOT part of this runtime project.
            string generation = Path.Combine(root, plan.Record.Generation!.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(generation)!); File.WriteAllBytes(generation, plan.DerivedBytes);
            File.WriteAllBytes(Path.Combine(root, "assets/model.fbx.ncmeta"), AssetRecordCodec.Encode(plan.Record));
            var manifest = ModelAssetManifestCodec.ValidateBundle(plan.DerivedBytes, plan.Record);
            var document = new SceneDocument("Imported FBX Player", RenderComponentRegistry.CreateRegistry(), SceneRenderValidation.RequireComposition);
            int characters = 0;
            foreach (var mesh in manifest.Meshes)
            {
                Guid obj = document.CreateObject("Character " + characters++); document.SetComponent(obj, TransformData.Identity with { Position = new(0, 0, -3) });
                document.SetComponent(obj, new SkinnedMeshData(model, mesh.Mesh, manifest.Skeleton!.Value, mesh.Materials, true, true, uint.MaxValue));
                document.SetComponent(obj, new ClipPlaybackData(manifest.Clips[0], true, true, 1, 0));
            }
            Guid camera = document.CreateObject("Camera"); document.SetComponent(camera, TransformData.Identity); document.SetComponent(camera, CameraData.Default with { Projection = CameraProjection.Orthographic, OrthographicHeight = 4 });
            Guid light = document.CreateObject("Light"); document.SetComponent(light, TransformData.Identity); document.SetComponent(light, DirectionalLightData.Default);
            string scene = Path.Combine(root, "start.ncmascene"), projectFile = Path.Combine(root, "scene.ncmaproject"); SceneDocumentFiles.Save(document, scene); byte[] saved = File.ReadAllBytes(scene);
            string assembly = Assembly.GetExecutingAssembly().Location; File.Copy(assembly, Path.Combine(root, "gameplay.dll")); File.Copy(Path.ChangeExtension(assembly, ".deps.json"), Path.Combine(root, "gameplay.deps.json"));
            var config = new ProjectConfiguration(1, project, "FBX scene", "start.ncmascene", "gameplay.dll", "Direct3D11", [], SceneCamera: camera);
            File.WriteAllBytes(projectFile, JsonSerializer.SerializeToUtf8Bytes(config, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            for (int i = 0; i < 3; i++)
            {

                using var playerRunOwner0 = new Ncma.Player.App.PlayerRunner();
                var options = PlayerOptions.Parse(["--project", projectFile, "--ticks", "6", "--max-runtime-seconds", "20", "--report", Path.Combine(output, fixture + "-player-" + i + ".json")]);
                var report = playerRunOwner0.Run(options, pluginRoot: plugins, visible: false);
                Check(report.ExitCode == 0 && report.Tick >= 6 && report.RenderedFrames > 0 && report.ObjectCount == characters + 2 && report.ValidationErrors == 0 && report.ValidationWarnings == 0 && report.ShutdownErrors.Length == 0,
                    "FBX runtime scene Player failed: " + report.Reason + " / " + report.ExitCode + " / " + string.Join(',', report.ShutdownErrors));
                Check(saved.SequenceEqual(File.ReadAllBytes(scene)), "Player changed animation authoring data");
                using var unlocked = new FileStream(generation, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            }
            Check(!File.Exists(Path.Combine(root, "assets/model.fbx")), "Player accidentally deployed/reparsed FBX source");
            RuntimePackageChecks.Run(repository, root, output, plugins.Contains("windows-ninja-release", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug", config, document.CaptureSnapshot());
            var coupled = new SceneDocument("FBX coupled character", CharacterComponents.Register(RenderComponentRegistry.CreateRegistry()), CharacterComponents.RequireComposition);
            coupled.RestoreBytes(document.CaptureBytes());
            Guid actor = coupled.GetObjectIds().First(o => coupled.HasComponent<SkinnedMeshData>(o)); coupled.SetComponent(actor, CharacterData.Default with { FootOffsetY = 0 });
            coupled.SetComponent(actor, TransformData.Identity with { Position = new(0, .1f, -3) });
            coupled.SetComponent(actor, new RootMotionData(0));
            Guid floor = coupled.CreateObject("Collision floor"); coupled.SetComponent(floor, TransformData.Identity with { Position = new(0, -.5f, 0) }); coupled.SetComponent(floor, new BoxColliderData(20, .5f, 20, 1000, 1, false));
            coupled.SetComponent(camera, new FollowCameraData(actor, 0, 2, 6, 1));
            string coupledScene = Path.Combine(root, "coupled.ncmascene"), coupledProject = Path.Combine(root, "coupled.ncmaproject"); SceneDocumentFiles.Save(coupled, coupledScene); byte[] coupledSaved = File.ReadAllBytes(coupledScene);
            var runtimeStartup = coupled.BakeRuntime(coupled.Revision).Image.Instantiate(coupled.Components);
            string startupRuntimeHash = Convert.ToHexString(SHA256.HashData(runtimeStartup.CaptureCheckpoint().CopyBytes()));
            File.WriteAllBytes(coupledProject, JsonSerializer.SerializeToUtf8Bytes(config with { StartupScene = "coupled.ncmascene", PhysicsEnabled = true }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            foreach (bool headless in new[] { true, false })
            {

                using var playerRunOwner1 = new Ncma.Player.App.PlayerRunner();
                var flags = new List<string> { "--project", coupledProject, "--ticks", "30", "--report", Path.Combine(root, headless ? "coupled-null.json" : "coupled-dx11.json") }; if (headless) flags.Add("--headless");
                var report = playerRunOwner1.Run(PlayerOptions.Parse(flags.ToArray()), pluginRoot: plugins, visible: false, trustedInput: headless ? (p, t) => CharacterHostChecks.Input(p, t) : null);
                Check(report.ExitCode == 0 && report.Tick >= 30 && report.ShutdownErrors.Length == 0 && report.ValidationErrors == 0 && report.ValidationWarnings == 0 && (headless || report.RenderedFrames > 0), "Actual animated FBX coupled Player: " + report.Reason);
                Check(coupledSaved.SequenceEqual(File.ReadAllBytes(coupledScene)), "Coupled Player edited authoring scene");
                Check(report.SchemaVersion == 2 && report.StateSha256 is { Length: 64 } && report.StateSha256 != startupRuntimeHash, "Actual coupled Player must change runtime state, not merely change serialization format");
            }
            string packedRoot = Path.Combine(root, "Coupled packed"); Directory.CreateDirectory(Path.Combine(packedRoot, "assets"));
            string packed = Path.Combine(packedRoot, "assets/game.ncpak"); File.WriteAllBytes(packed, SceneAssetPreparation.CreateRuntimePackage(root, project, coupled.CaptureSnapshot(), []));
            File.WriteAllBytes(Path.Combine(packedRoot, "start.ncmascene"), coupledSaved);
            foreach (string file in new[] { "gameplay.dll", "gameplay.deps.json" }) File.Copy(Path.Combine(root, file), Path.Combine(packedRoot, file));
            string packedProject = Path.Combine(packedRoot, "game.ncmaproject"); File.WriteAllBytes(packedProject, JsonSerializer.SerializeToUtf8Bytes(config with { PhysicsEnabled = true, AssetPackage = "assets/game.ncpak" }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            foreach (bool headless in new[] { true, false }) {

                using var playerRunOwner2 = new Ncma.Player.App.PlayerRunner();
                var flags = new List<string> { "--project", packedProject, "--ticks", "6", "--report", Path.Combine(packedRoot, headless ? "null.json" : "dx11.json") }; if (headless) flags.Add("--headless");
                var report = playerRunOwner2.Run(PlayerOptions.Parse(flags.ToArray()), pluginRoot: plugins, visible: false);
                Check(report.ExitCode == 0 && report.Tick >= 6 && report.ShutdownErrors.Length == 0 && report.ValidationErrors == 0 && report.ValidationWarnings == 0, "NCP1 retains typed character/collider/follow values without authoring source: " + report.Reason);
                using var unlocked = new FileStream(packed, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
        }
        return ["ASCII/binary FBX import->NCA save/restart->real animated Player, 3 cycles each, no source FBX runtime, validation 0/0",
            "M3.9 ASCII/binary source-free runtime packages: 32 Play cycles each/graphical and Null apphosts/corrupt preflight/manifest 0/0",
            "M4.4 ASCII/binary genuine FBX root-enabled coupled Player/Null and packed Player, follow camera, validation 0/0 and authoring isolation"];
    }
}
