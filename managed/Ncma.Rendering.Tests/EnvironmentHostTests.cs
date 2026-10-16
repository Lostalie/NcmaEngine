using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Assets;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static int EnvironmentHostTests(string root,string output)
    {

        using var playerRunOwner0 = new Ncma.Player.App.PlayerRunner();
        using var playerRunOwner2 = new Ncma.Player.App.PlayerRunner();

        using var playerRunOwner3 = new Ncma.Player.App.PlayerRunner();
        using var playerRunOwner4 = new Ncma.Player.App.PlayerRunner();

        using var playerRunOwner5 = new Ncma.Player.App.PlayerRunner();
        using var playerRunOwner6 = new Ncma.Player.App.PlayerRunner();

        using var playerRunOwner8 = new Ncma.Player.App.PlayerRunner();
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        int cases=0,maxError=0;void Pass(bool value,string label){Check(value,"M7.3-C2-C "+label);cases++;}
        var repository=new DirectoryInfo(Path.GetFullPath(root));while(!File.Exists(Path.Combine(repository.FullName,"Build.bat")))repository=repository.Parent??throw new Exception("Repository missing");
        var fixture=new SceneGpuFixture(output);var (document,camera,_,_)=fixture.Scene();
        global::Ncma.Runtime.Entity light=document.World.GetEntities().Single(o=>document.World.Has<DirectionalLightData>(o));document.World.Set(light, document.World.Get<DirectionalLightData>(light) with{Intensity=0,CastShadow=false});
        document.World.Set(document.World.CreateEntity("Rendering"), RenderConfiguration.Default(Guid.NewGuid()) with{Ambient=0});
        EnvironmentPackage package;var shaders=new List<RuntimeShaderPackage>();RuntimeShaderPackage wrong;RuntimeShaderPackage old;
        using(var loader=new PluginLoader()){
            loader.Load(Path.GetFullPath(root),Specs().Take(2).ToArray());var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Host fixture cook",256,256,false);
            using var renderer=new RendererSession(native,window,256,256);using var cook=new EnvironmentCookService(native,()=>true);
            var source=HdrEnvironmentSource.Prepare(Guid.NewGuid(),8,4,Enumerable.Range(0,32).SelectMany(_=>new[]{.18f,.18f,.18f,1f}).ToArray());
            package=cook.Cook(Guid.NewGuid(),1,source,new(8,4,8,128));
            using var compiler=new ShaderCompilerService(renderer,()=>true);
            RuntimeShaderPackage User(bool shadow,bool skin,bool bad=false){
                var selected=DefaultEnvironmentSceneShaders.Select(DefaultEnvironmentSceneShaders.CopyCatalog(renderer,shadow),shadow);
                var descriptors=shadow?new[]{selected.GeometryVertex,selected.GeometryPixel,selected.ToneVertex,selected.TonePixel,selected.ShadowVertex!,selected.ShadowPixel!}:new[]{selected.GeometryVertex,selected.GeometryPixel,selected.ToneVertex,selected.TonePixel};
                var inputs=new List<RuntimeShaderInput>();for(int i=0;i<descriptors.Length;i++){
                    var d=descriptors[i];if(i==1){var definition=d.CopyDefinition();string text=bad?definition.Source.Replace(":SV_TARGET",":SV_TARGET1",StringComparison.Ordinal):definition.Source.Replace("*ao*EnvironmentSettings.x;","*ao*EnvironmentSettings.x*.5;",StringComparison.Ordinal);Pass(text!=definition.Source,"actual custom/wrong pixel source");d=ShaderDescriptor.Prepare(definition with{AssetId=Guid.NewGuid(),Source=text,SourceHash=ShaderContractCodec.HashSource(text)});}
                    inputs.Add(new(i<2?(i==0?RuntimeShaderRole.EnvironmentGeometryVertex:RuntimeShaderRole.EnvironmentGeometryPixel):(RuntimeShaderRole)(i+2),compiler.Prepare(d)));
                }
                if(skin)inputs.Add(new(RuntimeShaderRole.SkinCompute,compiler.Prepare(DefaultSkinShader.Select(DefaultSkinShader.CopyCatalog(renderer)))));
                return RuntimeShaderPackage.Cook(ShaderProfile.SceneEnvironment,shadow,skin,inputs);
            }
            foreach(bool skin in new[]{false,true})foreach(bool shadow in new[]{false,true})shaders.Add(User(shadow,skin));wrong=User(false,false,true);
            old=renderer.DefaultRuntimeShaders.Prepare(ShaderProfile.Flat2D,false,false,()=>true).Package;
            Pass(renderer.EnvironmentStats.Live==0,"cook no GPU environments");
        }
        var envValue=new EnvironmentLightingData(1,package.AssetId,package.Generation,package.ContentHash,1,0,true);
        global::Ncma.Runtime.Entity env=document.World.CreateEntity("Environment");Guid environmentId=document.World.GetUuid(env);document.World.Set(env, envValue);
        void Publish(string projectRoot,Guid project){var d=EnvironmentAssetDescriptor.FromPackage(project,package);string path=Path.Combine(projectRoot,d.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,package.CopyBytes());File.WriteAllBytes(Path.Combine(projectRoot,"assets/environment.ncenv"),d.Encode());}
        Publish(fixture.Root,fixture.Project);Directory.CreateDirectory(Path.Combine(fixture.Root,"assets/shaders"));
        ShaderPackageFile Save(RuntimeShaderPackage p,string name){string path="assets/shaders/"+name+".ncshader";File.WriteAllBytes(Path.Combine(fixture.Root,path),p.CopyBytes());return new(path,p.ContentHash,p.Profile.ToString(),p.Shadows,p.Skinning);}
        var files=shaders.Select((p,i)=>Save(p,"environment"+i)).ToArray();var wrongFile=Save(wrong,"wrong-output");var oldFile=Save(old,"only-flat");
        var selection=new ShaderPackageSelection(2,files);string sample=Path.Combine(repository.FullName,"out/managed/Ncma.Gameplay.Sample.dll");
        void Gameplay(string projectRoot){File.Copy(sample,Path.Combine(projectRoot,"gameplay.dll"));File.Copy(Path.ChangeExtension(sample,".deps.json"),Path.Combine(projectRoot,"gameplay.deps.json"));}
        Gameplay(fixture.Root);string scenePath=Path.Combine(fixture.Root,"start.ncmascene"),projectPath=Path.Combine(fixture.Root,"project.ncmaproject");SceneDocumentFiles.Save(document,scenePath);
        var projectConfig=new ProjectConfiguration(1,fixture.Project,"Environment formal hosts","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:camera,ShaderPackages:selection);
        var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
        void Project(ProjectConfiguration value)=>File.WriteAllBytes(projectPath,JsonSerializer.SerializeToUtf8Bytes(value,json));
        Project(projectConfig);
        PlayerOptions Options(bool headless=false)=>new(projectPath,headless,null,4,1d/60,30,Path.Combine(output,Guid.NewGuid()+".json"),false,false);
        var oracle=new IblImageOracle(package);
        void Pixel(byte[] image,uint w,uint h,float strength){
            int x=(int)w/2,y=(int)h/2;var p=new Vector3(((x+.5f)/w*2-1)*2*w/h,(1-(y+.5f)/h*2)*2,-4);
            var expected=oracle.Evaluate(p,Vector3.UnitZ,new(.6f,.25f,.1f),0,.6f,1,strength,0);
            int error=0;for(int c=0;c<3;c++)error=Math.Max(error,Math.Abs(image[(y*(int)w+x)*4+c]-(int)MathF.Round(expected[c]*255)));
            maxError=Math.Max(maxError,error);Check(error<=3,"M7.3-C2-C formal host independent pixel error="+error);
        }
        int playerCaptures=0;var player=playerRunOwner0.RunForVerification(Options(),Path.GetFullPath(root),(image,w,h)=>{Pixel(image,w,h,.5f);if(playerCaptures++==0)File.WriteAllBytes(Path.Combine(output,"formal-player.png"),Png(w,h,image));});
        Pass(player.ExitCode==0&&player.Tick>=4&&player.RenderedFrames>0&&playerCaptures>0&&player.ValidationErrors==0&&player.ValidationWarnings==0&&player.ShutdownErrors.Length==0,"formal Player actual user shader/IBL/API0/0");
        var badSelection=selection with{Packages=files.Select(f=>f.Shadows==false&&!f.Skinning?wrongFile:f).ToArray()};
        foreach(var choice in new[]{new ShaderPackageSelection(1,[oldFile]),badSelection}){

            using var playerRunOwner1 = new Ncma.Player.App.PlayerRunner();
            Project(projectConfig with{ShaderPackages=choice});var rejected=playerRunOwner1.Run(Options(),pluginRoot:Path.GetFullPath(root),visible:false);
            Pass(rejected.ExitCode==3&&rejected.Tick==0&&rejected.SessionId==Guid.Empty&&rejected.RenderedFrames==0&&rejected.ShutdownErrors.Length==0,"missing profile/actual wrong output before gameplay initialization");
        }
        Project(projectConfig);byte[] sourceScene=File.ReadAllBytes(scenePath);
        document.World.Set(env, envValue with{Generation=99});SceneDocumentFiles.Save(document,Path.Combine(fixture.Root,"wrong-generation.ncmascene"));
        Project(projectConfig with{StartupScene="wrong-generation.ncmascene"});var wrongGeneration=playerRunOwner2.Run(Options(),pluginRoot:Path.GetFullPath(root),visible:false);
        Pass(wrongGeneration.ExitCode==3&&wrongGeneration.SessionId==Guid.Empty&&wrongGeneration.Tick==0&&wrongGeneration.ShutdownErrors.Length==0,"wrong startup environment generation pre-gameplay");document.World.Set(env, envValue);Project(projectConfig);
        int inputs=0;bool black=false,lit=false;
        var dynamic=playerRunOwner3.RunForVerification(Options() with{Ticks=8},Path.GetFullPath(root),(image,w,h)=>{int at=((int)h/2*(int)w+(int)w/2)*4;black|=image[at]==0&&image[at+1]==0&&image[at+2]==0;lit|=image[at]>0;},(play,_)=>{
            play.Document.World.Set(play.Document.World.FindEntity(environmentId), ++inputs==1?EnvironmentLightingData.Off:envValue);
        });
        Pass(dynamic.ExitCode==0&&black&&lit&&dynamic.ValidationErrors==0&&dynamic.ValidationWarnings==0,"formal Player committed Off/on switches affect pixels");
        var invalidDynamic=playerRunOwner4.Run(Options(),pluginRoot:Path.GetFullPath(root),visible:false,trustedInput:(play,_)=>play.Document.World.Set(play.Document.World.FindEntity(environmentId), envValue with{Generation=99}));
        Pass(invalidDynamic.ExitCode==5&&invalidDynamic.SessionId!=Guid.Empty&&invalidDynamic.ShutdownErrors.Length==0,"unprepared dynamic generation fails explicitly after initialization");
        Pass(sourceScene.SequenceEqual(File.ReadAllBytes(scenePath)),"Player never modifies author document");
        // Explicit runtime package; author-source closure remains only in fixture, not runtime reads.
        string packagePath="assets/game.ncpak";File.WriteAllBytes(Path.Combine(fixture.Root,packagePath),SceneAssetPreparation.CreateRuntimePackage(fixture.Root,fixture.Project,document.CaptureSnapshot(),[]));
        projectConfig=projectConfig with{AssetPackage=packagePath};Project(projectConfig);
        var packaged=playerRunOwner5.RunForVerification(Options(),Path.GetFullPath(root),(image,w,h)=>Pixel(image,w,h,.5f));
        Pass(packaged.ExitCode==0&&packaged.ValidationErrors==0&&packaged.ValidationWarnings==0,"formal source-free NCP Player image");
        var headless=playerRunOwner6.Run(Options(true),pluginRoot:Path.GetFullPath(root),visible:false);
        Pass(headless.ExitCode==0&&headless.Tick==4&&headless.RenderedFrames==0&&headless.Modules.Length==0,"Headless keeps CPU asset contract without renderer/environment GPU");
        using(var editor=new CandidatePresentation(Path.GetFullPath(root),ProjectContext.Load(projectPath),true,false,false)){
            editor.Start();ulong frame=0;
            byte[] Draw(){byte[]? pixels=null;editor.CaptureNextViewportForVerification((data,w,h)=>pixels=data);Pass(editor.PumpPlatform(),"formal Editor window pump");var context=new FrameContext(++frame,1d/60);editor.ApplyEditorIntents(context);editor.PumpAgent(context);editor.BeginPresentation(context);editor.SubmitInput(context);editor.AdvancePlay(context);editor.Present(context);return pixels??throw new Exception("Missing verified frame");}
            byte[] baseline=Draw();var size=editor.VerificationViewport;Pixel(baseline,size.Width,size.Height,.5f);File.WriteAllBytes(Path.Combine(output,"formal-editor.png"),Png(size.Width,size.Height,baseline));
            var owner=editor.VerificationEditor;var permission=new CapabilityPermissions(["ncma.scene.transaction","ncma.history.undo","ncma.history.redo"]);
            var workspace=new EditorWorkspace(owner);workspace.Select(workspace.Stamp,environmentId);Draw();
            var inspector=editor.VerificationPresenter;
            Pass(inspector.EnvironmentPreparationStatus.Contains("就绪"),"C3-B1 actual prepared status");
            var strengthItem=inspector.Items.ToArray().Single(i=>System.Text.Encoding.UTF8.GetString(inspector.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))=="环境光强度");
            Ncma.Gui.GuiEvent Field(uint phase,double value=0)=>new(){Kind=strengthItem.Kind,WidgetHigh=strengthItem.WidgetHigh,WidgetLow=strengthItem.WidgetLow,Phase=phase,Value=value,
                Frame=inspector.Frame.Frame,ViewGeneration=inspector.Frame.ViewGeneration,DocumentGeneration=inspector.Frame.DocumentGeneration,Revision=inspector.Frame.Revision};
            inspector.Apply([Field(1),Field(2,.25),Field(3,.25)],[]);
            var inspectorImage=Draw();Pixel(inspectorImage,size.Width,size.Height,.125f);
            Pass(!inspectorImage.SequenceEqual(baseline)&&owner.Document.World.Get<EnvironmentLightingData>(owner.Document.World.FindEntity(environmentId)).Strength==.25f,"C3-B1 actual Inspector events change IBL pixels");
            workspace.History(workspace.Stamp,false);Pass(Draw().SequenceEqual(baseline),"C3-B1 Inspector Undo restores actual image");
            Pass(owner.Edit!.Invoke(RenderingEditorAdapter.EnvironmentRequest(owner.Edit,environmentId,envValue with{Strength=.5f},owner.Edit.Revision)).Status=="denied","formal Editor Agent default read-only");
            Pass(owner.Edit.Invoke(RenderingEditorAdapter.EnvironmentRequest(owner.Edit,environmentId,envValue with{Strength=.5f},owner.Edit.Revision),permission).Changed,"formal command shared history");
            var half=Draw();Pixel(half,size.Width,size.Height,.25f);Pass(!half.SequenceEqual(baseline),"formal editor committed strength actual pixels");
            CapabilityRequest History(string id)=>new(EditSession.ContractVersion,Guid.NewGuid(),owner.Edit.SessionId,owner.Edit.Revision,id,JsonSerializer.SerializeToElement(new{}));
            Pass(owner.Edit.Invoke(History("ncma.history.undo"),permission).Changed,"formal undo");Pass(Draw().SequenceEqual(baseline),"undo original IBL image");
            Pass(owner.Edit.Invoke(History("ncma.history.redo"),permission).Changed,"formal redo");Pass(Draw().SequenceEqual(half),"redo IBL image");
            var play=owner.StartPlay();Pass(Draw().SequenceEqual(half),"Edit/Play shared IBL image");play.Pause();owner.ReloadGameplay(Path.Combine(fixture.Root,"gameplay.dll"));Pass(Draw().SequenceEqual(half),"Reload retains exact shader/environment");owner.StopPlay();Pass(Draw().SequenceEqual(half),"Stop preserves Edit image");
            owner.Document.World.Set(owner.Document.World.FindEntity(environmentId), envValue with{Generation=99});var failed=Draw();Pass(!failed.SequenceEqual(half)&&failed.Where((b,i)=>b!=failed[i%4]).Any()==false,"failed prepare uniform themed clear rather than stale configuration");File.WriteAllBytes(Path.Combine(output,"formal-editor-failed-clear.png"),Png(size.Width,size.Height,failed));Draw();
            Pass(inspector.EnvironmentPreparationStatus.Contains("失败"),"C3-B1 failed resource preparation visible in Inspector");
            owner.Document.World.Set(owner.Document.World.FindEntity(environmentId), envValue with{Strength=.5f});Pass(Draw().SequenceEqual(half),"explicit committed recovery restores retained resources");
            Pass(inspector.EnvironmentPreparationStatus.Contains("就绪")&&!inspector.EnvironmentPreparationStatus.Contains("失败"),"C3-B1 actual successful recovery clears error");
            Pass(editor.VerificationStats.ValidationErrors==0&&editor.VerificationStats.ValidationWarnings==0,"formal Editor DX11 API0/0");
        }
        // Original actual ASCII/binary imported animation, now through the formal Player.
        string build=Directory.GetParent(root)!.Parent!.FullName;string importPath=Path.Combine(build,"NcmaImportKernel.dll");
        foreach(string file in new[]{"blender_279_sausage_6100_ascii.fbx","blender_279_sausage_7400_binary.fbx"}){

            using var playerRunOwner7 = new Ncma.Player.App.PlayerRunner();
            using var importer=new Ncma.Asset.Import.ImportKernel(importPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(importPath))));
            var skinFixture=new SkinFixture(output,importer.LoadAndCopy(Path.Combine(repository.FullName,"tests/assets/fbx",file),30));var (skinDocument,skinCamera,_)=skinFixture.Scene();skinDocument.World.Set(skinDocument.World.CreateEntity("Environment"), envValue);Publish(skinFixture.Root,skinFixture.Project);Gameplay(skinFixture.Root);
            Directory.CreateDirectory(Path.Combine(skinFixture.Root,"assets/shaders"));foreach(var entry in files)File.Copy(Path.Combine(fixture.Root,entry.Path),Path.Combine(skinFixture.Root,entry.Path));
            string sp=Path.Combine(skinFixture.Root,"start.ncmascene"),pp=Path.Combine(skinFixture.Root,"project.ncmaproject");SceneDocumentFiles.Save(skinDocument,sp);File.WriteAllBytes(pp,JsonSerializer.SerializeToUtf8Bytes(projectConfig with{ProjectId=skinFixture.Project,SceneCamera=skinCamera,AssetPackage=null},json));
            int captures=0;bool drawn=false;var report=playerRunOwner7.RunForVerification(Options() with{Project=pp},Path.GetFullPath(root),(image,w,h)=>{captures++;drawn|=image.Where((_,i)=>i%4!=3).Any(b=>b>0);});
            Pass(report.ExitCode==0&&captures>0&&drawn&&report.ValidationErrors==0&&report.ValidationWarnings==0&&report.ShutdownErrors.Length==0,"formal FBX/NCA skin-shadow-IBL "+file);
        }
        string configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        foreach(string product in new[]{"Ncma.Editor.App","Ncma.Player.App"}){
            string exe=Path.Combine(repository.FullName,"managed",product,"bin",configuration,"net8.0",product=="Ncma.Editor.App"?"NcmaEngine.exe":"NcmaPlayer.exe");
            if(product=="Ncma.Player.App"){
                string moved=Path.Combine(output,"relocated-player");Directory.CreateDirectory(moved);
                foreach(string input in Directory.GetFiles(Path.GetDirectoryName(exe)!))File.Copy(input,Path.Combine(moved,Path.GetFileName(input)));
                string plugins=Path.Combine(moved,"plugins");Directory.CreateDirectory(plugins);
                foreach(string name in new[]{"NcmaPlatform.dll","NcmaRenderer.dll","glfw3.dll"})File.Copy(Path.Combine(root,name),Path.Combine(plugins,name));
                exe=Path.Combine(moved,"NcmaPlayer.exe");
            }
            var start=new System.Diagnostics.ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=fixture.Root};
            foreach(string argument in product=="Ncma.Editor.App"?new[]{"--project",projectPath,"--plugins",Path.GetFullPath(root),"--smoke-test","--frames","2"}:new[]{"--project",projectPath,"--ticks","2","--report",Path.Combine(output,"actual-player-apphost.json")})start.ArgumentList.Add(argument);
            using var process=System.Diagnostics.Process.Start(start)??throw new Exception("Cannot start actual host");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(30000))throw new Exception("Actual environment apphost timeout");string text=stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult();
            Pass(process.ExitCode==0,"actual apphost "+product+": "+text);
        }
        var empty=new SceneDocument("Empty",RenderComponentRegistry.CreateRegistry());SceneDocumentFiles.Save(empty,Path.Combine(fixture.Root,"empty.ncmascene"));
        Project(projectConfig with{StartupScene="empty.ncmascene",SceneCamera=null,AssetPackage=null,ShaderPackages=new(1,[oldFile])});
        var emptyReport=playerRunOwner8.Run(Options(),pluginRoot:Path.GetFullPath(root),visible:false);
        Pass(emptyReport.ExitCode==0&&emptyReport.ObjectCount==0&&emptyReport.ValidationErrors==0&&emptyReport.ValidationWarnings==0,"graphical empty with Flat2D-only closure requires no environment profile");Project(projectConfig);
        File.WriteAllText(Path.Combine(output,"host-results.json"),JsonSerializer.Serialize(new{cases,maxError,formalPlayer=true,formalEditor=true,defaultEnvironmentDeployment=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS M7.3-C2-C formal environment hosts cases={cases} maxPixelError={maxError}; C3 defaults/manual gates remain");return 0;
    }
}
