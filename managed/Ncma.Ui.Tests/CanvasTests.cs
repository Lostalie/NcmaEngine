using System.Numerics;
using System.Text.Json;
using Ncma.Assets.Authoring;
using Ncma.Editor.Core;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Scene;
using Ncma.Text;
using Ncma.Ui;
using Ncma.Ui.Rendering;

internal static class CanvasTests
{
    private static void Check(bool value) { if (!value) throw new Exception("UI canvas assertion failed."); }
    public static IEnumerable<(string Name, Action Run)> Cases(string[] args)
    {
        if (args.Length == 0 || !OperatingSystem.IsWindows()) yield break;
        yield return ("Saved document renders real HUD/text/widgets, handles input, resize, cached submit and shared Undo", () => {
            using var loader = new PluginLoader(); loader.Load(args[0], [
                new("platform", ModuleKind.Platform, "NcmaPlatform.dll", "NcmaPlatform.dll", 1, 0, []),
                new("renderer", ModuleKind.Renderer, "NcmaRenderer.dll", "NcmaRenderer.dll", 1, 2, ["platform"]),
                new("text", ModuleKind.Text, "NcmaText.dll", "NcmaText.dll", 1, 0, [])]);
            var platform = loader.Modules.Single(m => m.Kind == ModuleKind.Platform); var rendererModule = loader.Modules.Single(m => m.Kind == ModuleKind.Renderer);
            var textModule = loader.Modules.Single(m => m.Kind == ModuleKind.Text);
            using var window = new PlatformWindow(platform, "Ncma UI document integration", 256, 128, false);
            using var renderer = new RendererSession(rendererModule, window, 256, 128, pureUi: true); using var textService = new TextService(textModule);
            Guid fontId = Guid.NewGuid(); var font = new FontAsset(fontId, File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msyh.ttc")), "Windows regression only; no redistribution", false);
            var d = UiDefinition.Create(Guid.NewGuid(), "Action HUD"); var root = d.Elements[0] with { Style = UiStyle.Default with { Fill = new(.04f, .07f, .12f, 1), Clip = true } };
            var label = UiElement.Create(Guid.NewGuid(), "Health", UiKind.Text, d.Root) with { Font = fontId, Text = "生命值 100", Layout = UiLayout.Fixed(8, 4, 160, 32) };
            var button = UiElement.Create(Guid.NewGuid(), "Attack", UiKind.Button, d.Root) with { Order = 1, Font = fontId, Text = "攻击", Action = "attack", Layout = UiLayout.Fixed(8, 44, 88, 32), Style = UiStyle.Default with { Fill = new(.05f, .3f, .7f, 1), CornerRadius = 5 } };
            var input = UiElement.Create(Guid.NewGuid(), "Name", UiKind.TextInput, d.Root) with { Order = 2, Font = fontId, Text = "角色", Action = "name.changed", Layout = UiLayout.Fixed(112, 44, 120, 32), Style = UiStyle.Default with { Fill = new(.1f, .14f, .2f, 1) } };
            d = d with { Elements = [root, label, button, input] };
            string project = Path.Combine(AppContext.BaseDirectory, "ui-canvas-work", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(project, "assets"));
            var scope = new UiWriteScope(new Dictionary<string, Guid> { ["assets/Hud.ncmaui"] = d.AssetId }, (id, kind) => id == fontId && kind == UiResourceKind.Font, () => true);
            var clock = new AssetRevisionClock(); using var commands = new UiCommands(new(project), scope, clock);
            var edit = new EditSession(new SceneDocument()); edit.RegisterCommandParticipant(UiCommands.Descriptor, commands);
            var permissions = new CapabilityPermissions([UiCommands.CapabilityName, "ncma.history.undo", "ncma.history.redo"]);
            Guid proposal = commands.PrepareProposal("assets/Hud.ncmaui", d, 0);
            var commit = edit.Invoke(new(2, Guid.NewGuid(), edit.SessionId, edit.Revision, UiCommands.CapabilityName, JsonSerializer.SerializeToElement(new { proposalId = proposal, expectedAssetRevision = 0 })), permissions);
            Check(commit.Changed);
            using var canvas = UiCanvas.Create(new(commands.Read("assets/Hud.ncmaui")), renderer, textService, new Dictionary<Guid, FontAsset> { [fontId] = font });
            var viewport = new UiViewport(256, 128, 1, default); canvas.Prepare(viewport);
            byte[] pixels = new byte[256 * 128 * 4]; canvas.Submit(1, Vector4.Zero); renderer.Capture(pixels); renderer.Present();
            Check(pixels.Where((_, i) => i % 4 == 0).Count(r => r > 200) > 50); // Actual shaped glyphs, not a placeholder label.
            ulong prepares = canvas.Preparations, shapes = textService.PrepareCalls, uploads = renderer.UiStats.UploadedBytes;
            for (ulong frame = 2; frame <= 33; frame++) { canvas.Prepare(viewport); canvas.Submit(frame, Vector4.Zero); renderer.Present(); }
            Check(canvas.Preparations == prepares && textService.PrepareCalls == shapes && renderer.UiStats.UploadedBytes == uploads);
            var stamp = canvas.Runtime.Stamp;
            canvas.Runtime.Input(stamp, [new(UiInputKind.PointerDown, 1, new(30, 60)), new(UiInputKind.PointerUp, 2, new(30, 60))]); canvas.Prepare(viewport);
            UiAction[] actions = new UiAction[128]; Check(canvas.Runtime.Drain(stamp, actions) == 1 && actions[0].Action == "attack");
            canvas.Runtime.Input(stamp, [new(UiInputKind.PointerDown, 3, new(140, 60)), new(UiInputKind.PointerUp, 4, new(140, 60)), new(UiInputKind.Text, 5, default, " A")]); canvas.Prepare(viewport);
            Check(canvas.Runtime.Widget(input.Id).Text == "角色 A"); canvas.Submit(34, Vector4.Zero); renderer.Present();
            renderer.Resize(128, 256); canvas.Prepare(new(128, 256, 1, default)); canvas.Submit(35, Vector4.Zero); renderer.Present();
            canvas.Dispose(); Check(renderer.UiStats.ResidentBytes == 0 && textModule.Status.LiveResources == 0);
            Check(renderer.Stats.ValidationErrors == 0 && renderer.Stats.ValidationWarnings == 0);
            Check(edit.Invoke(new(2, Guid.NewGuid(), edit.SessionId, edit.Revision, "ncma.history.undo", JsonSerializer.SerializeToElement(new { })), permissions).Changed);
            Check(!File.Exists(Path.Combine(project, "assets/Hud.ncmaui")));
        });
    }
}
