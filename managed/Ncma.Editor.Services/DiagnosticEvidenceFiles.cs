using System.Security.Cryptography;

namespace Ncma.Editor.Services;

public sealed record DiagnosticEvidenceFileReceipt(long Bytes, string Sha256);

// Trusted local storage primitive, NOT an AI capability or schema validator. Caller validates bytes.
// Synchronous explicit export after measurement, never a frame-loop writer or automatic fallback.
public static class DiagnosticEvidenceFiles
{
    public const int MaximumBytes = 64 * 1024 * 1024;
    public static DiagnosticEvidenceFileReceipt WriteNew(string destination, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > MaximumBytes) throw new ArgumentException("diagnostic_evidence_byte_budget");
        ValidateNewDestination(destination);
        destination = WorkflowPhysicalPins.Absolute(destination);
        // Own the exact bytes through hash, write and same-handle readback. No caller-owned mutable array.
        byte[] owned = bytes.ToArray(); string hash = Convert.ToHexString(SHA256.HashData(owned));
        using var pins = new WorkflowPhysicalPins();
        pins.Directory(Path.GetDirectoryName(destination)!);
        using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)) {
            WorkflowPhysicalPins.ValidateHandle(file.SafeFileHandle, false);
            file.Write(owned); file.Flush(true); file.Position = 0;
            if (file.Length != owned.Length || Convert.ToHexString(SHA256.HashData(file)) != hash)
                throw new IOException("diagnostic_evidence_readback");
        }
        return new(owned.Length, hash);
    }
    public static void ValidateNewDestination(string destination)
    {
        destination = WorkflowPhysicalPins.Absolute(destination);
        string name = Path.GetFileName(destination);
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (!name.EndsWith(".ncobserve", StringComparison.Ordinal) || name.Length > 96 || name.StartsWith('.') ||
            name.Contains("..", StringComparison.Ordinal) || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_') ||
            stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && stem[3] is >= '1' and <= '9' && (stem.StartsWith("COM") || stem.StartsWith("LPT")))
            throw new ArgumentException("diagnostic_evidence_destination");
        using var pins = new WorkflowPhysicalPins();
        pins.Directory(Path.GetDirectoryName(destination)!); // Pins every existing ancestor; no mkdir/reparse/delete sharing.
        // Check point only: WriteNew repeats these guards and CreateNew remains the race-safe no-overwrite gate.
        if(File.Exists(destination)||Directory.Exists(destination)) throw new IOException("diagnostic_evidence_destination_exists");
    }
}
