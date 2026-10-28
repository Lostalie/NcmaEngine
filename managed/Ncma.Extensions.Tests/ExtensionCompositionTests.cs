using Ncma.Extensions;
using Ncma.Extensions.Hosting;
using Ncma.Runtime;

internal static class ExtensionCompositionTests
{
    private static void Check(bool value){if(!value)throw new Exception("Extension composition assertion");}
    private static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected composition rejection");}
    private static ExtensionDescriptor D(string id)=>new(id,new(1,0,0),new(1,0,0));
    internal static int Run()=>CaseHarness.Run("Extension original component aggregation",[
        ("generated assembly contribution uses original metadata defaults and independent registry",()=>{
            var descriptor=D("probe");var plan=ExtensionPlan.Compile(new(1,0,0),descriptor);
            var root=global::Ncma.Extensions.Tests.Generated.ComponentModule.Module;
            var registry=ExtensionComposition.Build(plan,global::Ncma.Runtime.Generated.ComponentModule.Module,new ExtensionComponentContribution(descriptor,root));
            Check(registry.IsRegistered<ExtensionProbe>()&&registry.IsRegistered<TransformData>());
            var data=registry.DefaultSnapshot("test.extension.probe",1);
            Check(registry.Decode<ExtensionProbe>(data).Amount==1&&registry.Describe("test.extension.probe").Metadata!.Fields.Count==1);
            Check(!CompositionRegistry.Build().IsRegistered<ExtensionProbe>());
            var duplicate=ExtensionComposition.Build(plan,root,new ExtensionComponentContribution(descriptor,root));
            Check(duplicate.Describe().Count==registry.Describe().Count);
        }),
        ("foreign duplicate null and over-budget bindings rejected before original callbacks",()=>{
            var a=D("a");var b=D("b");var plan=ExtensionPlan.Compile(new(1,0,0),a,b);int calls=0;
            var module=new ComponentModule("fixture",[],_=>calls++);
            Reject(()=>ExtensionComposition.Build(plan,module,new ExtensionComponentContribution(D("a"),module)));
            Reject(()=>ExtensionComposition.Build(plan,module,new(a,module),new(a,module)));
            Reject(()=>ExtensionComposition.Build(plan,module,[null!]));
            Reject(()=>ExtensionComposition.Build(plan,module,new(a,module),new(b,module),new(a,module)));
            Check(calls==0);
        }),
        ("original module identity collision and callback failures never return partial registry",()=>{
            var a=D("a");var plan=ExtensionPlan.Compile(new(1,0,0),a);int calls=0;
            var host=new ComponentModule("same",[],_=>calls++);var bad=new ComponentModule("same",[],_=>calls++);
            Reject(()=>ExtensionComposition.Build(plan,host,new ExtensionComponentContribution(a,bad)));Check(calls==0);
            var duplicate=new ComponentModule("duplicate",[],r=>TransformData.Generated.Register(r));
            Reject(()=>ExtensionComposition.Build(plan,global::Ncma.Runtime.Generated.ComponentModule.Module,new ExtensionComponentContribution(a,duplicate)));
            Check(CompositionRegistry.Build().Describe().Count==1);
        }),
        ("caller array changes inside callbacks cannot alter captured contribution roots",()=>{
            var a=D("a");var b=D("b");var plan=ExtensionPlan.Compile(new(1,0,0),a,b);int calls=0;
            var good=new ComponentModule("z-good",[],_=>calls++);var bad=new ComponentModule("bad",[],_=>throw new Exception("Mutated roots"));
            ExtensionComponentContribution[] input=[new(a,good),new(b,good)];
            var host=new ComponentModule("a-host",[],_=>input[1]=new(b,bad));
            _=ExtensionComposition.Build(plan,host,input);Check(calls==1);
        })
    ]);
}

[GenerateComponent("test.extension.probe",1,DefinitionSchema,nameof(Validate),nameof(Default))]
[ComponentField("amount","Amount","amount",Flags=ComponentFieldFlags.Persistent|ComponentFieldFlags.Editable|ComponentFieldFlags.RuntimeRead|ComponentFieldFlags.RuntimeWrite)]
public readonly partial record struct ExtensionProbe(float Amount):IComponent
{
    public const string DefinitionSchema="""{"type":"object","additionalProperties":false,"required":["amount"],"properties":{"amount":{"type":"number"}}}""";
    public static ExtensionProbe Default=>new(1);
    public static ExtensionProbe Validate(ExtensionProbe value)=>float.IsFinite(value.Amount)?value:throw new ArgumentException("Nonfinite probe");
}
