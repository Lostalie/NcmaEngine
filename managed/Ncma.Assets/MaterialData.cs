using System.Text.Json;
using System.Text.Json.Serialization;
namespace Ncma.Assets;
public enum MaterialMode { Opaque,AlphaMask }
public readonly record struct LinearColor([property:JsonRequired] float R,[property:JsonRequired] float G,[property:JsonRequired] float B,[property:JsonRequired] float A);
public sealed record MaterialDefinition(
    [property:JsonRequired] int Version,[property:JsonRequired] Guid AssetId,[property:JsonRequired] MaterialMode Mode,
    [property:JsonRequired] LinearColor BaseColor,[property:JsonRequired] LinearColor Emissive,
    [property:JsonRequired] float Metallic,[property:JsonRequired] float Roughness,[property:JsonRequired] float NormalScale,[property:JsonRequired] float AlphaCutoff,
    [property:JsonRequired] Guid BaseTexture,[property:JsonRequired] Guid NormalTexture,[property:JsonRequired] Guid MetallicTexture,
    [property:JsonRequired] Guid RoughnessTexture,[property:JsonRequired] Guid AOTexture,[property:JsonRequired] Guid EmissiveTexture,
    [property:JsonRequired] uint MetallicChannel,[property:JsonRequired] uint RoughnessChannel,[property:JsonRequired] uint AOChannel)
{
    public static MaterialDefinition Default(Guid id)=>new(1,id,MaterialMode.Opaque,new(1,1,1,1),new(0,0,0,0),0,.5f,1,.5f,Guid.Empty,Guid.Empty,Guid.Empty,Guid.Empty,Guid.Empty,Guid.Empty,0,0,0);
    [JsonIgnore] public Guid[] TextureIds=>[BaseTexture,NormalTexture,MetallicTexture,RoughnessTexture,AOTexture,EmissiveTexture];
}
public sealed record MaterialSetDefinition([property:JsonRequired] int Version,[property:JsonRequired] Guid AssetId,[property:JsonRequired] Guid[] Materials);
/// <summary>Strict .ncmaterial / .ncmatset JSON1. UUIDs only, no native handles, no source material aliases.</summary>
public static class MaterialCodec
{
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=12,
        Converters={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase,allowIntegerValues:false)}};
    public static void Validate(MaterialDefinition d)
    {
        ArgumentNullException.ThrowIfNull(d);
        if(d.Version!=1||d.AssetId==Guid.Empty||!Enum.IsDefined(d.Mode))throw new ArgumentException("Material identity/version/mode.");
        Unit(d.BaseColor.R);Unit(d.BaseColor.G);Unit(d.BaseColor.B);Unit(d.BaseColor.A);
        foreach(float x in new[]{d.Emissive.R,d.Emissive.G,d.Emissive.B,d.Emissive.A})if(!float.IsFinite(x)||x<0||x>16)throw new ArgumentException("Emissive range.");
        Unit(d.Metallic);Unit(d.Roughness);Unit(d.AlphaCutoff);
        if(d.Roughness<.045f||!float.IsFinite(d.NormalScale)||d.NormalScale<0||d.NormalScale>4||d.MetallicChannel>3||d.RoughnessChannel>3||d.AOChannel>3)throw new ArgumentException("Material scalar/channel.");
        MaterialSurfaceContract.ValidateRoles(d);
    }
    public static void Validate(MaterialSetDefinition d){ArgumentNullException.ThrowIfNull(d);if(d.Version!=1||d.AssetId==Guid.Empty||d.Materials is null||d.Materials.Length is <1 or >4096||d.Materials.Contains(Guid.Empty))throw new ArgumentException("Material set version/identity/slots.");}
    public static byte[] Encode(MaterialDefinition d){Validate(d);return JsonSerializer.SerializeToUtf8Bytes(d,Json);}
    public static byte[] Encode(MaterialSetDefinition d){Validate(d);return JsonSerializer.SerializeToUtf8Bytes(d,Json);}
    public static MaterialDefinition Decode(ReadOnlySpan<byte> bytes){Check(bytes);var d=JsonSerializer.Deserialize<MaterialDefinition>(bytes,Json)??throw new ArgumentException("Empty material.");Validate(d);return d;}
    public static MaterialSetDefinition DecodeSet(ReadOnlySpan<byte> bytes){Check(bytes);var d=JsonSerializer.Deserialize<MaterialSetDefinition>(bytes,Json)??throw new ArgumentException("Empty material set.");Validate(d);return d;}
    private static void Unit(float x){if(!float.IsFinite(x)||x<0||x>1)throw new ArgumentException("Finite unit scalar required.");}
    private static void Check(ReadOnlySpan<byte> bytes){if(bytes.Length is <2 or >4*1024*1024)throw new ArgumentException("Material JSON budget.");using var doc=JsonDocument.Parse(bytes.ToArray(),new JsonDocumentOptions{MaxDepth=12});Visit(doc.RootElement);}
    private static void Visit(JsonElement e){if(e.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject()){if(!names.Add(p.Name))throw new ArgumentException("Duplicate material JSON field.");Visit(p.Value);}}else if(e.ValueKind==JsonValueKind.Array)foreach(var x in e.EnumerateArray())Visit(x);}
}
public sealed record MaterialConversionResult(MaterialDefinition Material,string[] Diagnostics);
public static class MaterialConversion
{
    // G2 stores names/slots only. Do not invent Phong parameters/textures not present in imported data.
    public static MaterialConversionResult FromImportedSlot(Guid id,string name)
    {if(string.IsNullOrWhiteSpace(name)||name.Length>1024)throw new ArgumentException("Imported slot name.");return new(MaterialDefinition.Default(id),["material_slots_only: "+name,"pbr_default: source Phong/nonstandard values and textures were not imported; explicit author override required"]);}
}
