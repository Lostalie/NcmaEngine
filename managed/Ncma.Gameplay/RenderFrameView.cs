using System.Text.Json;
using System.Numerics;
using Ncma.Runtime;
namespace Ncma.Gameplay;

public sealed record RenderObject(Guid ObjectId, TransformData Transform);
public sealed record RenderFrameView(Guid SessionId, ulong Tick, ulong FrameSequence, double Alpha, RenderObject[] Objects);

internal sealed class RenderBuffer
{
    private Dictionary<Guid, TransformData> _previous = [], _current = [];
    internal Action Prepare(WorldSnapshot world, bool reset = false)
    {
        var current = new Dictionary<Guid, TransformData>();
        foreach (var obj in world.Objects)
            foreach (var value in obj.Components)
                if (value.TypeId == "ncma.transform") current.Add(obj.PersistentId, value.Data.Deserialize<TransformData>(SceneJson.Options));
        var previous = current.ToDictionary(p => p.Key, p => reset ? p.Value : _current.GetValueOrDefault(p.Key, p.Value));
        return () => { _previous = previous; _current = current; };
    }
    internal RenderFrameView Read(PlayStatus status)
    {
        float alpha = status.State == PlayState.Running ? (float)status.InterpolationAlpha : 1;
        var objects = _current.OrderBy(p => p.Key).Select(p =>
        {
            var before = _previous[p.Key]; var after = p.Value;
            return new RenderObject(p.Key, new(System.Numerics.Vector3.Lerp(before.Position, after.Position, alpha),
                Quaternion.Normalize(Quaternion.Slerp(before.Rotation, after.Rotation, alpha)), System.Numerics.Vector3.Lerp(before.Scale, after.Scale, alpha)));
        }).ToArray();
        return new(status.SessionId, status.Tick, status.FrameCount, alpha, objects);
    }
    internal void Clear() { _previous = []; _current = []; }
}
