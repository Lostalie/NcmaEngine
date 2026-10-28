using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Ncma.Extensions.Hosting;

// Windows review capture plus explicit new-file export of retained bytes.
// No directory discovery, source overwrite, approval or build execution.
// Existing parent directories only; explicit absent leaves support add/delete.
// Missing leaves cannot be locked: this read review is NEVER a later write lease.
public sealed class ExtensionSourceCapture
{
    public const int MaxRootLength = 512, MaxDirectories = 256, MaxDepth = 32;
    public string SourceRoot { get; }
    public string CandidateRoot { get; }
    public ExtensionSourcePatch Patch { get; }
    public string CandidateFingerprint { get; }
    private readonly string[] paths;
    private readonly string sourceIdentity, candidateIdentity;
    private ExtensionSourceCapture(string source, string candidate, string[] paths, ExtensionSourcePatch patch, Pair pair)
    {
        SourceRoot = source; CandidateRoot = candidate; this.paths = paths; Patch = patch;
        CandidateFingerprint = pair.CandidateHash; sourceIdentity = pair.SourceIdentity; candidateIdentity = pair.CandidateIdentity;
    }

    public static ExtensionSourceCapture Capture(ExtensionDescriptor extension, string sourceRoot, string candidateRoot, params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(extension); ArgumentNullException.ThrowIfNull(paths);
        if (paths.Length is < 1 or > ExtensionSourcePatch.MaxFiles) Fail("source_capture_scope");
        var copy = (string[])paths.Clone();
        // Reuse the original logical path, duplicate and overlap validation BEFORE IO.
        _ = ExtensionSourcePatch.Create(extension, new(Guid.NewGuid(), 1, new string('0', 64)), copy.Select(p =>
            new ExtensionSourceChange(ExtensionSourceFile.Absent(p), ExtensionSourceFile.Present(p, []))).ToArray());
        Array.Sort(copy, StringComparer.Ordinal);
        string source = Root(sourceRoot), candidate = Root(candidateRoot);
        if (string.Equals(source, candidate, StringComparison.OrdinalIgnoreCase)) Fail("source_roots_same");
        var pair = ReadPair(source, candidate, copy);
        var changes = pair.Before.Zip(pair.After, (before, after) => new ExtensionSourceChange(before, after)).ToArray();
        var patch = ExtensionSourcePatch.Create(extension, new(Guid.NewGuid(), 1, pair.SourceHash), changes);
        return new(source, candidate, copy, patch, pair);
    }

    // Explicit user action only. No polling/IO in a per-frame IsCurrent call.
    // Handles are released on return: success is a point-in-time observation,
    // not an authorization or a guarantee that a later writer may use it.
    public void RequireCurrent()
    {
        var pair = ReadPair(SourceRoot, CandidateRoot, paths, sourceIdentity, candidateIdentity);
        Patch.RequireCurrent(new(Patch.Source.WorkspaceSession, Patch.Source.Revision, pair.SourceHash), pair.Before);
        if (pair.CandidateHash != CandidateFingerprint) Fail("source_candidate_stale");
        for (int i = 0; i < pair.After.Length; i++)
            if (!pair.After[i].CopyBytes().AsSpan().SequenceEqual(Patch.Changes[i].After.CopyBytes())) Fail("source_candidate_stale");
    }

    // The compilation handoff is made ONLY from the retained immutable bytes.
    // No RequireCurrent/read of either source directory occurs here. Disk may
    // change after capture without changing this build input. Not an approval.
    public byte[] CopyBuildBundle() => JsonSerializer.SerializeToUtf8Bytes(new {
        schemaVersion = 1, kind = "ncma.extension.captured-sources.v1",
        patchFingerprint = Patch.Fingerprint, originFingerprint = Patch.Source.InputSha256,
        candidateOriginFingerprint = CandidateFingerprint,
        files = Patch.Changes.Select(c => new {
            path = c.Path, before = c.Before.Exists ? c.Before.CopyBytes() : null,
            after = c.After.Exists ? c.After.CopyBytes() : null
        }).ToArray()
    });

