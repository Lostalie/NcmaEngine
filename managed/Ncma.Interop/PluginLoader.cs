using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace Ncma.Interop;

// Supplied by a trusted project/publish bootstrap, never by an Agent request.
public sealed record PluginSpecification(string Id, ModuleKind Kind, string Path, string FileName,
    uint Major, uint Minor, string[] Dependencies, string? Sha256 = null);
public sealed class PluginLoader : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly List<PluginModule> _modules = [];
    private bool _disposed, _busy;
    public IReadOnlyList<PluginModule> Modules { get { Verify(); return _modules.ToArray(); } }
    private void Verify()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Plugin loader requires its owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new InvalidOperationException("Plugin loader reentry is forbidden.");
    }
    public void Load(string rootPath, IReadOnlyList<PluginSpecification> allowlist)
    {
        Verify();
        if (_modules.Count != 0) throw new InvalidOperationException("Hot replacement is not supported.");
        ArgumentNullException.ThrowIfNull(allowlist);
        string root = Path.GetFullPath(rootPath);
        for (var ancestor = new DirectoryInfo(root); ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Plugin root traverses a reparse point.");
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 || allowlist.Count > 64)
            throw new ArgumentException("Invalid plugin root or count.");
        var specs = new Dictionary<string, PluginSpecification>(StringComparer.Ordinal);
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var original in allowlist)
        {
            var spec = original is null ? null : original with { Dependencies = original.Dependencies is null ? null! : (string[])original.Dependencies.Clone() };
            Validate(spec);
        }
        void Validate(PluginSpecification? spec)
        {
            if (spec is null || string.IsNullOrWhiteSpace(spec.Id) || spec.Id.Length > 128 ||
                !spec.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') ||
                !Enum.IsDefined(spec.Kind) || spec.Major != 1 || spec.Minor > (spec.Kind == ModuleKind.Gui ? 7u : spec.Kind == ModuleKind.Renderer ? 2u : spec.Kind == ModuleKind.Physics ? 2u : 0u) || spec.Dependencies is null ||
                spec.Dependencies.Length > 64 || !specs.TryAdd(spec.Id, spec)) throw new ArgumentException("Invalid/duplicate plugin specification.");
            if (string.IsNullOrWhiteSpace(spec.Path) || Path.IsPathRooted(spec.Path) || spec.Path.Contains(':') ||
                spec.Path.Split('/', '\\').Any(p => p is "" or "." or "..") ||
                Path.GetFileName(spec.Path) != spec.FileName || !spec.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Plugin must match an explicit project-relative DLL filename.");
            string path = Path.GetFullPath(Path.Combine(root, spec.Path));
            if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !uniquePaths.Add(path))
                throw new ArgumentException("Plugin path escapes root or conflicts.");
            string current = root;
            foreach (string part in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, part);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Plugin path traverses a reparse point.");
            }
            if (!File.Exists(path)) throw new FileNotFoundException("Plugin missing.", path);
            if (spec.Sha256 is not null)
            {
                if (spec.Sha256.Length != 64 || !spec.Sha256.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid integrity hash.");
                using var stream = File.OpenRead(path);
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(spec.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Plugin integrity hash mismatch.");
            }
            paths.Add(spec.Id, path);
        }
        var order = new List<PluginSpecification>();
        var visiting = new HashSet<string>(StringComparer.Ordinal); var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in specs.Keys.Order(StringComparer.Ordinal)) Visit(id);
        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!specs.TryGetValue(id, out var spec)) throw new ArgumentException("Missing plugin dependency: " + id);
            if (!visiting.Add(id)) throw new ArgumentException("Plugin dependency cycle.");
            if (spec.Dependencies.Distinct(StringComparer.Ordinal).Count() != spec.Dependencies.Length)
                throw new ArgumentException("Duplicate dependency.");
            foreach (string dependency in spec.Dependencies.Order(StringComparer.Ordinal)) Visit(dependency);
            visiting.Remove(id); visited.Add(id); order.Add(spec);
        }
        _busy = true;
        try { foreach (var spec in order) _modules.Add(PluginModule.Load(spec, paths[spec.Id], spec.Dependencies.Select(id => _modules.Single(m => m.Id == id)).ToArray())); }
        catch (Exception initialize)
        {
            var errors = Close();
            if (errors.Count != 0) throw new AggregateException(new[] { initialize }.Concat(errors));
            throw;
        }
        finally { _busy = false; }
    }
    private List<Exception> Close()
    {
        var errors = new List<Exception>();
        for (int i = _modules.Count - 1; i >= 0; i--)
        {
            try { _modules[i].Dispose(); _modules.RemoveAt(i); }
            catch (Exception e) { errors.Add(e); break; } // Retain its dependencies, never free live function pointers.
        }
        return errors;
    }
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _owner || _busy) throw new InvalidOperationException("Owner-thread nonreentrant shutdown required.");
        if (_disposed) return;
        _busy = true;
        try
        {
            var errors = Close();
            if (errors.Count != 0) throw new AggregateException(errors);
            _disposed = true;
        }
        finally { _busy = false; }
    }
}

