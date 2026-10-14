using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Ui;
using Ncma.Ui.Rendering;

internal static unsafe partial class Program
{
    private static int SceneUiHostTests(string plugins, string output, bool sourceOnly = false)
    {
        output = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var repo = new DirectoryInfo(Path.GetFullPath(plugins)); while (!File.Exists(Path.Combine(repo.FullName, "Build.bat"))) repo = repo.Parent!;
        int checks = 0; var labels = new List<string>();
        void Pass(bool ok, string name) { Check(ok, "D3-B " + name); checks++; labels.Add(name); }
        void RejectUi(Action action, string name) { try { action(); } catch (Exception e) when (e is ArgumentException or IOException or InvalidOperationException or NotSupportedException or JsonException) { Pass(true, name); return; } throw new Exception("Accepted " + name); }
        string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        var fixture = new SceneGpuFixture(output); var (scene, camera, _, _) = fixture.Scene();
        var ui = UiDefinition.Create(Guid.NewGuid(), "Source-free scene HUD");
        Guid imageId = Guid.NewGuid(), fontId = Guid.NewGuid();
        var rect = UiElement.Create(Guid.NewGuid(), "Display color", UiKind.Rectangle, ui.Root) with { Layout = UiLayout.Fixed(8,8,48,24), Style = UiStyle.Default with { Fill = new(.2f,.4f,.8f,1) } };
        var image = UiElement.Create(Guid.NewGuid(), "Image", UiKind.Image, ui.Root) with { Order = 1, Image = imageId, Layout = UiLayout.Fixed(64,8,32,24) };
        var label = UiElement.Create(Guid.NewGuid(), "Label", UiKind.Text, ui.Root) with { Order = 2, Font = fontId, Text = "HUD 100", FontSize = 26, Layout = UiLayout.Fixed(8,40,240,40) };
        ui = ui with { Elements = [ui.Elements[0], rect, image, label] };
        byte[] fontBytes = File.ReadAllBytes(Path.Combine(repo.FullName,"engine/sdk/imgui/misc/fonts/ProggyClean.ttf"));
        UiPackageFont[] fonts = [new(fontId,fontBytes,"ProggyClean by Tristan Grimmer; MIT. SDK imgui docs/FONTS.md. Local regression fixture.",true)];
        UiPackageImage[] images = [new(imageId,1,1,[30,180,90,255])];
        byte[] package = RuntimeUiPackage.Encode(fixture.Project,1,ui,fonts,images);
        RuntimeUiPackage Decode(byte[] bytes) => RuntimeUiPackage.Decode(bytes,fixture.Project,ui.AssetId,1,Hash(bytes));
        using (var decoded = Decode(package)) {
            Pass(decoded.ProjectId == fixture.Project && decoded.HasFonts && decoded.Capture().AssetId == ui.AssetId,"exact package identity and closure");
            var copy = decoded.Capture(); copy.Elements[0] = copy.Elements[0] with { Name = "modified" };
            Pass(decoded.Capture().Elements[0].Name != "modified","owned document copy");
            Task.Run(() => RejectUi(() => decoded.Capture(),"foreign thread rejects")).GetAwaiter().GetResult();
        }
        var disposed = Decode(package); disposed.Dispose(); RejectUi(() => disposed.Capture(),"disposed package rejects");
        foreach (int length in new[]{0,16,39,52,package.Length-1}) RejectUi(() => Decode(package[..length]).Dispose(),"truncation " + length);
        byte[] trailing = [..package,0]; RejectUi(() => Decode(trailing).Dispose(),"trailing bytes");
        for (int at = 0; at < 40; at++) {
            byte[] bad = (byte[])package.Clone(); bad[at] ^= 0x80;
            RejectUi(() => Decode(bad).Dispose(),"closed header field " + at);
        }
        RejectUi(() => RuntimeUiPackage.Decode(package,fixture.Project,ui.AssetId,1,new('0',64)).Dispose(),"external hash mismatch");
        RejectUi(() => RuntimeUiPackage.Decode(package,Guid.NewGuid(),ui.AssetId,1,Hash(package)).Dispose(),"foreign project");
        RejectUi(() => RuntimeUiPackage.Decode(package,fixture.Project,Guid.NewGuid(),1,Hash(package)).Dispose(),"foreign document");
        RejectUi(() => RuntimeUiPackage.Decode(package,fixture.Project,ui.AssetId,2,Hash(package)).Dispose(),"stale generation");
        RejectUi(() => RuntimeUiPackage.Encode(fixture.Project,1,ui,[],images),"missing font");
        RejectUi(() => RuntimeUiPackage.Encode(fixture.Project,1,ui,fonts,[]),"missing image");
        RejectUi(() => RuntimeUiPackage.Encode(fixture.Project,1,ui,[fonts[0],fonts[0]],images),"duplicate payload");
        RejectUi(() => RuntimeUiPackage.Encode(fixture.Project,1,ui,[fonts[0] with { Redistributable=false }],images),"font distribution not declared");
        RejectUi(() => RuntimeUiPackage.Encode(fixture.Project,1,ui,fonts,[images[0],images[0] with { Id=Guid.NewGuid() }]),"unused payload");
        RejectUi(() => RuntimeUiPackage.Encode(fixture.Project,1,ui,fonts,[images[0] with { Width=4097 }]),"bad image dimensions");
        RejectUi(() => RuntimeUiPackage.Encode(fixture.Project,1,ui,fonts,Enumerable.Repeat(images[0],65).ToArray()),"resource count budget");
        RejectUi(() => RuntimeUiPackage.Encode(Guid.Empty,1,ui,fonts,images),"empty project");
        RejectUi(() => RuntimeUiPackage.Encode(fixture.Project,0,ui,fonts,images),"zero generation");
        RejectUi(() => RuntimeUiPackage.Encode(fixture.Project,1,ui with {Elements=ui.Elements.Select(e=>e.Id==image.Id?e with{Image=fontId}:e).ToArray()},fonts,[images[0] with{Id=fontId}]),"one UUID cannot have two resource roles");
        int resourceAt=40+BitConverter.ToInt32(package,32);
        foreach(int at in new[]{resourceAt+16,resourceAt+17,resourceAt+21,resourceAt+25,resourceAt+29}){
            byte[] bad=(byte[])package.Clone();bad[at]^=0x80;RejectUi(()=>Decode(bad).Dispose(),"resource metadata rejects "+at);
        }
        var imageOnly = ui with { Elements = ui.Elements.Take(3).ToArray() };
        byte[] imagePackage = RuntimeUiPackage.Encode(fixture.Project,2,imageOnly,[],images);
        string runtime = Path.Combine(output,"runtime"); Directory.CreateDirectory(Path.Combine(runtime,"assets"));
        string packagePath=Path.Combine(runtime,"assets/hud.ncupak"); File.WriteAllBytes(packagePath,package);
        var selection = new UiPackageReference(1,ui.AssetId,1,"assets/hud.ncupak",Hash(package));
        foreach (string path in new[]{"../hud.ncupak","assets/../hud.ncupak","assets\\hud.ncupak","assets/hud.ncmaui"}) RejectUi(() => (selection with {Path=path}).Validate(),"noncanonical UI path " + path);
        using (var pinned=RuntimeUiPackage.Open(runtime,fixture.Project,selection)) {
            Pass(PinLocked(packagePath),"UI package denies writes while pinned");
            RejectUi(() => File.Move(packagePath,packagePath+".moved"),"UI package denies rename while pinned");
        }
        Pass(!PinLocked(packagePath),"UI file released after close");
        string linked=Path.Combine(runtime,"assets/linked.ncupak");File.WriteAllBytes(linked,package);
        Pass(CreateHardLinkW(Path.Combine(runtime,"linked-alias.ncupak"),linked,0),"owned hardlink fixture");
        RejectUi(()=>RuntimeUiPackage.Open(runtime,fixture.Project,selection with{Path="assets/linked.ncupak"}).Dispose(),"hardlinked UI payload rejects");
        RejectUi(() => RuntimeUiPackage.Open(runtime,fixture.Project,selection with {Path="assets/Hud.ncupak"}).Dispose(),"case alias rejects");
        string nofont = Path.Combine(runtime,"assets/images.ncupak"); File.WriteAllBytes(nofont,imagePackage);
        byte[] badFont = RuntimeUiPackage.Encode(fixture.Project,3,ui,[fonts[0] with {Bytes=new byte[20]}],images);
        File.WriteAllBytes(Path.Combine(runtime,"assets/bad-font.ncupak"),badFont);
        byte[] interactive = RuntimeUiPackage.Encode(fixture.Project,4,ui with {Elements=[..ui.Elements, UiElement.Create(Guid.NewGuid(),"Button",UiKind.Button,ui.Root) with {Order=3,Action="attack"}]},fonts,images);
        File.WriteAllBytes(Path.Combine(runtime,"assets/interactive.ncupak"),interactive);
        using(var interactivePackage=RuntimeUiPackage.Decode(interactive,fixture.Project,ui.AssetId,4,Hash(interactive))) RejectUi(()=>SceneUiPresentation.Validate(interactivePackage),"unimplemented input never silently accepted");
        // Pure 2D exercise of the shared HUD wrapper; no mandatory 3D stages/resources.
        using (var loader=new PluginLoader()) {
            loader.Load(plugins,[new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"])]);
            using var window=new PlatformWindow(loader.Modules.First(m=>m.Kind==ModuleKind.Platform),"Scene HUD contract",256,128,false);
            using var renderer=new RendererSession(loader.Modules.First(m=>m.Kind==ModuleKind.Renderer),window,256,128,pureUi:true);
            using var prepared=RuntimeUiPackage.Open(runtime,fixture.Project,selection);
            using var hud=new SceneUiPresentation(); var view=new UiViewport(256,128,1,default); hud.Prepare(prepared,renderer,plugins,view);
            ulong frame=0; void Draw(){hud.Submit(++frame,overlay:false);}
            Draw(); byte[] pixels=new byte[256*128*4]; renderer.Capture(pixels);renderer.Present();
            int Pixel(int x,int y,int c)=>pixels[(y*256+x)*4+c];
            Pass(Math.Abs(Pixel(20,20,0)-51)<=1&&Math.Abs(Pixel(20,20,1)-102)<=1&&Math.Abs(Pixel(20,20,2)-204)<=1,"actual display-domain rectangle color");
            Pass(Pixel(75,20,0)==30&&Pixel(75,20,1)==180&&Pixel(75,20,2)==90,"actual copied RGBA image");
            Pass(Enumerable.Range(40,40).Sum(y=>Enumerable.Range(8,240).Count(x=>Pixel(x,y,0)>180))>50,"actual supplied font glyph pixels");
            ulong uploads=renderer.UiStats.UploadedBytes, preparations=hud.Preparations, shapes=hud.TextPreparations;
            for(int i=0;i<8;i++){hud.PrepareView(view);Draw();renderer.Present();}
            long allocated=GC.GetAllocatedBytesForCurrentThread();var timer=Stopwatch.StartNew();
            for(int i=0;i<32;i++){hud.PrepareView(view);Draw();renderer.Present();}
            timer.Stop();allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
            Pass(renderer.UiStats.UploadedBytes==uploads&&hud.Preparations==preparations&&hud.TextPreparations==shapes,"warm32 no reshape/upload/rebuild");
            File.WriteAllText(Path.Combine(output,"warm.json"),JsonSerializer.Serialize(new{frames=32,allocated,cpuMilliseconds=timer.Elapsed.TotalMilliseconds,residentBytes=renderer.UiStats.ResidentBytes,fullPerformanceAccepted=false},json));
            renderer.Resize(320,192);view=new(320,192,1,default);hud.PrepareView(view);Draw();renderer.Present();Pass(hud.Preparations==preparations+1,"resize prepares once");
            hud.Dispose();Pass(renderer.UiStats.ResidentBytes==0&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"HUD closes clean API0/0");
            using var noText=RuntimeUiPackage.Decode(imagePackage,fixture.Project,ui.AssetId,2,Hash(imagePackage));using var imageHud=new SceneUiPresentation();
            imageHud.Prepare(noText,renderer,"no-font-plugin-directory",view);Pass(!imageHud.TextLoaded,"no-font HUD never probes Text DLL");
        }
        File.WriteAllBytes(Path.Combine(runtime,"assets/scene.ncpak"),SceneAssetPreparation.CreateRuntimePackage(fixture.Root,fixture.Project,scene.CaptureSnapshot(),[]));
        SceneDocumentFiles.Save(scene,Path.Combine(runtime,"start.ncmascene"));
        string gameplay=Path.Combine(repo.FullName,"out/managed/Ncma.Gameplay.Sample.dll");File.Copy(gameplay,Path.Combine(runtime,"gameplay.dll"));File.Copy(Path.ChangeExtension(gameplay,".deps.json"),Path.Combine(runtime,"gameplay.deps.json"));
        var config=new ProjectConfiguration(1,fixture.Project,"HUD runtime","start.ncmascene","gameplay.dll","Direct3D11",[],false,camera,"assets/scene.ncpak",UiPackage:selection);
        void Config(string name,UiPackageReference? pick)=>File.WriteAllBytes(Path.Combine(runtime,name+".ncmaproject"),JsonSerializer.SerializeToUtf8Bytes(config with{UiPackage=pick},json));
        Config("project",selection);Config("no-ui",null);Config("no-font",selection with{Generation=2,Path="assets/images.ncupak",Hash=Hash(imagePackage)});
        Config("bad-font",selection with{Generation=3,Path="assets/bad-font.ncupak",Hash=Hash(badFont)});Config("interactive",selection with{Generation=4,Path="assets/interactive.ncupak",Hash=Hash(interactive)});
        Config("bad-hash",selection with{Hash=new('0',64)});Config("bad-generation",selection with{Generation=2});Config("missing",selection with{Path="assets/missing.ncupak"});
        var sceneShaders=ShaderPackageSelection.Decode(File.ReadAllBytes(Path.Combine(repo.FullName,"out/bin",RuntimeShaderFileSet.DefaultSelectionPath)));
        sceneShaders=sceneShaders with{Packages=sceneShaders.Packages.Where(p=>p.Profile!="Flat2D").ToArray()};
        foreach(var shader in sceneShaders.Packages){string to=Path.Combine(runtime,shader.Path);Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(Path.Combine(repo.FullName,"out/bin",shader.Path),to);}
        File.WriteAllBytes(Path.Combine(runtime,"missing-ui-shader.ncmaproject"),JsonSerializer.SerializeToUtf8Bytes(config with{ShaderPackages=sceneShaders},json));
        using(var editor=new Ncma.Editor.App.CandidatePresentation("unavailable-plugins",ProjectContext.Load(Path.Combine(runtime,"project.ncmaproject")),true,false,false))
            RejectUi(editor.Start,"Editor refuses pending HUD before loading plugins");
        Pass(!Directory.GetFiles(runtime,"*",SearchOption.AllDirectories).Any(p=>new[]{".ncmaui",".ttf",".png",".nca",".fbx",".cs"}.Contains(Path.GetExtension(p))),"runtime has no author UI/font/image/model sources");
        var locks=new List<FileStream>();
        try {
            foreach(string file in Directory.GetFiles(fixture.Root,"*",SearchOption.AllDirectories))locks.Add(new(file,FileMode.Open,FileAccess.Read,FileShare.None));
            using var fontLock=new FileStream(Path.Combine(repo.FullName,"engine/sdk/imgui/misc/fonts/ProggyClean.ttf"),FileMode.Open,FileAccess.Read,FileShare.None);
            void Probe(string root,string native,string kind,string name,bool relocated) {
                string evidence=Path.Combine(output,name);Directory.CreateDirectory(evidence);string request=Path.Combine(evidence,"request.json");
                File.WriteAllText(request,JsonSerializer.Serialize(new{probe="scene-ui",productRoot=root,plugins=native,project=Path.Combine(runtime,"project.ncmaproject"),output=evidence,kind,relocated},json));
                if(relocated){var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(string arg in new[]{typeof(Program).Assembly.Location,"--environment-product-child",request})info.ArgumentList.Add(arg);using var process=Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(90000)){process.Kill(entireProcessTree:true);process.WaitForExit();throw new Exception("Owned UI child timeout");}string log=stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult();File.WriteAllText(Path.Combine(evidence,"child.log"),log);Pass(process.ExitCode==0,name+" "+log);}
                else SceneUiHostProbe.Run(request);
                using var result=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(evidence,"probe.json")));checks+=result.RootElement.GetProperty("checks").GetInt32();
            }
            Probe(Path.Combine(repo.FullName,"out/bin"),Path.GetFullPath(plugins),"player","source",false);
            Probe(Path.Combine(repo.FullName,"out/bin"),Path.GetFullPath(plugins),"null","source-null",false);
            if(!sourceOnly){
                string configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;using var products=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repo.FullName,"out/verification/m2-7",configuration,"packages.json")));
                foreach(var(key,kind)in new[]{("playerDx11","player"),("playerNull","null")}) {
                    string source=products.RootElement.GetProperty(key).GetString()!,dest=Path.Combine(output,"product-"+kind);DeploymentManifest.Validate(source);Directory.CreateDirectory(dest);
                    using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(source,DeploymentManifest.FileName)));
                    foreach(var entry in manifest.RootElement.GetProperty("files").EnumerateArray()) {string relative=entry.GetProperty("path").GetString()!,to=Path.GetFullPath(Path.Combine(dest,relative));Pass(to.StartsWith(dest+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"checked copy path");Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(Path.Combine(source,relative),to);}
                    File.Copy(Path.Combine(source,DeploymentManifest.FileName),Path.Combine(dest,DeploymentManifest.FileName));DeploymentManifest.Validate(dest);
                    Probe(dest,Path.Combine(dest,"plugins"),kind,"relocated-"+kind,true);
                    if(kind=="null")Pass(!Directory.Exists(Path.Combine(dest,"plugins")),"Null package has no native UI/3D plugins");
                }
            }
        } finally {foreach(var pin in locks)pin.Dispose();}
        File.WriteAllText(Path.Combine(output,"scene-ui-results.json"),JsonSerializer.Serialize(new{checks,labels,sourceOnly,wholeD3Accepted=false,manualAccepted=false},json));Console.WriteLine($"PASS M7.4-D3-B scene HUD: {checks} checks; {output}");return 0;
    }
}
