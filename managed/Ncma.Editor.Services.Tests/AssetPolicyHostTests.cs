using System.Reflection;
using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static class AssetPolicyHostTests
{
    internal static IEnumerable<(string, Action)> Cases(string repository, string plugins, string output)
    {
        yield return ("asset policy host: actual CPU Editor uses simulation policy without opening visual package", () => {
            string root = Path.Combine(output, "asset-policy-cpu", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "assets"));
            // ProjectContext still validates explicitly declared paths. An existing but invalid
            // visual payload proves the CPU runtime does not decode/open the visual package.
            string unusedPackage = Path.Combine(root, "assets/unused.ncpak");
            File.WriteAllText(unusedPackage, "intentionally not a runtime package");
            foreach (string extension in new[] { ".dll", ".deps.json" })
                File.Copy(Path.Combine(repository, "out/managed/Ncma.Gameplay.Sample" + extension), Path.Combine(root, "gameplay" + extension));
            var document = new SceneDocument("CPU host", global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module), SceneRenderValidation.RequireComposition);
            var id = document.CreateObject("Visual metadata"); document.SetComponent(id, TransformData.Identity);
            document.SetComponent(id, new StaticMeshData(Guid.NewGuid(), Guid.NewGuid(), true, true, uint.MaxValue));
            SceneDocumentFiles.Save(document, Path.Combine(root, "start.ncmascene")); byte[] before = document.CaptureBytes();
            var config = new ProjectConfiguration(1, Guid.NewGuid(), "CPU host", "start.ncmascene", "gameplay.dll", "Direct3D11", [], AssetPackage: "assets/unused.ncpak");
            string path = Path.Combine(root, "project.ncmaproject");
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(config, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            using var presentation = new CandidatePresentation(plugins, ProjectContext.Load(path), true, true, false);
            presentation.Start();
            object? Field(string name) => typeof(CandidatePresentation).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(presentation);
            var owner = (EditorSessionOwner)Field("_editor")!;
            var proposals = owner.DomainProposals ?? throw new Exception("Actual Editor did not configure project proposal preflight.");
            Check(proposals.CaptureSource().ProjectId == config.ProjectId);
            Check(proposals.CaptureSource().DocumentSessionId == owner.Document.SessionId);
            Check(owner.AssetPolicy == SceneAssetPolicy.Simulation && Field("_renderer") is null);
            Check(owner.RenderAssets!.Assets.List().Count == 0);
            owner.StartPlay();
            Check(owner.PlayDependencies!.Assets.Resolution == "not-required" && !owner.PlayDependencies.Assets.PresentationRequired);
            owner.StopPlay(); Check(before.SequenceEqual(owner.Document.CaptureBytes()));
            Check(File.ReadAllText(unusedPackage) == "intentionally not a runtime package");
        });
    }
    private static void Check(bool value) { if (!value) throw new Exception("Asset policy CPU host assertion."); }
}
