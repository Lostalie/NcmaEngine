using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Editor.Core;

namespace Ncma.Editor.Services;

// Local content review only: NOT an observation/export grant, model input or write ticket.
// The complete selected record can contain private paths. Never attach it to summary reads.
public sealed class MetadataContentReview
{
    internal readonly EditorMetadataContent Owner;
    internal readonly DomainProposalSource Source;
    internal readonly ulong AssetRevision;
    internal readonly long Started;
    internal readonly AssetKind Kind;
    internal readonly string Path;
    private readonly byte[] bytes;
    public Guid AssetId { get; }
    public string SourceSha256 { get; }
    public string Fingerprint { get; }
    internal MetadataContentReview(EditorMetadataContent owner, DomainProposalSource source, ulong revision,
        long started, Guid id, AssetKind kind, string path, string hash, byte[] content)
    {
        Owner = owner; Source = source; AssetRevision = revision; Started = started;
        AssetId = id; Kind = kind; Path = path; SourceSha256 = hash; bytes = (byte[])content.Clone();
        Fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
    }
    public byte[] CopyBytes() => (byte[])bytes.Clone();
}

// Constructed only by the actual owner using its configured root; no caller-supplied file path.
public sealed partial class EditorMetadataContent
{
    public const int MaximumBytes = 65_536;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly EditorSessionOwner owner;
    private readonly AssetProjectAuthoring assets;
    private readonly EditorDomainProposals domains;
    private readonly string root;
    private readonly TimeProvider time;
    private MetadataContentReview? latest;
    private bool busy;
    internal EditorMetadataContent(EditorSessionOwner owner, string root, TimeProvider? time)
    {
        this.owner = owner; this.root = root; this.time = time ?? TimeProvider.System;
        assets = owner.Assets ?? throw new InvalidOperationException("metadata_assets_required");
        domains = owner.DomainProposals ?? throw new InvalidOperationException("metadata_domains_required");
    }
    private void Verify()
    {
        if (thread != Environment.CurrentManagedThreadId || busy)
            throw new InvalidOperationException("metadata_content_owner_or_reentry");
    }
    private DomainProposalSource Source()
    {
        if (!ReferenceEquals(owner.Assets, assets) || !ReferenceEquals(owner.DomainProposals, domains) || owner.Play is not null ||
            assets.SnapshotRevision != assets.Clock.Revision)
            throw new EditRejectedException("metadata_content_source_unavailable");
        return domains.CaptureSource();
    }
    private (string Path, AssetRecord Record) Resolve(Guid exactRoot, AssetKind kind)
    {
        if (exactRoot == Guid.Empty || !Enum.IsDefined(kind)) throw new EditRejectedException("metadata_exact_root_required");
        var snapshot = assets.Snapshot;
        if (!snapshot.Catalog.TryResolve(new(new(exactRoot), kind), out var record, out _) || record is null || record.AssetId != exactRoot ||
            snapshot.MetadataFiles is null || !snapshot.MetadataFiles.TryGetValue(exactRoot, out string? path))
            throw new EditRejectedException("metadata_exact_root_required");
        // Match the ORIGINAL metadata writer's supported slice, without broadening import authority.
        if (record.Generation is not null || record.Subassets.Length != 0 || record.Dependencies.Length != 0)
            throw new EditRejectedException("metadata_imported_record_unsupported");
        path = AssetPaths.Validate(path);
        if (!path.EndsWith(".ncmeta", StringComparison.Ordinal)) throw new EditRejectedException("metadata_path_required");
        return (path, record);
    }
    private byte[] Read(string path)
    {
        var paths = new AssetProjectPaths(root);
        string journal = paths.Resolve(path + ".journal");
        if (File.Exists(journal) || Directory.Exists(journal)) throw new EditRejectedException("metadata_recovery_required");
        // Shared bounded handle/parent leases reject hardlinks/reparse points; no source-asset read.
        return UiAuthoringSource.Read(root, path, MaximumBytes);
    }
    public MetadataContentReview Capture(Guid exactRoot, AssetKind kind)
    {
        Verify(); latest = null; busy = true;
        try {
            var source = Source(); ulong revision = assets.Clock.Revision;
            var selected = Resolve(exactRoot, kind);
            byte[] raw = Read(selected.Path), canonical = AssetRecordCodec.Encode(AssetRecordCodec.Decode(raw));
            if (!canonical.AsSpan().SequenceEqual(AssetRecordCodec.Encode(selected.Record)))
                throw new EditRejectedException("metadata_catalog_file_mismatch_refresh_required");
            string hash = Convert.ToHexString(SHA256.HashData(raw));
            using var record = JsonDocument.Parse(canonical);
            long started = time.GetTimestamp();
            byte[] content = JsonSerializer.SerializeToUtf8Bytes(new {
                version = 1, purpose = "local-review-only-not-export-approved", source, assetRevision = revision,
                assetId = exactRoot, metadataPath = selected.Path, rawSha256 = hash, record = record.RootElement
            });
            if (content.Length > MaximumBytes) throw new EditRejectedException("metadata_content_byte_budget");
            if (source != Source() || revision != assets.Clock.Revision)
                throw new EditRejectedException("metadata_content_stale");
            return latest = new(this, source, revision, started, exactRoot, kind, selected.Path, hash, content);
        }
        finally { busy = false; }
    }
    // Cheap identity/lifetime checks only; does NOT claim the external file is still identical.
    public bool IsCurrent(MetadataContentReview review)
    {
        Verify(); busy = true;
        try { return Current(review); }
        catch (Exception e) when (e is InvalidOperationException or EditRejectedException) { return false; }
        finally { busy = false; }
    }
    private bool Current(MetadataContentReview review) => ReferenceEquals(review, latest) && ReferenceEquals(review.Owner, this) &&
        time.GetElapsedTime(review.Started) < TimeSpan.FromSeconds(EditorDomainProposals.ReviewSeconds) &&
        review.Source == Source() && review.AssetRevision == assets.Clock.Revision;
    // Explicit local revalidation, not approval. No drawing/polling caller should perform this IO.
    public void ValidateCurrent(MetadataContentReview review)
    {
        Verify(); busy = true;
        try {
            if (!Current(review)) throw new EditRejectedException("metadata_content_stale");
            var selected = Resolve(review.AssetId, review.Kind);
            if (selected.Path != review.Path || Convert.ToHexString(SHA256.HashData(Read(selected.Path))) != review.SourceSha256 || !Current(review))
                throw new EditRejectedException("metadata_content_file_changed");
        }
        catch { latest = null; throw; }
        finally { busy = false; }
    }
    public void Invalidate() { Verify(); latest = null; }
}
