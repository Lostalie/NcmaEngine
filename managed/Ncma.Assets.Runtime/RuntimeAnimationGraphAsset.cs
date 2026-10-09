using Ncma.Animation;
namespace Ncma.Assets.Runtime;

// Owned canonical data. Content generation is independent of the numerical skeleton generation.
public sealed class RuntimeAnimationGraphAsset : RuntimeAsset
{
    private readonly AnimationGraphDocument _document;
    private Guid _preparedPublication;
    private AnimationProgram? _preparedProgram;
    internal RuntimeAnimationGraphAsset(byte[] bytes, ulong generation, string hash)
        : this(AnimationGraphCodec.Decode(bytes), generation, hash) { }
    internal RuntimeAnimationGraphAsset(AnimationGraphDefinition definition, ulong generation, string hash)
        : base(definition.AssetId, AssetKind.AnimationGraph, generation, hash)
    {
        _document = new(definition);
        string canonical = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_document.CopyBytes()));
        ulong token = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(Convert.FromHexString(canonical));
        if (canonical != hash || generation != (token == 0 ? 1 : token)) throw new ArgumentException("Graph canonical hash/generation mismatch.");
    }
    public AnimationGraphDefinition CopyDefinition() => _document.CopyDefinition();
    public byte[] CopyData() => _document.CopyBytes();
    // Trusted OFF-frame preparation, using actual retained NCA/package bytes, not supplied descriptors.
    public AnimationProgram PrepareProgram(RuntimeAssetLease lease) => PrepareProgram(lease, retain: true);
    // Package/catalog preflight validates ALL included graphs, including unused ones. Do not
    // retain compiled programs just because a package carries an unused animation asset.
    internal void ValidateProgram(RuntimeAssetLease lease) => _ = PrepareProgram(lease, retain: false);
    private AnimationProgram PrepareProgram(RuntimeAssetLease lease, bool retain)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!ReferenceEquals(lease.Require(Id, AssetKind.AnimationGraph), this)) throw new ArgumentException("Graph lease identity mismatch.");
        // One immutable program per exact publication, not playback state or an asset lease.
        // Require above retains disposed/foreign/owner-thread rejection even on cache hits.
        if (retain && _preparedPublication == lease.Identity && _preparedProgram is { } prepared) return prepared;
        var graph = CopyDefinition();
        var skeleton = (RuntimeDataAsset)lease.Require(graph.SkeletonId, AssetKind.Skeleton);
        var root = (RuntimeDataAsset)lease.Require(skeleton.ModelId, AssetKind.Character);
        var manifest = ModelAssetManifestCodec.Decode(root.CopyData());
        if (root.Generation != skeleton.Generation || root.SkeletonId != skeleton.Id || skeleton.SkeletonId != skeleton.Id ||
            manifest.Skeleton != skeleton.Id) throw new ArgumentException("Graph exact skeleton/model generation required.");
        var layout=DescribeSkeleton(skeleton);int bones=layout.Bones.Length;
        var clips = new List<AnimationClipDescriptor>();
        foreach (var dependency in AnimationGraphValidation.Dependencies(graph).Where(d => !d.IsSkeleton)) {
            var asset = (RuntimeDataAsset)lease.Require(dependency.Id, AssetKind.Clip);
            var clip = ModelPayloadCodec.DecodeClip(asset.CopyData());
            if (asset.ModelId != root.Id || asset.SkeletonId != skeleton.Id || asset.Generation != skeleton.Generation ||
                clip.BoneCount != bones || !manifest.Clips.Contains(asset.Id)) throw new ArgumentException("Graph exact clip closure required.");
            clips.Add(new(asset.Id, skeleton.Id, skeleton.Generation, clip.Clip.Duration));
        }
        var program = AnimationProgram.Compile(graph, skeleton.Generation, clips,skeleton:layout);
        if ((long)program.MaximumPlanInstructions * bones > 65536) throw new ArgumentException("Graph numerical scratch budget exceeded.");
        if (retain) { _preparedPublication = lease.Identity; _preparedProgram = program; }
        return program;
    }
    public static AnimationSkeletonDescriptor DescribeSkeleton(RuntimeDataAsset skeleton)
    {
        if(skeleton.Kind!=AssetKind.Skeleton)throw new ArgumentException("Skeleton data required.");
        var bones=ModelPayloadCodec.DecodeSkeleton(skeleton.CopyData()).Bones;var rows=new AnimationBoneIdentity[bones.Length];
        for(int i=0;i<bones.Length;i++){string segment=Uri.EscapeDataString(bones[i].Name);if(segment is "." or "..")segment=segment.Replace(".","%2E",StringComparison.Ordinal);rows[i]=new(bones[i].Parent<0?segment:rows[bones[i].Parent].Path+"/"+segment,bones[i].Parent);}
        return new(skeleton.Id,skeleton.ContentHash,rows);
    }
}
