using System.Buffers.Binary;
using System.Security.Cryptography;
namespace Ncma.Rendering;

// CPU-only composition of already-owned source-free artifacts. No compiler, native loading or
// GPU credential. The existing NPH1 envelope and query16 admission remain unchanged.
public static class PostProcessComposition
{
    public static PostProcessPackage Programs(IEnumerable<PostProcessPackage> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var inputs = packages.Take(9).ToArray();
        if (inputs.Length is < 1 or > 8 || inputs.Any(p => p is null)) throw new ArgumentException("Post composition package budget.");
        var rows = new Dictionary<Guid, PostProgramData>();
        foreach (var package in inputs) foreach (var p in package.Programs) {
            if (rows.TryGetValue(p.Metadata.Id, out var old)) {
                if (p.Metadata != old.Metadata || !p.Vertex.AsSpan().SequenceEqual(old.Vertex) || !p.Pixel.AsSpan().SequenceEqual(old.Pixel))
                    throw new ArgumentException("Post program UUID collision with different immutable definition.");
            } else if (rows.Count == 8) throw new ArgumentException("Composed post program budget.");
            else rows.Add(p.Metadata.Id, p);
        }
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(0x3148504e); writer.Write(1); writer.Write(0); writer.Write(rows.Count); writer.Write(47); writer.Write(0x48800);
        foreach (var p in rows.Values.OrderBy(p => p.Metadata.Id)) {
            if (stream.Length + 92 + p.Vertex.Length + p.Pixel.Length > PostProcessPackage.MaxBytes) throw new ArgumentException("Composed post byte budget.");
            writer.Write(p.Metadata.Id.ToByteArray()); writer.Write(p.Metadata.Inputs); writer.Write(p.Vertex.Length); writer.Write(p.Pixel.Length);
            writer.Write(Convert.FromHexString(p.Metadata.VertexAuthorHash)); writer.Write(Convert.FromHexString(p.Metadata.PixelAuthorHash)); writer.Write(p.Vertex); writer.Write(p.Pixel);
        }
        byte[] bytes = stream.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), bytes.Length);
        return PostProcessPackage.Preflight(bytes, Convert.ToHexString(SHA256.HashData(bytes)));
    }
    public static PostProcessPlan Chain(uint width, uint height,
        IEnumerable<(PostProcessPlan Plan, PostProcessPackage Programs)> features, PostProcessPackage programs)
    {
        ArgumentNullException.ThrowIfNull(features); ArgumentNullException.ThrowIfNull(programs);
        var selected = features.Take(9).ToArray();
        if (selected.Length is < 1 or > 8) throw new ArgumentException("Post Feature chain budget.");
        var expected = Programs(selected.Select(f => f.Programs));
        if (programs.ContentHash != expected.ContentHash) throw new ArgumentException("Exact composed post programs required.");
        var graph = new PostProcessGraph(); PostResource previous = PostProcessGraph.SceneHdr;
        foreach (var (plan, package) in selected) {
            if (plan is null || plan.Width != width || plan.Height != height || plan.ProgramHash != package.ContentHash)
                throw new ArgumentException("Post Feature plan dimensions/program identity.");
            var resources = new List<PostResource> { previous };
            foreach (var target in plan.CopyTargets()) resources.Add(graph.AddTarget(target));
            foreach (var pass in plan.CopyPasses()) graph.AddPass(pass with {
                Input = resources[pass.Input.Value], Second = pass.Second is { } second ? resources[second.Value] : null, Output = resources[pass.Output.Value] });
            previous = resources[checked((int)plan.Output)];
        }
        return graph.Compile(width, height, previous, programs);
    }
}
