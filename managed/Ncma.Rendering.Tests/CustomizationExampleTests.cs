using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Rendering.Scene.Examples;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static int CustomizationExampleTests(string plugins, string output)
    {
        output = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        int cases = 0, maxError = 0; ulong frame = 1;
        void Pass(bool ok, string label) { Check(ok, "D1 " + label); cases++; }
        void Bad(Action action) { Reject(action); cases++; }
        var modules = SceneCustomizationExamples.Modules();
        var gain = new PostFeatureInput(ExampleHdrGain.ModuleId, 1, [new("gain", .5f)]);
        var bloom = PostProcessModules.Bloom(new(Levels: 2, Threshold: 0, Intensity: .8f));
        var active = PostProcessData.Off with { Enabled = true, StackJson = PostProcessData.EncodeStack([gain, bloom]) };
        Pass(modules.Validate(active) == active, "user Feature before official Bloom");
        Bad(() => modules.Validate(active with { StackJson = PostProcessData.EncodeStack([bloom, gain]) }));
        foreach (var size in new uint[] { 1, 63, 128 }) foreach (bool shadows in new[] { false, true }) {
            var plan = SceneCustomizationExamples.ReplacePipeline(1, .2f, shadows).Build(size, size).Compile(RenderCapabilities.SceneDx11);
            Pass(plan.PassCount == (shadows ? 3 : 2) && plan.PassNames.All(n => n.StartsWith("Example", StringComparison.Ordinal)), "independent full graph");
            Pass(!plan.RequiresReferenceResources && plan.Resources.Count == (shadows ? 4 : 3) &&
                (!shadows || plan.Resources.Single(r => r.Role == RenderRole.SceneShadow).Width == 512), "owned supported resource topology");
        }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1, 4 }) Bad(() => ExampleHdrGain.Build(64, 64, invalid, null!));
        Bad(() => new ExampleScenePipeline(17, .2f, false).Build(64, 64));
        Bad(() => new ExampleScenePipeline(1, 2, false).Build(64, 64));
        Bad(() => new ExampleScenePipeline(1, .2f, false).Build(0, 64));
        Pass(ExampleHdrGain.Build(64, 64, 0, null!) is null && !ProcessHasGraphics(), "CPU-only examples and zero gain no resources");

        var repo = new DirectoryInfo(Path.GetFullPath(plugins)); while (!File.Exists(Path.Combine(repo.FullName, "Build.bat"))) repo = repo.Parent ?? throw new Exception("Repository missing");
        string deployed = Path.Combine(repo.FullName, "out/bin"), libraryRoot = Path.Combine(output, "library");
        // Explicit unique project files/UUIDs; never alter the deployed index or hide a fallback.
        PostProcessLibrarySelection selection;
        using (var original = RuntimePostProcessLibrary.LoadDeployment(deployed)) selection = original.CopySelection();
        foreach (string path in selection.Programs.Select(p => p.Path).Concat(selection.Tones.Select(t => t.Path))) {
            string destination = Path.Combine(libraryRoot, path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(Path.Combine(deployed, path), destination);
        }
        var originalBloom = selection.Programs.Single(p => p.ModuleId == PostProcessLibrarySelection.BloomModule);
        const string gainPath = "assets/postprocess/example-gain.nph";
        File.Copy(Path.Combine(deployed, originalBloom.Path), Path.Combine(libraryRoot, gainPath));
        var index = (selection with { Programs = [.. selection.Programs, new(ExampleHdrGain.ModuleId, gainPath, originalBloom.Sha256)] }).Encode();
        File.WriteAllBytes(Path.Combine(libraryRoot, PostProcessLibrarySelection.IndexPath), index);
        using var library = RuntimePostProcessLibrary.Load(libraryRoot, Convert.ToHexString(SHA256.HashData(index)));
        using var files = RuntimeShaderFileSet.ForHost(deployed, null, null)!;
        var composed = modules.Build(active, 64, 64, library.Program)!;
        Pass(composed.Plan.CopyPasses()[0].Program == BloomFeature.CombineProgram && composed.Plan.PassCount == 9, "gain truly inserted into linear HDR chain");
        Pass(!ProcessHasGraphics() && !composed.Programs.GpuValidated, "source-free CPU construction not GPU credential");

        using var loader = new PluginLoader(); loader.Load(Path.GetFullPath(plugins), Specs().Take(2).ToArray());
        using var window = new PlatformWindow(loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "Customization examples", 128, 128, false);
        using var renderer = new RendererSession(loader.Modules.Single(m => m.Kind == ModuleKind.Renderer), window, 128, 128);
        files.InstallSelection(renderer, () => true);
        using var target = renderer.CreateViewTarget(64, 64); byte[] image = new byte[64 * 64 * 4];
        var programs = PreparedPostProcessPrograms.Prepare(renderer, composed.Programs, () => true);
        using (var pipeline = new ScenePipelineSession(renderer, SceneCustomizationExamples.ReplacePipeline(1, .2f, false), 64, 64)) {
            pipeline.ConfigurePostProcess(composed.Plan, programs, () => true);
            pipeline.Submit(frame++, [], [], new(Vector3.Zero, Vector3.UnitZ, Vector4.UnitW), Matrix4x4.Identity, new(), target, clear: new(.25f, .125f, .0625f, 1));
            renderer.CaptureTarget(target, image); renderer.Present();
            int Expected(double value) {
                double source = (double)(Half)value;
                double boosted = (double)(Half)(source + source * .5);
                double hdr = (double)(Half)(boosted + boosted * .8);
                double c = Math.Clamp(hdr * (2.51 * hdr + .03) / (hdr * (2.43 * hdr + .59) + .14), 0, 1);
                return (int)Math.Round((c <= .0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - .055) * 255);
            }
            int[] expected = [Expected(.25), Expected(.125), Expected(.0625), 255];
            maxError = image.Select((v, i) => Math.Abs(v - expected[i % 4])).Max();
            Pass(maxError <= 1, "full graph + gain + Bloom independent scalar/Half whole image");
            foreach (float g in new[] { 0f, .25f, 3f }) foreach (float value in new[] { 0f, .001f, .25f, 1f }) {
                var settings = active with { StackJson = PostProcessData.EncodeStack([gain with { Parameters = [new("gain", g)] }, bloom]) };
                var combination = modules.Build(settings, 64, 64, library.Program)!;
                pipeline.ConfigurePostProcess(combination.Plan, PreparedPostProcessPrograms.Prepare(renderer, combination.Programs, () => true), () => true);
                pipeline.Submit(frame++, [], [], new(Vector3.Zero, Vector3.UnitZ, Vector4.UnitW), Matrix4x4.Identity, new(), target, clear: new(value, value, value, 1));
                renderer.CaptureTarget(target, image); renderer.Present();
                double x = (double)(Half)value; x = (double)(Half)(x + x * g); x = (double)(Half)(x + x * .8);
                x = Math.Clamp(x * (2.51 * x + .03) / (x * (2.43 * x + .59) + .14), 0, 1);
                int scalar = (int)Math.Round((x <= .0031308 ? 12.92 * x : 1.055 * Math.Pow(x, 1 / 2.4) - .055) * 255);
                int error = image.Select((v, i) => Math.Abs(v - (i % 4 == 3 ? 255 : scalar))).Max(); maxError = Math.Max(maxError, error);
                Pass(error <= 1, $"gain {g} linear input {value} zero/near-black/HDR bound whole-image oracle");
            }
        }

        bool approved = true;
        using var runtime = new PostProcessRuntime(renderer, files, library, modules, () => approved);
        var fixture = new SceneGpuFixture(output); var (document, camera, _, _) = fixture.Scene();
        global::Ncma.Runtime.Entity light = document.World.GetEntities().Single(o => document.World.Has<DirectionalLightData>(o)); document.World.Set(light, document.World.Get<DirectionalLightData>(light) with { CastShadow = false });
        document.World.Set(document.World.CreateEntity("Post"), active);
        using (var presets = EnvironmentPresetLibrary.LoadDeployment(deployed)) {
            var environment = presets.Package.CloneForAsset(Guid.NewGuid(), 1); var descriptor = EnvironmentAssetDescriptor.FromPackage(fixture.Project, environment);
            string path = Path.Combine(fixture.Root, descriptor.Path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, environment.CopyBytes());
            File.WriteAllBytes(Path.Combine(fixture.Root, "assets/environment.ncenv"), descriptor.Encode());
            document.World.Set(document.World.CreateEntity("Environment"), new EnvironmentLightingData(1, environment.AssetId, 1, environment.ContentHash, 1, 0, true));
        }
        using var assets = SceneAssetPreparation.Prepare(fixture.Root, fixture.Project, document.CaptureSnapshot(), true);
        using var cache = new RenderResourceCache(renderer); var observations = new List<object>(); byte[]? stageImage = null;
        foreach (bool full in new[] { false, true }) {
            int builds = 0; Action? duringBuild = null; bool invalidGraph = false;
            RenderPipeline Factory(float exposure, float ambient, bool shadows) {
                builds++; duringBuild?.Invoke();
                if (invalidGraph) return new InvalidExamplePipeline();
                return full ? SceneCustomizationExamples.ReplacePipeline(exposure, ambient, shadows) : SceneCustomizationExamples.ReplaceGeometryStage(exposure, ambient, shadows);
            }
            using var scene = new SceneRenderSession(renderer, cache, document.World);scene.Prepare(assets, document.CaptureSnapshot(),
                pipelineFactory: Factory, environmentPreparationAllowed: () => approved, postProcessing: runtime);
            void Prepare(uint size = 64) => scene.PrepareEnvironmentView(size, size, camera, ambient: .2f);
            byte[] Draw(uint size = 64) {
                Pass(scene.Submit(frame++, size, size, camera, target: target, ambient: .2f), "shared ready custom scene");
                renderer.CaptureTarget(target, image); renderer.Present(); return image.ToArray();
            }
            Bad(() => scene.Submit(frame++, 64, 64, camera, target: target)); Prepare(); var baseline = Draw();
            Pass(scene.Plan!.PassNames.Contains("Example geometry stage") && renderer.PostStats.Bindings == 1 && renderer.EnvironmentStats.Live == 1, "shared IBL + registered gain/Bloom + stage replacement");
            if (stageImage is null) stageImage = baseline; else Pass(baseline.SequenceEqual(stageImage), "independent full graph and stage path same whole image without shadow");
            ulong creates = renderer.PipelineStats.Creates, publications = renderer.PostStats.Publications, uploaded = renderer.ResourceStats.UploadedBytes; int built = builds;
            for (int i = 0; i < 8; i++) { Prepare(); scene.Submit(frame++, 64, 64, camera, target: target, ambient: .2f); renderer.Present(); }
            long bytes = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
            for (int i = 0; i < 64; i++) { scene.Submit(frame++, 64, 64, camera, target: target, ambient: .2f); renderer.Present(); }
            bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            observations.Add(new { full, bytes, elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds, builds });
            Pass(bytes == 0 && builds == built && renderer.PipelineStats.Creates == creates && renderer.PostStats.Publications == publications && renderer.ResourceStats.UploadedBytes == uploaded, "warm stable Submit no allocation/build/upload");
            foreach (Action forbidden in new Action[] { () => document.World.CreateEntity("illegal callback"), () => scene.Dispose(), () => Prepare(63) }) {
                duringBuild = forbidden; Bad(() => Prepare(63)); duringBuild = null;
                Pass(scene.IsPostProcessReady && Draw().SequenceEqual(baseline), "reentry/write candidate failure retains old binding and pixels");
            }
            invalidGraph = true; Bad(() => Prepare(63)); invalidGraph = false;
            Pass(renderer.PipelineStats.Creates == creates && Draw().SequenceEqual(baseline), "invalid full graph never allocates or replaces installed candidate");
            scene.Submit(frame++, 64, 64, camera, target: target, ambient: .2f); int beforeFrame = builds;
            Bad(() => Prepare(63)); renderer.Present(); Pass(builds == beforeFrame, "active frame rejects before user factory");
            approved = false; Bad(() => Prepare(63)); approved = true; Pass(Draw().SequenceEqual(baseline), "revoked preparation retained old binding");
            Exception? ownerError = null; var thread = new Thread(() => { try { Prepare(); } catch (Exception e) { ownerError = e; } }); thread.Start(); thread.Join();
            Pass(ownerError is InvalidOperationException, "shared custom owner boundary");
            Prepare(63); Draw(63); Prepare(); Pass(Draw().SequenceEqual(baseline), "odd resize and back exact pixels");
            document.World.Set(light, document.World.Get<DirectionalLightData>(light) with { CastShadow = true }); Prepare(); var shadowed = Draw();
            Pass(scene.Plan!.Resources.Single(r => r.Role == RenderRole.SceneShadow).Width == (full ? 512 : 1024) && !shadowed.SequenceEqual(baseline), "custom shadow topology actually executed");
            document.World.Set(light, document.World.Get<DirectionalLightData>(light) with { CastShadow = false }); Prepare(); Pass(Draw().SequenceEqual(baseline), "shadow removal restores baseline");
        }
        cache.Trim(); Pass(renderer.PostStats.Bindings == 0 && renderer.PipelineStats.Pipelines == 0 && renderer.EnvironmentStats.Live == 0, "close returns GPU resources");
        Pass(renderer.Stats.ValidationErrors == 0 && renderer.Stats.ValidationWarnings == 0, "API0/0");
        File.WriteAllText(Path.Combine(output, "customization-results.json"), JsonSerializer.Serialize(new { cases, maxError, observations, apiErrors = 0, apiWarnings = 0, jointFbxUiAccepted = false, relocatedProductsAccepted = false, manualAccepted = false }));
        Console.WriteLine($"PASS M7.4-D1 customization examples: {cases} checks; maxError={maxError}; API0/0; {output}"); return 0;
    }
    private sealed class InvalidExamplePipeline : RenderPipeline { public override RenderGraph Build(uint width, uint height) => new(); }
}
