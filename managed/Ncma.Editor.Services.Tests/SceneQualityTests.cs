using System.Text.Json;
using Ncma.Assets;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static class SceneQualityTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Scene quality assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Expected quality rejection.");
    }
    private sealed class Fixture
    {
        internal readonly SceneDocument Document;
        internal readonly EditSession Edit;
        internal readonly Guid Fallback, Shadow, Post;
        internal readonly PostProcessModules Modules = PostProcessModules.Official();
        internal Fixture(bool existing = true)
        {
            var registry = global::Ncma.Runtime.CompositionRegistry.Build();
            SceneShadowData.Register(registry); PostProcessData.Register(registry);
            Document = new("Quality", registry);
            var fallback = Document.CreateObject("Settings");
            var shadow = Document.CreateObject("Shadow owner");
            var post = Document.CreateObject("Post owner");
            Fallback = fallback; Shadow = shadow; Post = post;
            if (existing) {
                Document.SetComponent(shadow, new SceneShadowData(1, SceneShadowQuality.High));
                Document.SetComponent(post, PostProcessData.Off with { Enabled = true, ExposureEv = -1,
                    ToneModule = PostProcessLibrarySelection.ReinhardModule,
                    StackJson = PostProcessData.EncodeStack([PostProcessModules.Bloom(new())]) });
            }
            Edit = new(Document);
        }
        internal CapabilityRequest Request() => RenderingEditorAdapter.QualityRequest(Edit, Fallback,
            SceneQualityPresets.LowV1, Modules, Edit.Revision);
        internal CapabilityPermissions Permissions(string[]? types = null, Guid[]? ids = null) =>
            new(["ncma.scene.transaction", "ncma.history.undo", "ncma.history.redo"],
                objectScope: ids ?? [Fallback, Shadow, Post], componentScope: types ?? [SceneShadowData.TypeId, PostProcessData.TypeId]);
        internal CapabilityResult History(string name) => Edit.Invoke(new(EditSession.ContractVersion, Guid.NewGuid(),
            Edit.SessionId, Edit.Revision, name, JsonSerializer.SerializeToElement(new { })), Permissions());
    }
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("E5-D Low v1 preserves Tone exposure and has no optional feature passes", () => {
            var f = new Fixture(); var current = ScenePostProcessState.Read(f.Document.CaptureSnapshot());
            var selected = SceneQualityPresets.Create(SceneQualityPresets.LowV1, f.Modules, current);
            Check(selected.Shadows == new SceneShadowData(1, SceneShadowQuality.Low));
            Check(selected.PostProcess == current with { StackJson = "[]" });
            Check(f.Modules.Build(selected.PostProcess, 1920, 1080, _ => throw new Exception("Feature callback must not run")) is null);
            var preset = SceneShadowPresets.Get(selected.Shadows.Quality);
            Check(preset.Budget.Resolution == 512 && preset.Budget.MaximumDepthReadsPerShadedPixel == 9 && !preset.RequiresHighShader);
            var graph = SceneShadowPipelinePolicy.Create(null, 1, .03f, true, selected.Shadows, ShaderProfile.Scene3D)
                .Build(1920, 1080).Compile(RenderCapabilities.SceneDx11);
            Check(graph.Resources.Single(r => r.Role == RenderRole.SceneShadow).Width == 512);
            Check(SceneQualityPresets.Create(SceneQualityPresets.LowV1, f.Modules, selected.PostProcess) == selected);
        });
        yield return ("E5-D Low request is read-only and retains separate singleton owners", () => {
            var f = new Fixture(); byte[] before = f.Document.CaptureBytes(); var revision = f.Edit.Revision;
            var request = f.Request(); var operations = request.Input.GetProperty("operations");
            Check(operations.GetArrayLength() == 2 && request.Capability == "ncma.scene.transaction");
            Check(operations[0].GetProperty("objectId").GetGuid() == f.Shadow && operations[1].GetProperty("objectId").GetGuid() == f.Post);
            Check(before.SequenceEqual(f.Document.CaptureBytes()) && f.Edit.Revision == revision && f.Edit.State.UndoCount == 0);
        });
        yield return ("E5-D Low two settings commit atomically with one Undo and exact Redo", () => {
            var f = new Fixture(); byte[] before = f.Document.CaptureBytes();
            Check(f.Edit.Invoke(f.Request(), f.Permissions()).Changed && f.Edit.State.UndoCount == 1);
            byte[] after = f.Document.CaptureBytes();
            Check(SceneShadowState.Read(f.Document.CaptureSnapshot())?.Quality == SceneShadowQuality.Low);
            var post = ScenePostProcessState.Read(f.Document.CaptureSnapshot());
            Check(post.Enabled && post.ExposureEv == -1 && post.ToneModule == PostProcessLibrarySelection.ReinhardModule && post.StackJson == "[]");
            Check(f.History("ncma.history.undo").Changed && before.SequenceEqual(f.Document.CaptureBytes()));
            Check(f.History("ncma.history.redo").Changed && after.SequenceEqual(f.Document.CaptureBytes()));
        });
        yield return ("E5-D Low missing selections use explicit fallback without enabling post", () => {
            var f = new Fixture(false); Check(f.Edit.Invoke(f.Request(), f.Permissions()).Changed);
            var objectId = f.Fallback;
            Check(f.Document.GetComponent<SceneShadowData>(objectId).Quality == SceneShadowQuality.Low);
            Check(f.Document.GetComponent<PostProcessData>(objectId) == PostProcessData.Off);
            Check(f.Document.ObjectCount == 3);
        });
        yield return ("E5-D Low rejects permission and object/component scope without partial writes", () => {
            foreach (var mode in new[] { 0, 1, 2 }) {
                var f = new Fixture(); byte[] before = f.Document.CaptureBytes();
                var grant = mode == 0 ? CapabilityPermissions.ReadOnly : mode == 1
                    ? f.Permissions([SceneShadowData.TypeId]) : f.Permissions(ids: [f.Shadow]);
                Check(!f.Edit.Invoke(f.Request(), grant).Changed);
                Check(before.SequenceEqual(f.Document.CaptureBytes()) && f.Edit.State.UndoCount == 0);
            }
        });
        yield return ("E5-D Low stale build and delayed commit cannot overwrite newer edits", () => {
            var f = new Fixture(); var old = f.Edit.Revision; var request = f.Request();
            Check(f.Edit.Invoke(request, f.Permissions()).Changed);
            byte[] before = f.Document.CaptureBytes();
            Reject(() => RenderingEditorAdapter.QualityRequest(f.Edit, f.Fallback, SceneQualityPresets.LowV1, f.Modules, old));
            Check(!f.Edit.Invoke(request with { RequestId = Guid.NewGuid() }, f.Permissions()).Changed);
            Check(before.SequenceEqual(f.Document.CaptureBytes()) && f.Edit.State.UndoCount == 1);
        });
        yield return ("E5-D Low rejects ambiguous invalid unknown and foreign-thread inputs", () => {
            var f = new Fixture();
            Reject(() => RenderingEditorAdapter.QualityRequest(f.Edit, Guid.NewGuid(), SceneQualityPresets.LowV1, f.Modules, f.Edit.Revision));
            Reject(() => SceneQualityPresets.Create("ncma.quality.low.v2", f.Modules, PostProcessData.Off));
            Reject(() => SceneQualityPresets.Create(SceneQualityPresets.LowV1, f.Modules, PostProcessData.Off with {
                StackJson = PostProcessData.EncodeStack([new(Guid.NewGuid(), 1, [])]) }));
            Task.Run(() => Reject(() => f.Request())).GetAwaiter().GetResult();
            foreach (bool post in new[] { false, true }) {
                var duplicate = new Fixture(); var fallback = duplicate.Fallback;
                if (post) duplicate.Document.SetComponent(fallback, PostProcessData.Off);
                else duplicate.Document.SetComponent(fallback, SceneShadowData.Default);
                byte[] before = duplicate.Document.CaptureBytes();
                Reject(() => duplicate.Request());
                Check(before.SequenceEqual(duplicate.Document.CaptureBytes()));
            }
        });
    }
}
