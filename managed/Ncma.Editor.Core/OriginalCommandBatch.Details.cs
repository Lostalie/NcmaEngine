using System.Text.Json;
using Ncma.Scene;

namespace Ncma.Editor.Core;

public sealed partial class EditSession
{
    // Cold local review of the ORIGINAL candidate, never a second transaction implementation.
    // Full affected object values (including removals) or an explicit budget failure; no truncation.
    public byte[] DescribeOriginalBatchChanges(OriginalCommandBatchReview review)
    {
        ArgumentNullException.ThrowIfNull(review); Observe();
        if (_invoking || InteractionBusy || _invalidated || review.SessionId != SessionId ||
            review.DocumentSessionId != _document.SessionId || review.DocumentGeneration != DocumentGeneration ||
            review.BehaviourCatalogGeneration != BehaviourCatalogGeneration || review.Revision != Revision)
            throw new EditRejectedException("batch_source_stale");
        _invoking = true;
        try {
            var candidate = BuildOriginalBatch(review.Input(), null);
            byte[] after = [];
            _ = _document.PrepareRestore(candidate.After, review.Revision, normalized => after = SceneDocumentCodec.Encode(normalized));
            if (Hash(candidate.Before) != review.BeforeHash || Hash(after) != review.AfterHash)
                throw new EditRejectedException("batch_source_stale");
            var old = SceneDocumentCodec.Decode(candidate.Before).Objects.ToDictionary(x => x.Id);
            var next = SceneDocumentCodec.Decode(after).Objects.ToDictionary(x => x.Id);
            var impact = MergeImpact(candidate.Intent, Impact(candidate.Before, after, _selection, candidate.Selection, false));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, review.Fingerprint,
                beforeSelection = _selection, afterSelection = candidate.Selection,
                changes = impact.Objects.Select(id => new { objectId = id, before = old.GetValueOrDefault(id), after = next.GetValueOrDefault(id) }) }, OutputJson);
            if (bytes.Length > MaxInputBytes) throw new EditRejectedException("batch_review_byte_budget");
            return bytes;
        }
        finally { _invoking = false; }
    }
}
