using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation.Native;
using Ncma.Asset.Import;
using Ncma.Assets;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Gameplay;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Ui;
using Ncma.Ui.Rendering;

internal static unsafe partial class Program
{
    private static int PresentationStateTests(string plugins,string output)
    {
        plugins=Path.GetFullPath(plugins);output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        var repo=new DirectoryInfo(plugins);while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");
        int cases=0;void Pass(bool ok,string name){Check(ok,"M7.6 retained presentation state: "+name);cases++;}
        using(var loader=new PluginLoader()) {
            loader.Load(plugins,Specs());var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Retained presentation state",256,256,false);
            string kernelPath=Path.Combine(plugins,"NcmaAnimationKernel.dll");
            using var kernel=new PoseKernel(kernelPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(kernelPath))));
            string importPath=Path.Combine(Directory.GetParent(plugins)!.Parent!.FullName,"NcmaImportKernel.dll");
            foreach(string source in new[]{"synthetic","blender_279_sausage_6100_ascii.fbx","blender_279_sausage_7400_binary.fbx"}) {
                using var importer=new ImportKernel(importPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(importPath))));
                var fixture=new SkinFixture(output,source=="synthetic"?null:importer.LoadAndCopy(Path.Combine(repo.FullName,"tests/assets/fbx",source),30));
                var(document,camera,objects)=fixture.Scene();
                var blocks=DerivedAssetCodec.Decode(fixture.Plan.DerivedBytes);
                var animated=fixture.Manifest.Clips.Select(id=>(Id:id,Clip:ModelPayloadCodec.DecodeClip(blocks.Single(b=>b.AssetId==id).Data).Clip))
                    .OrderByDescending(c=>c.Clip.Tracks.Sum(t=>t.Keys.Count(k=>k.Value!=t.Keys[0].Value))).First();
                document.World.Set(document.World.FindEntity(objects[0]), document.World.Get<Ncma.Animation.ClipPlaybackData>(document.World.FindEntity(objects[0])) with{ClipId=animated.Id});
                using var prepared=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true);
                using var play=new PlaySession(document);play.Start(_=>throw new Exception("No behaviour factory expected"));
                RendererSession renderer=new(native,window,256,256);RenderResourceCache cache=new(renderer);
                SceneRenderSession? scene=new(renderer,cache,document.World);SceneAnimationSession? retained=null;GpuViewTarget? target=null;
                try {
                    scene.Prepare(prepared,document.CaptureSnapshot(),poseKernel:kernel,play:play);target=renderer.CreateViewTarget(256,256);ulong frame=0;
                    byte[] Draw(){Pass(scene!.Submit(++frame,256,256,camera,target:target),"actual skin submitted "+source);var pixels=new byte[256*256*4];renderer.CaptureTarget(target!,pixels);renderer.Present();renderer.WaitIdle();return pixels;}
                    Draw();var initial=new byte[fixture.Source.Meshes[0].Vertices.Length*48];scene.CaptureCharacterVertices(objects[0],initial);
                    for(int i=0;i<36;i++)play.AdvanceFrame(1d/60);play.Pause();var status=play.Status;var world=document.World;var bytes=document.CaptureBytes();
                    var before=Draw();var vertices=new byte[initial.Length];scene.CaptureCharacterVertices(objects[0],vertices);
                    Pass(!initial.SequenceEqual(vertices),"nonzero committed animation before recovery "+source);
                    retained=scene.RetainAnimationForRecovery();Pass(retained is not null&&ReferenceEquals(retained,scene.Animation),"exact CPU animation owner transfer "+source);
                    Reject(retained!.Dispose);Reject(()=>scene.RetainAnimationForRecovery());
                    using(var premature=new SceneRenderSession(renderer,cache,world))Reject(()=>premature.Prepare(prepared,document.CaptureSnapshot(),poseKernel:kernel,play:play,retainedAnimation:retained));
                    Pass(play.Status==status,"borrow/early-release refusals never restart or tick Play "+source);
                    scene.Dispose();scene=null;target.Dispose();target=null;cache.Dispose();ulong old=renderer.ReadFrameState().Generation;renderer.Dispose();
                    Pass(PinLocked(fixture.GenerationPath)&&play.Status==status,"CPU assets/clocks survive complete device close "+source);
                    renderer=new(native,window,256,256);cache=new(renderer);target=renderer.CreateViewTarget(256,256);
                    using(var foreign=new SceneRenderSession(renderer,cache,fixture.Scene().Document.World))Reject(()=>foreign.Prepare(prepared,document.CaptureSnapshot(),poseKernel:kernel,play:play,retainedAnimation:retained));
                    scene=new(renderer,cache,world);scene.Prepare(prepared,document.CaptureSnapshot(),poseKernel:kernel,play:play,retainedAnimation:retained);
                    Pass(ReferenceEquals(scene.Animation,retained)&&ReferenceEquals(world,document.World)&&play.Status==status&&bytes.SequenceEqual(document.CaptureBytes()),"same World/status/clock instance at replacement "+source);
                    Pass(renderer.ReadFrameState().Generation!=old&&before.SequenceEqual(Draw()),"new-device exact final pixels "+source);
                    var after=new byte[vertices.Length];scene.CaptureCharacterVertices(objects[0],after);Pass(vertices.SequenceEqual(after),"exact GPU vertices retain clip progress "+source);
                    play.Step();Draw();Pass(play.Tick==status.Tick+1&&play.State==PlayState.Paused,"original committed observer steps once after rebind "+source);
                    scene.CaptureCharacterVertices(objects[0],after);Pass(!vertices.SequenceEqual(after),"original clock continues instead of freezing "+source);
                    Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"API0/0 "+source);
                }finally{scene?.Dispose();target?.Dispose();cache.Dispose();retained?.Dispose();renderer.Dispose();}
            }
            var definition=UiDefinition.Create(Guid.NewGuid(),"Retained HUD");Guid font=Guid.NewGuid();
            var text=UiElement.Create(Guid.NewGuid(),"Text",UiKind.TextInput,definition.Root) with{Font=font,Layout=UiLayout.Fixed(8,8,200,32),Action="text"};
            var slider=UiElement.Create(Guid.NewGuid(),"Value",UiKind.Slider,definition.Root) with{Order=1,Layout=UiLayout.Fixed(8,48,160,24),Action="value"};
            var button=UiElement.Create(Guid.NewGuid(),"Click",UiKind.Button,definition.Root) with{Order=2,Layout=UiLayout.Fixed(8,88,80,24),Action="click"};
            definition=definition with{Elements=[definition.Elements[0],text,slider,button]};Guid project=Guid.NewGuid();
            var packageBytes=RuntimeUiPackage.Encode(project,1,definition,[new(font,File.ReadAllBytes(Path.Combine(repo.FullName,"engine/sdk/imgui/misc/fonts/ProggyClean.ttf")),"ProggyClean MIT",true)],[]);
            string hash=Convert.ToHexString(SHA256.HashData(packageBytes));using var package=RuntimeUiPackage.Decode(packageBytes,project,definition.AssetId,1,hash);
            using var duplicate=RuntimeUiPackage.Decode(packageBytes,project,definition.AssetId,1,hash);
            RendererSession uiRenderer=new(native,window,256,256,pureUi:true);using var hud=new SceneUiPresentation();var view=new UiViewport(256,256,1,default);
            try {
                hud.Prepare(package,uiRenderer,plugins,view,interactive:true);var runtime=hud.Runtime;runtime.SetText(text.Id,"Retained text");runtime.SetValue(slider.Id,.73f);hud.PrepareView(view);
                byte[] DrawUi(ulong f){hud.Submit(f,false);var p=new byte[256*256*4];uiRenderer.Capture(p);uiRenderer.Present();return p;}
                var before=DrawUi(1);var stamp=runtime.Stamp;
                runtime.Input(stamp,[new(UiInputKind.PointerDown,1,new(20,100)),new(UiInputKind.PointerUp,2,new(20,100))]);
                Pass(runtime.QueuedActions==1,"pre-fault copied UI proposal exists");
                hud.RetireGraphics();Pass(runtime.QueuedActions==0&&runtime.Stamp.Generation>stamp.Generation&&runtime.Focus==Guid.Empty,"retirement revokes input without replacing runtime");
                Reject(()=>hud.Prepare(duplicate,uiRenderer,plugins,view,true));Pass(ReferenceEquals(runtime,hud.Runtime),"same bytes foreign package cannot replace recovery identity");
                uiRenderer.Dispose();uiRenderer=new(native,window,256,256,pureUi:true);hud.Prepare(package,uiRenderer,plugins,view,true);
                Pass(ReferenceEquals(runtime,hud.Runtime)&&runtime.Text(text.Id)=="Retained text"&&runtime.Widget(slider.Id).Value==.73f,"CPU text and slider survive GPU replacement");
                Reject(()=>runtime.Input(stamp,[new(UiInputKind.Activate,3,default)]));Pass(before.SequenceEqual(DrawUi(2)),"exact glyph/widget pixels after recovery");
                Pass(uiRenderer.Stats.ValidationErrors==0&&uiRenderer.Stats.ValidationWarnings==0&&uiRenderer.Stats.LiveGroups==0,"pure2D no3D/API0/0");
            }finally{hud.Dispose();uiRenderer.Dispose();}
        }
        // Formal Edit also retains independent preview progress. The separate Play recovery
        // suite covers its control freeze, private scripts and ordinary application loop.
        var author=new SkinFixture(output);var(editDocument,editCamera,_)=author.Scene();
        string gameplay=Path.Combine(repo.FullName,"out/managed/Ncma.Gameplay.Sample.dll");File.Copy(gameplay,Path.Combine(author.Root,"gameplay.dll"));File.Copy(Path.ChangeExtension(gameplay,".deps.json"),Path.Combine(author.Root,"gameplay.deps.json"));
        SceneDocumentFiles.Save(editDocument,Path.Combine(author.Root,"start.ncmascene"));string projectPath=Path.Combine(author.Root,"project.ncmaproject");
        File.WriteAllBytes(projectPath,JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1,author.Project,"Retained preview","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:editCamera),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        using(var editor=new CandidatePresentation(plugins,ProjectContext.Load(projectPath),true,false,false)) {
            editor.Start();ulong frame=0;
            byte[] DrawEditor(){byte[]? pixels=null;editor.CaptureNextCompositeViewportForVerification((p,_,_)=>pixels=p);editor.PumpPlatform();var f=new FrameContext(++frame,0);editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);return pixels??throw new Exception("No capture");}
            DrawEditor();var scene=EnvironmentProductEvidence.Field<SceneRenderSession>(editor,"_editScene");var animation=scene.Animation!;animation.AdvancePreview(.2);animation.AdvancePreview(.2);var before=DrawEditor();
            editor.RebuildGraphics(editor.ReviewGraphicsRecovery());DrawEditor();
            Pass(ReferenceEquals(animation,EnvironmentProductEvidence.Field<SceneRenderSession>(editor,"_editScene").Animation)&&before.SequenceEqual(DrawEditor()),"formal Edit exact preview clock and pixels survive");
            editor.RebuildGraphics(editor.ReviewGraphicsRecovery());DrawEditor();Pass(before.SequenceEqual(DrawEditor()),"second recovery preserves same borrowed animation");
            var owner=editor.VerificationEditor;var workspace=new Ncma.Editor.Services.EditorWorkspace(owner);
            var id=workspace.CreateObject(workspace.Stamp);DrawEditor();
            Pass(!ReferenceEquals(animation,EnvironmentProductEvidence.Field<SceneRenderSession>(editor,"_editScene").Animation)&&owner.Document.CaptureSnapshot().Objects.Any(o=>o.Id==id),"ordinary document replacement releases invalidated CPU borrower after recovery");
            workspace.History(workspace.Stamp,false);DrawEditor();Pass(!owner.Document.CaptureSnapshot().Objects.Any(o=>o.Id==id),"animated Undo after recovery releases old World owners");
            workspace.History(workspace.Stamp,true);DrawEditor();Pass(owner.Document.CaptureSnapshot().Objects.Any(o=>o.Id==id),"animated Redo after recovery rebuilds current World");
            editor.RebuildGraphics(editor.ReviewGraphicsRecovery());workspace.History(workspace.Stamp,false);
            editor.RebuildGraphics(editor.ReviewGraphicsRecovery());DrawEditor();
            Pass(EnvironmentProductEvidence.Field<SceneRenderSession>(editor,"_editScene").Animation!.WorldId==owner.Document.World.Identity,"recovery before next normal frame drops obsolete snapshot clocks safely");
        }
        // A first recovery can follow Undo before any normal frame replaced the stale scene.
        // Unlike the cases above, no CPU owner has been transferred to the host yet.
        using(var editor=new CandidatePresentation(plugins,ProjectContext.Load(projectPath),true,false,false)) {
            editor.Start();var f=new FrameContext(1,0);editor.PumpPlatform();editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);
            var owner=editor.VerificationEditor;var workspace=new Ncma.Editor.Services.EditorWorkspace(owner);
            var oldAnimation=EnvironmentProductEvidence.Field<SceneRenderSession>(editor,"_editScene").Animation!;
            workspace.CreateObject(workspace.Stamp);workspace.History(workspace.Stamp,false);
            editor.RebuildGraphics(editor.ReviewGraphicsRecovery());
            var currentAnimation=EnvironmentProductEvidence.Field<SceneRenderSession>(editor,"_editScene").Animation!;
            Pass(!ReferenceEquals(oldAnimation,currentAnimation)&&currentAnimation.WorldId==owner.Document.World.Identity,"first recovery after pending Undo retires stale owned animation without borrowing it");
        }
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,actualDx11=true,asciiBinaryFbx=true,retainedCpuAnimation=true,retainedUiRuntime=true,formalEdit=true,formalPlayRecovery=false,wholeStageAccepted=false}));
        Console.WriteLine($"PASS M7.6 presentation state {cases} checks; {output}");return 0;
    }
}
