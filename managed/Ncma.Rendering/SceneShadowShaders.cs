namespace Ncma.Rendering;

public sealed record SceneShadowComposition(ShaderCatalog Catalog, SceneShaderSelection Selection);

// Declaration-only geometry-stage replacement. Installation still requires the public
// compiler/reflection, exact catalog, trusted approval and runtime package contracts.
// Does not rewrite the native reference source or change its default bytecode.
public static class SceneShadowShaders
{
    public const int HighBlockerReads = 9;
    public const int HighFilterComparisons = 49;
    public const int HighMaximumDepthReads = HighBlockerReads + HighFilterComparisons * 4;
    private static readonly Guid SceneId = Guid.Parse("79500001-0000-4000-8000-000000000001");
    private static readonly Guid EnvironmentId = Guid.Parse("79500001-0000-4000-8000-000000000002");

    public static SceneShadowComposition ComposeHigh(ShaderCatalog source, SceneShaderSelection original)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(original);
        if (source.Profile is not (ShaderProfile.Scene3D or ShaderProfile.SceneEnvironment) ||
            original.ShadowVertex is null || original.ShadowPixel is null)
            throw new ArgumentException("High shadow requires an exact shadowed scene group.");
        ShaderDescriptor[] selected = [original.GeometryVertex, original.GeometryPixel, original.ToneVertex,
            original.TonePixel, original.ShadowVertex, original.ShadowPixel];
        foreach (var item in selected) { ArgumentNullException.ThrowIfNull(item); _ = source.Require(item.AssetId, item.ContentHash); }
        bool environment = source.Profile == ShaderProfile.SceneEnvironment;
        var expected = environment ? DefaultEnvironmentSceneShaders.Definitions("void unused(){}", "void unused(){}", true)
            : DefaultSceneShaders.Definitions("void unused(){}", true);
        for (int i = 0; i < selected.Length; i++)
        {
            if (selected[i].Stage != expected[i].Stage || selected[i].CopyDefinition().Dependencies.Length != 0)
                throw new ArgumentException("Closed typed scene stages required.");
            ShaderBindingValidation.Validate(selected[i], new(selected[i].ContentHash, selected[i].Stage,
                expected[i].Inputs, expected[i].Constants, expected[i].Resources));
        }
        var pixel = CreateHighPixel(source.Profile);
        selected[1] = pixel;
        return new(ShaderCatalog.Create(source.Profile, selected.Select(s => s.CopyDefinition())), original with { GeometryPixel = pixel });
    }

    public static ShaderDescriptor CreateHighPixel(ShaderProfile profile)
    {
        if (profile is not (ShaderProfile.Scene3D or ShaderProfile.SceneEnvironment))
            throw new ArgumentException("High shadow requires a scene pixel profile.", nameof(profile));
        bool environment = profile == ShaderProfile.SceneEnvironment;
        var expected = environment ? DefaultEnvironmentSceneShaders.Definitions("void unused(){}", "void unused(){}", true)
            : DefaultSceneShaders.Definitions("void unused(){}", true);
        string text = Header + (environment ? ",EnvironmentSettings;};\n" + Environment : ";};\n") + Body
            + (environment ? "+EnvironmentLight(n,v,base.rgb,metal,rough,nv,ao)" : "")
            + "+Emissive.rgb*EmissiveTex.Sample(S,i.uv).rgb;return float4(clamp(color,0,65504),1);}";
        return ShaderDescriptor.Prepare(new(1, environment ? EnvironmentId : SceneId,
            "Scene High Shadow Bilinear PCF7", profile, ShaderStage.Pixel, "PSMain", text,
            ShaderContractCodec.HashSource(text), [], expected[1].Constants, expected[1].Resources, []));
    }

    private const string Header = """
        cbuffer C:register(b0){column_major float4x4 MVP,Model,NormalMatrix,LightVP;
        float4 Base,Emissive,Surface,Camera,LightDirection,LightColor,Settings,Channels,ShadowParameters
        """;
    private const string Environment = """
        TextureCube IrradianceTex:register(t7);TextureCube SpecularTex:register(t8);
        Texture2D BrdfTex:register(t9);SamplerState EnvironmentSampler:register(s7);
        float3 EnvironmentDirection(float3 d){return float3(EnvironmentSettings.y*d.x-EnvironmentSettings.z*d.z,d.y,EnvironmentSettings.z*d.x+EnvironmentSettings.y*d.z);}
        float3 EnvironmentLight(float3 n,float3 v,float3 base,float metal,float rough,float nv,float ao){
         if(EnvironmentSettings.x==0)return 0;
         float3 f0=lerp(.04.xxx,base,metal),f=f0+(max((1-rough).xxx,f0)-f0)*pow(1-nv,5);
         float3 irradiance=IrradianceTex.SampleLevel(EnvironmentSampler,EnvironmentDirection(n),0).rgb;
         float3 prefiltered=SpecularTex.SampleLevel(EnvironmentSampler,EnvironmentDirection(reflect(-v,n)),rough*EnvironmentSettings.w).rgb;
         uint width,height;BrdfTex.GetDimensions(width,height);float2 uv=(float2(nv,rough)*(float2(width,height)-1)+.5)/float2(width,height);
         float2 ab=BrdfTex.SampleLevel(EnvironmentSampler,uv,0).rg;
         return ((1-f)*(1-metal)*base*irradiance/3.14159265+prefiltered*(f0*ab.x+ab.y))*ao*EnvironmentSettings.x;
        }
        """;
    private const string Body = """
        Texture2D BaseTex:register(t0);Texture2D NormalTex:register(t1);Texture2D MetalTex:register(t2);
        Texture2D RoughTex:register(t3);Texture2D AOTex:register(t4);Texture2D EmissiveTex:register(t5);
        Texture2D ShadowTex:register(t6);SamplerState S:register(s0);SamplerState SS:register(s6);
        struct Output{float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 n:TEXCOORD1;float4 t:TEXCOORD2;float2 uv:TEXCOORD3;};
        // Bilinear interpolation of FOUR comparisons, never interpolation of blocker depths.
        float CompareBilinear(float2 uv,float z,float texel){
         float2 pixel=uv/texel-.5,base=floor(pixel),fraction=frac(pixel);
         float2 start=(base+.5)*texel;
         float a=z<=ShadowTex.SampleLevel(SS,start,0).r?1:0;
         float b=z<=ShadowTex.SampleLevel(SS,start+float2(texel,0),0).r?1:0;
         float c=z<=ShadowTex.SampleLevel(SS,start+float2(0,texel),0).r?1:0;
         float d=z<=ShadowTex.SampleLevel(SS,start+float2(texel,texel),0).r?1:0;
         return lerp(lerp(a,b,fraction.x),lerp(c,d,fraction.x),fraction.y);
        }
        float Visibility(float3 world,float nl){
         if(Settings.z==0)return 1;float4 q=mul(LightVP,float4(world,1));if(abs(q.w)<1e-6)return 1;q.xyz/=q.w;
         float2 uv=q.xy*float2(.5,-.5)+.5;if(any(uv<0)||any(uv>1)||q.z<0||q.z>1)return 1;
         float z=q.z-ShadowParameters.x-ShadowParameters.y*(1-nl),texel=Channels.w,radius=1;
         if(ShadowParameters.z==1){float blockers=0,count=0;
          [unroll]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++){
           float b=ShadowTex.SampleLevel(SS,uv+float2(x,y)*texel*ShadowParameters.w,0).r;
           if(b<z){blockers+=b;count++;}}
          if(count==0)return 1;radius=clamp((z-blockers/count)*ShadowParameters.w*64,1,8);
         }
         float visibility=0;
         [unroll]for(int y=-3;y<=3;y++)[unroll]for(int x=-3;x<=3;x++)
          visibility+=CompareBilinear(uv+float2(x,y)*(radius/3)*texel,z,texel);
         return visibility/49;
        }
        float4 PSMain(Output i):SV_TARGET{
         float4 base=Base*BaseTex.Sample(S,i.uv);uint flags=(uint)Settings.w;if((flags&1)!=0)clip(base.a-Surface.w);
         float3 n=normalize(i.n);if((flags&2)!=0){float3 t=normalize(i.t.xyz-n*dot(n,i.t.xyz)),b=cross(n,t)*i.t.w;
          float3 m=NormalTex.Sample(S,i.uv).xyz*2-1;m.xy*=Surface.z;if((flags&4)!=0)m.y=-m.y;n=normalize(t*m.x+b*m.y+n*m.z);}
         float metal=saturate(Surface.x*MetalTex.Sample(S,i.uv)[(uint)Channels.x]);float rough=clamp(Surface.y*RoughTex.Sample(S,i.uv)[(uint)Channels.y],.045,1);
         float ao=saturate(AOTex.Sample(S,i.uv)[(uint)Channels.z]);float3 v=normalize(Camera.xyz-i.world),l=normalize(LightDirection.xyz),sum=v+l,h=sum*rsqrt(max(dot(sum,sum),1e-12));
         float nl=saturate(dot(n,l)),nv=max(saturate(dot(n,v)),.0001),nh=saturate(dot(n,h));float a=rough*rough,a2=a*a,d=nh*nh*(a2-1)+1;
         float distribution=a2/max(3.14159265*d*d,.000001),k=(rough+1)*(rough+1)/8;
         float g=nv/(nv*(1-k)+k)*nl/max(nl*(1-k)+k,.0001);float3 f0=lerp(.04.xxx,base.rgb,metal),f=f0+(1-f0)*pow(1-saturate(dot(h,v)),5);
         float3 spec=distribution*g*f/max(4*nv*nl,.0001),diffuse=(1-f)*(1-metal)*base.rgb/3.14159265;
         float3 color=(diffuse+spec)*LightColor.rgb*LightColor.w*nl*Visibility(i.world,nl)+base.rgb*Settings.y*ao
        """;
}
