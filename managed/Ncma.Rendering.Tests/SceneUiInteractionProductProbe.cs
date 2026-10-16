using System.Numerics;
using System.Text.Json;
using Ncma.Application;
using Ncma.Application.Runtime;
using Ncma.Gameplay;
using Ncma.Platform;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Ui;
using Ncma.Ui.Rendering;

// Test-only trusted startup composition; not a new apphost or a JSON-driven action registry.
// Keep Editor types out of the Player/Null JIT dependency closure.
internal sealed class SceneUiInteractionEvidence
{
    internal readonly JsonDocument Request;
    internal readonly string Root, Project, Output, Plugins, Kind;
    internal int Checks;
    internal readonly List<string> Labels=[];
    internal SceneUiInteractionEvidence(string path)
    {
        Request=JsonDocument.Parse(File.ReadAllBytes(path));var r=Request.RootElement;
        Root=r.GetProperty("productRoot").GetString()!;Project=r.GetProperty("project").GetString()!;
        Output=r.GetProperty("output").GetString()!;Plugins=r.GetProperty("plugins").GetString()!;Kind=r.GetProperty("kind").GetString()!;
        DeploymentManifest.Validate(Root);
    }
    internal static SceneUiActions Routes()=>new(new("fire",Ncma.GameplayUiActionKind.Activate),new("text",Ncma.GameplayUiActionKind.TextChanged),new("value",Ncma.GameplayUiActionKind.ValueChanged));
    internal void Pass(bool value,string name){if(!value)throw new Exception("Relocated interactive HUD: "+name);Checks++;Labels.Add(name);}
    internal void Finish(string[] native)
    {
        // Validate both when live and after close; exact native bytes are independently manifested.
        using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root,DeploymentManifest.FileName)));
        foreach(string path in native){var entry=manifest.RootElement.GetProperty("files").EnumerateArray().Single(e=>e.GetProperty("path").GetString()==Path.GetRelativePath(Root,path).Replace('\\','/'));
            Pass(entry.GetProperty("sha256").GetString()==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),"exact native product hash "+Path.GetFileName(path));}
        Pass(EnvironmentProductEvidence.NativePaths(Root).Length==0,"all native modules unloaded");
        File.WriteAllText(Path.Combine(Output,"probe.json"),JsonSerializer.Serialize(new{checks=Checks,labels=Labels,native,relocatedInteraction=true,syntheticPlatformEvents=true,customApphost=false,wholeD3Accepted=false}));Request.Dispose();
    }
}

