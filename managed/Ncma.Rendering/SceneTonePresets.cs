namespace Ncma.Rendering;

// These are scene-linear -> SDR display curves, not complete ACES color management.
public enum SceneToneCurve { AcesFitted, Reinhard, LinearClamp }
public sealed record SceneToneSelection(ShaderCatalog Catalog, ShaderDescriptor Vertex, ShaderDescriptor Pixel);
public sealed record SceneToneComposition(ShaderCatalog Catalog, SceneShaderSelection Selection)
{
    // The same public, declaration-only path for official and user-authored Tone programs.
    // Compilation, real reflection, GPU installation and trusted permission remain separate.
    public static SceneToneComposition Compose(ShaderCatalog source, SceneShaderSelection stages, SceneToneSelection tone)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(stages);ArgumentNullException.ThrowIfNull(tone);
        ArgumentNullException.ThrowIfNull(tone.Catalog);ArgumentNullException.ThrowIfNull(tone.Vertex);ArgumentNullException.ThrowIfNull(tone.Pixel);
        if(source.Profile is not (ShaderProfile.Scene3D or ShaderProfile.SceneEnvironment)||tone.Catalog.Profile!=source.Profile)
            throw new ArgumentException("Tone requires an exact scene profile, never Flat2D/Skinning.");
        bool shadow=stages.ShadowVertex is not null;if(shadow!=(stages.ShadowPixel is not null))throw new ArgumentException("Complete shadow pair required.");
        ShaderDescriptor[] original=shadow?[stages.GeometryVertex,stages.GeometryPixel,stages.ToneVertex,stages.TonePixel,stages.ShadowVertex!,stages.ShadowPixel!]:[stages.GeometryVertex,stages.GeometryPixel,stages.ToneVertex,stages.TonePixel];
        foreach(var shader in original){ArgumentNullException.ThrowIfNull(shader);_=source.Require(shader.AssetId,shader.ContentHash);}
        var vertex=tone.Catalog.Require(tone.Vertex.AssetId,tone.Vertex.ContentHash);var pixel=tone.Catalog.Require(tone.Pixel.AssetId,tone.Pixel.ContentHash);
        if(vertex.Stage!=ShaderStage.Vertex||pixel.Stage!=ShaderStage.Pixel||vertex.CopyDefinition().Dependencies.Length!=0||pixel.CopyDefinition().Dependencies.Length!=0)
            throw new ArgumentException("Closed Tone vertex/pixel pair required.");
        ShaderBindingValidation.Validate(vertex,new(vertex.ContentHash,ShaderStage.Vertex,DefaultSceneTone.Inputs,[],[]));
        ShaderBindingValidation.Validate(pixel,new(pixel.ContentHash,ShaderStage.Pixel,[],DefaultSceneTone.Constants,DefaultSceneTone.Resources));
        var selected=stages with{ToneVertex=vertex,TonePixel=pixel};original[2]=vertex;original[3]=pixel;
        var catalog=ShaderCatalog.Create(source.Profile,original.Select(s=>s.CopyDefinition()));
        return new(catalog,selected);
    }
}

public static class SceneTonePresets
{
    private static readonly Guid VertexId=Guid.Parse("7a14ee01-00a1-44b2-8844-000000000001");
    private static readonly Guid[] PixelIds=[Guid.Parse("7a14ee01-00a1-44b2-8844-000000000010"),Guid.Parse("7a14ee01-00a1-44b2-8844-000000000011"),Guid.Parse("7a14ee01-00a1-44b2-8844-000000000012")];
    // Deliberate independent small source; no textual rewriting of native reference shaders.
    private const string Header="""
        cbuffer C:register(b0){column_major float4x4 MVP,Model,NormalMatrix,LightVP;
        float4 Base,Emissive,Surface,Camera,LightDirection,LightColor,Settings,Channels,ShadowParameters;};
        Texture2D BaseTex:register(t0);SamplerState S:register(s0);
        struct Full{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
        Full VSTone(uint id:SV_VertexID){Full o;o.uv=float2((id<<1)&2,id&2);o.p=float4(o.uv*float2(2,-2)+float2(-1,1),0,1);return o;}
        float4 PSTone(Full i):SV_TARGET{float3 c=max(BaseTex.SampleLevel(S,i.uv,0).rgb,0)*Settings.x;
        """;
    private const string Footer="""
        c=lerp(12.92*c,1.055*pow(max(c,0),1.0/2.4)-.055,step(.0031308,c));return float4(c,1);}
        """;
    public static float ExposureFromStops(double stops)
    {
        if(!double.IsFinite(stops)||stops is < -6 or > 4)throw new ArgumentOutOfRangeException(nameof(stops),"Supported EV range is [-6,+4].");
        return (float)Math.Pow(2,stops);
    }
    public static SceneToneSelection Create(SceneToneCurve curve,ShaderProfile profile=ShaderProfile.Scene3D)
    {
        if(!Enum.IsDefined(curve))throw new ArgumentOutOfRangeException(nameof(curve));
        if(profile is not (ShaderProfile.Scene3D or ShaderProfile.SceneEnvironment))throw new ArgumentException("Exact scene profile required.",nameof(profile));
        string operation=curve switch{SceneToneCurve.AcesFitted=>"c=saturate((c*(2.51*c+.03))/(c*(2.43*c+.59)+.14));",SceneToneCurve.Reinhard=>"c=c/(1+c);",SceneToneCurve.LinearClamp=>"c=saturate(c);",_=>throw new ArgumentOutOfRangeException(nameof(curve))};
        string source=Header+"\n"+operation+"\n"+Footer;string hash=ShaderContractCodec.HashSource(source);
        var vertex=ShaderDescriptor.Prepare(new(1,VertexId,"Scene Tone Vertex",profile,ShaderStage.Vertex,"VSTone",source,hash,DefaultSceneTone.Inputs,[],[],[]));
        var pixel=ShaderDescriptor.Prepare(new(1,PixelIds[(int)curve],"Scene Tone "+curve,profile,ShaderStage.Pixel,"PSTone",source,hash,[],DefaultSceneTone.Constants,DefaultSceneTone.Resources,[]));
        return new(ShaderCatalog.Create(profile,[vertex.CopyDefinition(),pixel.CopyDefinition()]),vertex,pixel);
    }
}
