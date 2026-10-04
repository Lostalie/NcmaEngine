using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Ncma;
using Ncma.Gameplay;
using Ncma.Scene;

namespace Ncma.Scripting;

public sealed record ExportDescriptor(string Name, uint Kind, string DisplayName, string Category, double DefaultValue);
public sealed record ScriptDescriptor(string TypeName, ExportDescriptor[] Exports);
public sealed record ScriptCatalogSnapshot(Guid Generation, ScriptDescriptor[] Types);

// Trusted bootstrap service. Never expose an assembly path parameter as an Agent capability.
public sealed class ScriptCatalogService : IDisposable
{
    internal sealed record ExportMember(MemberInfo Member, uint Kind, string DisplayName, string Category, double DefaultValue)
    {
        public void Set(Behaviour instance, double number)
        {
            if (!double.IsFinite(number)) throw new ArgumentException("Export value must be finite.");
            object value = Kind switch
            {
                1 when float.IsFinite((float)number) => (object)(float)number,
                2 => number,
                3 when number == Math.Truncate(number) && number >= int.MinValue && number <= int.MaxValue => (int)number,
                4 when number == 0 || number == 1 => number != 0,
                _ => throw new ArgumentException("Invalid exported property value.")
            };
            if (Member is PropertyInfo property) property.SetValue(instance, value);
            else ((FieldInfo)Member).SetValue(instance, value);
        }
    }

