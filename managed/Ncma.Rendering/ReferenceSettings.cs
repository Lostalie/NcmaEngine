using System.Runtime.InteropServices;
namespace Ncma.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct ReferenceSettings
{
    public uint StructSize,ShadowEnabled,ShadowFilter,ContactEnabled,ContactSteps,Reserved;
    public float BaseRed,BaseGreen,BaseBlue,LightIntensity,Ambient,ConstantBias,SlopeBias,MaxDistance,CascadeLambda,LightRadius,ContactDistance,ContactThickness,ContactStrength;
    public static ReferenceSettings From(RenderConfiguration config) {
        return new() {StructSize=76,ShadowEnabled=config.ShadowEnabled?1u:0u,ShadowFilter=(uint)config.ShadowFilter,
            ContactEnabled=config.ContactEnabled?1u:0u,ContactSteps=(uint)config.ContactSteps,
            BaseRed=config.BaseRed,BaseGreen=config.BaseGreen,BaseBlue=config.BaseBlue,LightIntensity=config.LightIntensity,Ambient=config.Ambient,
            ConstantBias=config.ConstantBias,SlopeBias=config.SlopeBias,MaxDistance=config.ShadowDistance,CascadeLambda=config.CascadeLambda,
            LightRadius=config.LightRadius,ContactDistance=config.ContactDistance,ContactThickness=config.ContactThickness,ContactStrength=config.ContactStrength};
    }
}
