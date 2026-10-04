using System.Diagnostics;
using System.Security.Cryptography;
using Ncma.Application;
using Ncma.Application.Runtime;
using Ncma.Gameplay;
using Ncma.Interop;
using Ncma.Physics;
using Ncma.Rendering;
using Ncma.Scene;
namespace Ncma.Player.App;

public static class PlayerRunner
{
    // Trusted local host only. No EditorEndpoint, hidden EditSession or Agent execution channel.
    public static PlayerReport Run(PlayerOptions options, Func<bool>? cancelled = null, string? pluginRoot = null,
        bool visible = true, Action<PlaySession, ulong>? trustedInput = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var clock = Stopwatch.StartNew(); ProjectContext? project = null; RuntimeSessionOwner? owner = null;
        PhysicsService? physics = null; PlayerPresentation? presentation = null; PlayStatus? status = null;
        int exit = 0, count = 0; string reason = "completed", renderer = options.Headless ? "null" : "unknown";
        string? digest = null; string phase = "configuration"; var errors = new List<string>(); PlayerModule[] modules = [];
        ulong rendered = 0, validationErrors = 0, validationWarnings = 0; bool running = false;
        try
        {
            options.ValidateRun(); PlayerReports.ValidateTarget(options.Report);
            project = ProjectContext.Load(options.Project ?? throw new ArgumentException("project_required"));
            renderer = options.EffectiveRenderer(project);
            if (renderer == "vulkan" || !options.Headless && renderer == "null") { exit = 7; reason = "renderer_unimplemented"; }
            else
            {
                if (project.Configuration.Plugins.Length != 0) throw new NotSupportedException("project_plugin_overrides_unimplemented");
                var document = new SceneDocument(project.Configuration.Name, RenderConfiguration.CreateRegistry());
                SceneDocumentFiles.Load(document, project.StartupScenePath);
                owner = new(document); owner.LoadGameplay(project.GameplayAssemblyPath);
                string plugins = Path.GetFullPath(pluginRoot ?? Path.Combine(AppContext.BaseDirectory, "plugins"));
                phase = "dependencies"; physics = new(plugins, project.Configuration.PhysicsEnabled);
                if (physics.Inspect() is { Enabled: true } inspected) modules = [new("ncma.physics", inspected.AbiMajor, inspected.AbiMinor, (ulong)inspected.Capabilities)];
                if (!options.Headless)
                {
                    presentation = new(plugins, visible); presentation.Start();
                    modules = modules.Concat(presentation.Modules).ToArray();
                }
                phase = "initialize"; var play = owner.StartPlay(factory: d => new PlaySession(d,
                    options.Headless ? FrameTimePolicy.Strict : FrameTimePolicy.Interactive, options.FixedDelta,
                    advanceMode: options.Headless ? PlayAdvanceMode.FixedSteps : PlayAdvanceMode.Frames));
                running = true; double previous = clock.Elapsed.TotalSeconds;
                while (options.Ticks is null || play.Tick < options.Ticks)
                {
                    if (cancelled?.Invoke() == true) { exit = 130; reason = "cancelled"; break; }
                    if (clock.Elapsed.TotalSeconds >= options.MaxRuntimeSeconds) { exit = 8; reason = "time_budget"; break; }
                    if (presentation is not null && !presentation.Pump(play)) { reason = "window_closed"; break; }
                    trustedInput?.Invoke(play, play.Tick + 1);
                    if (options.Headless) status = play.AdvanceFixedStep();
                    else
                    {
                        double now = clock.Elapsed.TotalSeconds;
                        status = play.AdvanceFrame(now - previous); previous = now;
                        presentation!.Present(play);
                    }
                    if (status.State == PlayState.Faulted) { exit = 5; reason = "gameplay_fault"; break; }
                }
                status = play.Status; count = document.CaptureSnapshot().Objects.Length;
                digest = Convert.ToHexString(SHA256.HashData(document.CaptureBytes()));
                if (presentation is not null)
                {
                    var stats = presentation.Stats; rendered = stats.SubmittedFrames;
                    validationErrors = stats.ValidationErrors; validationWarnings = stats.ValidationWarnings;
                    if (validationErrors != 0 || validationWarnings != 0) { exit = 5; reason = "graphics_validation_failed"; }
                }
            }
        }
        catch (Exception e)
        {
            status = owner?.Play?.Status ?? owner?.LastStartFailure ?? status;
            exit = status?.State == PlayState.Faulted || running ? 5 : e switch
            { FileNotFoundException or BadImageFormatException or DllNotFoundException or PluginException => 3,
                NotSupportedException => 7, ArgumentException when phase == "dependencies" => 3,
                ArgumentException or System.Text.Json.JsonException => 2, _ => 4 };
            reason = exit switch { 2 => "invalid_configuration", 3 => "dependency_failed", 5 => "gameplay_fault", 7 => "feature_unimplemented", _ => "initialize_failed" };
        }
        finally
        {
            void Close(IDisposable? service, string id) { if (service is null) return; try { service.Dispose(); } catch { errors.Add(id); } }
            Close(owner, "runtime_shutdown_failed"); Close(physics, "physics_shutdown_failed"); Close(presentation, "presentation_shutdown_failed");
        }
        if (exit == 0 && errors.Count != 0) { exit = 6; reason = "shutdown_failed"; }
        var fault = status?.Fault is { } f ? f with { Message = "Gameplay callback failed." } : null;
        var report = new PlayerReport(1, options.Headless ? "headless" : "graphical", renderer, project?.Configuration.ProjectId ?? Guid.Empty,
            status?.SessionId ?? Guid.Empty, status?.WorldId ?? Guid.Empty, options.Ticks, status?.Tick ?? 0, options.FixedDelta,
            status?.State ?? PlayState.Stopped, exit, reason, fault, errors.ToArray(), null, digest, count,
            clock.Elapsed.TotalSeconds, modules, rendered, validationErrors, validationWarnings);
        try { PlayerReports.Write(options.Report, report); }
        catch { report = report with { ReportError = "report_write_failed", ExitCode = exit == 0 ? 9 : exit,
            Reason = exit == 0 ? "report_write_failed" : reason }; }
        return report;
    }
}
