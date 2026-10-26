using System.Text.Json;
using Ncma.Editor.Services;

namespace Ncma.Editor.Modules;

// Trusted local presentation ticket only. Not an AI output schema, remote approval or writer.
public sealed class AiAnimationResourceReview
{
    private readonly byte[] bytes;
    internal AnimationGraphWriteReview Original { get; }
    public Guid Graph => Original.Graph;
    public string Fingerprint => Original.Fingerprint;
    internal AiAnimationResourceReview(AnimationGraphWriteReview original, byte[] candidate)
    {
        Original = original with { Dependencies = (Guid[])original.Dependencies.Clone(),
            ResourceHashes = (string[])original.ResourceHashes.Clone(), Audience = (Guid[])original.Audience.Clone() };
        using var json = JsonDocument.Parse(candidate);
        bytes = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, review = Original, completeCandidate = json.RootElement });
        if (bytes.Length > 1024 * 1024) throw new ArgumentException("ai_resource_review_budget");
    }
    public byte[] CopyBytes() => (byte[])bytes.Clone();
}
