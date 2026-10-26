using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Editor.Core;

namespace Ncma.Editor.Services;

// A typed local selection, never a remote capability or export/write grant.
public sealed class MetadataDomainObservation
{
    internal readonly EditorMetadataContent Owner;
    internal readonly MetadataContentReview Review;
    public DomainProposalSource Source => Review.Source;
    public Guid AssetId => Review.AssetId;
    internal MetadataDomainObservation(EditorMetadataContent owner, MetadataContentReview review) { Owner = owner; Review = review; }
    public bool IsCurrent() => Owner.DomainCurrent(this);
    internal bool IdentityCurrent() => Owner.DomainCurrent(this, includeAssets: false);
    internal JsonElement Read() => Owner.ReadDomain(this);
}

public sealed partial class EditorMetadataContent
{
    public MetadataDomainObservation SelectDomainObservation(DomainProposalSource source, Guid exactRoot, AssetKind kind)
    {
        Verify();
        if (source != Source()) throw new EditRejectedException("metadata_observation_source_mismatch");
        var review = Capture(exactRoot, kind);
        if (review.Source != source) { Invalidate(); throw new EditRejectedException("metadata_observation_source_mismatch"); }
        return new(this, review);
    }
    internal bool DomainCurrent(MetadataDomainObservation observation, bool includeAssets = true)
    {
        Verify(); busy = true;
        try {
            var review = observation.Review;
            return ReferenceEquals(observation.Owner, this) && ReferenceEquals(review, latest) &&
                time.GetElapsedTime(review.Started) is var elapsed && elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromSeconds(EditorDomainProposals.ReviewSeconds) &&
                ReferenceEquals(owner.Assets, assets) && ReferenceEquals(owner.DomainProposals, domains) && owner.Play is null &&
                (!includeAssets || review.AssetRevision == assets.Clock.Revision && assets.SnapshotRevision == assets.Clock.Revision) &&
                domains.ObservationSourceCurrent(review.Source);
        }
        catch (Exception e) when (e is InvalidOperationException or EditRejectedException) { return false; }
        finally { busy = false; }
    }
    internal JsonElement ReadDomain(MetadataDomainObservation observation)
    {
        if (!DomainCurrent(observation)) throw new EditRejectedException("metadata_observation_stale");
        var review = observation.Review;
        try {
            var selected = Resolve(review.AssetId, review.Kind);
            if (selected.Path != review.Path || Convert.ToHexString(SHA256.HashData(Read(selected.Path))) != review.SourceSha256 || !DomainCurrent(observation))
                throw new EditRejectedException("metadata_observation_file_changed");
            using var document = JsonDocument.Parse(review.CopyBytes());
            // Exact full selected content, visibly exported only after original request approval.
            return JsonSerializer.SerializeToElement(new { assetId = review.AssetId, assetRevision = review.AssetRevision,
                metadataPath = review.Path, rawSha256 = review.SourceSha256, record = document.RootElement.GetProperty("record") });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or JsonException) {
            latest = null; throw new EditRejectedException("metadata_observation_source_unavailable");
        }
    }
    // Host-owned envelope only. Original domain review/participant enforces path/source/hash,
    // explicit write scope and exact before/after bytes; this method performs no file mutation.
    public byte[] PrepareSettingsProposal(MetadataDomainObservation observation, int version, int sampleRate, bool generateTangents)
    {
        var data = ReadDomain(observation);
        var original = AssetRecordCodec.Decode(Encoding.UTF8.GetBytes(data.GetProperty("record").GetRawText()));
        using var candidate = JsonDocument.Parse(AssetRecordCodec.Encode(original with { Settings = new(version, sampleRate, generateTangents) }));
        byte[] result = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, capability = AssetMetadataCommands.CapabilityName,
            input = new { operation = "settings", path = observation.Review.Path, expectedAssetRevision = observation.Review.AssetRevision, record = candidate.RootElement } });
        if (result.Length > EditorDomainProposals.MaximumBytes) throw new EditRejectedException("metadata_proposal_byte_budget");
        return result;
    }
}
