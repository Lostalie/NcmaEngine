using System.Text.Json.Serialization;
namespace Ncma.Assets;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ShadowLibraryReference([property:JsonRequired] int Version,[property:JsonRequired] string IndexHash)
{
    public ShadowLibraryReference Validate()
    {
        if(Version!=1||IndexHash is null||IndexHash.Length!=64||IndexHash.Any(c=>!char.IsAsciiHexDigit(c)))throw new ArgumentException("Shadow library reference version/hash.");
        return this with{IndexHash=IndexHash.ToUpperInvariant()};
    }
}
