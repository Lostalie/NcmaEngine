using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace Ncma.Extensions;

// Host-supplied identity of the complete DECLARED review input capture, not a signature
// or proof that the caller read a disk tree. The future filesystem adapter must
// produce and revalidate this capture; this model never reads or writes files.
// A selected-source capture is not a full project/build closure or build receipt.
public sealed record ExtensionSourceStamp
{
    public Guid WorkspaceSession { get; }
    public ulong Revision { get; }
    public string InputSha256 { get; }
    public ExtensionSourceStamp(Guid workspaceSession, ulong revision, string inputSha256)
    {
        if (workspaceSession == Guid.Empty || inputSha256 is null || inputSha256.Length != 64 ||
            inputSha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
            throw new ExtensionRejectedException("source_stamp_invalid");
        WorkspaceSession = workspaceSession; Revision = revision; InputSha256 = inputSha256;
    }
}

// Absence is distinct from a present, zero-byte file. Bytes, UTF-8 BOM and line
// endings are never normalized. Returned buffers are detached copies.
public sealed class ExtensionSourceFile
{
    public const int MaxBytes = 64 * 1024, MaxPathLength = 240;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly byte[] bytes;
    public string Path { get; }
    public bool Exists { get; }
    public int ByteCount => bytes.Length;
    public string? Sha256 { get; }
    public string Text { get; }
    public bool HasUtf8Bom { get; }
    public int CrLfCount { get; }
    public int LineFeedCount { get; }
    public int CarriageReturnCount { get; }
    private ExtensionSourceFile(string path, bool exists, ReadOnlySpan<byte> input)
    {
        ValidatePath(path);
        if (input.Length > MaxBytes) throw new ExtensionRejectedException("source_file_budget");
        Path = path; Exists = exists; bytes = input.ToArray();
        try { Text = Utf8.GetString(bytes); }
        catch (DecoderFallbackException) { throw new ExtensionRejectedException("source_utf8_invalid"); }
        // NUL and non-source controls are ambiguous in presentation. TAB/CR/LF,
        // Unicode and BOM are retained, not silently rewritten or redacted.
        if (Text.Any(c => char.IsControl(c) && c is not '\t' and not '\r' and not '\n'))
            throw new ExtensionRejectedException("source_text_control");
        HasUtf8Bom = Text.StartsWith('\uFEFF');
        for (int i = 0; i < Text.Length; i++) {
            if (Text[i] == '\r') { if (i + 1 < Text.Length && Text[i + 1] == '\n') { CrLfCount++; i++; } else CarriageReturnCount++; }
            else if (Text[i] == '\n') LineFeedCount++;
        }
        Sha256 = exists ? Convert.ToHexString(SHA256.HashData(bytes)) : null;
    }
    public static ExtensionSourceFile Present(string path, ReadOnlySpan<byte> bytes) => new(path, true, bytes);
    public static ExtensionSourceFile Absent(string path) => new(path, false, []);
    public byte[] CopyBytes() => (byte[])bytes.Clone();
    internal bool SameContent(ExtensionSourceFile other) => Exists == other.Exists && bytes.AsSpan().SequenceEqual(other.bytes);
    internal ReadOnlySpan<byte> Bytes => bytes;

    // Portable, unambiguous *logical* C# source paths only. This is deliberately
    // not filesystem containment: symlinks, junctions, hardlinks and races still
    // require a real host boundary before any IO is introduced.
    private static void ValidatePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > MaxPathLength || !path.EndsWith(".cs", StringComparison.Ordinal))
            throw new ExtensionRejectedException("source_path_invalid");
        foreach (char c in path)
            if (c is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9')
                and not '/' and not '.' and not '_' and not '-')
                throw new ExtensionRejectedException("source_path_invalid");
        foreach (string part in path.Split('/')) {
            if (part.Length == 0 || part[0] == '.' || part[^1] == '.' || part.Contains("..", StringComparison.Ordinal))
                throw new ExtensionRejectedException("source_path_invalid");
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
                stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '0' and <= '9')
                throw new ExtensionRejectedException("source_path_invalid");
            if (part.Equals("bin", StringComparison.OrdinalIgnoreCase) || part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("out", StringComparison.OrdinalIgnoreCase))
                throw new ExtensionRejectedException("source_generated_path");
        }
    }
}

public enum ExtensionSourceChangeKind { Add, Replace, Delete }

