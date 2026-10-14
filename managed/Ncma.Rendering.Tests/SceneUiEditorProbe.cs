using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Rendering;
using Ncma.Scene.Rendering;

// Kept separate from the Player probe: a relocated Player must never resolve Editor assemblies.
internal static class SceneUiEditorProbe
{
    internal static void Run(string requestPath)
    {
        using var request=JsonDocument.Parse(File.ReadAllBytes(requestPath));var r=request.RootElement;
        string project=r.GetProperty("project").GetString()!,output=r.GetProperty("output").GetString()!,plugins=r.GetProperty("plugins").GetString()!,root=r.GetProperty("productRoot").GetString()!;
        bool relocated=r.GetProperty("relocated").GetBoolean();int checks=0;
        void Pass(bool value,string name){if(!value)throw new Exception("Editor HUD "+name);checks++;}
        string Project(string name)=>Path.Combine(Path.GetDirectoryName(project)!,name+".ncmaproject");
        byte[]? baseline=null;uint baselineWidth=0;
        foreach(string name in new[]{"no-ui","project","no-font"}) {
            var context=ProjectContext.Load(Project(name));
            using var editor=new CandidatePresentation(plugins,context,true,false,false);
            editor.Start();ulong frame=0;uint width=0,height=0;
            byte[] Draw(bool capture=true) {
                byte[]? pixels=null;if(capture)editor.CaptureNextCompositeViewportForVerification((p,w,h)=>{pixels=p;width=w;height=h;});
                if(!editor.PumpPlatform())throw new Exception("Window closed");var f=new FrameContext(++frame,0);
                editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);return pixels??[];
            }
            Draw(false);var image=Draw();Pass(image.Length==width*height*4,"actual final composited viewport");
            if(name=="no-ui"){baseline=image;baselineWidth=width;continue;}
            float scale=width/editor.VerificationPresenter.Viewport.Width;
            int at=checked(((int)(20*scale)*(int)width+(int)(20*scale))*4);
            Pass(Math.Abs(image[at]-51)<=1&&Math.Abs(image[at+1]-102)<=1&&Math.Abs(image[at+2]-204)<=1,"HUD appears after scene Tone and GUI");
            int changed=0,outside=0;for(int i=0;i<image.Length;i+=4){if(!image.AsSpan(i,4).SequenceEqual(baseline!.AsSpan(i,4))){changed++;int x=i/4%(int)width,y=i/4/(int)width;if(x>257*scale||y>81*scale)outside++;}}
            Pass(baselineWidth==width&&changed>500&&outside==0,"uncovered scene preserved");
            File.WriteAllBytes(Path.Combine(output,name+"-final-viewport.rgba"),image);
            var renderer=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");var before=renderer.UiTargetStats;ulong uploaded=renderer.UiStats.UploadedBytes;
            for(int i=0;i<16;i++)Draw(false);
            Pass(renderer.UiTargetStats.Productions==before.Productions&&renderer.UiStats.UploadedBytes==uploaded&&renderer.UiTargetStats.Leases==0,"warm16 cached target no upload/production/live lease");
            var owner=editor.VerificationEditor;
            var workspace=new EditorWorkspace(owner);var edit=owner.Edit!;
            var light=owner.Document.World.GetObjects().Single(o=>o.Has<DirectionalLightData>());
            Guid lightId=light.PersistentId;var initial=light.Get<DirectionalLightData>();
            var command=new CapabilityRequest(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,edit.Revision,"ncma.scene.transaction",
                JsonSerializer.SerializeToElement(new{operations=new[]{new{op="set_component",objectId=lightId,typeId=DirectionalLightData.TypeId,version=1,data=owner.Document.World.Components.Encode(initial with{Intensity=initial.Intensity*.25f})}}}));
            Pass(edit.Invoke(command).Status=="denied","Agent remains read-only");
            Pass(edit.Invoke(command,new(["ncma.scene.transaction"],objectScope:[lightId],componentScope:[DirectionalLightData.TypeId])).Changed,"original scoped light command");
            var dim=Draw();Pass(!dim.SequenceEqual(image)&&dim.AsSpan(at,4).SequenceEqual(image.AsSpan(at,4)),"scene changes without re-toning HUD");
            workspace.History(workspace.Stamp,false);Pass(Draw().SequenceEqual(image),"Undo restores final scene plus HUD");
            workspace.History(workspace.Stamp,true);Pass(Draw().SequenceEqual(dim),"Redo restores final scene plus HUD");
            workspace.History(workspace.Stamp,false);Pass(Draw().SequenceEqual(image),"restore edit baseline");
            var view=editor.VerificationPresenter;view.AttachLayout(new(Path.Combine(output,name+"-workspace.json")));Draw(false);
            var splitter=view.Items.ToArray().Single(i=>i.Kind==(uint)GuiItemKind.Splitter&&i.WidgetHigh==32&&i.WidgetLow==1);var vf=view.Frame;
            GuiEvent Split(uint phase)=>new(){Kind=splitter.Kind,WidgetHigh=32,WidgetLow=1,Phase=phase,Value=320,Frame=vf.Frame,ViewGeneration=vf.ViewGeneration,DocumentGeneration=vf.DocumentGeneration,Revision=vf.Revision};
            view.Apply([Split(1),Split(2),Split(3)],[]);uint previousWidth=width;image=Draw();
            scale=width/view.Viewport.Width;at=checked(((int)(20*scale)*(int)width+(int)(20*scale))*4);
            Pass(width!=previousWidth&&Math.Abs(image[at+2]-204)<=1,"actual viewport resize retains HUD");
            Pass(renderer.UiTargetStats.Targets==1&&renderer.UiTargetStats.Leases==0,"resize releases old target and all GUI leases");
            owner.RefreshAssets(true);owner.Assets?.PrepareStoredGenerationsAsync().GetAwaiter().GetResult();
            for(int cycle=0;cycle<3;cycle++) {
                var play=owner.StartPlay();Pass(Draw().SequenceEqual(image),"isolated Play displays HUD");
                play.Pause();owner.ReloadGameplay(context.GameplayAssemblyPath);Pass(Draw().SequenceEqual(image),"Reload HUD");
                owner.StopPlay();Pass(Draw().SequenceEqual(image),"Stop restores Edit HUD");
            }
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"API0/0");
        }
        foreach(string name in new[]{"bad-hash","bad-generation","missing","interactive","bad-font","missing-ui-shader"}) {
            using var editor=new CandidatePresentation(plugins,ProjectContext.Load(Project(name)),true,false,false);
            bool rejected=false;try{editor.Start();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or System.IO.IOException or NotSupportedException or Ncma.Interop.PluginException){rejected=true;}
            Pass(rejected,name+" fails before successful startup");
        }
        if(relocated) {
            var info=new System.Diagnostics.ProcessStartInfo(Path.Combine(root,"NcmaEngine.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string arg in new[]{"--project",project,"--smoke-test","--frames","4"})info.ArgumentList.Add(arg);
            using var child=System.Diagnostics.Process.Start(info)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
            if(!child.WaitForExit(45000)){child.Kill(entireProcessTree:true);child.WaitForExit();throw new Exception("Owned Editor child timeout");}
            File.WriteAllText(Path.Combine(output,"editor-apphost.log"),stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult());Pass(child.ExitCode==0,"real relocated Editor apphost");
        }
        File.WriteAllText(Path.Combine(output,"probe.json"),JsonSerializer.Serialize(new{checks,relocated,interactionAccepted=false,wholeD3Accepted=false}));
    }
}
