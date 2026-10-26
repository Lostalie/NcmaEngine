using System.Text.Json;
using Ncma.Assets.Authoring.Storage;
using Ncma.Editor.Core;

namespace Ncma.Assets.Authoring;

internal static class AssetCommandReview
{
    internal static JsonElement Describe(ParticipantMemento memento)
    {
        object[] Decode(byte[] bytes) => AssetDiskTransaction.Decode(bytes).Select(image => {
            // Only inline semantic authoring JSON is supported by these three participants.
            // Never expose an internal immutable blob path or truncate binary/large values.
            if (image.Blob is not null) throw new EditRejectedException("asset_review_binary_unsupported");
            JsonElement? value = null;
            if (image.Data is not null) { using var json = JsonDocument.Parse(image.Data); value = json.RootElement.Clone(); }
            return (object)new { path = image.Path, sha256 = image.Hash, value };
        }).ToArray();
        return JsonSerializer.SerializeToElement(new { version = 1, before = Decode(memento.Before), after = Decode(memento.After) });
    }
}
