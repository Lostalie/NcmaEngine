using System.Globalization;
namespace Ncma.Player.App;

public sealed record PlayerOptions(string? Project, bool Headless, string? Renderer, ulong? Ticks,
    double FixedDelta, double MaxRuntimeSeconds, string Report, bool Help, bool Version)
{
    public static PlayerOptions Parse(string[] args, string? defaultReportRoot = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length > 32) throw new ArgumentException("argument_budget");
        string? project = null, renderer = null, report = null; bool headless = false, help = false, version = false;
        ulong? ticks = null; double delta = 1d / 60, seconds = 120;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i];
            if (!seen.Add(option)) throw new ArgumentException("duplicate_argument");
            string Value()
            {
                if (++i >= args.Length || args[i].Length is 0 or > 4096 || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("missing_argument_value");
                return args[i];
            }
            double Number()
            {
                if (!double.TryParse(Value(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || !double.IsFinite(n))
                    throw new ArgumentException("invalid_number");
                return n;
            }
            switch (option)
            {
                case "--project": project = Path.GetFullPath(Value()); break;
                case "--headless": headless = true; break;
                case "--renderer": renderer = Value(); if (renderer is not ("d3d11" or "null" or "vulkan")) throw new ArgumentException("invalid_renderer"); break;
                case "--ticks": if (!ulong.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong n) || n is 0 or > 1_000_000)
                        throw new ArgumentException("invalid_ticks"); ticks = n; break;
                case "--fixed-delta": delta = Number(); if (delta is < 1d / 240 or > .1) throw new ArgumentException("invalid_fixed_delta"); break;
                case "--max-runtime-seconds": seconds = Number(); if (seconds is < 1 or > 3600) throw new ArgumentException("invalid_time_budget"); break;
                case "--report": report = Path.GetFullPath(Value()); break;
                case "--help": help = true; break;
                case "--version": version = true; break;
                default: throw new ArgumentException("unknown_argument");
            }
        }
        if ((help || version) && args.Length != 1) throw new ArgumentException("information_argument_conflict");
        if (!help && !version && (project is null || headless && (ticks is null || renderer is "d3d11" or "vulkan")))
            throw new ArgumentException("mode_or_project_required");
        report ??= Path.Combine(defaultReportRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NcmaEngine", "player"), "player-" + Guid.NewGuid().ToString("N") + ".json");
        report = Path.GetFullPath(report);
        if (!report.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("report_extension");
        return new(project, headless, renderer, ticks, delta, seconds, report, help, version);
    }
    public void ValidateRun()
    {
        if (Help || Version || Project is null || !Path.IsPathFullyQualified(Project) || !Path.IsPathFullyQualified(Report) ||
            Renderer is not (null or "d3d11" or "null" or "vulkan") || Ticks is 0 or > 1_000_000 ||
            Headless && (Ticks is null || Renderer is "d3d11" or "vulkan") ||
            !double.IsFinite(FixedDelta) || FixedDelta is < 1d / 240 or > .1 ||
            !double.IsFinite(MaxRuntimeSeconds) || MaxRuntimeSeconds is < 1 or > 3600)
            throw new ArgumentException("invalid_run_options");
    }
    public string EffectiveRenderer(Ncma.Application.ProjectContext project) => Renderer ?? (Headless ? "null" : project.Configuration.Renderer switch
    { "Direct3D11" => "d3d11", "Vulkan" => "vulkan", "Null" => "null", _ => throw new ArgumentException("invalid_project_renderer") });
}
