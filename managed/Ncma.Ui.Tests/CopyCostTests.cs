using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Ui;

internal static class CopyCostTests
{
    private static void Check(bool value,string message){if(!value)throw new Exception("UI copy: "+message);}
    private static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("UI copy accepted invalid input");}
    // Independent wire-size oracle using the public schema, not the product copy/count path.
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,MaxDepth=16,
        UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,Converters={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase,false)}};
    private static byte[] Wire(UiDefinition d)=>JsonSerializer.SerializeToUtf8Bytes(d,Json);
    private static UiDefinition Large()
    {
        var d=UiDefinition.Create(Guid.NewGuid(),"Large copy");
        return d with{Elements=[d.Elements[0],..Enumerable.Range(1,3074).Select(i=>UiElement.Create(Guid.NewGuid(),"Cell "+i,UiKind.Rectangle,d.Root) with{Order=i})]};
    }
    public static IEnumerable<(string Name,Action Run)> Cases()
    {
        yield return("Owned UI capture copies arrays without serializing or allocating per element",()=>{
            var d=Large();_ = new UiDocument(d).Capture();
            long bytes=GC.GetAllocatedBytesForCurrentThread(),time=Stopwatch.GetTimestamp();var owned=new UiDocument(d);
            double createMs=Stopwatch.GetElapsedTime(time).TotalMilliseconds;long createBytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
            bytes=GC.GetAllocatedBytesForCurrentThread();time=Stopwatch.GetTimestamp();var copy=owned.Capture();
            double captureMs=Stopwatch.GetElapsedTime(time).TotalMilliseconds;long captureBytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
            Console.WriteLine(JsonSerializer.Serialize(new{scope="3075-element UiDocument constructor and owned Capture; warmed serializer, CPU only",createMs,createBytes,captureMs,captureBytes,fullPerformanceAccepted=false}));
            Check(copy.Elements.Length==3075&&!ReferenceEquals(copy.Elements,d.Elements),"independent array");
            Check(captureBytes<=d.Elements.Length*16+1024,"owned capture has only bounded array/header allocation, no JSON/per-element clone");
            Check(createBytes<4*1024*1024,"validated cold document copy avoids JSON DOM and reconstructed object graph");
        });
        yield return("UI copy shares only sealed immutable records and independently owns both arrays",()=>{
            foreach(var type in new[]{typeof(UiDefinition),typeof(UiElement),typeof(UiStyle),typeof(UiLayout),typeof(UiToken)}){
                Check(type.IsSealed,"snapshot record must remain sealed: "+type.Name);
                foreach(var p in type.GetProperties(BindingFlags.Public|BindingFlags.Instance)){
                    Check(p.SetMethod is null||p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)),"only init access: "+p.Name);
                    Check(p.PropertyType.IsValueType||p.PropertyType==typeof(string)||p.PropertyType==typeof(UiStyle)||p.PropertyType==typeof(UiLayout)||
                        type==typeof(UiDefinition)&&(p.Name==nameof(UiDefinition.Elements)&&p.PropertyType==typeof(UiElement[])||p.Name==nameof(UiDefinition.Tokens)&&p.PropertyType==typeof(UiToken[])),"new mutable field requires explicit ownership review: "+p.Name);
                }
            }
            var d=UiDefinition.Create(Guid.NewGuid(),"Owned");var token=new UiToken(Guid.NewGuid(),"Color",UiTokenKind.Color,UiColor.White,0);
            d=d with{Tokens=[token],Elements=[d.Elements[0] with{Style=d.Elements[0].Style with{FillToken=token.Id}}]};
            var owned=new UiDocument(d);byte[] expected=owned.CaptureBytes();var a=owned.Capture();var b=owned.Capture();
            Check(!ReferenceEquals(a.Elements,b.Elements)&&!ReferenceEquals(a.Tokens,b.Tokens),"independent captures");
            d.Elements[0]=d.Elements[0] with{Name="Caller"};d.Tokens[0]=token with{Color=UiColor.Transparent};
            a.Elements[0]=a.Elements[0] with{Layout=a.Elements[0].Layout with{Width=99},Style=a.Elements[0].Style with{Fill=UiColor.Transparent}};
            b.Tokens[0]=token with{Name="Capture"};Check(expected.SequenceEqual(owned.CaptureBytes()),"caller/capture changes cannot modify owned state");
            Task.Run(()=>{try{owned.Capture();throw new Exception("wrong thread accepted");}catch(InvalidOperationException){}}).GetAwaiter().GetResult();
        });
        yield return("Typed UI copy preserves exact canonical serialization and all scalar/Unicode guards",()=>{
            foreach(float size in new[]{0f,-0f,.00012345f,65536f}){
                var d=UiDefinition.Create(Guid.NewGuid(),"Escapes 中 😀");var child=UiElement.Create(Guid.NewGuid(),"Text",UiKind.Text,d.Root) with{
                    Text="quote\" slash\\ line\n tab\t 中😀e\u0301",Layout=UiLayout.Fixed(-size,size,size,32),Font=Guid.NewGuid()};
                d=d with{Elements=[d.Elements[0],child]};byte[] wire=UiCodec.Encode(d);
                Check(wire.SequenceEqual(UiCodec.Encode(UiCodec.Copy(d)))&&wire.SequenceEqual(Wire(UiCodec.Copy(d))),"wire value equivalence");
                Check(UiCodec.Encode(UiCodec.Decode(wire)).SequenceEqual(UiCodec.Encode(UiCodec.Copy(d))),"old wire roundtrip semantics preserved");
                Reject(()=>UiCodec.Copy(d with{Version=2}));Reject(()=>UiCodec.Copy(d with{Elements=[d.Elements[0],child with{Text="\uD800"}]}));
                Reject(()=>UiCodec.Copy(d with{Elements=[d.Elements[0],child with{Layout=child.Layout with{Width=float.NaN}}]}));
                Reject(()=>UiCodec.Copy(d with{Elements=[d.Elements[0],child with{Style=child.Style with{FillToken=Guid.NewGuid()}}]}));
                Reject(()=>UiCodec.Copy(d with{Tokens=null!}));Reject(()=>UiCodec.Copy(d with{Elements=new UiElement[UiCodec.MaxElements+1]}));
                Reject(()=>UiCodec.Copy(d with{Elements=[d.Elements[0],null!]}));Reject(()=>UiCodec.Copy(d with{Tokens=[null!]}));
                Reject(()=>UiCodec.Copy(d with{Elements=[d.Elements[0],child with{Parent=Guid.NewGuid()}]}));
            }
        });
        yield return("Typed UI copies enforce exact 4 MiB canonical wire limit including escaping",()=>{
            var d=UiDefinition.Create(Guid.NewGuid(),"Budget");var elements=new UiElement[65];elements[0]=d.Elements[0];
            for(int i=1;i<elements.Length;i++)elements[i]=UiElement.Create(Guid.NewGuid(),"Text",UiKind.Text,d.Root) with{Order=i};
            d=d with{Elements=elements};int remaining=UiCodec.MaxBytes-Wire(d).Length;
            for(int i=1;i<elements.Length&&remaining>0;i++){
                int escaped=Math.Min(16384,remaining/6);string value=new('中',escaped);remaining-=escaped*6;
                int ascii=Math.Min(16384-escaped,remaining%6);value+=new string('x',ascii);remaining-=ascii;elements[i]=elements[i] with{Text=value};
            }
            Check(remaining==0,"boundary fixture fits per-field budgets");byte[] exact=Wire(d);Check(exact.Length==UiCodec.MaxBytes,"independent escaped wire exactly 4 MiB");
            Check(UiCodec.Encode(UiCodec.Copy(d)).SequenceEqual(exact),"exact limit accepted with unchanged bytes");
            var owned=new UiDocument(d);Check(owned.CaptureBytes().SequenceEqual(exact),"owned capture exact boundary");
            var over=d with{Name=d.Name+"x"};Check(Wire(over).Length==UiCodec.MaxBytes+1,"one byte over fixture");UiCodec.Validate(over);
            Reject(()=>UiCodec.Copy(over));Reject(()=>new UiDocument(over));Reject(()=>UiCodec.Encode(over));Reject(()=>UiCodec.Decode(Wire(over)));
        });
    }
}
