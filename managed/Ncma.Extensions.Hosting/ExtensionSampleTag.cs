using Ncma.Runtime;

namespace Ncma.Extensions.Hosting;

// Small persisted, generated component contribution shared by optional Editor
// and Player. No scene owner, scripts, Editor APIs or per-frame callback.
[GenerateComponent("ncma.extensions.sample-tag",1,DefinitionSchema,nameof(Validate),nameof(Default))]
[ComponentField("value","Value","value",Flags=ComponentFieldFlags.Persistent|ComponentFieldFlags.Editable|ComponentFieldFlags.RuntimeRead)]
public readonly partial record struct ExtensionSampleTag(float Value):IComponent
{
    public const string DefinitionSchema="""{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"number"}}}""";
    public static ExtensionSampleTag Default=>new(1);
    public static ExtensionSampleTag Validate(ExtensionSampleTag value)=>float.IsFinite(value.Value)&&value.Value is >=0 and <=1?value:throw new ArgumentException("extension_sample_tag_value");
}
