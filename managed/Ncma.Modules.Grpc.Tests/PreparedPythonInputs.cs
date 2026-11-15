using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

// TEST INPUT AUDIT ONLY: snapshots before/after the harness, not a continuous
// executable lease or permission. Original relocation cases must still acquire
// FileShare.None on their sources; keeping shared file pins here would defeat them.
internal sealed class PreparedPythonInputs
{
    private readonly string workspace, root, receiptHash;
    internal string EnvironmentRoot { get; }
    internal string WorkerRoot { get; }
    private PreparedPythonInputs(string workspace, string root, string hash, string worker)
    { this.workspace = workspace; this.root = root; receiptHash = hash; EnvironmentRoot = Path.Combine(root, "venv"); WorkerRoot = worker; }

    internal static PreparedPythonInputs? Extract(ref string[] args, string workspace, bool required)
    {
        const string flag = "--python-environment";
        int at = Array.IndexOf(args, flag);
        if (at < 0)
        {
            if (required) throw new InvalidDataException("python_verification_preparation_required");
            return null;
        }
        if (at == 0 || at != args.Length - 3 || Array.LastIndexOf(args, flag) != at)
            throw new ArgumentException("python_verification_arguments");
        string root = Generation(workspace, args[at + 1], "python-environments"), hash = args[at + 2];
        HashShape(hash);
        string worker = Audit(workspace, root, hash);
        var result = new PreparedPythonInputs(workspace, root, hash, worker);
        args = args[..at]; // Do not alter argv on a failed validation.
        return result;
    }

    internal void VerifyUnchanged()
    {
        if (!string.Equals(Audit(workspace, root, receiptHash), WorkerRoot, StringComparison.Ordinal))
            throw new InvalidDataException("python_verification_worker_changed");
    }

    private static string Audit(string workspace, string root, string hash)
    {
        using var receipt = ReadReceipt(root, hash);
        var r = receipt.RootElement;
        Require(r.GetProperty("kind").GetString() == "ncma.python-worker.environment.v1" &&
            r.GetProperty("root").GetString() == root && r.GetProperty("sourceRole").GetString() == "copied-inputs" &&
            r.GetProperty("environment").GetString() == Path.Combine(root, "venv"), "python_verification_receipt_binding");
        FalseFlags(r, "networkAuthorized", "workerExecuted", "testsExecuted", "canonicalEnvironmentReplaced", "installed", "accepted");
        string inputRoot = Generation(workspace, r.GetProperty("inputsRoot").GetString()!, "python-inputs");
        using var inputReceipt = ReadReceipt(inputRoot, r.GetProperty("inputsReceiptSha256").GetString()!);
        var input = inputReceipt.RootElement;
        string inputs = Path.Combine(inputRoot, "inputs"), worker = Path.Combine(inputs, "worker");
        Require(input.GetProperty("kind").GetString() == "ncma.python-worker.inputs.v1" &&
            input.GetProperty("root").GetString() == inputRoot && input.GetProperty("sourceRole").GetString() == "copied-inputs" &&
            r.GetProperty("inputsSha256").GetString() == input.GetProperty("inputsSha256").GetString() &&
            r.GetProperty("worker").GetString() == worker, "python_verification_input_binding");
        FalseFlags(input, "networkAuthorized", "interpreterExecuted", "environmentCreated", "testsExecuted", "installed", "accepted");
        AuditFiles(inputs, input.GetProperty("files"), input.GetProperty("inputsSha256").GetString()!, 536870912, false);
        AuditFiles(root, r.GetProperty("files"), r.GetProperty("filesSha256").GetString()!, 268435456, true);
        foreach (string file in new[] { "ncma_module_worker.py", "tests/retained_work_probe.py", "tests/malformed_reply_probe.py", "requirements-win-py312.lock" })
            Require(File.Exists(Path.Combine(worker, file)), "python_verification_worker_missing");
        string environment = Path.Combine(root, "venv");
        string cfg = Path.Combine(environment, "pyvenv.cfg");
        Require(File.Exists(cfg) && File.Exists(Path.Combine(environment, "Scripts/python.exe")) && new FileInfo(cfg).Length <= 16384,
            "python_verification_environment_missing");
        string executable = File.ReadLines(cfg).Single(line => line.StartsWith("executable = ", StringComparison.Ordinal))[13..];
        Require(Same(executable, Path.Combine(inputs, "runtime/python.exe")), "python_verification_base_runtime");
        return worker;
    }