internal static unsafe class SceneUiInteractionPlayerProbe
{
    internal static void Run(string requestPath)
    {

        using var playerRunOwner0 = new Ncma.Player.App.PlayerRunner();
        using var playerRunOwner1 = new Ncma.Player.App.PlayerRunner();
        var e=new SceneUiInteractionEvidence(requestPath);var context=ProjectContext.Load(e.Project);var routes=SceneUiInteractionEvidence.Routes();
        bool headless=e.Kind=="null";var options=new PlayerOptions(e.Project,headless,headless?"null":"d3d11",2,1d/60,30,Path.Combine(e.Output,"report.json"),false,false);
        var denied=playerRunOwner0.Run(options,pluginRoot:e.Plugins,visible:false);
        e.Pass(denied.ExitCode==7&&denied.Tick==0&&denied.SessionId==Guid.Empty&&denied.Modules.Length==0,"default startup has no route authority");
        var accepted=playerRunOwner1.Run(options with{Report=Path.Combine(e.Output,"accepted-report.json")},pluginRoot:e.Plugins,visible:false,uiActions:routes);
        e.Pass(accepted.ExitCode==0&&accepted.Tick>=2&&accepted.ShutdownErrors.Length==0&&accepted.ValidationErrors==0&&accepted.ValidationWarnings==0,"trusted formal PlayerRunner runs and closes: "+JsonSerializer.Serialize(accepted));
        if(headless){e.Pass(accepted.Tick==2&&accepted.Modules.Length==0&&accepted.RenderedFrames==0&&!Directory.Exists(e.Plugins),"Null exact ticks no plugins");e.Finish([]);return;}
        var document=new SceneDocument(context.Configuration.Name,Ncma.Characters.CharacterComponents.Register(RenderComponentRegistry.Register(RenderConfiguration.CreateRegistry())),Ncma.Characters.CharacterComponents.RequireComposition);
        SceneDocumentFiles.Load(document,context.StartupScenePath);
        Guid target=document.World.GetUuid(document.World.GetEntities().Single(o=>document.World.GetName(o)=="HUD target"));
        string[] native;
        using(var package=RuntimeUiPackage.Open(context.Root,context.Configuration.ProjectId,context.Configuration.UiPackage!))
        using(var assets=SceneAssetPreparation.Prepare(context.Root,context.Configuration.ProjectId,document.CaptureSnapshot(),true,context.Configuration.AssetPackage))
        using(var owner=new RuntimeSessionOwner(document))
        using(var player=new PlayerPresentation(e.Plugins,false)) {
            player.Start();player.PrepareBeforeGameplay(document,assets,package,routes);owner.LoadGameplay(context.GameplayAssemblyPath);
            var play=owner.StartPlay(compose:p=>{routes.Install(p);return null;});player.BindScene(play,assets,context.Configuration.SceneCamera);
            var window=EnvironmentProductEvidence.Field<PlatformWindow>(player,"_window");var state=window.State;
            state.Focused=1;state.InputReset=state.Overflow=0;state.Sequence=0;ulong sequence=0;
            InputEvent Pointer(uint action,double x,double y)=>new(){Kind=2,Action=action,Sequence=++sequence,X=x,Y=y};
            void Input(InputEvent[] events,double dt=1d/60){state.Sequence=sequence;player.ProcessInput(play,state,events);play.AdvanceFrame(dt);e.Pass(play.State==PlayState.Running,"Player running after input");player.Present(play);}
            Vector3 Position()=>play.Document.World.Get<TransformData>(play.Document.World.FindEntity(target)).Position;
            Input([]);Input([Pointer(1,20,20),Pointer(0,20,20)]);e.Pass(Position()==new Vector3(1,0,0),"click consumed once by C# not physical input");
            Input([new(){Kind=1,Key=32,Action=1,Sequence=++sequence}]);e.Pass(Position()==new Vector3(2,0,0),"Space consumed once");
            Input([Pointer(1,20,60),Pointer(0,20,60),new(){Kind=5,Codepoint='A',Sequence=++sequence}]);e.Pass(Position()==new Vector3(2,0,1),"text in fixed step");
            Input([Pointer(1,58,98),Pointer(0,58,98)]);e.Pass(Position()==new Vector3(2,.5f,1),"slider in fixed step");
            Input([Pointer(1,20,20),Pointer(0,20,20)],0);play.Pause();player.ProcessInput(play,state,[]);play.Step();play.Resume();Input([]);e.Pass(Position()==new Vector3(2,.5f,1),"pause revokes pending action");
            Input([Pointer(1,20,20),Pointer(0,20,20)],0);state.Focused=0;Input([]);state.Focused=1;Input([]);e.Pass(Position()==new Vector3(2,.5f,1),"focus loss revokes pending action");
            player.Present(play,(pixels,w,h)=>{e.Pass(pixels.Length==w*h*4,"actual interactive Player image");File.WriteAllBytes(Path.Combine(e.Output,"player.rgba"),pixels);});
            e.Pass(player.Stats.ValidationErrors==0&&player.Stats.ValidationWarnings==0,"Player API0/0");native=EnvironmentProductEvidence.NativePaths(e.Root);e.Pass(native.Length>=3,"live product native modules recorded");
        }
        e.Finish(native);
    }
}
