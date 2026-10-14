using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int ShadowPresetContracts()
    {
        int checks = 0;
        void Pass(bool value, string reason) { Check(value, "Shadow preset: " + reason); checks++; }
        foreach (var quality in Enum.GetValues<SceneShadowQuality>())
        {
            var preset = SceneShadowPresets.Get(quality);
            uint size = quality switch { SceneShadowQuality.Off => 0, SceneShadowQuality.Low => 512,
                SceneShadowQuality.Medium => 1024, _ => 2048 };
            Pass(ReferenceEquals(preset, SceneShadowPresets.Get(quality)), "immutable cached selection");
            Pass(preset.Budget.Resolution == size && preset.Budget.DepthTextureBytes == 4UL * size * size, "depth-only bytes");
            Pass(preset.Budget.MaximumDepthReadsPerShadedPixel == (size == 0 ? 0 : quality == SceneShadowQuality.High ? 205 : 9), "official sample upper bound");
            Pass(preset.Budget.BlockerSearchReads == (quality == SceneShadowQuality.High ? 9 : 0), "blocker search budget");
            Pass(preset.Settings.Pcss == (quality == SceneShadowQuality.High) && preset.Settings.Distance == 40, "filter and world distance");
            foreach (bool casts in new[] { false, true })
            {
                var graph = preset.CreatePipeline(casts).Build(320, 180).Compile(RenderCapabilities.SceneDx11);
                bool enabled = casts && quality != SceneShadowQuality.Off;
                var shadow = graph.Resources.SingleOrDefault(r => r.Role == RenderRole.SceneShadow);
                Pass((shadow is not null) == enabled && (!enabled || shadow!.Width == size && shadow.Height == size), "exact resource allocation");
                Pass(graph.PassCount == (enabled ? 3 : 2), "no hidden shadow pass");
                Pass(preset.EffectiveBudget(casts) == (casts ? preset.Budget : default), "effective light-disabled budget");
                Pass(graph.Width == 320 && graph.Height == 180, "independent viewport size");
                Pass((graph.RequiredSceneGeometryPixelHash is not null) == (enabled && quality == SceneShadowQuality.High), "High exact pixel requirement survives compilation");
            }
        }
        Pass(SceneShadowPresets.Get(SceneShadowQuality.Medium).Settings == new SceneShadowSettings(), "unchanged default settings");
        var copied = SceneShadowPresets.Get(SceneShadowQuality.High).Settings with { LightRadius = 1 };
        Pass(copied.LightRadius == 1 && SceneShadowPresets.Get(SceneShadowQuality.High).Settings.LightRadius == 8, "settings are value copies");
        Reject(() => SceneShadowPresets.Get((SceneShadowQuality)(-1))); checks++;
        Reject(() => SceneShadowPresets.Get((SceneShadowQuality)4)); checks++;
        Reject(() => SceneShadowShaders.CreateHighPixel(ShaderProfile.Flat2D)); checks++;
        var required = new Scene3DPipeline().Build(32, 32);
        var originalPlan = required.Compile(RenderCapabilities.SceneDx11);
        var highPixel = SceneShadowShaders.CreateHighPixel(ShaderProfile.Scene3D);
        required.RequireSceneGeometryPixel(highPixel);
        var requiredPlan = required.Compile(RenderCapabilities.SceneDx11);
        Pass(!ReferenceEquals(originalPlan, requiredPlan) && originalPlan.RequiredSceneGeometryPixelHash is null && requiredPlan.RequiredSceneGeometryPixelHash == highPixel.ContentHash, "requirements invalidate graph cache, preserve prior plan");
        Reject(() => required.RequireSceneGeometryPixel(SceneShadowShaders.CreateHighPixel(ShaderProfile.SceneEnvironment))); checks++;
        var clear = new ClearPipeline().Build(32, 32);
        clear.RequireSceneGeometryPixel(highPixel);
        Reject(() => clear.Compile(RenderCapabilities.ReferenceDx11)); checks++;
        Reject(() => SceneShadowPresets.Get(SceneShadowQuality.Off).CreatePipeline(true, float.NaN).Build(32, 32).Compile(RenderCapabilities.SceneDx11)); checks++;
        Pass(!Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(m => m.ModuleName is "NcmaRenderer.dll" or "NcmaPlatform.dll"), "CPU declaration loads no native plugin");
        Console.WriteLine($"PASS M7.5 shadow declaration {checks} checks; no host selection or full image acceptance claimed");
        return checks;
    }

    private static int ShadowPresetTests(string root, string output)
    {
        int checks = ShadowPresetContracts();
        output = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        void Pass(bool value, string reason) { Check(value, "Shadow preset GPU: " + reason); checks++; }
        using var loader = new PluginLoader(); loader.Load(Path.GetFullPath(root), Specs().Take(2).ToArray());
        using var window = new PlatformWindow(loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "Shadow preset baseline", 256, 256, false);
        var rows = new List<object>(); ulong frame = 1;
        using (var renderer = new RendererSession(loader.Modules.Single(m => m.Kind == ModuleKind.Renderer), window, 256, 256))
        {
            using var cache = new RenderResourceCache(renderer);
            var data = Quad(); using var mesh = cache.AcquireMesh(new(Guid.NewGuid(), 1, new string('A', 64)), data);
            var materialDefinition = MaterialDefinition.Default(Guid.NewGuid()) with { BaseColor = new(.6f, .25f, .1f, 1), Metallic = .1f, Roughness = .6f };
            using var material = cache.AcquireMaterial(Version(materialDefinition), materialDefinition, _ => null, true, true, out _);
            using var target = renderer.CreateViewTarget(256, 256);
            var camera = new Ncma.Scene.Rendering.SceneCameraView(Guid.Empty, Matrix4x4.CreateOrthographic(4, 4, .1f, 50), Vector3.Zero, Ncma.Scene.Rendering.CameraData.Default);
            var source = DefaultSceneShaders.CopyCatalog(renderer);
            var high = SceneShadowShaders.ComposeHigh(source, DefaultSceneShaders.Select(source));
            var highShaders = RegisteredSceneShaders.Prepare(renderer, high.Catalog, high.Selection, () => true);
            var light = Vector3.Normalize(new Vector3(1, 0, 1));
            var lighting = new ResourceLighting(Vector3.Zero, light, new(1, 1, 1, 3));
            SceneGpuDraw[] geometry = [SceneGpuDraw.Create(mesh.Resource, material.Resource, data.Ranges[0], Matrix4x4.CreateScale(4, 4, 1) * Matrix4x4.CreateTranslation(0, 0, -4.5f), camera.ViewProjection)];
            SceneGpuDraw[] casters = [SceneGpuDraw.Create(mesh.Resource, material.Resource, data.Ranges[0], Matrix4x4.CreateTranslation(3, 0, -.5f), camera.ViewProjection)];
            byte[] lit = new byte[256 * 256 * 4], image = new byte[lit.Length];
            foreach (var quality in Enum.GetValues<SceneShadowQuality>())
            {
                var preset = SceneShadowPresets.Get(quality);
                var lightVp = Ncma.Rendering.Scene.ShadowVolume.Create(camera, light, preset.Settings.Distance);
                var before = renderer.PipelineStats;
                using (var pipeline = new ScenePipelineSession(renderer, preset.CreatePipeline(true), 256, 256,
                    shaders: preset.RequiresHighShader ? highShaders : null))
                {
                    ulong resident = renderer.PipelineStats.ResidentBytes - before.ResidentBytes;
                    Pass(resident == 256UL * 256 * 12 + 408 + preset.Budget.DepthTextureBytes, "native accounted bytes equal declared depth plus scene targets/constants");
                    void Draw() { pipeline.Submit(frame++, geometry, preset.Enabled ? casters : [], lighting, lightVp, preset.Settings, target); renderer.Present(); }
                    pipeline.Submit(frame++, geometry, preset.Enabled ? casters : [], lighting, lightVp, preset.Settings, target);
                    renderer.CaptureTarget(target, image); renderer.Present();
                    if (!preset.Enabled) image.CopyTo(lit, 0);
                    int shaded = 0; for (int i = 0; i < image.Length; i += 4) if (lit[i] - image[i] > 20) shaded++;
                    Pass(preset.Enabled ? shaded > 1000 : shaded == 0 && image.Where((_, i) => i % 4 != 3).Any(v => v > 50), "off-camera caster visibility / Off lit output");
                    File.WriteAllBytes(Path.Combine(output, quality + ".png"), Png(256, 256, image));
                    for (int i = 0; i < 8; i++) Draw();
                    ulong creates = renderer.PipelineStats.Creates, uploads = renderer.ResourceStats.UploadedBytes;
                    var counters = renderer.PipelineStats;
                    long allocated = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
                    for (int i = 0; i < 32; i++) Draw();
                    double milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                    var after = renderer.PipelineStats;
                    Pass(allocated <= 1024 && after.Creates == creates && renderer.ResourceStats.UploadedBytes == uploads, "warm bounded allocation/no creation/upload");
                    Pass(after.ShadowDraws - counters.ShadowDraws == (preset.Enabled ? 32UL : 0) && after.GeometryDraws - counters.GeometryDraws == 32, "exact caster/geometry work; Off does not submit casters");
                    var timer = renderer.Stats;
                    rows.Add(new { quality = quality.ToString(), preset.Budget, nativeAccountedResidentBytes = resident, shadedPixels = shaded,
                        frames = 32, threadAllocatedBytes = allocated, cpuMilliseconds = milliseconds,
                        gpuLastValidMilliseconds = timer.GpuMilliseconds, gpuSampleValid = timer.GpuSampleValid,
                        gpuOriginFrameAvailable = false, independentPerFrameGpuSamples = false });
                }
                Pass(renderer.PipelineStats.Pipelines == before.Pipelines && renderer.PipelineStats.ResidentBytes == before.ResidentBytes, "lease release returns scene resources");
            }
            renderer.WaitIdle();
            Pass(renderer.Stats.ValidationErrors == 0 && renderer.Stats.ValidationWarnings == 0, "DX11 API validation 0/0");
        }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { schema = 1, checks, rows,
            formalHostSelection = false, independentShadowImageOracle = false, animatedQualityAcceptance = false,
            fullPerformanceAccepted = false, manualAccepted = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS M7.5 initial shadow preset {checks} checks; evidence {output}; stage B/C remain open");
        return 0;
    }
}
