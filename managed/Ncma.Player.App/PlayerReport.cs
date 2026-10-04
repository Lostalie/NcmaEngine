using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Gameplay;
namespace Ncma.Player.App;

public sealed record PlayerModule(string Id, uint AbiMajor, uint AbiMinor, ulong Capabilities = 0);
public sealed record PlayerReport(int SchemaVersion, string Mode, string Renderer, Guid ProjectId, Guid SessionId,
    Guid WorldId, ulong? TargetTicks, ulong Tick, double FixedDeltaSeconds, PlayState State, int ExitCode,
    string Reason, PlayFault? Fault, string[] ShutdownErrors, string? ReportError, string? StateSha256,
    int ObjectCount, double ElapsedSeconds, PlayerModule[] Modules, ulong RenderedFrames, ulong ValidationErrors, ulong ValidationWarnings);

public static class PlayerReports
{
    public const int MaxBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }, WriteIndented = true };
    // Check before any runtime startup. CreateNew is the final no-overwrite guard, not a TOCTOU assumption.
    public static void ValidateTarget(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || File.Exists(path) || Directory.Exists(path))
            throw new ArgumentException("report_target_invalid_or_exists");
        for (var part = new DirectoryInfo(Path.GetDirectoryName(path)!); part is not null; part = part.Parent)
            if (part.Exists && (part.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("report_reparse_path");
    }
    public static void Write(string path, PlayerReport report)
    {
        ValidateTarget(path);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(report, Json);
        if (bytes.Length > MaxBytes) throw new ArgumentException("report_budget");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ValidateTarget(path);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes); file.Flush(true);
    }
}
