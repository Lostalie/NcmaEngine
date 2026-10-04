using System.Numerics;
using System.Text.Json;
namespace Ncma.Editor.Services;

public readonly record struct PreviewLine(Vector2 A, Vector2 B, uint Color, float Width);

// Presentation only. All skinning is computed by the immutable native kernel; no scene writes.
public sealed class FbxWireframe
{
    public const int TriangleLimit = 10000;
    private Guid? _resource;
    private uint[][] _indices = [];
    private int[] _vertexCounts = [], _parents = [];
    private float[] _sample = [];
    private System.Numerics.Vector3 _center;
    private float _radius;
    private readonly List<PreviewLine> _lines = [];
    private ulong _revision = ulong.MaxValue;
    private double _time = -1;
    public int TotalTriangles { get; private set; }
    public int DisplayedTriangles { get; private set; }
    public ReadOnlySpan<PreviewLine> Build(FbxPreviewSession session, float yaw, float width, float height)
    {
        if (!float.IsFinite(yaw) || Math.Abs(yaw) > MathF.PI || !float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentException("Invalid preview camera.");
        var state = session.Capture();
        if (state.AssetId is null) { _lines.Clear(); return []; }
        if (_resource != session.ResourceIdentity) {
            JsonElement report = session.Report!.Value;
            System.Numerics.Vector3 Vector(string name) { var v = report.GetProperty(name); return new(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle()); }
            System.Numerics.Vector3 min = Vector("bounds_min"), max = Vector("bounds_max");
            _center = (min + max) * .5f; _radius = Math.Max((max-min).Length() * .5f, .01f);
            _indices = new uint[state.Meshes][]; _vertexCounts = new int[state.Meshes];
            TotalTriangles = 0;
            for (int mesh = 0; mesh < state.Meshes; mesh++) {
                _indices[mesh] = new uint[session.IndexCount(mesh)]; session.ReadIndices(mesh,_indices[mesh]);
                _vertexCounts[mesh] = session.VertexCount(mesh); TotalTriangles += _indices[mesh].Length/3;
            }
            _parents = Enumerable.Range(0,state.Bones).Select(session.BoneParent).ToArray();
            _sample = new float[session.SampleFloatCount]; _resource = session.ResourceIdentity; _revision = ulong.MaxValue;
        }
        if (_revision != state.Revision || _time != state.Time) {
            session.Sample(_sample);
            if (_sample.Any(v => !float.IsFinite(v))) throw new InvalidOperationException("Nonfinite preview pose.");
            _revision = state.Revision; _time = state.Time;
        }
        float scale = Math.Min(width,height)*.44f/_radius, cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
        Vector2 Project(System.Numerics.Vector3 position) {
            System.Numerics.Vector3 p = position-_center;
            return new(Math.Clamp(.5f+(cos*p.X+sin*p.Z)*scale/width,-64,64), Math.Clamp(.5f-p.Y*scale/height,-64,64));
        }
        System.Numerics.Vector3 Position(int offset) => new(_sample[offset],_sample[offset+1],_sample[offset+2]);
        _lines.Clear(); int remaining = TriangleLimit, start = state.Bones*16;
        for (int mesh = 0; mesh < state.Meshes; mesh++) {
            uint[] indices = _indices[mesh]; int triangles = Math.Min(remaining,indices.Length/3);
            for (int triangle = 0; triangle < triangles; triangle++)
                for (int edge = 0; edge < 3; edge++)
                    _lines.Add(new(Project(Position(start+checked((int)indices[triangle*3+edge]*3))),
                        Project(Position(start+checked((int)indices[triangle*3+(edge+1)%3]*3))),0xA0BB875Bu,1));
            start += _vertexCounts[mesh]*3; remaining -= triangles;
        }
        DisplayedTriangles = TriangleLimit-remaining;
        for (int bone = 0; bone < state.Bones; bone++) {
            Vector2 point = Project(Position(bone*16+12));
            if (_parents[bone] >= 0) _lines.Add(new(Project(Position(_parents[bone]*16+12)),point,0xFF51A8F4u,2));
            _lines.Add(new(point,point,0xFF7AD6FFu,3));
        }
        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_lines);
    }
}
