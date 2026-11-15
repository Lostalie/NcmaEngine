using System.Security.Cryptography;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;
using Ncma.Modules;

internal static class ModuleObservationFileTests
{
    private static void Check(bool condition) { if (!condition) throw new Exception("Observation export assertion"); }
    private static void Reject(Action action) {
        try { action(); } catch (Exception e) when (e is ArgumentException or IOException or InvalidOperationException) { return; }
        throw new Exception("Observation export must reject");
    }
    private static string Root() {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Observation export requires Windows, not skipped");
        string path = Path.Combine(Path.GetTempPath(), "ncma-observation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path; // Retain evidence; never delete or overwrite old fixtures.
    }
    private static ModuleObservationReport Report() => new ModuleObservationCapture(new(Guid.NewGuid(), new('A', 64), new('B', 64),
        ObservationConfiguration.Debug, ObservationVariant.Ai, 1000), 0).Seal(1);
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("M8.7-E2B2 export canonical bytes hash and existing target preserved", () => {
            string path = Path.Combine(Root(), "report.ncobserve"); var report = Report();
            byte[] expected = ModuleObservationEvidence.Encode(report); var receipt = ModuleObservationFiles.ExportNew(path, report);
            Check(receipt.Bytes == expected.Length && receipt.Sha256 == Convert.ToHexString(SHA256.HashData(expected)));
            byte[] actual = File.ReadAllBytes(path); Check(actual.SequenceEqual(expected) && ModuleObservationEvidence.Decode(actual).EvidenceSha256 == report.EvidenceSha256);
            Reject(() => ModuleObservationFiles.ExportNew(path, Report())); Check(File.ReadAllBytes(path).SequenceEqual(expected));
        });
        yield return ("M8.7-E2B2 export rejects tampering and byte limit before file creation", () => {
            string path = Path.Combine(Root(), "invalid.ncobserve"); var report = Report();
            Reject(() => ModuleObservationFiles.ExportNew(path, report with { EvidenceSha256 = new('0', 64) }));
            Check(!File.Exists(path)); Reject(() => ModuleObservationFiles.ExportNew(path, report, 1)); Check(!File.Exists(path));
            Reject(() => DiagnosticEvidenceFiles.WriteNew(path, ReadOnlySpan<byte>.Empty)); Check(!File.Exists(path));
        });
        yield return ("M8.7-E2B2 export rejects path aliases devices and absent parents without mkdir", () => {
            string root = Root(); var report = Report();
            foreach (string name in new[] { "CON.ncobserve", "com1.ncobserve", ".ncobserve", "a..b.ncobserve", "file.json", "x.ncobserve:stream", "a b.ncobserve" })
                Reject(() => ModuleObservationFiles.ExportNew(Path.Combine(root, name), report));
            Reject(() => ModuleObservationFiles.ExportNew("relative.ncobserve", report));
            Reject(() => ModuleObservationFiles.ExportNew(root + "\\..\\escape.ncobserve", report));
            string absent = Path.Combine(root, "missing"); Reject(() => ModuleObservationFiles.ExportNew(Path.Combine(absent, "x.ncobserve"), report));
            Check(!Directory.Exists(absent) && Directory.GetFileSystemEntries(root).Length == 0);
        });
        yield return ("M8.7-E2B2 export directory collision preserves contents and never falls back", () => {
            string root = Root(), target = Path.Combine(root, "occupied.ncobserve"); Directory.CreateDirectory(target);
            string marker = Path.Combine(target, "marker.txt"); File.WriteAllText(marker, "keep");
            Reject(() => ModuleObservationFiles.ExportNew(target, Report()));
            Check(File.ReadAllText(marker) == "keep" && Directory.GetFileSystemEntries(root).Length == 1);
        });
    }
}