    internal sealed record ScriptType(Type Type, ExportMember[] Exports);

    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly HashSet<Candidate> _candidates = [];
    private GameplayLoadContext? _context;
    private ScriptType[] _types = [];
    private Guid _generation;
    private bool _disposed, _busy;
    private int _leases;
    public IDisposable AcquireLease()
    {
        Verify(); _leases = checked(_leases + 1); return new CatalogLease(this);
    }
    private sealed class CatalogLease(ScriptCatalogService owner) : IDisposable
    {
        private bool _released;
        public void Dispose() { owner.Verify(); if (_released) return; owner._leases--; _released = true; }
    }
    private void Verify()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Catalog requires its owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new InvalidOperationException("Catalog callback reentry is forbidden.");
    }
    public ScriptCatalogSnapshot Snapshot
    {
        get
        {
            Verify();
            return new(_generation, _types.Select(t => new ScriptDescriptor(t.Type.FullName!,
                t.Exports.Select(e => new ExportDescriptor(e.Member.Name, e.Kind, e.DisplayName, e.Category, e.DefaultValue)).ToArray())).ToArray());
        }
    }
    public sealed class Candidate : IDisposable
    {
        internal readonly ScriptCatalogService _owner;
        internal GameplayLoadContext? _context;
        internal ScriptType[] _types;
        internal bool _closed;
        internal bool _committed;
        internal Candidate(ScriptCatalogService owner, GameplayLoadContext context, ScriptType[] types)
        { _owner = owner; _context = context; _types = types; }
        public int Count { get { Verify(); return _types.Length; } }
        public WeakReference ContextReference { get { Verify(); return new(_context); } }
        internal void Verify()
        {
            _owner.Verify();
            if (_closed) throw new InvalidOperationException("Candidate was disposed or committed.");
        }
        public Behaviour Instantiate(BehaviourBindingData binding)
        {
            if (_committed) return _owner.Instantiate(binding);
            Verify(); _owner._busy = true;
            try { return ScriptCatalogService.Instantiate(_types, binding); }
            finally { _owner._busy = false; }
        }
        public void Dispose()
        {
            _owner.Verify();
            if (_closed) return;
            _owner._candidates.Remove(this); _closed = true; _types = [];
            var context = _context; _context = null; context?.Unload();
        }
    }
    public Candidate LoadCandidate(string assemblyPath)
    {
        Verify();
        string path = Path.GetFullPath(assemblyPath);
        _busy = true;
        GameplayLoadContext? context = null;
        try
        {
            context = new(path);
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var assembly = context.LoadFromStream(stream);
            var types = assembly.GetTypes().Where(t => typeof(Behaviour).IsAssignableFrom(t) && !t.IsAbstract && !t.ContainsGenericParameters)
                .OrderBy(t => t.FullName, StringComparer.Ordinal).Select(DescribeType).ToArray();
            var candidate = new Candidate(this, context, types); _candidates.Add(candidate); return candidate;
        }
        catch { context?.Unload(); throw; }
        finally { _busy = false; }
    }
    // Preflight/reload must succeed before commit. The new Play can remain faulted after activation;
    // in that case its new factories still require this candidate, exactly as PlaySession.Reload specifies.
    public void CommitCandidate(Candidate candidate)
    {
        Verify(); candidate.Verify();
        if (!ReferenceEquals(candidate._owner, this)) throw new ArgumentException("Foreign catalog candidate.");
        var previous = _context;
        _context = candidate._context; _types = candidate._types; _generation = Guid.NewGuid();
        candidate._closed = true; candidate._committed = true; candidate._context = null; candidate._types = [];
        _candidates.Remove(candidate); previous?.Unload();
    }
    public Behaviour Instantiate(BehaviourBindingData binding)
    {
        Verify(); _busy = true;
        try { return Instantiate(_types, binding); }
        finally { _busy = false; }
    }
    public void Clear()
    {
        Verify();
        if (_leases != 0) throw new InvalidOperationException("Stop all catalog consumers before unloading.");
        foreach (var candidate in _candidates.ToArray()) candidate.Dispose();
        _types = []; _generation = Guid.Empty;
        var context = _context; _context = null; context?.Unload();
    }
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Catalog requires its owner thread.");
        if (_disposed) return;
        Clear(); _disposed = true;
    }
    private static ScriptType DescribeType(Type type)
    {
        if (type.FullName is null || Encoding.UTF8.GetByteCount(type.FullName) >= 512)
            throw new ArgumentException("Behaviour type name exceeds the ABI budget.");
        var prototype = (Behaviour)Activator.CreateInstance(type)!;
        var exports = new List<ExportMember>();
        foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance).OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            ExportAttribute? attribute = member.GetCustomAttribute<ExportAttribute>();
            if (attribute is null) continue;
            Type valueType;
            object? value;
            if (member is PropertyInfo property && property.GetIndexParameters().Length == 0 &&
                property.GetMethod?.IsPublic == true && property.SetMethod?.IsPublic == true)
            { valueType = property.PropertyType; value = property.GetValue(prototype); }
            else if (member is FieldInfo field && !field.IsInitOnly)
            { valueType = field.FieldType; value = field.GetValue(prototype); }
            else throw new InvalidOperationException($"Export '{type.FullName}.{member.Name}' must be writable and public.");
            uint kind = valueType == typeof(float) ? 1u : valueType == typeof(double) ? 2u :
                valueType == typeof(int) ? 3u : valueType == typeof(bool) ? 4u : 0u;
            if (kind == 0) throw new NotSupportedException($"Unsupported Export type: {valueType.Name} on {member.Name}.");
            double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (!double.IsFinite(number)) throw new InvalidOperationException("Export defaults must be finite.");
            string display = attribute.DisplayName ?? member.Name, category = attribute.Category ?? "Gameplay";
            if (Encoding.UTF8.GetByteCount(member.Name) >= 128 || Encoding.UTF8.GetByteCount(display) >= 128 || Encoding.UTF8.GetByteCount(category) >= 128)
                throw new ArgumentException("Export metadata exceeds the ABI budget.");
            exports.Add(new(member, kind, display, category, number));
        }
        return new(type, exports.ToArray());
    }


    private static Behaviour Instantiate(ScriptType[] catalog, BehaviourBindingData binding)
    {
        var type = catalog.FirstOrDefault(t => t.Type.FullName == binding.TypeName)
            ?? throw new ArgumentException("Missing Behaviour type: " + binding.TypeName);
        var instance = (Behaviour)Activator.CreateInstance(type.Type)!;
        foreach (var value in binding.Exports)
        {
            var member = type.Exports.FirstOrDefault(e => e.Member.Name == value.Name && e.Kind == (uint)value.Kind)
                ?? throw new ArgumentException("Missing or changed Export: " + value.Name);
            member.Set(instance, value.Value);
        }
        return instance;
    }

    internal sealed class GameplayLoadContext(string path) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(path);
        protected override Assembly? Load(AssemblyName name)
        {
            foreach (var shared in new[] { typeof(Behaviour).Assembly, typeof(Ncma.Runtime.World).Assembly,
                typeof(SceneDocument).Assembly, typeof(PlaySession).Assembly })
                if (AssemblyName.ReferenceMatchesDefinition(name, shared.GetName()))
                {
                    var definition = shared.GetName();
                    if (name.Version != definition.Version || (name.CultureName ?? "") != (definition.CultureName ?? "") ||
                        !(name.GetPublicKeyToken() ?? []).SequenceEqual(definition.GetPublicKeyToken() ?? []))
                        throw new ArgumentException("Shared gameplay assembly identity mismatch.");
                    return shared;
                }
            string? resolved = _resolver.ResolveAssemblyToPath(name);
            if (resolved is null) return null;
            using var stream = File.Open(resolved, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return LoadFromStream(stream);
        }
        protected override nint LoadUnmanagedDll(string name)
        {
            string? resolved = _resolver.ResolveUnmanagedDllToPath(name);
            return resolved is null ? 0 : LoadUnmanagedDllFromPath(resolved);
        }
    }
}
