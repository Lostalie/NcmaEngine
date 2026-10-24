struct PS_INPUT {
    float4 pos : SV_POSITION;
    float4 col : COLOR0;
    float2 uv : TEXCOORD0;
    nointerpolation uint alphaOnly : TEXCOORD1;
};
sampler sampler0;
Texture2D texture0;
float4 main(PS_INPUT input) : SV_Target
{
    float4 sampled = texture0.Sample(sampler0, input.uv);
    if (input.alphaOnly != 0)
        sampled = float4(1, 1, 1, sampled.r);
    return input.col * sampled;
}