public sealed unsafe class PluginModule : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly byte[] _table;
    private readonly ModuleApi _api;
    private nint _library;
    private ulong _context;
    private int _leases, _calls;
    private bool _closing, _draining;
    private readonly PluginLease[] _dependencies;
    private readonly Queue<Action> _releases = [];
    private readonly object _releaseGate = new();
    public string Id { get; }
    public ModuleKind Kind => (ModuleKind)_api.Kind;
    public ulong Capabilities => _api.Capabilities;
    public uint AbiMajor => _api.Major;
    public uint AbiMinor => _api.Minor;
    public int OutstandingLeases { get { Verify(); return _leases; } }
    private PluginModule(string id, nint library, byte[] table, ModuleApi api, ulong context, PluginModule[] dependencies)
    { Id = id; _library = library; _table = table; _api = api; _context = context;
      _dependencies = dependencies.Select(d => d.AcquireLease()).ToArray(); }
    internal static PluginModule Load(PluginSpecification spec, string path, PluginModule[] dependencies)
    {
        nint library = NativeLibrary.Load(path, typeof(PluginModule).Assembly,
            DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
        try
        {
            if (!NativeLibrary.TryGetExport(library, "ncma_plugin_get_api", out var export))
                throw new PluginException(spec.Id, "negotiate", PluginResult.AbiMismatch, "missing_export");
            var get = Marshal.GetDelegateForFunctionPointer<GetApi>(export);
            byte[] table = new byte[4096]; PluginError error = default;
            fixed (byte* output = table) Check(spec.Id, "negotiate", get(spec.Major, spec.Minor, output, 4096, &error), error);
            ModuleApi api;
            fixed (byte* bytes = table) api = *(ModuleApi*)bytes;
            if (api.StructSize < sizeof(ModuleApi) || api.StructSize > 4096 || api.Major != spec.Major || api.Minor < spec.Minor ||
                api.Minor > (spec.Kind == ModuleKind.Gui ? 7u : spec.Kind == ModuleKind.Renderer ? 2u : spec.Kind == ModuleKind.Physics ? 2u : 0u) ||
                api.Kind != (uint)spec.Kind || api.Initialize == 0 || api.Shutdown == 0 || api.GetStatus == 0 || api.ReadDiagnostic == 0)
                throw new PluginException(spec.Id, "negotiate", PluginResult.AbiMismatch, "Invalid module API layout/version/kind.");
            ulong context = 0;
            Check(spec.Id, "initialize", Marshal.GetDelegateForFunctionPointer<Initialize>(api.Initialize)(null, 0, &context, &error), error);
            if (context == 0) throw new PluginException(spec.Id, "initialize", PluginResult.InvalidHandle, "Module returned null context.");
            return new(spec.Id, library, table, api, context, dependencies);
        }
        catch { NativeLibrary.Free(library); throw; }
    }
    internal static void Check(string id, string phase, uint result, PluginError error)
    {
        if (result != 0) throw new PluginException(id, phase, Enum.IsDefined((PluginResult)result) ? (PluginResult)result : PluginResult.InternalError,
            error.MessageLength <= 512 ? error.Text : "Malformed plugin diagnostic.");
    }
    internal void Verify()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new PluginException(Id, "access", PluginResult.WrongThread, "Module requires its owner thread.");
        if (_library == 0) throw new ObjectDisposedException(Id);
    }
    public PluginLease AcquireLease()
    {
        Verify(); _leases = checked(_leases + 1); return new(this);
    }
    internal void ReleaseLease() { Verify(); _leases--; }
    // Only typed trusted adapters can obtain a temporary delegate; they retain a resource lease.
    internal T ReadFunction<T>(int offset) where T : Delegate
    {
        Verify();
        if (offset < sizeof(ModuleApi) || offset % 8 != 0 || offset + 8 > _api.StructSize) throw new ArgumentException("Invalid API offset.");
        nint pointer; fixed (byte* bytes = _table) pointer = *(nint*)(bytes + offset);
        if (pointer == 0) throw new PluginException(Id, "negotiate", PluginResult.AbiMismatch, "Missing required operation.");
        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }
    internal ulong Context { get { Verify(); return _context; } }
    public string ReadDiagnostics()
    {
        Verify(); byte[] output = new byte[65536]; uint required = 0; PluginError error = default;
        fixed (byte* bytes = output)
            Check(Id, "diagnostics", Marshal.GetDelegateForFunctionPointer<ReadDiagnostic>(_api.ReadDiagnostic)(_context, bytes, 65536, &required, &error), error);
        if (required > 65536) throw new PluginException(Id, "diagnostics", PluginResult.InternalError, "Diagnostic budget exceeded.");
        return new System.Text.UTF8Encoding(false, true).GetString(output.AsSpan(0, (int)required));
    }
    public ModuleStatus Status
    {
        get
        {
            Verify(); _calls++;
            try
            {
                ModuleStatus status = default; PluginError error = default;
                Check(Id, "status", Marshal.GetDelegateForFunctionPointer<GetStatus>(_api.GetStatus)(_context, &status, &error), error);
                if (status.StructSize != sizeof(ModuleStatus)) throw new PluginException(Id, "status", PluginResult.AbiMismatch, "Invalid status layout.");
                return status;
            }
            finally { _calls--; }
        }
    }
    // Queue only an owned native resource's release; the retained lease keeps this DLL loaded.
    // No finalizer calls GLFW/COM. Queue full leaves the lease with its caller for explicit recovery.
    internal void EnqueueRelease(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_releaseGate)
        {
            if (_library == 0 || _releases.Count == 256) throw new InvalidOperationException("Native release queue unavailable/full.");
            _releases.Enqueue(action);
        }
    }
    public int DrainReleases()
    {
        Verify();
        if (_draining) throw new InvalidOperationException("Release queue reentry is forbidden.");
        _draining = true;
        try
        {
        int count = 0;
        while (true)
        {
            Action action;
            lock (_releaseGate) { if (_releases.Count == 0) return count; action = _releases.Peek(); }
            action(); // Failure retains the action and lease; no unsafe stronger cleanup.
            lock (_releaseGate) _releases.Dequeue();
            count++;
        }
        }
        finally { _draining = false; }
    }
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Owner-thread module shutdown required.");
        if (_library == 0) return;
        if (_closing) throw new InvalidOperationException("Module shutdown reentry is forbidden.");
        _closing = true;
        try {
        DrainReleases();
        if (_leases != 0 || _calls != 0) throw new PluginException(Id, "shutdown", PluginResult.Busy, "Resource leases/calls remain; module retained.");
        var status = Status;
        if (status.LiveResources != 0 || status.LiveJobs != 0) throw new PluginException(Id, "shutdown", PluginResult.Busy, "Native resources/jobs remain; module retained.");
        PluginError error = default;
        Check(Id, "shutdown", Marshal.GetDelegateForFunctionPointer<Shutdown>(_api.Shutdown)(_context, &error), error);
        foreach (var dependency in _dependencies) dependency.Dispose();
        _context = 0; var library = _library; _library = 0;
        Array.Clear(_table); NativeLibrary.Free(library);
        }
        finally { _closing = false; }
    }
}
public sealed class PluginLease : IDisposable
{
    private readonly PluginModule _module;
    private int _state; // active=0, queued=1, released=2
    internal PluginLease(PluginModule module) { _module = module; }
    internal PluginModule Module
    {
        get { _module.Verify(); if (Volatile.Read(ref _state) != 0) throw new ObjectDisposedException(nameof(PluginLease)); return _module; }
    }
    internal void ScheduleRelease(Action releaseResource)
    {
        ArgumentNullException.ThrowIfNull(releaseResource);
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) throw new InvalidOperationException("Lease already released/queued.");
        try
        {
            _module.EnqueueRelease(() => {
                releaseResource();
                _module.ReleaseLease(); Volatile.Write(ref _state, 2);
            });
        }
        catch { Volatile.Write(ref _state, 0); throw; }
    }
    public void Dispose()
    {
        if (Volatile.Read(ref _state) == 2) return;
        _module.Verify();
        if (Volatile.Read(ref _state) == 1) throw new InvalidOperationException("Release is owned by the pending queue.");
        _module.ReleaseLease(); Volatile.Write(ref _state, 2);
    }
}
