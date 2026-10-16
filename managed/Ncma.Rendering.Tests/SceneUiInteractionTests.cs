using System.Numerics;
using Vector3 = System.Numerics.Vector3;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma;
using Ncma.Application;
using Ncma.Application.Runtime;
using Ncma.Assets;
using Ncma.Editor.App;
using Ncma.Gameplay;
using Ncma.Platform;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Ui;
using Ncma.Ui.Rendering;

internal static unsafe partial class Program
{
    private static int SceneUiInteractionTests(string plugins,string output,bool relocatedOnly=false)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        var repo=new DirectoryInfo(Path.GetFullPath(plugins));while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent!;
        var fixture=new SceneGpuFixture(output);var(document,camera,_,_)=fixture.Scene();
        global::Ncma.Runtime.Entity target=document.World.CreateEntity("HUD target");document.World.Set(target, TransformData.Identity);
        document.SetBindings(document.World.GetUuid(target),[new(Guid.NewGuid(),"Ncma.Gameplay.UiFixture.HudProbe",true,[])]);
        int checks=0;var labels=new List<string>();
        void Pass(bool value,string label){Check(value,"UI interaction "+label);checks++;labels.Add(label);}
        void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or NotSupportedException){Pass(true,label);return;}throw new Exception("Accepted "+label);}
        string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
        string runtime=Path.Combine(output,"runtime");Directory.CreateDirectory(Path.Combine(runtime,"assets"));
        string configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string script=Path.Combine(repo.FullName,"managed/Ncma.Gameplay.UiFixture/bin",configuration,"net8.0/Ncma.Gameplay.UiFixture.dll");
        File.Copy(script,Path.Combine(runtime,"gameplay.dll"));File.Copy(Path.ChangeExtension(script,".deps.json"),Path.Combine(runtime,"gameplay.deps.json"));
        var definition=UiDefinition.Create(Guid.NewGuid(),"Interactive HUD");Guid font=Guid.NewGuid();
        var button=UiElement.Create(Guid.NewGuid(),"Button",UiKind.Button,definition.Root) with { Action="fire",Layout=UiLayout.Fixed(8,8,80,30),Style=UiStyle.Default with{Fill=new(.2f,.4f,.8f,1)} };
        var text=UiElement.Create(Guid.NewGuid(),"Input",UiKind.TextInput,definition.Root) with { Order=1,Action="text",Font=font,Layout=UiLayout.Fixed(8,48,160,30),Style=UiStyle.Default with{Fill=new(.05f,.1f,.15f,1)} };
        var slider=UiElement.Create(Guid.NewGuid(),"Slider",UiKind.Slider,definition.Root) with { Order=2,Action="value",Layout=UiLayout.Fixed(8,88,100,24),Style=UiStyle.Default with{Fill=new(.1f,.2f,.3f,1)} };
        definition=definition with{Elements=[definition.Elements[0],button,text,slider]};
        string fontPath=Path.Combine(repo.FullName,"engine/sdk/imgui/misc/fonts/ProggyClean.ttf");
        byte[] bytes=RuntimeUiPackage.Encode(fixture.Project,1,definition,[new(font,File.ReadAllBytes(fontPath),"ProggyClean MIT local regression",true)],[]);
        File.WriteAllBytes(Path.Combine(runtime,"assets/hud.ncupak"),bytes);
        File.WriteAllBytes(Path.Combine(runtime,"assets/scene.ncpak"),SceneAssetPreparation.CreateRuntimePackage(fixture.Root,fixture.Project,document.CaptureSnapshot(),[]));
        SceneDocumentFiles.Save(document,Path.Combine(runtime,"start.ncmascene"));
        var selection=new UiPackageReference(1,definition.AssetId,1,"assets/hud.ncupak",Hash(bytes));
        var config=new ProjectConfiguration(1,fixture.Project,"Interactive HUD","start.ncmascene","gameplay.dll","Direct3D11",[],false,camera,"assets/scene.ncpak",UiPackage:selection);
        string projectPath=Path.Combine(runtime,"project.ncmaproject");
        File.WriteAllBytes(projectPath,JsonSerializer.SerializeToUtf8Bytes(config,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        var routes=new SceneUiActions(new("fire",GameplayUiActionKind.Activate),new("text",GameplayUiActionKind.TextChanged),new("value",GameplayUiActionKind.ValueChanged));
        using var fontLock=new FileStream(fontPath,FileMode.Open,FileAccess.Read,FileShare.None);
        var locks=Directory.GetFiles(fixture.Root,"*",SearchOption.AllDirectories).Select(f=>new FileStream(f,FileMode.Open,FileAccess.Read,FileShare.None)).ToArray();
        try {

            using var playerRunOwner0 = new Ncma.Player.App.PlayerRunner();
            using var playerRunOwner1 = new Ncma.Player.App.PlayerRunner();

            using var playerRunOwner2 = new Ncma.Player.App.PlayerRunner();
            if(relocatedOnly) {
                SceneUiRelocatedInteraction(repo.FullName,configuration,runtime,projectPath,output,Pass);
                return 0;
            }
            var context=ProjectContext.Load(projectPath);
            using(var blocked=new CandidatePresentation("missing-plugins",context,true,false,false))Reject(blocked.Start,"default Editor requires trusted routes before plugins");
            using(var blocked=new CandidatePresentation("missing-plugins",context,true,false,false,uiActions:new()))Reject(blocked.Start,"unknown routes reject before plugins");
            var headless=new PlayerOptions(projectPath,true,"null",2,1d/60,30,Path.Combine(output,"null.json"),false,false);
            var noRoutes=playerRunOwner0.Run(headless,pluginRoot:"missing-plugins",visible:false);
            Pass(noRoutes.ExitCode==7&&noRoutes.Modules.Length==0,"default Null denies interactive package");
            var accepted=playerRunOwner1.Run(headless with{Report=Path.Combine(output,"null-routes.json")},pluginRoot:"missing-plugins",visible:false,uiActions:routes);
            Pass(accepted.ExitCode==0&&accepted.Modules.Length==0&&accepted.Tick==2,"Null CPU closure and exact routes no native modules");
            var graphical=playerRunOwner2.Run(headless with{Headless=false,Renderer="d3d11",Report=Path.Combine(output,"graphical-routes.json")},pluginRoot:plugins,visible:false,uiActions:routes);
            Pass(graphical.ExitCode==0&&graphical.Tick>=2&&graphical.RenderedFrames>0&&graphical.ValidationErrors==0&&graphical.ValidationWarnings==0,"formal PlayerRunner graphical startup with exact routes");

            // Actual Player class: same input method used immediately after native Poll in production.
            using(var package=RuntimeUiPackage.Open(runtime,fixture.Project,selection))
            using(var pins=SceneAssetPreparation.Prepare(runtime,fixture.Project,document.CaptureSnapshot(),true,"assets/scene.ncpak"))
            using(var owner=new RuntimeSessionOwner(document))
            using(var player=new PlayerPresentation(plugins,false)) {
                player.Start();player.PrepareBeforeGameplay(document,pins,package,routes);owner.LoadGameplay(context.GameplayAssemblyPath);
                var play=owner.StartPlay(compose:p=>{routes.Install(p);return null;});player.BindScene(play,pins,camera);
                var window=EnvironmentProductEvidence.Field<PlatformWindow>(player,"_window");var state=window.State;state.Focused=1;state.InputReset=state.Overflow=0;state.Sequence=0;
                ulong sequence=0;Vector3 Position()=>document.World.Get<TransformData>(document.World.FindEntity(document.World.GetUuid(target))).Position;
                InputEvent Pointer(uint action,double x,double y)=>new(){Kind=2,Action=action,Sequence=++sequence,X=x,Y=y};
                void Input(InputEvent[] input,double x=20,double y=20,double delta=1d/60) {
                    state.Sequence=sequence;state.PointerX=x;state.PointerY=y;player.ProcessInput(play,state,input);play.AdvanceFrame(delta);
                    Pass(play.State==PlayState.Running,"Player remains Running");player.Present(play);
                }
                Input([]);Input([Pointer(1,20,20),Pointer(0,20,20)]);
                Pass(Position()==new Vector3(1,0,0),"Player click updates C# once, mouse edge suppressed");
                Input([new(){Kind=1,Key=32,Action=1,Sequence=++sequence}]);
                Pass(Position()==new Vector3(2,0,0),"Player keyboard activation once, physical Space suppressed");
                Input([Pointer(1,20,60),Pointer(0,20,60),new(){Kind=5,Codepoint='A',Sequence=++sequence}],20,60);
                Pass(Position()==new Vector3(2,0,1),"Player text proposal consumes through fixed step");
                Input([Pointer(1,58,98),Pointer(0,58,98)],58,98);
                Pass(Position()==new Vector3(2,.5f,1),"Player slider normalized value");
                var hud=EnvironmentProductEvidence.Field<SceneUiPresentation>(player,"_ui");
                Pass(hud.Runtime.Widget(text.Id).Text=="A","Player owns runtime widget text");
                player.Present(play,(pixels,w,h)=>File.WriteAllBytes(Path.Combine(output,"player-interactive.rgba"),pixels));
                Input([Pointer(1,20,20),Pointer(0,20,20)],delta:0);play.Pause();player.ProcessInput(play,state,[]);play.Step();play.Resume();Input([]);
                Pass(Position()==new Vector3(2,.5f,1),"Player pause cancels pending click");
                Input([Pointer(1,20,20),Pointer(0,20,20)],delta:0);state.Focused=0;Input([]);state.Focused=1;Input([]);
                Pass(Position()==new Vector3(2,.5f,1),"Player focus loss cancels pending click");
                Input([Pointer(1,20,20),Pointer(0,20,20)],delta:0);var visibleState=state;state.Minimized=1;Input([]);state=visibleState;Input([]);
                Pass(Position()==new Vector3(2,.5f,1),"Player minimize cancels pending HUD input before resume");
                Input([Pointer(1,20,20),Pointer(0,20,20)],delta:0);state.FramebufferWidth=0;Input([]);state=visibleState;Input([]);
                Pass(Position()==new Vector3(2,.5f,1),"Player zero framebuffer cancels pending HUD input without minimized flag");
                hud.Runtime.CancelInput();Input([],delta:0);byte[]? beforeRecovery=null;
                player.Present(play,(p,_,_)=>beforeRecovery=p);var runtimeBefore=hud.Runtime;
                Input([Pointer(1,20,20),Pointer(0,20,20)],delta:0);var recoveryStatus=play.Status;
                player.RebuildGraphics(player.ReviewGraphicsRecovery());
                Pass(play.Status==recoveryStatus&&ReferenceEquals(runtimeBefore,hud.Runtime)&&hud.Runtime.Widget(text.Id).Text=="A"&&hud.Runtime.Widget(slider.Id).Value==.5f,"Player GPU recovery retains exact gameplay and text/slider runtime");
                byte[]? afterRecovery=null;player.Present(play,(p,_,_)=>afterRecovery=p);
                Pass(beforeRecovery is not null&&afterRecovery is not null&&beforeRecovery.SequenceEqual(afterRecovery),"Player recovery restores exact scene and glyph/widget pixels");
                Input([]);Pass(Position()==new Vector3(2,.5f,1),"Player recovery revokes pending pre-fault HUD proposal");
                Input([Pointer(1,20,20),Pointer(0,20,20)]);Pass(Position()==new Vector3(3,.5f,1),"Player new HUD input reaches original script once after recovery");
                Pass(player.Stats.ValidationErrors==0&&player.Stats.ValidationWarnings==0,"Player API0/0");
            }

            using(var editor=new CandidatePresentation(plugins,context,true,false,false,uiActions:routes)) {
                editor.Start();var owner=editor.VerificationEditor;var window=EnvironmentProductEvidence.Field<PlatformWindow>(editor,"_window");
                var author=EnvironmentProductEvidence.Field<Ncma.Editor.Services.EditorUiWorkspace>(editor,"_uiWorkspace");
                Pass(author.RegisteredActions.SequenceEqual(routes.RegisteredActions),"UI authoring and runtime use the same trusted action names");
                var state=window.State;state.Focused=1;state.InputReset=state.Overflow=0;state.Sequence=0;
                ulong sequence=0,frame=0;uint width=0,height=0;
                byte[] Draw(InputEvent[]? input=null,double delta=0,bool capture=false) {
                    state.Sequence=sequence;editor.SetWindowInputForVerification(state,input??[]);var f=new FrameContext(++frame,delta);
                    byte[]? pixels=null;if(capture)editor.CaptureNextCompositeViewportForVerification((p,w,h)=>{pixels=p;width=w;height=h;});
                    editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);return pixels??[];
                }
                InputEvent Pointer(uint action,float x,float y) {var area=editor.VerificationPresenter.Viewport;state.PointerX=area.X+x;state.PointerY=area.Y+y;return new(){Kind=2,Action=action,Sequence=++sequence,X=state.PointerX,Y=state.PointerY};}
                Draw();var baseline=Draw(capture:true);byte[] editBytes=owner.Document.CaptureBytes();
                owner.RefreshAssets(true);owner.Assets?.PrepareStoredGenerationsAsync().GetAwaiter().GetResult();
                for(int cycle=0;cycle<3;cycle++) {
                    var play=owner.StartPlay();Draw();Draw(delta:1d/60);
                    var editHud=EnvironmentProductEvidence.Field<SceneUiPresentation>(editor,"_sceneHud");var playHud=EnvironmentProductEvidence.Field<SceneUiPresentation>(editor,"_playHud");
                    if(cycle==0){var shared=EnvironmentProductEvidence.Field<UiTextResources>(editor,"_uiTextResources");Pass(shared.Modules.Count==1,"one lazy Text context shared by independent Edit/Play HUDs");Reject(shared.Dispose,"live fonts prevent shared Text shutdown");Reject(()=>shared.Get(Path.Combine(plugins,"foreign")),"shared Text rejects changing plugin roots");}
                    Pass(editHud.Runtime.Stamp.Instance!=playHud.Runtime.Stamp.Instance&&playHud.Runtime.Widget(text.Id).Text=="","independent fresh Play UI instance");
                    Draw([Pointer(1,20,20),Pointer(0,20,20)],1d/60);
                    Vector3 Position()=>play.Document.World.Get<TransformData>(play.Document.World.FindEntity(document.World.GetUuid(target))).Position;
                    Pass(Position()==new Vector3(1,0,0),"Editor click reaches script once without physical edge");
                    Draw([new(){Kind=1,Key=32,Action=1,Sequence=++sequence}],1d/60);
                    Pass(Position()==new Vector3(2,0,0),"Editor HUD consumes Space before shortcuts/gameplay");
                    var changed=Draw([Pointer(1,20,60),Pointer(0,20,60),new(){Kind=5,Codepoint='A',Sequence=++sequence}],1d/60,true);
                    Pass(Position()==new Vector3(2,0,1)&&playHud.Runtime.Widget(text.Id).Text=="A","Editor runtime text controls C#");
                    Pass(editHud.Runtime.Widget(text.Id).Text==""&&owner.Document.CaptureBytes().SequenceEqual(editBytes),"Edit UI/document remain isolated");
                    Pass(changed.Length==width*height*4&&!changed.SequenceEqual(baseline),"final Editor GUI composition displays changed text");
                    File.WriteAllBytes(Path.Combine(output,$"editor-interactive-{cycle}.rgba"),changed);
                    playHud.Runtime.CancelInput();Draw();var recoveryPixels=Draw(capture:true);
                    Draw([Pointer(1,20,20),Pointer(0,20,20)],0); // Queue a proposal without a fixed step.
                    var recoveryStatus=play.Status;
                    var recoveryRuntime=playHud.Runtime;var recoveryHistory=owner.Edit!.State;
                    editor.RebuildGraphics(editor.ReviewGraphicsRecovery());
                    Pass(ReferenceEquals(play,owner.Play)&&play.Status==recoveryStatus&&owner.Edit.State==recoveryHistory,"graphics recovery retains exact Play clocks/sole history");
                    Draw();
                    Pass(ReferenceEquals(playHud,EnvironmentProductEvidence.Field<SceneUiPresentation>(editor,"_playHud"))&&ReferenceEquals(recoveryRuntime,playHud.Runtime)&&playHud.Runtime.Widget(text.Id).Text=="A","graphics recovery retains original HUD text/runtime instance");
                    Pass(Draw(capture:true).SequenceEqual(recoveryPixels),"recovered final HUD glyph and scene pixels identical");
                    Draw(delta:1d/60);Pass(Position()==new Vector3(2,0,1),"recovery revokes pre-fault queued gameplay HUD proposal");
                    Draw([Pointer(1,20,20),Pointer(0,20,20)],1d/60);Pass(Position()==new Vector3(3,0,1),"fresh post-recovery click reaches original script exactly once");
                    Draw([Pointer(1,20,20),Pointer(0,20,20)],0);play.Pause();Draw();play.Step();play.Resume();Draw(delta:1d/60);
                    Pass(Position()==new Vector3(3,0,1),"Editor pause/single-step cancels pending input");
                    Draw([Pointer(1,20,20),Pointer(0,20,20)],0);var visibleState=state;state.Minimized=1;Draw(delta:1d/60);state=visibleState;Draw(delta:1d/60);
                    Pass(Position()==new Vector3(3,0,1),"Editor minimize cancels pending HUD input before resume");
                    Draw([Pointer(1,20,20),Pointer(0,20,20)],0);state.FramebufferWidth=0;Draw(delta:1d/60);state=visibleState;Draw(delta:1d/60);
                    Pass(Position()==new Vector3(3,0,1),"Editor zero framebuffer cancels pending HUD input without minimized flag");
                    Draw([Pointer(1,20,20),Pointer(0,20,20)],0);owner.ReloadGameplay(context.GameplayAssemblyPath);Draw();play.Resume();Draw(delta:1d/60);
                    Pass(Position()==new Vector3(3,0,1),"Editor reload revokes old proposal");
                    owner.StopPlay();Pass(Draw(capture:true).SequenceEqual(baseline),"Stop restores exact Edit HUD image");
                    var renderer=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");
                    Pass(renderer.UiTargetStats.Targets==1&&renderer.UiTargetStats.Leases==0,"Stop releases Play target/leases");
                    Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"Editor API0/0");
                }
            }
        } finally {foreach(var file in locks)file.Dispose();}
        File.WriteAllText(Path.Combine(output,"interaction-results.json"),JsonSerializer.Serialize(new{checks,labels,sourceHostProbes=true,relocatedInteraction=false,wholeD3Accepted=false}));
        Console.WriteLine($"PASS D3-C source HUD interaction: {checks} checks; {output}");return 0;
    }
}
