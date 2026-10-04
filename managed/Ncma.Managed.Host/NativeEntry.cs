using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Ncma;
using Ncma.Gameplay;
using Ncma.Scene;
using Ncma.Scripting;

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

    private static readonly int s_ownerThread = Environment.CurrentManagedThreadId;
    private static readonly ScriptCatalogService s_catalog = new();
    private static PlaySession? s_play => s_playScene == 0 ? null : Scene(s_playScene).Owner.Play;
    private static ulong s_playScene;
    private static SceneWorld? s_world => s_play?.Facade;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static uint GetBridgeVersion() => 5;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int LoadGameplay(byte* assemblyPath, byte* error, int capacity) => Guard(() =>
    {
        if (s_play is not null) throw new InvalidOperationException("End Play before loading another gameplay catalog.");
        using var candidate = s_catalog.LoadCandidate(ReadUtf8(assemblyPath));
        int count = candidate.Count;
        try
        {
            foreach (var scene in s_scenes.Values.Where(s => s.Editor is not null))
                using (scene.Document.World.ReadOnly())
                    foreach (var obj in scene.Document.CaptureSnapshot().Objects)
                        foreach (var binding in obj.Behaviours) _ = candidate.Instantiate(binding);
            s_catalog.CommitCandidate(candidate); PublishEditorCatalog();
            return count;
        }
        catch { throw; }
    }, error, capacity);

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
            using var candidate = s_catalog.LoadCandidate(ReadUtf8(path));
            int count = candidate.Count;
            bool published = false;
            try
            {
                if (play is not null) play.Reload(candidate.Instantiate);
                else foreach (var doc in s_scenes.Values.Where(s => s.Editor is not null))
                    using (doc.Document.World.ReadOnly())
                        foreach (var obj in doc.Document.CaptureSnapshot().Objects)
                            foreach (var binding in obj.Behaviours) _ = candidate.Instantiate(binding);
                Publish();
                return count;
            }
            catch { if (play?.State == PlayState.Faulted) Publish(); throw; }

            void Publish()
            {
                if (published) return;
                s_catalog.CommitCandidate(candidate); published = true; PublishEditorCatalog();
            }
        }
        finally { if (play is not null) *output = CopyStatus(play.Status); }
    }, error, capacity);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static WeakReference ProbeCollectibleCatalog(string path)
    {
        using var service = new ScriptCatalogService();
        using var candidate = service.LoadCandidate(path);
        return candidate.ContextReference;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int GetTypeInfo(int index, byte* name, int nameCapacity, byte* error, int capacity) => Guard(() =>
    {
        var type = s_catalog.Snapshot.Types[index];
        WriteUtf8(type.TypeName, name, nameCapacity);
        return type.Exports.Length;
    }, error, capacity);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int GetPropertyInfo(int type, int index, PropertyInfoV2* output, byte* error, int capacity) => Guard(() =>
    {
        if (output is null) throw new ArgumentException("Property output is null.");
        ExportDescriptor member = s_catalog.Snapshot.Types[type].Exports[index];
        *output = default;
        output->Kind = member.Kind;
        output->DefaultValue = member.DefaultValue;
        WriteUtf8(member.Name, output->Name, 128);
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
    private static Behaviour Instantiate(BehaviourBindingData binding) => s_catalog.Instantiate(binding);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int BeginScene(ulong sceneToken, PlayStatusV5* output, byte* error, int capacity) => Guard(() =>
    {
        if (output is null) throw new ArgumentException("Null Play status.");
        var scene = Scene(sceneToken);
        if (scene.Editor is not null) throw new InvalidOperationException("Clone the committed edit document into an isolated Play scene.");
        if (s_play is not null) throw new InvalidOperationException("End the current Play session before binding another.");
        var candidate = scene.Owner.StartPlay();
        s_playScene = sceneToken;
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
        try { Scene(s_playScene).Owner.StopPlay(); }
        finally { if (previous.State == PlayState.Stopped) s_playScene = 0; }
    }

    private static void UnloadCore()
    {
        try { ClearInstances(); }
        finally
        {
            if (s_play is null)
            {
                s_catalog.Clear();
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

}
