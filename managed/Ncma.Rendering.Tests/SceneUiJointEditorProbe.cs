using System.Diagnostics;
using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Rendering;
using Ncma.Rendering.Scene.Examples;
using Ncma.Scene.Rendering;

internal static class SceneUiJointEditorProbe
{
    internal static void Run(string requestPath)
    {
        using var e=new UiPublicationProductEvidence(requestPath);string[] native=[];var costs=new List<object>();int builds=0;byte[]? defaultImage=null;
        foreach(int mode in new[]{0,1,2}){
            var context=ProjectContext.Load(e.Project);
            RenderPipeline Factory(float exposure,float ambient,bool shadows){builds++;return mode==1?SceneCustomizationExamples.ReplaceGeometryStage(exposure*.5f,ambient,shadows):SceneCustomizationExamples.ReplacePipeline(exposure*.5f,ambient,shadows);}
            using(var editor=new CandidatePresentation(e.Plugins,context,true,false,false,pipelineFactory:mode==0?null:Factory)){
                editor.Start();ulong frame=0;uint width=0;byte[] Draw(bool capture=true){byte[]? pixels=null;if(capture)editor.CaptureNextCompositeViewportForVerification((p,w,h)=>{pixels=p;width=w;});editor.PumpPlatform();var f=new FrameContext(++frame,0);editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);return pixels??[];}
                Draw();byte[] baseline=Draw();void Hud(byte[] pixels){float scale=width/editor.VerificationPresenter.Viewport.Width;int at=((int)(20*scale)*(int)width+(int)(20*scale))*4;e.Pass(Math.Abs(pixels[at]-51)<=1&&Math.Abs(pixels[at+1]-102)<=1&&Math.Abs(pixels[at+2]-204)<=1,"final HUD color after full scene Tone");}
                Hud(baseline);if(mode==0)defaultImage=baseline;else e.Pass(!baseline.SequenceEqual(defaultImage!),"custom pipeline changes actual joint image");
                var renderer=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");var owner=editor.VerificationEditor;var workspace=new EditorWorkspace(owner);var edit=owner.Edit!;
                e.Pass(renderer.SkinStats.Meshes>0&&renderer.SkinStats.Batches>0&&renderer.PipelineStats.GeometryDraws>0&&renderer.PipelineStats.ShadowDraws>0&&renderer.EnvironmentStats.Live>0&&renderer.PostStats.Bindings==1&&renderer.UiTargetStats.Targets==1,"all real FBX skin geometry shadow IBL Bloom HUD subsystems live");
                var post=owner.Document.World.GetObjects().Single(o=>o.Has<PostProcessData>());Guid id=post.PersistentId;var original=post.Get<PostProcessData>();
                void Set(PostProcessData value){var command=new CapabilityRequest(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,edit.Revision,"ncma.scene.transaction",JsonSerializer.SerializeToElement(new{operations=new[]{new{op="set_component",objectId=id,typeId=PostProcessData.TypeId,version=1,data=owner.Document.World.Components.Encode(value)}}}));e.Pass(edit.Invoke(command).Status=="denied","default Agent cannot mutate");e.Pass(edit.Invoke(command,new(["ncma.scene.transaction"],objectScope:[id],componentScope:[PostProcessData.TypeId])).Changed,"original scoped joint command");}
                Set(original with{ExposureEv=-1});var changed=Draw();Hud(changed);e.Pass(!changed.SequenceEqual(baseline),"scene exposure changes without re-toning HUD");
                workspace.History(workspace.Stamp,false);e.Pass(Draw().SequenceEqual(baseline),"exact joint Undo");workspace.History(workspace.Stamp,true);e.Pass(Draw().SequenceEqual(changed),"exact joint Redo");Set(original);e.Pass(Draw().SequenceEqual(baseline),"joint restore");
                var hudObject=owner.Document.World.GetObjects().Single(o=>o.Has<SceneUiData>());Guid hudId=hudObject.PersistentId;
                var nextHud=SceneUiState.Read(Ncma.Scene.SceneDocumentCodec.Decode(File.ReadAllBytes(Path.Combine(context.Root,"next-ui.ncmascene"))))!.Value;
                var hudCommand=new CapabilityRequest(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,edit.Revision,"ncma.scene.transaction",JsonSerializer.SerializeToElement(new{operations=new[]{new{op="set_component",objectId=hudId,typeId=SceneUiData.TypeId,version=1,data=owner.Document.World.Components.Encode(nextHud)}}}));
                e.Pass(edit.Invoke(hudCommand).Status=="denied","joint HUD change default denied");e.Pass(edit.Invoke(hudCommand,new(["ncma.scene.transaction"],objectScope:[hudId],componentScope:[SceneUiData.TypeId])).Changed,"joint explicit prepublished HUD generation3");
                var nextImage=Draw();e.Pass(!nextImage.SequenceEqual(baseline),"new HUD generation changes joint final image");
                int changedOutside=0;float hudScale=width/editor.VerificationPresenter.Viewport.Width;for(int pixel=0;pixel<baseline.Length;pixel+=4){int x=pixel/4%(int)width,y=pixel/4/(int)width;if((x>73*hudScale||y>49*hudScale)&&!baseline.AsSpan(pixel,4).SequenceEqual(nextImage.AsSpan(pixel,4)))changedOutside++;}
                e.Pass(changedOutside==0,"HUD replacement preserves all outside scene pixels");workspace.History(workspace.Stamp,false);e.Pass(Draw().SequenceEqual(baseline),"Undo HUD generation exact joint image");workspace.History(workspace.Stamp,true);e.Pass(Draw().SequenceEqual(nextImage),"Redo HUD generation exact joint image");workspace.History(workspace.Stamp,false);Draw();
                var view=editor.VerificationPresenter;view.AttachLayout(new(Path.Combine(e.Output,$"workspace-{mode}.json")));Draw();var size=editor.VerificationViewport;var vf=view.Frame;
                GuiEvent Split(uint phase)=>new(){Kind=(uint)GuiItemKind.Splitter,WidgetHigh=32,WidgetLow=1,Phase=phase,Value=320,Frame=vf.Frame,ViewGeneration=vf.ViewGeneration,DocumentGeneration=vf.DocumentGeneration,Revision=vf.Revision};
                view.Apply([Split(1),Split(2),Split(3)],[]);baseline=Draw();Hud(baseline);e.Pass(editor.VerificationViewport!=size,"joint real viewport resize");
                owner.RefreshAssets(true);owner.Assets?.PrepareStoredGenerationsAsync().GetAwaiter().GetResult();
                for(int cycle=0;cycle<3;cycle++){
                    var play=owner.StartPlay();Draw();play.Pause();var first=Draw();bool moved=false;
                    for(int i=0;i<(cycle==0?64:2);i++){play.Step();var pose=Draw();moved|=!pose.SequenceEqual(first);if(i==0)Hud(pose);}
                    if(cycle==0)e.Pass(moved&&play.Document.World.Tick==64,"64 committed real FBX animated poses change joint image");
                    owner.ReloadGameplay(context.GameplayAssemblyPath);Hud(Draw());owner.StopPlay();e.Pass(Draw().SequenceEqual(baseline),"Stop restores Edit complete joint image");
                }
                for(int i=0;i<8;i++)Draw(false);var beforeUi=renderer.UiStats;var beforeTarget=renderer.UiTargetStats;var beforePost=renderer.PostStats;var beforeEnv=renderer.EnvironmentStats;var beforeScene=renderer.PipelineStats;int built=builds;
                long allocation=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();for(int i=0;i<16;i++)Draw(false);double ms=Stopwatch.GetElapsedTime(start).TotalMilliseconds;allocation=GC.GetAllocatedBytesForCurrentThread()-allocation;
                e.Pass(builds==built&&renderer.UiStats.UploadedBytes==beforeUi.UploadedBytes&&renderer.UiTargetStats.Productions==beforeTarget.Productions&&renderer.UiTargetStats.Leases==0&&renderer.PostStats.Publications==beforePost.Publications&&renderer.EnvironmentStats.UploadedBytes==beforeEnv.UploadedBytes&&renderer.PipelineStats.Creates==beforeScene.Creates,"warm no graph recreation or immutable HUD/environment uploads");
                e.Pass(renderer.Stats.ValidationWarnings==0&&renderer.Stats.ValidationErrors==0,"API0/0");e.Pass(renderer.EnvironmentStats.ResidentBytes<=renderer.EnvironmentStats.BudgetBytes,"environment budget");
                costs.Add(new{mode,frames=16,allocatedBytes=allocation,cpuMilliseconds=ms,fullPerformanceAccepted=false,gpuOriginFrameKnown=false,ui=renderer.UiStats,post=renderer.PostStats,skin=renderer.SkinStats,pipeline=renderer.PipelineStats});
                native=EnvironmentProductEvidence.NativePaths(e.Root);File.WriteAllBytes(Path.Combine(e.Output,$"editor-{mode}.rgba"),baseline);
            }
            e.Pass(EnvironmentProductEvidence.NativePaths(e.Root).Length==0,"all resources unload after each mode");
        }
        foreach(string name in new[]{"bad-model","bad-environment","bad-ui","bad-post-module"}){
            bool rejected=false;using(var editor=new CandidatePresentation(e.Plugins,ProjectContext.Load(Path.Combine(Path.GetDirectoryName(e.Project)!,name+".ncmaproject")),true,false,false)){
                try{editor.Start();}catch(Exception error)when(error is ArgumentException or InvalidOperationException or IOException or NotSupportedException){rejected=true;}
            }
            e.Pass(rejected,"joint Editor startup rejects closure: "+name);e.Pass(EnvironmentProductEvidence.NativePaths(e.Root).Length==0,"Editor failed closure closes native: "+name);
        }
        File.WriteAllText(Path.Combine(e.Output,"costs.json"),JsonSerializer.Serialize(costs,new JsonSerializerOptions{IncludeFields=true}));e.Finish(native);
    }
}
