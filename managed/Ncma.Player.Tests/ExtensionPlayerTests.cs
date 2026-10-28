#if NCMA_EXTENSIONS
using System.Text.Json;
using Ncma.Application;
using Ncma.Extensions.Hosting;
using Ncma.Player.App;
using Ncma.Runtime;
using Ncma.Scene;

internal static class ExtensionPlayerTests
{
    internal static void Run(string output,string gameplayAssembly)
    {
        static void Check(bool value){if(!value)throw new Exception("Extension Player closure assertion");}
        string root=Path.Combine(output,"extension-player",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var registry=CompositionRegistry.Build(global::NcmaPlayer.Generated.ComponentModule.Module);
        var scene=new SceneDocument("Optional component",registry);Guid id=scene.CreateObject("Extension value");
        scene.SetComponent(id,new ExtensionSampleTag(.375f));
        byte[] before=scene.CaptureBytes();string path=Path.Combine(root,"start.ncmascene");SceneDocumentFiles.Save(scene,path);
        // Default registry must reject the unknown persisted type, not drop it.
        bool rejected=false;try{new SceneDocument().RestoreBytes(before);}catch(ArgumentException){rejected=true;}Check(rejected);
        string project=Path.Combine(root,"game.ncmaproject");
        File.Copy(gameplayAssembly,Path.Combine(root,"gameplay.dll"));
        File.Copy(Path.ChangeExtension(gameplayAssembly,".deps.json"),Path.Combine(root,"gameplay.deps.json"));
        var config=new ProjectConfiguration(1,Guid.NewGuid(),"Optional Player","start.ncmascene","gameplay.dll","Direct3D11",[]);
        File.WriteAllBytes(project,JsonSerializer.SerializeToUtf8Bytes(config,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        using var runner=new PlayerRunner();bool observed=false;
        var report=runner.Run(new(project,true,"null",1,1d/60,10,Path.Combine(root,"report.json"),false,false),visible:false,
            trustedInput:(play,_)=>{
                var world=play.Scene.World;var entity=world.FindEntity(id);Check(world.Get<ExtensionSampleTag>(entity).Value==.375f);observed=true;
            });
        Check(report.ExitCode==0&&observed&&before.AsSpan().SequenceEqual(scene.CaptureBytes()));
        Check(!typeof(PlayerRunner).Assembly.GetReferencedAssemblies().Any(a=>a.Name!.StartsWith("Ncma.Editor",StringComparison.Ordinal)));
    }
}
#endif
