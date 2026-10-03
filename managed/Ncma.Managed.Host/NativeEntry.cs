using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using Ncma;
using Ncma.Gameplay;
using Ncma.Scene;

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
    private static readonly int s_ownerThread = Environment.CurrentManagedThreadId;
    private static GameplayLoadContext? s_loadContext;
    private static ScriptType[] s_types = [];
    private static PlaySession? s_play;
    private static ulong s_playScene;
    private static SceneWorld? s_world => s_play?.Facade;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static uint GetBridgeVersion() => 5;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int LoadGameplay(byte* assemblyPath, byte* error, int capacity) => Guard(() =>
    {
        if (s_play is not null) throw new InvalidOperationException("End Play before loading another gameplay catalog.");
        var candidate = LoadCandidate(ReadUtf8(assemblyPath));
        try
        {
            foreach (var scene in s_scenes.Values.Where(s => s.Editor is not null))
                using (scene.Document.World.ReadOnly())
                    foreach (var obj in scene.Document.CaptureSnapshot().Objects)
                        foreach (var binding in obj.Behaviours) _ = Instantiate(candidate.Types, binding);
            var previous = s_loadContext;
            s_loadContext = candidate.Context; s_types = candidate.Types; PublishEditorCatalog(); previous?.Unload();
            return s_types.Length;
        }
        catch { candidate.Context.Unload(); throw; }
    }, error, capacity);

    private static (GameplayLoadContext Context, ScriptType[] Types) LoadCandidate(string assemblyPath)
    {
        string path = Path.GetFullPath(assemblyPath);
        var context = new GameplayLoadContext(path);
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var assembly = context.LoadFromStream(stream);
            var types = assembly.GetTypes().Where(t => typeof(Behaviour).IsAssignableFrom(t) && !t.IsAbstract && !t.ContainsGenericParameters)
                .OrderBy(t => t.FullName, StringComparer.Ordinal).Select(DescribeType).ToArray();
            return (context, types);
        }
        catch { context.Unload(); throw; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int ReloadGameplay(byte* path, ulong scene, ulong high, ulong low, PlayStatusV5* output,
        byte* error, int capacity) => Guard(() =>
    {
        PlaySession? play = null;
        if (s_play is not null)
        {
            if (output is null) throw new ArgumentException("Null reload status.");
            play = Play(scene, high, low); play.Pause();
        }
        try
        {
            var candidate = LoadCandidate(ReadUtf8(path));
            bool published = false;
            try
            {
                if (play is not null) play.Reload(binding => Instantiate(candidate.Types, binding));
                else foreach (var doc in s_scenes.Values.Where(s => s.Editor is not null))
                    using (doc.Document.World.ReadOnly())
                        foreach (var obj in doc.Document.CaptureSnapshot().Objects)
                            foreach (var binding in obj.Behaviours) _ = Instantiate(candidate.Types, binding);
                Publish();
                return candidate.Types.Length;
            }
            catch { if (play?.State == PlayState.Faulted) Publish(); throw; }
            finally { if (!published) candidate.Context.Unload(); }
            void Publish()
            {
                var previous = s_loadContext; s_loadContext = candidate.Context; s_types = candidate.Types;
                published = true; PublishEditorCatalog(); previous?.Unload();
            }
        }
        finally { if (play is not null) *output = CopyStatus(play.Status); }
    }, error, capacity);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static WeakReference ProbeCollectibleCatalog(string path)
    {
        var candidate = LoadCandidate(path);
        foreach (var type in candidate.Types)
            if (type.Type.BaseType != typeof(Behaviour) && !typeof(Behaviour).IsAssignableFrom(type.Type))
                throw new InvalidOperationException("Shared SDK identity mismatch.");
        var weak = new WeakReference(candidate.Context);
        candidate.Context.Unload();
        return weak;
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

    [StructLayout(LayoutKind.Sequential)]
    public struct PlayStatusV5
    {
        public uint Version, State;
        public ulong SessionHigh, SessionLow, WorldHigh, WorldLow, FrameCount, Tick;
        public int StepsExecuted;
        public uint Reserved;
        public double FixedDeltaSeconds, SimulationSeconds, Accumulator, InterpolationAlpha, DroppedSeconds, TotalDroppedSeconds;
        public uint FaultCode, Reserved2;
    }
    private static PlayStatusV5 CopyStatus(PlayStatus status)
    {
        var session = SceneWorld.ToUuid(status.SessionId); var world = SceneWorld.ToUuid(status.WorldId);
        return new()
        {
            Version = 5, State = (uint)status.State, SessionHigh = session.High, SessionLow = session.Low,
            WorldHigh = world.High, WorldLow = world.Low, FrameCount = status.FrameCount, Tick = status.Tick,
            StepsExecuted = status.StepsExecuted, FixedDeltaSeconds = status.FixedDeltaSeconds,
            SimulationSeconds = status.SimulationSeconds, Accumulator = status.Accumulator,
            InterpolationAlpha = status.InterpolationAlpha, DroppedSeconds = status.DroppedSeconds,
            TotalDroppedSeconds = status.TotalDroppedSeconds, FaultCode = status.Fault is null ? 0u : 1u
        };
    }
    private static PlaySession Play(ulong scene, ulong high, ulong low)
    {
        _ = Scene(scene); // Thread/disposal checks precede session validation.
        var play = s_play ?? throw new InvalidOperationException("No bound Play session.");
        if (scene != s_playScene || play.SessionId != SceneWorld.ToGuid(high, low))
            throw new InvalidOperationException("Stale or foreign Play session.");
        return play;
    }
    private static Behaviour Instantiate(BehaviourBindingData binding) => Instantiate(s_types, binding);
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
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int BeginScene(ulong sceneToken, PlayStatusV5* output, byte* error, int capacity) => Guard(() =>
    {
        if (output is null) throw new ArgumentException("Null Play status.");
        var scene = Scene(sceneToken);
        if (scene.Editor is not null) throw new InvalidOperationException("Clone the committed edit document into an isolated Play scene.");
        if (s_play is not null) throw new InvalidOperationException("End the current Play session before binding another.");
        var candidate = new PlaySession(scene.Document, scene.World);
        try { candidate.Start(Instantiate); }
        catch { candidate.Dispose(); throw; }
        s_play = candidate; s_playScene = sceneToken;
        *output = CopyStatus(candidate.Status);
        return candidate.BehaviourCount;
    }, error, capacity);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int AdvanceFrame(ulong scene, ulong high, ulong low, double deltaSeconds, PlayStatusV5* output,
        byte* error, int capacity) => Guard(() =>
    {
        if (output is null) throw new ArgumentException("Null Play status.");
        var result = Play(scene, high, low).AdvanceFrame(deltaSeconds);
        *output = CopyStatus(result);
        if (result.Fault is { } fault)
            throw new InvalidOperationException($"{fault.Phase} world={fault.WorldId} session={fault.SessionId} committedTick={fault.Tick} attemptedTick={fault.AttemptTick} object={fault.ObjectId} binding={fault.BindingId} type={fault.TypeName}: {fault.Message}");
        return result.StepsExecuted;
    }, error, capacity);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int ControlPlay(ulong scene, ulong high, ulong low, uint command, PlayStatusV5* output,
        byte* error, int capacity) => Guard(() =>
    {
        if (output is null) throw new ArgumentException("Null Play status.");
        var play = Play(scene, high, low);
        var result = command switch
        {
            0 => play.Pause(), 1 => play.Resume(), 2 => play.Step(),
            _ => throw new ArgumentException("Unknown Play control.")
        };
        *output = CopyStatus(result);
        if (result.Fault is { } fault) throw new InvalidOperationException(fault.Phase + ": " + fault.Message);
        return 0;
    }, error, capacity);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int GetPlayStatus(ulong scene, PlayStatusV5* output, byte* error, int capacity) => Guard(() =>
    {
        if (output is null || s_play is null || s_playScene != scene) throw new ArgumentException("No bound Play session.");
        _ = Scene(scene); *output = CopyStatus(s_play.Status); return 0;
    }, error, capacity);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int EndScene(ulong scene, ulong high, ulong low, byte* error, int capacity) => Guard(() =>
    { _ = Play(scene, high, low); ClearInstances(); return 0; }, error, capacity);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int UnloadGameplay(byte* error, int capacity) => Guard(() =>
    {
        if (s_play is not null) throw new InvalidOperationException("End Play before unloading gameplay.");
        UnloadCore(); return 0;
    }, error, capacity);
    private static void ClearInstances()
    {
        var previous = s_play;
        // Reject foreign/reentrant cleanup before releasing ownership.
        if (previous is null) return;
        try { previous.Stop(); }
        finally
        {
            if (previous.State == PlayState.Stopped)
            { s_play = null; s_playScene = 0; previous.Dispose(); }
        }
    }

    private static void UnloadCore()
    {
        try { ClearInstances(); }
        finally
        {
            if (s_play is null)
            {
                s_types = [];
                GameplayLoadContext? context = s_loadContext;
                s_loadContext = null;
                context?.Unload();
            }
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
            if (Environment.CurrentManagedThreadId != s_ownerThread)
                throw new InvalidOperationException("Managed host entry requires its owner thread.");
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
