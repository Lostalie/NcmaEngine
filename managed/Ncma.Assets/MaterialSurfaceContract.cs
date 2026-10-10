namespace Ncma.Assets;

public enum MaterialTextureSlot { BaseColor, Normal, Metallic, Roughness, Occlusion, Emissive }
public enum MaterialPreset { Default, Matte, PolishedMetal, Cutout }
public enum PackedSurfaceLayout { OcclusionRoughnessMetallic, MetallicRoughnessOcclusion }
public sealed record MaterialTextureBinding(MaterialTextureSlot Slot, Guid AssetId, TextureSemantic Semantic, uint Channel);
public sealed record MaterialSurfaceDiagnostic(string Code, MaterialTextureSlot Slot, Guid AssetId);

// Pure bounded policy. Linear factors are never gamma decoded; only Color texture RGB is sRGB.
// Alpha and all Normal/Data channels remain linear. No importer inference or live resources.
public static class MaterialSurfaceContract
{
    public const int TextureSlots = 6;
    public static TextureSemantic Semantic(int slot) => slot switch {
        0 or 5 => TextureSemantic.Color, 1 => TextureSemantic.Normal,
        2 or 3 or 4 => TextureSemantic.Data, _ => throw new ArgumentException("Material texture slot.") };
    public static MaterialTextureBinding[] Bindings(MaterialDefinition material)
    {
        MaterialCodec.Validate(material); var ids=material.TextureIds;
        return Enumerable.Range(0,TextureSlots).Select(i=>new MaterialTextureBinding((MaterialTextureSlot)i,ids[i],Semantic(i),
            i==2?material.MetallicChannel:i==3?material.RoughnessChannel:i==4?material.AOChannel:0)).ToArray();
    }
    internal static void ValidateRoles(MaterialDefinition material)
    {
        var ids=material.TextureIds;
        for(int i=0;i<TextureSlots;i++)for(int j=0;j<i;j++)
            if(ids[i]!=Guid.Empty && ids[i]==ids[j] && Semantic(i)!=Semantic(j))throw new ArgumentException("One texture UUID cannot use incompatible material roles.");
    }
    public static MaterialDefinition Apply(MaterialDefinition material,MaterialPreset preset)
    {
        MaterialCodec.Validate(material);
        // Presets change only surface controls. Identity, linear color and authored textures persist.
        var candidate=preset switch {
            MaterialPreset.Default => material with{Mode=MaterialMode.Opaque,Metallic=0,Roughness=.5f,NormalScale=1,AlphaCutoff=.5f},
            MaterialPreset.Matte => material with{Mode=MaterialMode.Opaque,Metallic=0,Roughness=1,NormalScale=1},
            MaterialPreset.PolishedMetal => material with{Mode=MaterialMode.Opaque,Metallic=1,Roughness=.2f,NormalScale=1},
            MaterialPreset.Cutout => material with{Mode=MaterialMode.AlphaMask,Metallic=0,Roughness=.5f,NormalScale=1,AlphaCutoff=.5f},
            _ => throw new ArgumentException("Unregistered material preset.") };
        MaterialCodec.Validate(candidate);return candidate;
    }
    public static MaterialDefinition WithPackedSurface(MaterialDefinition material,Guid texture,PackedSurfaceLayout layout)
    {
        MaterialCodec.Validate(material); if(texture==Guid.Empty||!Enum.IsDefined(layout))throw new ArgumentException("Explicit packed texture/layout required.");
        // Unit factors allow the packed texture's full authored metallic/roughness ranges.
        var candidate=material with{Metallic=1,Roughness=1,MetallicTexture=texture,RoughnessTexture=texture,AOTexture=texture,
            MetallicChannel=layout==PackedSurfaceLayout.OcclusionRoughnessMetallic?2u:0u,RoughnessChannel=1,
            AOChannel=layout==PackedSurfaceLayout.OcclusionRoughnessMetallic?0u:2u};
        MaterialCodec.Validate(candidate);return candidate;
    }
    public static MaterialSurfaceDiagnostic[] Inspect(MaterialDefinition material,Func<Guid,TextureSemantic?> lookup,bool strictMissing)
    {
        ArgumentNullException.ThrowIfNull(lookup);var diagnostics=new List<MaterialSurfaceDiagnostic>();
        foreach(var binding in Bindings(material)) {
            if(binding.AssetId==Guid.Empty)continue;
            var semantic=lookup(binding.AssetId);
            if(semantic is null) {if(strictMissing)throw new ArgumentException("Material texture missing.");diagnostics.Add(new("missing_texture",binding.Slot,binding.AssetId));}
            else if(semantic!=binding.Semantic)throw new ArgumentException("Material texture semantic mismatch.");
        }
        return diagnostics.ToArray();
    }
}
