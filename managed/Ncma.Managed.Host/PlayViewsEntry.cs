using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ncma;
using Ncma.Gameplay;
namespace Ncma.ManagedHost;

public static unsafe partial class NativeEntry
{
    [StructLayout(LayoutKind.Sequential)]
    public struct InputFrameV1
    {
        public uint Version, Focused;
        public ulong Sequence, SessionHigh, SessionLow;
        public fixed ulong Held[8], Pressed[8], Released[8];
        public double PointerX, PointerY;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct RenderObjectV1
    {
        public ulong ObjectHigh, ObjectLow;
        public fixed float Position[3], Rotation[4], Scale[3];
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct RenderHeaderV1
    {
        public uint Version, Count;
        public ulong SessionHigh, SessionLow, Tick, FrameSequence;
        public double Alpha;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int SubmitPlayInput(ulong scene, InputFrameV1* frame, byte* error, int capacity) => Guard(() =>
    {
        if (frame is null || frame->Version != 1 || frame->Focused > 1) throw new ArgumentException("Invalid input ABI.");
        var play = Play(scene, frame->SessionHigh, frame->SessionLow);
        play.SubmitInput(new(play.SessionId, frame->Sequence, frame->Focused == 1,
            new ReadOnlySpan<ulong>(frame->Held, 8).ToArray(), new ReadOnlySpan<ulong>(frame->Pressed, 8).ToArray(),
            new ReadOnlySpan<ulong>(frame->Released, 8).ToArray(), frame->PointerX, frame->PointerY));
        return 0;
    }, error, capacity);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int GetRenderFrame(ulong scene, ulong high, ulong low, RenderHeaderV1* header,
        RenderObjectV1* output, int count, byte* error, int capacity) => Guard(() =>
    {
        if (header is null || count < 0 || count > 4096 || (count != 0 && output is null)) throw new ArgumentException("Invalid render buffers.");
        var view = Play(scene, high, low).RenderView;
        if (count < view.Objects.Length) throw new ArgumentException("Render output capacity is too small.");
        var values = new RenderObjectV1[view.Objects.Length];
        for (int i = 0; i < values.Length; i++)
        {
            var value = view.Objects[i]; var uuid = SceneWorld.ToUuid(value.ObjectId); var t = value.Transform;
            var item = new RenderObjectV1 { ObjectHigh = uuid.High, ObjectLow = uuid.Low };
            item.Position[0] = t.Position.X; item.Position[1] = t.Position.Y; item.Position[2] = t.Position.Z;
            item.Rotation[0] = t.Rotation.X; item.Rotation[1] = t.Rotation.Y; item.Rotation[2] = t.Rotation.Z; item.Rotation[3] = t.Rotation.W;
            item.Scale[0] = t.Scale.X; item.Scale[1] = t.Scale.Y; item.Scale[2] = t.Scale.Z; values[i] = item;
        }
        values.AsSpan().CopyTo(new Span<RenderObjectV1>(output, count));
        var session = SceneWorld.ToUuid(view.SessionId);
        *header = new() { Version = 1, Count = (uint)values.Length, SessionHigh = session.High, SessionLow = session.Low,
            Tick = view.Tick, FrameSequence = view.FrameSequence, Alpha = view.Alpha };
        return values.Length;
    }, error, capacity);
}