public sealed class ExtensionSourceChange
{
    public ExtensionSourceFile Before { get; }
    public ExtensionSourceFile After { get; }
    public string Path => Before.Path;
    public ExtensionSourceChangeKind Kind { get; }
    public ExtensionSourceChange(ExtensionSourceFile before, ExtensionSourceFile after)
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(after);
        if (before.Path != after.Path) throw new ExtensionRejectedException("source_path_mismatch");
        if (before.SameContent(after)) throw new ExtensionRejectedException("source_change_empty");
        Before = before; After = after;
        Kind = !before.Exists ? ExtensionSourceChangeKind.Add : !after.Exists ? ExtensionSourceChangeKind.Delete : ExtensionSourceChangeKind.Replace;
    }
}

// Complete before/after review data only. No approve/apply/install operation,
// approval flag, command payload, executable callbacks or workflow capability.
public sealed class ExtensionSourcePatch
{
    public const int FormatVersion = 1, MaxFiles = 16, MaxTotalBytes = 256 * 1024;
    public ExtensionDescriptor Extension { get; }
    public ExtensionSourceStamp Source { get; }
    public ReadOnlyCollection<ExtensionSourceChange> Changes { get; }
    public int TotalBytes { get; }
    public string Fingerprint { get; }
    private ExtensionSourcePatch(ExtensionDescriptor extension, ExtensionSourceStamp source, ExtensionSourceChange[] changes, int total)
    {
        Extension = extension; Source = source; Changes = Array.AsReadOnly(changes); TotalBytes = total;
        Fingerprint = Digest();
    }
    public static ExtensionSourcePatch Create(ExtensionDescriptor extension, ExtensionSourceStamp source, params ExtensionSourceChange[] changes)
    {
        ArgumentNullException.ThrowIfNull(extension); ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(changes);
        if (changes.Length is < 1 or > MaxFiles) throw new ExtensionRejectedException("source_patch_count");
        var copy = (ExtensionSourceChange[])changes.Clone();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); int total = 0;
        foreach (var change in copy) {
            if (change is null) throw new ExtensionRejectedException("source_change_null");
            if (!paths.Add(change.Path)) throw new ExtensionRejectedException("source_path_duplicate");
            total += change.Before.ByteCount + change.After.ByteCount;
            if (total > MaxTotalBytes) throw new ExtensionRejectedException("source_patch_budget");
        }
        Array.Sort(copy, (left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        // A.cs and A.cs/B.cs cannot both denote ordinary files even on a
        // case-sensitive host. Reject prefix collisions before any future IO.
        foreach (var left in copy)
            foreach (var right in copy)
                if (!ReferenceEquals(left, right) && right.Path.StartsWith(left.Path + "/", StringComparison.OrdinalIgnoreCase))
                    throw new ExtensionRejectedException("source_path_overlap");
        return new(extension, source, copy, total);
    }

    // Comparison against a caller-supplied fresh capture, not a disk freshness
    // guarantee. Exact input set required, including explicit absent add targets.
    // This does not consume or authorize the proposal, or permit TOCTOU writes.
    public void RequireCurrent(ExtensionSourceStamp source, params ExtensionSourceFile[] files)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(files);
        if (source != Source) throw new ExtensionRejectedException("source_stamp_stale");
        if (files.Length != Changes.Count) throw new ExtensionRejectedException("source_capture_scope");
        var current = new Dictionary<string, ExtensionSourceFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            if (file is null || !current.TryAdd(file.Path, file)) throw new ExtensionRejectedException("source_capture_scope");
        }
        foreach (var change in Changes) {
            if (!current.TryGetValue(change.Path, out var file) || file.Path != change.Path)
                throw new ExtensionRejectedException("source_capture_scope");
            if (!change.Before.SameContent(file)) throw new ExtensionRejectedException("source_content_stale");
        }
    }

    private string Digest()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Number(ulong n) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(bytes, n); hash.AppendData(bytes); }
        void Bytes(ReadOnlySpan<byte> bytes) { Number((ulong)bytes.Length); hash.AppendData(bytes); }
        void Text(string text) => Bytes(Encoding.UTF8.GetBytes(text));
        void Version(ExtensionVersion v) { Number((ulong)v.Major); Number((ulong)v.Minor); Number((ulong)v.Patch); }
        void File(ExtensionSourceFile file) { Number(file.Exists ? 1UL : 0UL); Bytes(file.Bytes); }
        Text("ncma.extension.source-patch"); Number(FormatVersion);
        Text(Extension.Id); Version(Extension.Version); Version(Extension.MinimumHostApi);
        Number((ulong)Extension.Dependencies.Count);
        foreach (var d in Extension.Dependencies) { Text(d.Id); Version(d.Minimum); Version(d.Maximum); }
        Text(Source.WorkspaceSession.ToString("D")); Number(Source.Revision); Text(Source.InputSha256);
        Number((ulong)Changes.Count);
        foreach (var change in Changes) { Text(change.Path); File(change.Before); File(change.After); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
