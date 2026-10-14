using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;

internal static unsafe partial class Program
{
    private static void SceneUiRelocatedInteraction(string repo,string configuration,string runtime,string project,string output,Action<bool,string> pass)
    {
        var json=new JsonSerializerOptions{WriteIndented=true};int checks=0;var productsEvidence=new List<object>();
        using var products=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repo,"out/verification/m2-7",configuration,"packages.json")));
        foreach(var(key,kind) in new[]{("playerDx11","player"),("playerNull","null"),("editor","editor")}){
            string source=products.RootElement.GetProperty(key).GetString()!,dest=Path.Combine(output,"product-"+kind);DeploymentManifest.Validate(source);Directory.CreateDirectory(dest);
            using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(source,DeploymentManifest.FileName)));
            foreach(var entry in manifest.RootElement.GetProperty("files").EnumerateArray()){
                string relative=entry.GetProperty("path").GetString()!,to=Path.GetFullPath(Path.Combine(dest,relative));
                pass(to.StartsWith(dest+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"bounded relocation path");checks++;
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(Path.Combine(source,relative),to);
            }
            File.Copy(Path.Combine(source,DeploymentManifest.FileName),Path.Combine(dest,DeploymentManifest.FileName));DeploymentManifest.Validate(dest);
            pass(!File.Exists(Path.Combine(dest,"Ncma.Gameplay.UiFixture.dll")),"test gameplay fixture not shipped in engine product");checks++;
            // Runtime project is a distinct copied artifact, never a pointer to the source fixture.
            string relocatedProject=Path.Combine(dest,"game");Directory.CreateDirectory(relocatedProject);
            var files=new List<object>();
            foreach(string file in Directory.GetFiles(runtime,"*",SearchOption.AllDirectories)){
                string relative=Path.GetRelativePath(runtime,file),target=Path.GetFullPath(Path.Combine(relocatedProject,relative));
                if(!target.StartsWith(relocatedProject+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Runtime copy path escaped");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);string sha=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
                pass(sha==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))),"runtime exact copy "+relative);checks++;files.Add(new{path=relative,sha256=sha});
            }
            string evidence=Path.Combine(output,"relocated-"+kind);Directory.CreateDirectory(evidence);string request=Path.Combine(evidence,"request.json");
            File.WriteAllText(request,JsonSerializer.Serialize(new{probe="scene-ui-interaction",productRoot=dest,plugins=Path.Combine(dest,"plugins"),project=Path.Combine(relocatedProject,Path.GetFileName(project)),output=evidence,kind,relocated=true},json));
            // Original runtime files are exclusively locked throughout the child. Any fallback fails.
            var pins=Directory.GetFiles(runtime,"*",SearchOption.AllDirectories).Select(p=>new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.None)).ToArray();
            try{
                var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(string arg in new[]{typeof(Program).Assembly.Location,"--environment-product-child",request})info.ArgumentList.Add(arg);
                using var child=Process.Start(info)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
                if(!child.WaitForExit(60000)){child.Kill(entireProcessTree:true);child.WaitForExit();throw new Exception("Owned interactive product child timed out");}
                string log=stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult();File.WriteAllText(Path.Combine(evidence,"child.log"),log);pass(child.ExitCode==0,"checked interactive "+kind+" "+log);checks++;
                // The shipped CLI deliberately has no arbitrary route-registration flag. Prove
                // its default refusal separately from trusted API success, using the real apphost.
                var cli=new ProcessStartInfo(Path.Combine(dest,kind=="editor"?"NcmaEngine.exe":"NcmaPlayer.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(string arg in new[]{"--project",Path.Combine(relocatedProject,Path.GetFileName(project))})cli.ArgumentList.Add(arg);
                string report=Path.Combine(evidence,"apphost-denied.json");
                foreach(string arg in kind=="editor"?new[]{"--smoke-test","--frames","2"}:new[]{"--ticks","2","--report",report})cli.ArgumentList.Add(arg);
                if(kind=="null")cli.ArgumentList.Add("--headless");
                using var app=Process.Start(cli)!;var appOut=app.StandardOutput.ReadToEndAsync();var appErr=app.StandardError.ReadToEndAsync();
                if(!app.WaitForExit(45000)){app.Kill(entireProcessTree:true);app.WaitForExit();throw new Exception("Owned default apphost timed out");}
                string appLog=appOut.GetAwaiter().GetResult()+appErr.GetAwaiter().GetResult();File.WriteAllText(Path.Combine(evidence,"apphost-denied.log"),appLog);
                pass(app.ExitCode==(kind=="editor"?4:7)&&(kind!="editor"||appLog.Contains("trusted application startup routes",StringComparison.Ordinal)),"actual default apphost refuses unregistered interactive HUD");checks++;
                if(kind!="editor"){
                    using var denied=JsonDocument.Parse(File.ReadAllBytes(report));var d=denied.RootElement;
                    pass(d.GetProperty("tick").GetUInt64()==0&&d.GetProperty("sessionId").GetGuid()==Guid.Empty&&d.GetProperty("modules").GetArrayLength()==0&&d.GetProperty("reason").GetString()=="feature_unimplemented","actual Player refuses before gameplay/native startup");checks++;
                }
            }finally{foreach(var pin in pins)pin.Dispose();}
            using var result=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(evidence,"probe.json")));checks+=result.RootElement.GetProperty("checks").GetInt32();
            using var loaded=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(evidence,"loaded-product-assemblies.json")));
            var names=loaded.RootElement.EnumerateArray().Select(a=>a.GetProperty("name").GetString()!).ToArray();
            if(kind!="editor"){pass(!names.Any(n=>n.StartsWith("Ncma.Editor",StringComparison.Ordinal)||n.Contains("Mcp",StringComparison.Ordinal)||n=="Ncma.Gui"),"Player never resolves Editor MCP GUI");checks++;}
            productsEvidence.Add(new{kind,root=dest,files,assemblies=names});
        }
        File.WriteAllText(Path.Combine(output,"relocated-interaction-results.json"),JsonSerializer.Serialize(new{checks,products=productsEvidence,relocatedInteraction=true,syntheticPlatformEvents=true,customApphost=false,authorPublicationAccepted=false,fullFbxJointAccepted=false,wholeD3Accepted=false},json));
        Console.WriteLine($"PASS D3-C relocated interactive HUD: {checks} checks; {output}");
    }
}
