using System.Diagnostics;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Assets;
using Ncma.Characters;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Services;
using Ncma.Gameplay;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Physics;
using Ncma.Runtime;
using Ncma.Samples;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static class AnimationRuntimeInspectionTests
{
    private static void Check(bool value,string message="Animation runtime inspection assertion") {if(!value)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or EditRejectedException or EditCommandRejectedException){return;}throw new Exception("Invalid runtime review accepted.");}
    private sealed class Clock:TimeProvider{internal long Time;public override long TimestampFrequency=>1000;public override long GetTimestamp()=>Time;}
    private sealed class Failure(Func<bool>? fail=null):IWorldSystem{public void FixedUpdate(World w,double h){if(fail is null||fail())throw new InvalidOperationException("private injected graph failure");}}
    internal static void Run(string repository,string plugins,string output)
    {
        var sample=ActionSample.Create(Path.Combine(output,"m6-3-d"),Path.Combine(repository,"out/managed/Ncma.Gameplay.Sample.dll"),2);
        var record=AssetRecordCodec.Decode(File.ReadAllBytes(Path.Combine(sample.Root,"assets/procedural.fbx.ncmeta")));
        Guid rig=record.Subassets.Single(s=>s.Kind==AssetKind.Skeleton).AssetId;var clips=record.Subassets.Where(s=>s.Kind==AssetKind.Clip).Select(s=>s.AssetId).ToArray();
        var clip=AnimationGraphNode.Create(Guid.NewGuid(),"Idle",AnimationNodeKind.Clip) with{ClipId=clips[0],Speed=1,Loop=true};
        var end=AnimationGraphNode.Create(Guid.NewGuid(),"Output",AnimationNodeKind.Output);
        var graph=new AnimationGraphDefinition(1,Guid.NewGuid(),"Runtime approved graph",rig,Guid.Empty,[],[clip,end],[new(Guid.NewGuid(),clip.Id,"pose",end.Id,"pose")],[],[]);
        string graphFile=Path.Combine(sample.Root,"assets/runtime.ncmaanim");File.WriteAllBytes(graphFile,AnimationGraphCodec.Encode(graph));
        using var physics=new PhysicsService(plugins,characterSupport:true);ScenePlayRuntime? runtime=null;bool injectFailure=false;
        using var owner=new EditorSessionOwner("Graph runtime",components:CharacterComponents.Register(RenderComponentRegistry.CreateRegistry()),validateComposition:CharacterComponents.RequireComposition,
            composePreparedPlay:(play,assets)=>{runtime=ScenePlayRuntime.Compose(play,physics,assets);play.AddSystem(new Failure(()=>injectFailure));return runtime;});
        SceneDocumentFiles.Load(owner.Document,Path.Combine(sample.Root,"start.ncmascene"));
        foreach(Guid id in sample.Actors){var actor=owner.Document.World.FindObject(id);actor.Remove<ActionDefinitionData>();actor.Remove<ClipPlaybackData>();actor.Set(new AnimatorData(graph.AssetId,rig));}
        owner.Edit!.Resynchronize();owner.PrepareRenderAssets(sample.Root,sample.ProjectId);var workspace=new EditorWorkspace(owner);var clock=new Clock();
        SceneRenderSession? presentation=null;
        var service=new AnimationRuntimeInspectionService(owner,()=>runtime,presentation:()=>presentation,clock:clock);service.Register();owner.ConfigureEndpoint(true,output);var endpoint=owner.Endpoint!;
        var launch=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        launch.ArgumentList.Add(Path.Combine(repository,"out/managed/editor-mcp/Ncma.Editor.Mcp.dll"));launch.ArgumentList.Add("--descriptor");launch.ArgumentList.Add(endpoint.DescriptorPath);
        using var client=Process.Start(launch)!;var errors=client.StandardError.ReadToEndAsync();int counter=1;Guid request=Guid.NewGuid();
        JsonElement Send(object message){client.StandardInput.WriteLine(JsonSerializer.Serialize(message,Wire.Json));client.StandardInput.Flush();var pending=client.StandardOutput.ReadLineAsync();var watch=Stopwatch.StartNew();while(!pending.IsCompleted){if(watch.ElapsedMilliseconds>10000)throw new TimeoutException("Runtime MCP timeout");foreach(var c in endpoint.View.Connections.Where(c=>!c.Paired&&c.Connected))endpoint.Pair(c.ConnectionId,true);endpoint.Pump();Thread.Sleep(1);}using var parsed=JsonDocument.Parse(pending.GetAwaiter().GetResult()??throw new IOException("Runtime MCP closed"));return parsed.RootElement.Clone();}
        JsonElement Call(object input)=>Send(new{jsonrpc="2.0",id=counter++,method="tools/call",@params=new{name="ncma.animgraph.runtime",arguments=new{contractVersion=2,requestId=request,sessionId=owner.Edit.SessionId,expectedRevision=owner.Edit.Revision,input}}}).GetProperty("result").GetProperty("structuredContent");
        object Input(Guid id,Guid? play=null,ulong? tick=null)=>new{playSessionId=play??owner.Play!.SessionId,worldId=owner.Play!.Document.World.Identity,objectId=id,expectedTick=tick??owner.Play.Tick,offset=0,limit=1};
        try {
            Send(new{jsonrpc="2.0",id=counter++,method="initialize",@params=new{protocolVersion="2025-11-25"}});client.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");client.StandardInput.Flush();
            var tool=Send(new{jsonrpc="2.0",id=counter++,method="tools/list"}).GetProperty("result").GetProperty("tools").EnumerateArray().Single(t=>t.GetProperty("name").GetString()=="ncma.animgraph.runtime");
            Check(tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());var descriptor=AnimationRuntimeInspectionSchemas.Descriptor;Check(tool.GetProperty("outputSchema").GetRawText()==descriptor.OutputSchema.GetRawText());
            var saved=owner.Document.CaptureBytes();int history=owner.Edit.State.UndoCount;owner.StartPlay();var play=owner.Play!;
            Check(Call(Input(sample.Actors[0])).GetProperty("code").GetString()=="animator_not_visible");
            var review=service.Capture([sample.Actors[0]]);Check(review.Bindings.Single().Resources.Count==record.Subassets.Length+2,"Review includes complete pinned NCA + graph");
            Reject(()=>service.Approve(review,review.Fingerprint,false));Reject(()=>service.Approve(review with{WorldId=Guid.NewGuid()},review.Fingerprint,true));
            service.Approve(review,review.Fingerprint,true);
            var read=Call(Input(sample.Actors[0]));CharacterInspectionTests.Schema(read,descriptor.OutputSchema);Check(read.GetProperty("status").GetString()=="ok"&&read.GetProperty("data").GetProperty("snapshotValid").GetBoolean());
            var observation=read.GetProperty("data").GetProperty("observation");Check(observation.GetProperty("binding").GetProperty("graphId").GetGuid()==graph.AssetId&&observation.GetProperty("pose").ValueKind==JsonValueKind.Null,"Headless observation must not manufacture pose evidence");
            Check(!read.GetRawText().Contains(sample.Root,StringComparison.OrdinalIgnoreCase));Check(Call(Input(sample.Actors[1])).GetProperty("code").GetString()=="animator_not_visible");
            Check(Call(Input(sample.Actors[0],Guid.NewGuid())).GetProperty("code").GetString()=="animator_snapshot_stale");
            Check(Call(new{playSessionId=play.SessionId,worldId=play.Document.World.Identity,objectId=sample.Actors[0],step=true}).GetProperty("code").GetString()=="animator_input_invalid");
            Check(Call(new{playSessionId=play.SessionId,worldId=play.Document.World.Identity,objectId=sample.Actors[0],limit="1"}).GetProperty("code").GetString()=="animator_input_invalid");
            Check(play.AdvanceFrame(1d/60).State==PlayState.Running);Check(Call(Input(sample.Actors[0],tick:0)).GetProperty("code").GetString()=="animator_snapshot_stale");
            read=Call(Input(sample.Actors[0]));CharacterInspectionTests.Schema(read,descriptor.OutputSchema);Check(read.GetProperty("data").GetProperty("observation").GetProperty("sequence").GetUInt64()==1);
            ulong tick=play.Tick;var before=play.Document.CaptureBytes();
            using(var file=new FileStream(graphFile,FileMode.Open,FileAccess.Read,FileShare.None))for(int i=0;i<16;i++)Check(Call(Input(sample.Actors[0])).GetProperty("status").GetString()=="ok");
            Check(play.Tick==tick&&before.SequenceEqual(play.Document.CaptureBytes()),"Observation does not read graph file, step or mutate World");
            using(var loader=new PluginLoader()) {
                loader.Load(plugins,[new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"])]);
                using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"M6.3-D current pose observation",320,240,false);
                using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,320,240);using var cache=new RenderResourceCache(renderer);
                string kernelPath=Path.Combine(plugins,"NcmaAnimationKernel.dll");using var kernel=new Ncma.Animation.Native.PoseKernel(kernelPath,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(kernelPath))),blendSupport:true);
                using var scene=new SceneRenderSession(renderer,cache,play.Document.World,owner.PlayRenderAssets!,play.Document.CaptureSnapshot(),poseKernel:kernel,play:play,rootMotion:runtime!.Characters,animators:runtime.Animators);
                presentation=scene;Guid camera=play.Document.World.GetObjects().Single(o=>o.Has<CameraData>()).PersistentId;
                Check(scene.Submit(1,320,240,camera),"Runtime observation requires actual successful skin/shadow submission");renderer.Present();
                var pose=Call(Input(sample.Actors[0])).GetProperty("data").GetProperty("observation").GetProperty("pose");Check(pose.GetProperty("rendererFrame").GetUInt64()==1&&pose.GetProperty("geometryDraws").GetInt32()==2,"Current exact publication/tick pose evidence");
                Check(play.AdvanceFrame(1d/60).State==PlayState.Running);Check(Call(Input(sample.Actors[0])).GetProperty("data").GetProperty("observation").GetProperty("pose").ValueKind==JsonValueKind.Null,"Previous successful pose cannot label a new graph tick");
                Check(scene.Submit(2,320,240,camera));renderer.Present();Check(Call(Input(sample.Actors[0])).GetProperty("data").GetProperty("observation").GetProperty("pose").GetProperty("rendererFrame").GetUInt64()==2);
                Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0);presentation=null;
            }
            clock.Time=60000;Check(Call(Input(sample.Actors[0])).GetProperty("code").GetString()=="animator_not_visible");
            review=service.Capture([sample.Actors[0]]);service.Approve(review,review.Fingerprint,true);var connection=endpoint.View.Connections.Single(c=>c.Paired).ConnectionId;
            endpoint.Revoke(connection);endpoint.Pair(connection,true);Check(Call(Input(sample.Actors[0])).GetProperty("code").GetString()=="animator_not_visible","Same audience IDs cannot revive epoch-revoked grant");
            var view=new EditorPresenter(workspace,null,sample.Root,workspaceStyle:true);view.AttachAnimationRuntime(service);ulong uiFrame=1;
            void Press(ulong high,ulong low,double value=0){view.Build(uiFrame++,1280,720);var item=view.Items.ToArray().Single(i=>i.WidgetHigh==high&&i.WidgetLow==low);var frame=view.Frame;view.Apply([new(){Kind=item.Kind,WidgetHigh=high,WidgetLow=low,Phase=3,Frame=frame.Frame,ViewGeneration=frame.ViewGeneration,DocumentGeneration=frame.DocumentGeneration,Revision=frame.Revision,Value=value}],[]);}
            Press(24,12);Press(24,49);Press(52,100);Press(52,1);Press(52,4);Check(service.Grant.Objects==0,"Disabled UI approval cannot grant");Press(52,3,1);Press(52,4);Check(service.Grant.Objects==1,"Visible exact UI review grants graph read");
            service.Revoke();Check(Call(Input(sample.Actors[0])).GetProperty("code").GetString()=="animator_not_visible","Repeat request rechecks revoke");
            review=service.Capture([sample.Actors[0]]);service.Approve(review,review.Fingerprint,true);Guid old=play.SessionId;play.Reload(_=>throw new Exception("No behaviours"));Check(service.Grant.Objects==0&&play.SessionId!=old&&play.State==PlayState.Paused,"Reload identities revoke runtime grant");
            owner.StopPlay();Check(saved.SequenceEqual(owner.Document.CaptureBytes())&&history==owner.Edit.State.UndoCount&&physics.Inspect().Worlds==0);
            owner.StartPlay();play=owner.Play!;review=service.Capture([sample.Actors[0]]);service.Approve(review,review.Fingerprint,true);
            injectFailure=true;play.Pause();Check(play.Step().State==PlayState.Faulted);var fault=Call(Input(sample.Actors[0]));CharacterInspectionTests.Schema(fault,descriptor.OutputSchema);
            Check(!fault.GetProperty("data").GetProperty("snapshotValid").GetBoolean()&&fault.GetProperty("data").GetProperty("observation").ValueKind==JsonValueKind.Null,"Fault observation cannot expose old successful frame as current");
            owner.StopPlay();
        }finally{client.StandardInput.Close();if(!client.WaitForExit(5000))throw new TimeoutException("Runtime MCP close");if(owner.Play is not null)owner.StopPlay();}
        Check(client.ExitCode==0&&errors.GetAwaiter().GetResult().Length==0,"Runtime MCP stdout/log and lifecycle");
        var d=owner.Document.CreateIsolatedCopy();using var prepared=SceneAssetPreparation.Prepare(sample.Root,sample.ProjectId,d.CaptureSnapshot(),true);
        using var isolated=new PlaySession(d,FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);using var failed=ScenePlayRuntime.Compose(isolated,physics,prepared);isolated.AddSystem(new Failure());
        isolated.Start(_=>throw new Exception("No behaviours"));try{Check(isolated.AdvanceFixedStep().State==PlayState.Faulted&&isolated.Tick==0);Reject(()=>failed.Animators!.ReadDebug(sample.Actors[0]));}finally{isolated.Stop();}
        File.WriteAllText(Path.Combine(output,"m6-3-d-runtime-mcp-results.json"),JsonSerializer.Serialize(new{realStdio=true,closedSchema=true,exactNcaGraphReview=true,uiReview=true,noTickIo=true,currentGpuPose=true,stalePoseRejected=true,expiry=true,audienceEpoch=true,reload=true,liveControls=false,manualThirdPartyAccepted=false}));
    }
}