    // Explicit trusted local export only; a fresh file, never overwrite source.
    // Parent identity is pinned with the same no-reparse/no-delete boundary.
    public string ExportBuildBundle(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination) || !destination.EndsWith(".nccapture", StringComparison.Ordinal))
            Fail("source_bundle_destination");
        string directory = Root(Path.GetDirectoryName(destination) ?? "");
        string name = Path.GetFileName(destination);
        _ = ExtensionSourceFile.Absent(name + ".cs"); // reuse portable/device-name rejection
        if (name.Length > 96 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_') ||
            name.StartsWith('.') || name.Contains("..", StringComparison.Ordinal)) Fail("source_bundle_destination");
        byte[] bytes = CopyBuildBundle();
        using var pins = new Pins();
        pins.PinParents(directory);
        using var file = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes); file.Flush(true);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private sealed record Pair(ExtensionSourceFile[] Before, ExtensionSourceFile[] After, string SourceHash, string CandidateHash, string SourceIdentity, string CandidateIdentity);
    private static Pair ReadPair(string source, string candidate, string[] paths, string? expectedSource = null, string? expectedCandidate = null)
    {
        if (!OperatingSystem.IsWindows()) Fail("source_windows_required");
        try {
            using var pins = new Pins();
            var left = paths.Select(p => pins.File(source, p)).ToArray();
            var right = paths.Select(p => pins.File(candidate, p)).ToArray();
            string sourceIdentity = pins.Digest(source, left, null), candidateIdentity = pins.Digest(candidate, right, null);
            // On explicit recheck reject directory/file replacement BEFORE reading
            // contents from the newly opened locations, even for identical bytes.
            if (expectedSource is not null && sourceIdentity != expectedSource || expectedCandidate is not null && candidateIdentity != expectedCandidate)
                Fail("source_identity_stale");
            if (left.Concat(right).Sum(p => (long)p.Info.SizeLow) > ExtensionSourcePatch.MaxTotalBytes) Fail("source_patch_budget");
            // Both existing-file sets are locked before content reads. Absence
            // is boundedly checked before/after, never represented as a lock.
            var before = left.Select((p, i) => pins.Read(p, paths[i])).ToArray();
            var after = right.Select((p, i) => pins.Read(p, paths[i])).ToArray();
            foreach (var pin in left.Concat(right)) if (!pin.Exists) pins.CheckMissing(pin);
            return new(before, after, pins.Digest(source, left, before), pins.Digest(candidate, right, after), sourceIdentity, candidateIdentity);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) {
            // Do not expose file contents, absolute paths or arbitrary exception messages.
            throw new ExtensionRejectedException("source_capture_io");
        }
    }
    private static string Root(string path)
    {
        if (!OperatingSystem.IsWindows()) Fail("source_windows_required");
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxRootLength || path.Length < 4 ||
            !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\' || path.Contains('/')) Fail("source_root_invalid");
        string trimmed = Path.TrimEndingDirectorySeparator(path);
        foreach (string part in trimmed[3..].Split('\\'))
            if (part.Length == 0 || part is "." or ".." || part[^1] is '.' or ' ' || part.Any(c => char.IsControl(c) || c is ':' or '*' or '?' or '"' or '<' or '>' or '|'))
                Fail("source_root_invalid");
        if (trimmed[3..].Split('\\').Length > MaxDepth || !Path.IsPathFullyQualified(trimmed)) Fail("source_root_invalid");
        string full = Path.GetFullPath(trimmed);
        if (GetDriveTypeW(Path.GetPathRoot(full)!) != 3) Fail("source_local_fixed_drive_required");
        return full;
    }
    private static void Fail(string code) => throw new ExtensionRejectedException(code);

    private sealed class Pins : IDisposable
    {
        private readonly Dictionary<string, Pin> directories = new(StringComparer.Ordinal);
        private readonly List<Pin> files = [];
        internal sealed record Pin(SafeFileHandle Handle, Info Info, string Path, bool Exists = true);
        private Pin Directory(string path)
        {
            if (directories.TryGetValue(path, out var existing)) return existing;
            if (directories.Count >= MaxDirectories) { Fail("source_directory_budget"); }
            var pin = Open(path, true); directories.Add(path, pin); return pin;
        }
        internal Pin File(string root, string relative)
        {
            PinParents(root);
            string current = root; string[] parts = relative.Split('/');
            if (parts.Length > MaxDepth) Fail("source_directory_depth");
            for (int i = 0; i < parts.Length - 1; i++) {
                current = Path.Combine(current, parts[i]); var parent = Directory(current);
                if (Path.GetFileName(parent.Path) != parts[i]) Fail("source_path_casing");
            }
            var file = Open(Path.Combine(Directory(current).Path, parts[^1]), false, allowAbsent:true); files.Add(file);
            if (Path.GetFileName(file.Path) != parts[^1]) Fail("source_path_casing");
            if (!file.Exists) CheckMissing(file);
            return file;
        }
        internal void PinParents(string root)
        {
            var ancestors = new Stack<string>();
            for (DirectoryInfo? d = new(root); d is not null; d = d.Parent) ancestors.Push(d.FullName);
            if (ancestors.Count > MaxDepth + 1) Fail("source_directory_depth");
            // Always pin from drive root outward, rejecting each reparse point
            // before opening a descendant. Handles deny directory deletion.
            foreach (string path in ancestors) Directory(path);
        }
        internal void CheckMissing(Pin pin)
        {
            // Enumerate names only, under pinned parent identities; never read
            // out-of-scope contents. Reject case aliases on case-sensitive NTFS.
            var names = System.IO.Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(pin.Path)!).Take(4097).ToArray();
            if (names.Length > 4096) Fail("source_absence_directory_budget");
            if (names.Any(p => string.Equals(Path.GetFileName(p),Path.GetFileName(pin.Path),StringComparison.OrdinalIgnoreCase)))
                Fail("source_capture_changed");
        }
        internal ExtensionSourceFile Read(Pin pin, string relative)
        {
            if (!pin.Exists) { CheckMissing(pin); return ExtensionSourceFile.Absent(relative); }
            var bytes = new byte[(int)pin.Info.SizeLow]; int offset = 0;
            while (offset < bytes.Length) {
                int count = RandomAccess.Read(pin.Handle, bytes.AsSpan(offset), offset);
                if (count == 0) Fail("source_capture_changed");
                offset += count;
            }
            if (!GetFileInformationByHandle(pin.Handle, out var after) || pin.Info.Identity != after.Identity ||
                pin.Info.SizeHigh != after.SizeHigh || pin.Info.SizeLow != after.SizeLow ||
                pin.Info.WriteHigh != after.WriteHigh || pin.Info.WriteLow != after.WriteLow || after.Links != 1 || (after.Attributes & 0x410) != 0)
                Fail("source_capture_changed");
            return ExtensionSourceFile.Present(relative, bytes);
        }
        internal string Digest(string root, Pin[] set, ExtensionSourceFile[]? content)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            void Text(string value) {
                byte[] bytes = Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length); hash.AppendData(length); hash.AppendData(bytes);
            }
            Text(content is null ? "ncma.extension.selected-source-identities.v1" : "ncma.extension.selected-source-capture.v1"); Text(root);
            // Only directories belonging to this set, not unrelated candidate trees.
            var required = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var file in set)
                for (DirectoryInfo? d = new(Path.GetDirectoryName(file.Path)!); d is not null; d = d.Parent) required.Add(d.FullName);
            Text(required.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (string path in required) {
                // Native final-path casing may differ in the user-selected root.
                var pin = directories.Values.First(p => string.Equals(p.Path, path, StringComparison.Ordinal));
                Text(pin.Path); Text(pin.Info.Identity);
            }
            Text(set.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (int i = 0; i < set.Length; i++) { Text(set[i].Path); Text(set[i].Exists ? set[i].Info.Identity : "absent"); if (content is not null) Text(content[i].Sha256??"absent"); }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        private static Pin Open(string path, bool directory, bool allowAbsent = false)
        {
            var handle = CreateFileW("\\\\?\\" + path, directory ? 0u : 0x80000000u, directory ? 3u : 1u,
                0, 3, 0x00200000u | (directory ? 0x02000000u : 0), 0);
            try {
                if (handle.IsInvalid) {
                    int error=Marshal.GetLastWin32Error();
                    if (allowAbsent && !directory && error==2) return new(handle,default,path,false);
                    Fail("source_open_failed");
                }
                if (GetFileType(handle) != 1 || !GetFileInformationByHandle(handle, out var info) ||
                    (info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory || !directory && info.Links != 1)
                    { Fail("source_link_or_type"); throw new InvalidOperationException(); }
                if (!directory && (info.SizeHigh != 0 || info.SizeLow > ExtensionSourceFile.MaxBytes)) Fail("source_file_budget");
                var name = new StringBuilder(1024); uint size = GetFinalPathNameByHandleW(handle, name, (uint)name.Capacity, 0);
                if (size == 0 || size >= name.Capacity || !name.ToString().StartsWith("\\\\?\\", StringComparison.Ordinal)) Fail("source_identity_unavailable");
                string final = name.ToString()[4..];
                if (!string.Equals(final, path, StringComparison.OrdinalIgnoreCase)) Fail("source_path_alias");
                return new(handle, info, final);
            } catch { handle.Dispose(); throw; }
        }
        public void Dispose()
        {
            for (int i = files.Count - 1; i >= 0; i--) files[i].Handle.Dispose(); files.Clear();
            foreach (var pin in directories.Values.Reverse()) pin.Handle.Dispose(); directories.Clear();
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Info
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        internal readonly string Identity => $"{Volume:X8}:{IndexHigh:X8}{IndexLow:X8}:{CreationHigh:X8}{CreationLow:X8}";
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out Info info);
    [DllImport("kernel32.dll")] private static extern uint GetFileType(SafeFileHandle file);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetDriveTypeW(string root);
}
