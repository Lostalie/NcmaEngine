using System.Text.Json.Serialization;
namespace Ncma.Assets;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UiPackageReference([property:JsonRequired] int Version,
    [property:JsonRequired] Guid DocumentId, [property:JsonRequired] ulong Generation,
    [property:JsonRequired] string Path, [property:JsonRequired] string Hash)
{
    public UiPackageReference Validate()
    {
        if (Version != 1 || DocumentId == Guid.Empty || Generation == 0 || Path is null ||
            !Path.EndsWith(".ncupak", StringComparison.Ordinal) || Hash is null || Hash.Length != 64 ||
            Hash.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("UI package identity/version/hash.");
        AssetPaths.Validate(Path, "assets");
        return this with { Hash = Hash.ToUpperInvariant() };
    }
}
