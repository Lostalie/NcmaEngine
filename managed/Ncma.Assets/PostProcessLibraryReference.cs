using System.Text.Json.Serialization;
namespace Ncma.Assets;

// Explicit project selection. The index path is fixed by the library contract, not executable input.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PostProcessLibraryReference([property:JsonRequired] int Version,[property:JsonRequired] string IndexHash)
{
    public PostProcessLibraryReference Validate()
    {
        if(Version!=1||IndexHash is null||IndexHash.Length!=64||IndexHash.Any(c=>!char.IsAsciiHexDigit(c)))throw new ArgumentException("Post library reference version/hash.");
        return this with{IndexHash=IndexHash.ToUpperInvariant()};
    }
}
