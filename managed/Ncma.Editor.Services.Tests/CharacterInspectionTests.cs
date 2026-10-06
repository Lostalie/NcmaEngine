using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ncma.Characters;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Services;
using Ncma.Gameplay;
using Ncma.Gui;
using Ncma.Physics;
using Ncma.Runtime;
using Ncma.Samples;
using Ncma.Scene.Rendering;

internal static class CharacterInspectionTests
{
    private static void Check(bool v,string m="Character inspection assertion"){if(!v)throw new InvalidOperationException(m);}
    private static void Reject(Action a){try{a();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or EditRejectedException){return;}throw new InvalidOperationException("Expected character review rejection");}
    private sealed class Clock:TimeProvider {internal long Time;public override long TimestampFrequency=>TimeSpan.TicksPerSecond;public override long GetTimestamp()=>Time;}
    private sealed class StepAction(Action action):IWorldSystem {public void FixedUpdate(World world,double h)=>action();}
    private static JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value,Wire.Json);
    private static void Schema(JsonElement value,JsonElement schema)
    {
        if(schema.TryGetProperty("oneOf",out var branches)){int matches=0;foreach(var branch in branches.EnumerateArray())try{Schema(value,branch);matches++;}catch(InvalidOperationException){}Check(matches==1,"schema oneOf");return;}
        if(schema.TryGetProperty("anyOf",out var choices)){foreach(var choice in choices.EnumerateArray())try{Schema(value,choice);return;}catch(InvalidOperationException){}throw new InvalidOperationException("schema anyOf");}
        if(schema.TryGetProperty("const",out var constant))Check(value.GetRawText()==constant.GetRawText());
        if(schema.TryGetProperty("enum",out var enums))Check(enums.EnumerateArray().Any(e=>e.GetRawText()==value.GetRawText()));
        if(!schema.TryGetProperty("type",out var type))return;
        switch(type.GetString()) {
            case "object":Check(value.ValueKind==JsonValueKind.Object);var properties=schema.GetProperty("properties");foreach(var p in value.EnumerateObject()){Check(properties.TryGetProperty(p.Name,out var s));Schema(p.Value,s);}foreach(var r in schema.GetProperty("required").EnumerateArray())Check(value.TryGetProperty(r.GetString()!,out _));break;
            case "array":Check(value.ValueKind==JsonValueKind.Array&&value.GetArrayLength()<=schema.GetProperty("maxItems").GetInt32()&&value.GetArrayLength()>=schema.GetProperty("minItems").GetInt32());foreach(var v in value.EnumerateArray())Schema(v,schema.GetProperty("items"));break;
            case "number":case "integer":Check(value.ValueKind==JsonValueKind.Number&&double.IsFinite(value.GetDouble()));if(type.GetString()=="integer")Check(value.TryGetUInt64(out _));Check(value.GetDouble()>=schema.GetProperty("minimum").GetDouble()&&value.GetDouble()<=schema.GetProperty("maximum").GetDouble());break;
            case "string":Check(value.ValueKind==JsonValueKind.String);if(schema.TryGetProperty("maxLength",out var max))Check(value.GetString()!.Length<=max.GetInt32());if(schema.TryGetProperty("pattern",out var pattern))Check(Regex.IsMatch(value.GetString()!,pattern.GetString()!));break;
            case "boolean":Check(value.ValueKind is JsonValueKind.False or JsonValueKind.True);break;
            case "null":Check(value.ValueKind==JsonValueKind.Null);break;
            default:throw new InvalidOperationException("Unknown schema subset");
        }
    }
    internal static void Run(string repository,string plugins,string output)
    {
        var sample=ActionSample.Create(Path.Combine(output,"character-mcp"),Path.Combine(repository,"out/managed/Ncma.Gameplay.Sample.dll"),2);
        using var physics=new PhysicsService(plugins,characterSupport:true);CharacterPlayRuntime? runtime=null;
        using var owner=new EditorSessionOwner("K6 inspection",components:CharacterComponents.Register(RenderComponentRegistry.CreateRegistry()),validateComposition:CharacterComponents.RequireComposition,
            composePreparedPlay:(p,assets)=>runtime=CharacterPlayRuntime.Compose(p,physics,assets));
        var workspace=new EditorWorkspace(owner);workspace.Open(workspace.Stamp,Path.Combine(sample.Root,"start.ncmascene"),true);owner.Edit!.Resynchronize();owner.PrepareRenderAssets(sample.Root,sample.ProjectId);
        var clock=new Clock();var service=new CharacterInspectionService(owner,()=>runtime,clock);service.Register();owner.ConfigureEndpoint(true,output);var endpoint=owner.Endpoint!;
        string helper=Path.Combine(repository,"out/managed/editor-mcp/Ncma.Editor.Mcp.dll");
        var launch=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};launch.ArgumentList.Add(helper);launch.ArgumentList.Add("--descriptor");launch.ArgumentList.Add(endpoint.DescriptorPath);
        using var process=Process.Start(launch)!;var errors=process.StandardError.ReadToEndAsync();
        JsonElement Send(object message) {
            process.StandardInput.WriteLine(JsonSerializer.Serialize(message,Wire.Json));process.StandardInput.Flush();var pending=process.StandardOutput.ReadLineAsync();var watch=Stopwatch.StartNew();
            while(!pending.IsCompleted){if(watch.ElapsedMilliseconds>10000)throw new TimeoutException("Character MCP timeout");foreach(var c in endpoint.View.Connections.Where(c=>!c.Paired&&c.Connected))endpoint.Pair(c.ConnectionId,true);endpoint.Pump();Thread.Sleep(1);}
            using var doc=JsonDocument.Parse(pending.GetAwaiter().GetResult()??throw new IOException("MCP closed"));return doc.RootElement.Clone();
        }
        int call=10;Guid retry=Guid.NewGuid();
        object Input(Guid[] ids,Guid? play=null,Guid? world=null)=>new{playSessionId=play??owner.Play!.SessionId,worldId=world??owner.Play!.Document.World.Identity,objectIds=ids};
        JsonElement Call(string name,object input)=>Send(new{jsonrpc="2.0",id=call++,method="tools/call",@params=new{name,arguments=new{contractVersion=2,requestId=retry,sessionId=owner.Edit.SessionId,expectedRevision=owner.Edit.Revision,input}}}).GetProperty("result").GetProperty("structuredContent");
        var descriptors=CharacterInspectionSchemas.Descriptors();
        try {
            Send(new{jsonrpc="2.0",id=1,method="initialize",@params=new{protocolVersion="2025-11-25"}});process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");process.StandardInput.Flush();
            var tools=Send(new{jsonrpc="2.0",id=2,method="tools/list",@params=new{}}).GetProperty("result").GetProperty("tools");
            foreach(var d in descriptors){var t=tools.EnumerateArray().Single(t=>t.GetProperty("name").GetString()==d.Name);Check(t.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()&&t.GetProperty("outputSchema").GetRawText()==d.OutputSchema.GetRawText());}
            byte[] edit=owner.Document.CaptureBytes();int history=owner.Edit.State.UndoCount;workspace.PlayControl(workspace.Stamp,"start");var play=owner.Play!;
            var denied=Call(descriptors[0].Name,Input([sample.Actors[0]]));Check(denied.GetProperty("code").GetString()=="character_not_visible");Schema(denied,descriptors[0].OutputSchema);
            var review=service.Capture([sample.Actors[0]]);Reject(()=>service.Approve(review,review.Fingerprint,false));Reject(()=>service.Approve(review with{ObjectIds=[sample.Actors[1]]},review.Fingerprint,true));
            service.Approve(review,review.Fingerprint,true);var read=Call(descriptors[0].Name,Input([sample.Actors[0]]));Check(read.GetProperty("status").GetString()=="ok");Schema(read,descriptors[0].OutputSchema);
            Check(read.GetProperty("data").GetProperty("characters").GetArrayLength()==1&&!read.GetRawText().Contains(sample.Root,StringComparison.OrdinalIgnoreCase)&&!read.GetRawText().Contains("resource",StringComparison.OrdinalIgnoreCase),"sanitized bounded character schema");
            Check(Call(descriptors[0].Name,Input([sample.Actors[1]])).GetProperty("code").GetString()=="character_not_visible","foreign exact object scope");
            Check(Call(descriptors[0].Name,Input([sample.Actors[0]],Guid.NewGuid())).GetProperty("code").GetString()=="character_snapshot_stale");
            Check(Call(descriptors[0].Name,Input([sample.Actors[0],sample.Actors[0]])).GetProperty("code").GetString()=="character_input_invalid");
            Check(Call(descriptors[0].Name,new{playSessionId=play.SessionId,worldId=play.Document.World.Identity,objectIds=sample.Actors,step=true}).GetProperty("code").GetString()=="character_input_invalid");
            var local=runtime!.ReadDebugFrame();local.Characters[0]=local.Characters[0] with{Health=0};Check(runtime.ReadDebugFrame().Characters[0].Health==100,"local copied diagnostics");
            runtime.RequestAction(sample.Actors[0],ActionRequest.Attack);bool hit=false;
            for(int i=0;i<24;i++){Check(play.AdvanceFrame(1d/60).State==PlayState.Running);if(runtime.ReadCombatEvents().Any(e=>e.Kind==CombatEventKind.Hit)) {
                var events=Call(descriptors[1].Name,Input([sample.Actors[0]]));Schema(events,descriptors[1].OutputSchema);
                var e=events.GetProperty("data").GetProperty("events").EnumerateArray().Single(e=>e.GetProperty("kind").GetString()=="Hit");Check(e.GetProperty("target").ValueKind==JsonValueKind.Null&&e.GetProperty("damage").ValueKind==JsonValueKind.Null,"hidden target and damage redacted");hit=true;
            }}Check(hit,"actual sample combat hit");
            ulong tick=play.Tick;byte[] before=play.Document.CaptureBytes();for(int i=0;i<8;i++)Call(descriptors[0].Name,Input([sample.Actors[0]]));Check(play.Tick==tick&&before.SequenceEqual(play.Document.CaptureBytes()),"MCP cannot step/write/replay damage");
            clock.Time=60*TimeSpan.TicksPerSecond;Check(Call(descriptors[0].Name,Input([sample.Actors[0]])).GetProperty("code").GetString()=="character_not_visible","expiry exact boundary");
            service.Approve(service.Capture([sample.Actors[0]]),service.Capture([sample.Actors[0]]).Fingerprint,true);service.Revoke();Check(Call(descriptors[0].Name,Input([sample.Actors[0]])).GetProperty("code").GetString()=="character_not_visible","same request ID rechecks authority");
            var presenter=new EditorPresenter(workspace,null);presenter.AttachCharacters(service);
            void Press(string label,double value=0){presenter.Build((ulong)call++,1280,720);var item=presenter.Items.ToArray().Single(i=>Encoding.UTF8.GetString(presenter.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))==label);var f=presenter.Frame;presenter.Apply([new(){Kind=item.Kind,WidgetHigh=item.WidgetHigh,WidgetLow=item.WidgetLow,Phase=3,Frame=f.Frame,ViewGeneration=f.ViewGeneration,DocumentGeneration=f.DocumentGeneration,Revision=f.Revision,Value=value}],[]);}
            Press("Character Debug");presenter.Build((ulong)call++,1280,720);Check(presenter.Items.ToArray().Any(i=>Encoding.UTF8.GetString(presenter.Text.Slice((int)i.LabelOffset,(int)i.LabelLength)).Contains("Snapshot",StringComparison.OrdinalIgnoreCase)));
            // Two actors: choose the exact row by action identity, not ambiguous duplicate labels.
            var item=presenter.Items.ToArray().First(i=>i.Kind==(uint)GuiItemKind.Checkbox&&i.WidgetHigh==22&&i.WidgetLow>=100);var frame=presenter.Frame;
            presenter.Apply([new(){Kind=item.Kind,WidgetHigh=22,WidgetLow=item.WidgetLow,Phase=3,Frame=frame.Frame,ViewGeneration=frame.ViewGeneration,DocumentGeneration=frame.DocumentGeneration,Revision=frame.Revision,Value=1}],[]);
            Press("Review exact Play/object scope and paired audience");Press("Approve displayed read-only scope for 60 seconds");Check(service.Grant.Objects.Length==0,"forged disabled approval cannot grant");
            Press("I reviewed every UUID and the complete paired audience",1);Press("Approve displayed read-only scope for 60 seconds");Check(service.Grant.Objects.Length==1,"visible UI exact review grants reads");
            endpoint.Revoke(endpoint.View.Connections.Single(c=>c.Paired).ConnectionId);Check(service.Grant.Objects.Length==0,"paired-audience revocation invalidates grant");
            process.StandardInput.Close();Check(process.WaitForExit(5000),"stdio closes before endpoint replacement");
            owner.ConfigureEndpoint(false,output);Check(service.Grant.Objects.Length==0,"endpoint replacement invalidates grant");
            owner.ConfigureEndpoint(true,output);endpoint=owner.Endpoint!;
            // Existing process reconnect is not automatic; direct trusted read must still be denied.
            play.Reload(_=>throw new Exception("No behaviours"));Check(service.Grant.Objects.Length==0&&runtime.ReadDebugFrame().Tick==play.Tick,"reload rotates scope identity and resets derived state");
            workspace.PlayControl(workspace.Stamp,"stop");Check(edit.SequenceEqual(owner.Document.CaptureBytes())&&owner.Edit.State.UndoCount==history&&physics.Inspect().Worlds==0,"inspections preserve Edit/history and release numerics");
            workspace.PlayControl(workspace.Stamp,"start");play=owner.Play!;
            // A candidate-step read cannot expose prepared root/health/contact data.
            workspace.PlayControl(workspace.Stamp,"stop");
        } finally{process.StandardInput.Close();if(!process.WaitForExit(5000))process.Kill(true);if(owner.Play is not null)owner.StopPlay();}
        Check(process.ExitCode==0&&errors.GetAwaiter().GetResult().Length==0,"stdio lifetime/log discipline");
        // Actual owner callback safety test in an independent coupled Play.
        var doc=new Ncma.Scene.SceneDocument("K6 safe read",CharacterComponents.Register(RenderComponentRegistry.CreateRegistry()),CharacterComponents.RequireComposition);
        Ncma.Scene.SceneDocumentFiles.Load(doc,Path.Combine(sample.Root,"start.ncmascene"));
        using var assets=SceneAssetPreparation.Prepare(sample.Root,sample.ProjectId,doc.CaptureSnapshot(),true);using var isolated=new PlaySession(doc,FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);
        using var isolatedRuntime=CharacterPlayRuntime.Compose(isolated,physics,assets)!;bool fail=false;
        isolated.AddSystem(new StepAction(()=>{if(fail){var pending=isolated.Commands.SpawnEmpty("late");isolated.Commands.AttachBehaviour(pending.ObjectId,new(Guid.NewGuid(),"Missing",true,[]));}else{Reject(()=>isolatedRuntime.ReadDebugFrame());Reject(()=>isolatedRuntime.InspectRootMotion(sample.Actors[0]));}}));
        isolated.Start(_=>throw new Exception("No behaviours"));try{
            Check(isolated.AdvanceFixedStep().State==PlayState.Running,"rejected diagnostic read does not mutate/poison World");
            Reject(()=>Task.Run(()=>isolatedRuntime.ReadDebugFrame()).GetAwaiter().GetResult());
            fail=true;isolated.AdvanceFixedStep();var fault=isolatedRuntime.ReadDebugFrame();Check(!fault.SnapshotValid&&fault.FaultCode=="play_faulted"&&fault.Characters.Length==0&&fault.Events.Length==0&&fault.Tick==1,"post-solver fault never exposes candidate/old-valid pose or private error text");
        }finally{isolated.Stop();}
    }
}
