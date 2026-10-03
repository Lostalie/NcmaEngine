using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using Ncma;

namespace Ncma.ManagedHost;

// Versioned C ABI. Exceptions are translated to caller-owned UTF-8 buffers.
public static unsafe partial class NativeEntry
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyInfoV2
    {
        public uint Kind;
        public uint Reserved;
        public double DefaultValue;
        public fixed byte Name[128];
        public fixed byte DisplayName[128];
        public fixed byte Category[128];
    }

    private sealed record ExportMember(MemberInfo Member, uint Kind, string DisplayName, string Category, double DefaultValue)
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

    private sealed record ScriptType(Type Type, ExportMember[] Exports);
    private sealed class Instance(Behaviour behaviour, int typeIndex, bool enabled)
    {
        public Behaviour Behaviour = behaviour;
        public int TypeIndex = typeIndex;
        public bool Enabled = enabled;
        public bool Created;
        public bool Activated;
    }

    private static GameplayLoadContext? s_loadContext;
    private static ScriptType[] s_types = [];
    private static readonly List<Instance> s_instances = [];
    private static SceneWorld? s_world;
    private static long s_tickCount;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static uint GetBridgeVersion() => 3;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int LoadGameplay(byte* assemblyPath, byte* error, int capacity) => Guard(() =>
    {
        UnloadCore();
        string path = Path.GetFullPath(ReadUtf8(assemblyPath));
        var context = new GameplayLoadContext(path);
        s_loadContext = context;
        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Assembly assembly = context.LoadFromStream(stream);
            s_types = assembly.GetTypes()
                .Where(type => typeof(Behaviour).IsAssignableFrom(type) && !type.IsAbstract && !type.ContainsGenericParameters)
                .OrderBy(type => type.FullName, StringComparer.Ordinal).Select(DescribeType).ToArray();
            s_tickCount = 0;
            return s_types.Length;
        }
        catch
        {
            s_types = [];
            s_loadContext = null;
            context.Unload();
            throw;
        }
    }, error, capacity);

    private static ScriptType DescribeType(Type type)
    {
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
            exports.Add(new(member, kind, attribute.DisplayName ?? member.Name, attribute.Category ?? "Gameplay", number));
        }
        return new(type, exports.ToArray());
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int GetTypeInfo(int index, byte* name, int nameCapacity, byte* error, int capacity) => Guard(() =>
    {
        WriteUtf8(s_types[index].Type.FullName!, name, nameCapacity);
        return s_types[index].Exports.Length;
    }, error, capacity);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int GetPropertyInfo(int type, int index, PropertyInfoV2* output, byte* error, int capacity) => Guard(() =>
    {
        if (output is null) throw new ArgumentException("Property output is null.");
        ExportMember member = s_types[type].Exports[index];
        *output = default;
        output->Kind = member.Kind;
        output->DefaultValue = member.DefaultValue;
        WriteUtf8(member.Member.Name, output->Name, 128);
        WriteUtf8(member.DisplayName, output->DisplayName, 128);
        WriteUtf8(member.Category, output->Category, 128);
        return 0;
    }, error, capacity);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int BeginScene(ulong world, byte* error, int capacity) => Guard(() =>
    {
        ClearInstances();
        var scene = Scene(world);
        if (scene.Editor is not null) throw new InvalidOperationException("Clone the committed edit document into an isolated Play scene.");
        s_world = scene.World;
        s_tickCount = 0;
        return 0;
    }, error, capacity);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int CreateBehaviour(int type, ulong gameObject, int enabled, byte* error, int capacity) => Guard(() =>
    {
        if (s_world is null) throw new InvalidOperationException("No play scene is bound.");
        var target = s_world.FindId(gameObject);
        var behaviour = (Behaviour)Activator.CreateInstance(s_types[type].Type)!;
        behaviour.GameObject = target;
        s_instances.Add(new Instance(behaviour, type, enabled != 0));
        return s_instances.Count - 1;
    }, error, capacity);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int SetProperty(int instance, int property, double value, byte* error, int capacity) => Guard(() =>
    {
        Instance target = s_instances[instance];
        if (target.Created) throw new InvalidOperationException("Configure properties before activation.");
        s_types[target.TypeIndex].Exports[property].Set(target.Behaviour, value);
        return 0;
    }, error, capacity);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int ActivateScene(byte* error, int capacity) => Guard(() =>
    {
        foreach (Instance instance in s_instances)
        {
            if (instance.Created) continue;
            instance.Created = true;
            instance.Behaviour.DispatchCreate();
            if (!instance.Enabled) continue;
            instance.Activated = true;
            instance.Behaviour.DispatchEnable();
        }
        return s_instances.Count;
    }, error, capacity);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int Tick(double deltaSeconds, byte* error, int capacity) => Guard(() =>
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentException("Invalid delta time.");
        foreach (Instance instance in s_instances)
            if (instance.Activated) instance.Behaviour.DispatchUpdate(deltaSeconds);
        ++s_tickCount;
        return s_instances.Count;
    }, error, capacity);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int EndScene(byte* error, int capacity) => Guard(() => { ClearInstances(); return 0; }, error, capacity);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int UnloadGameplay(byte* error, int capacity) => Guard(() => { UnloadCore(); return 0; }, error, capacity);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static long GetTickCount() => s_tickCount;

    private static void ClearInstances()
    {
        List<Exception> errors = [];
        foreach (Instance instance in s_instances)
        {
            try { if (instance.Activated) instance.Behaviour.DispatchDisable(); }
            catch (Exception exception) { errors.Add(exception); }
            try { if (instance.Created) instance.Behaviour.DispatchDestroy(); }
            catch (Exception exception) { errors.Add(exception); }
        }
        s_instances.Clear();
        s_world = null;
        if (errors.Count != 0) throw new AggregateException(errors);
    }

    private static void UnloadCore()
    {
        try { ClearInstances(); }
        finally
        {
            s_types = [];
            GameplayLoadContext? context = s_loadContext;
            s_loadContext = null;
            context?.Unload();
        }
    }

    private static string ReadUtf8(byte* text) => Marshal.PtrToStringUTF8((nint)text)
        ?? throw new ArgumentException("Null UTF-8 input.");

    private static void WriteUtf8(string text, byte* output, int capacity)
    {
        if (output is null || capacity < 1) throw new ArgumentException("Invalid output buffer.");
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length >= capacity) throw new ArgumentException("Metadata exceeds ABI buffer capacity.");
        bytes.CopyTo(new Span<byte>(output, capacity));
        output[bytes.Length] = 0;
    }

    private static int Guard(Func<int> operation, byte* error, int capacity)
    {
        try
        {
            int result = operation();
            if (error is not null && capacity > 0) error[0] = 0;
            return result;
        }
        catch (Exception exception)
        {
            if (error is not null && capacity > 0)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(exception.GetBaseException().Message);
                int length = Math.Min(bytes.Length, capacity - 1);
                bytes.AsSpan(0, length).CopyTo(new Span<byte>(error, capacity));
                error[length] = 0;
            }
            return -1;
        }
    }

    private sealed class GameplayLoadContext(string path) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(path);
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == typeof(Behaviour).Assembly.GetName().Name) return typeof(Behaviour).Assembly;
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