    private static JsonDocument ReadReceipt(string root, string hash)
    {
        HashShape(hash); CheckPath(root);
        byte[] bytes = ReadFile(Path.Combine(root, "receipt.json"), 4194304);
        Require(Convert.ToHexString(SHA256.HashData(bytes)) == hash, "python_verification_receipt_changed");
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        try { UniqueProperties(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private static void UniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in element.EnumerateObject())
            { Require(names.Add(p.Name), "python_verification_duplicate_property"); UniqueProperties(p.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) UniqueProperties(child);
    }

    private static void FalseFlags(JsonElement value, params string[] names)
    { foreach (string name in names) Require(value.GetProperty(name).ValueKind == JsonValueKind.False, "python_verification_receipt_claim"); }

    private static void AuditFiles(string root, JsonElement list, string fingerprint, long budget, bool excludeReceipt)
    {
        HashShape(fingerprint);
        Require(list.ValueKind == JsonValueKind.Array && list.GetArrayLength() is > 0 and <= 8192, "python_verification_inventory_budget");
        var expected = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        aggregate.AppendData("ncma-source-inputs-v1\n"u8);
        string? previous = null; long total = 0;
        foreach (var row in list.EnumerateArray())
        {
            string path = row.GetProperty("path").GetString()!;
            Require(path.Length is > 0 and <= 1024 && path.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or ' ' or '/' or '-') &&
                !Path.IsPathRooted(path) && path.Split('/').All(PortablePart) &&
                (previous is null || StringComparer.Ordinal.Compare(previous, path) < 0) && expected.Add(path) && aliases.Add(path) &&
                (!excludeReceipt || path != "receipt.json"), "python_verification_inventory_path");
            previous = path;
            long size = row.GetProperty("size").GetInt64(); string hash = row.GetProperty("sha256").GetString()!; HashShape(hash);
            Require(size is >= 0 and <= 67108864 && (total += size) <= budget, "python_verification_inventory_budget");
            byte[] bytes = ReadFile(Path.Combine(root, path), 67108864);
            Require(bytes.LongLength == size && Convert.ToHexString(SHA256.HashData(bytes)) == hash, "python_verification_file_changed");
            string frame = Encoding.UTF8.GetByteCount(path).ToString(CultureInfo.InvariantCulture) + ":" + path + "\n" +
                size.ToString(CultureInfo.InvariantCulture) + ":" + hash + "\n";
            aggregate.AppendData(Encoding.UTF8.GetBytes(frame));
        }
        Require(Convert.ToHexString(aggregate.GetHashAndReset()) == fingerprint, "python_verification_inventory_hash");
        var actual = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(); pending.Enqueue(root); int directories = 0;
        while (pending.TryDequeue(out string? directory))
        {
            Require(++directories <= 4096, "python_verification_directory_budget"); CheckPath(directory);
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                CheckPath(path);
                if (Directory.Exists(path)) { pending.Enqueue(path); continue; }
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (excludeReceipt && relative == "receipt.json") continue;
                Require(actual.Count < 8192 && actual.Add(relative), "python_verification_inventory_budget");
            }
        }
        Require(expected.SetEquals(actual), "python_verification_file_set_changed");
    }

    private static byte[] ReadFile(string path, int maximum)
    {
        CheckPath(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(GetFileInformationByHandle(file.SafeFileHandle, out var info) && info.Links == 1 && (info.Attributes & 0x410) == 0 &&
            file.Length <= maximum, "python_verification_file_identity");
        var bytes = new byte[checked((int)file.Length)]; file.ReadExactly(bytes); return bytes;
    }

    internal static string Generation(string workspace, string path, string scope)
    {
        Require(Path.IsPathFullyQualified(path), "python_verification_absolute_root");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string parent = Path.GetFullPath(Path.Combine(workspace, "out/verification/m8-6", scope));
        Require(Same(Path.GetDirectoryName(root)!, parent) && Path.GetFileName(root).Length == 32 &&
            Path.GetFileName(root).All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "python_verification_generation");
        return root;
    }

    private static void CheckPath(string path)
    {
        for (string? part = Path.GetFullPath(path); part is not null; part = Path.GetDirectoryName(part))
            Require((File.GetAttributes(part) & FileAttributes.ReparsePoint) == 0, "python_verification_reparse");
    }
    private static bool Same(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    private static bool PortablePart(string value)
    {
        if (value is "" or "." or ".." || value.EndsWith('.') || value.EndsWith(' ')) return false;
        string stem = value.Split('.')[0].ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL") &&
            !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '0' and <= '9');
    }
    private static void HashShape(string value) => Require(value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F'), "python_verification_hash_shape");
    private static void Require(bool value, string code) { if (!value) throw new InvalidDataException(code); }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfoNative
    { public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfoNative info);
}
