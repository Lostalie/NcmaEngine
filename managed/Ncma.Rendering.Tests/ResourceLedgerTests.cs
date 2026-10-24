using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int ResourceLedgerTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        int checks=0;var copies=new List<RendererResourceLedger>();
        void Pass(bool value,string why){Check(value,"Resource ledger: "+why);checks++;}
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());
        var module=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Resource ledger",64,64,false);
        // Test-only exact ABI access; no production reflection/authority expansion.
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        byte[] table=(byte[])typeof(PluginModule).GetField("_table",flags)!.GetValue(module)!;
        ulong context=(ulong)typeof(PluginModule).GetField("_context",flags)!.GetValue(module)!;
        var query=(delegate* unmanaged[Cdecl]<ulong,uint,void*,uint,void*,uint>)(nint)BitConverter.ToInt64(table,144);
        byte* error=stackalloc byte[528];byte* api=stackalloc byte[24];new Span<byte>(api,24).Fill(0xA5);
        Pass(query(context,22,api,23,error)==(uint)PluginResult.BufferTooSmall&&new ReadOnlySpan<byte>(api,24).ToArray().All(v=>v==0xA5),"short table no write");
        Pass(query(context,23,api,24,error)==(uint)PluginResult.AbiMismatch,"unknown query");
        Pass(query(context,22,null,24,error)==(uint)PluginResult.InvalidArgument,"null table");
        Pass(query(context,22,api,24,error)==0&&*(uint*)api==24&&*(uint*)(api+4)==1&&*(ulong*)(api+8)==1,"query22/v1");
        var read=(delegate* unmanaged[Cdecl]<ulong,ulong,void*,uint,void*,uint>)(*(nint*)(api+16));
        ulong generation;
        using(var renderer=new RendererSession(module,window,64,64)) {
            var empty=renderer.CaptureResourceLedger();generation=empty.Generation;copies.Add(empty);
            Pass(empty.Domains.Length==8&&empty.Domains.Select(r=>r.Domain).SequenceEqual(Enum.GetValues<RendererResourceDomain>()),"fixed ordered domains");
            Pass(empty.Domains.All(r=>r.LogicalBytes==0&&r.LiveResources==0)&&!empty.PureUi&&!empty.Failed&&!empty.ActiveFrame,"empty logical resources");
            Pass(!empty.MemoryAccepted&&!empty.PhysicalGpuMemoryAvailable&&!empty.CompleteNativeMemoryCoverage&&empty.TotalBytes is null,"no invented total or physical residency");
            byte* snapshot=stackalloc byte[152];new Span<byte>(snapshot,152).Fill(0xA5);
            Pass(read(context,generation,snapshot,151,error)==(uint)PluginResult.BufferTooSmall&&new ReadOnlySpan<byte>(snapshot,152).ToArray().All(v=>v==0xA5),"short snapshot no write");
            Pass(read(context,generation,null,152,error)==(uint)PluginResult.InvalidArgument,"null snapshot");
            Pass(read(context,0,snapshot,152,error)==(uint)PluginResult.InvalidHandle,"invalid renderer handle");
            Pass(Task.Run(()=>{try{renderer.CaptureResourceLedger();return false;}catch(PluginException e){return e.Result==PluginResult.WrongThread;}}).GetAwaiter().GetResult(),"owner thread");
            foreach(string name in new[]{"_createMesh","_createSkin","_createTexture","_createScene","_publishEnvironment","_uiApi","_uiTargetApi","_postBind"})
                Pass(typeof(RendererSession).GetField(name,flags)!.GetValue(renderer) is null,"unused API stays unbound "+name);
            var source=SyntheticModel();var mesh=source.Meshes[0];
            var upload=SkinUploadData.Prepare(new MeshPayload(true,source.Bones.Length,mesh.Vertices,[],mesh.Indices,mesh.TriangleMaterials,mesh.Bindings,mesh.Materials.Length));
            using(var skin=renderer.CreateSkinnedMesh(upload))
            using(var texture=renderer.CreateTexture(TextureData.Prepare(1,1,TextureSemantic.Data,[255,255,255,255])))
            using(var target=renderer.CreateViewTarget(64,64))
            using(var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false),64,64))
            using(var image=renderer.CreateUiImage(1,1,[255,255,255,255]))
            using(var uiTarget=renderer.CreateUiTarget(32,32))
            using(var cook=new EnvironmentCookService(module,()=>true))
            using(var environment=renderer.CreateEnvironment(cook.Cook(Guid.NewGuid(),1,HdrEnvironmentSource.Neutral(Guid.NewGuid()),new(8,4,8,64)),()=>true)) {
                var definitions=BloomFeature.CreatePrograms();
                var package=PostProcessPackage.Cook(renderer,definitions.Catalog,definitions.CopyPrograms(),()=>true);
                var programs=PreparedPostProcessPrograms.Prepare(renderer,package,()=>true);
                scene.ConfigurePostProcess(BloomFeature.Build(64,64,new(),package),programs,()=>true);
                var captured=renderer.CaptureResourceLedger();copies.Add(captured with{Domains=captured.Domains.ToArray()});
                ulong[] expected=[renderer.SceneStats.ResidentBytes,renderer.SkinStats.ResidentBytes,renderer.ResourceStats.ResidentBytes,
                    renderer.PipelineStats.ResidentBytes,renderer.EnvironmentStats.ResidentBytes,renderer.UiStats.ResidentBytes,
                    renderer.UiTargetStats.ResidentBytes,renderer.PostStats.ResidentBytes];
                for(int i=0;i<8;i++)Pass(captured.Domains[i].LogicalBytes==expected[i]&&expected[i]>0&&captured.Domains[i].LiveResources>0,"actual existing domain counter "+i);
                Pass(captured.Domains[0].LiveResources==1&&captured.Domains[1].LiveResources==1,"skin output overlaps meshes, no disjoint sum");
                var before=renderer.Stats;
                Pass(renderer.CaptureResourceLedger().Domains.SequenceEqual(captured.Domains),"repeat copied counters");
                var after=renderer.Stats;Pass(before.SubmittedFrames==after.SubmittedFrames&&before.Presents==after.Presents,"observation submits no GPU frame");
                captured.Domains[0]=default;
                Pass(renderer.CaptureResourceLedger().Domains[0].LogicalBytes>0,"returned array cannot mutate native counters");
            }
            var retired=renderer.CaptureResourceLedger();copies.Add(retired);
            Pass(retired.Domains.All(r=>r.LiveResources==0&&r.LogicalBytes==0),"all owned logical resources retired; not full native memory zero");
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"native validation clean");
        }
        using(var renderer=new RendererSession(module,window,64,64,pureUi:true)) {
            var fresh=renderer.CaptureResourceLedger();copies.Add(fresh);
            Pass(fresh.Generation!=generation&&fresh.PureUi&&fresh.Domains.All(r=>r.LogicalBytes==0&&r.LiveResources==0),"new pure2D generation reset");
            using(var image=renderer.CreateUiImage(1,1,[255,255,255,255])) {
                var live=renderer.CaptureResourceLedger();copies.Add(live);
                Pass(live.Domains[5].LogicalBytes==4&&live.Domains[5].LiveResources==1&&live.Domains.Where((_,i)=>i!=5).All(r=>r.LogicalBytes==0&&r.LiveResources==0),"pure2D image without 3D resources");
                Pass(typeof(RendererSession).GetField("_createSkin",flags)!.GetValue(renderer) is null&&typeof(RendererSession).GetField("_createScene",flags)!.GetValue(renderer) is null,"no unused 3D API binding");
            }
            renderer.Dispose();Reject(()=>renderer.CaptureResourceLedger());checks++;
        }
        File.WriteAllText(Path.Combine(output,"ledger.json"),JsonSerializer.Serialize(new{checks,copies,performanceAccepted=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS resource ledger {checks} checks; logical domains only; report={output}");return 0;
    }
}
