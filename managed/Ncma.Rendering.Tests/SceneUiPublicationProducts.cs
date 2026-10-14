using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;

internal static unsafe partial class Program
{
    private static void SceneUiPublicationProducts(string repo,string runtime,string output,Action<bool,string> pass,string probeName="scene-ui-publication")
    {
        string configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        using var products=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repo,"out/verification/m2-7",configuration,"packages.json")));
        foreach(var(key,kind)in new[]{("editor","editor"),("playerDx11","player"),("playerNull","null")}){
            string source=products.RootElement.GetProperty(key).GetString()!,dest=Path.Combine(output,"product-"+kind);DeploymentManifest.Validate(source);Directory.CreateDirectory(dest);
            using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(source,DeploymentManifest.FileName)));
            foreach(var entry in manifest.RootElement.GetProperty("files").EnumerateArray()){
                string relative=entry.GetProperty("path").GetString()!,to=Path.GetFullPath(Path.Combine(dest,relative));
                pass(to.StartsWith(dest+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"bounded product copy");
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(Path.Combine(source,relative),to);
            }
            File.Copy(Path.Combine(source,DeploymentManifest.FileName),Path.Combine(dest,DeploymentManifest.FileName));DeploymentManifest.Validate(dest);
            string game=Path.Combine(dest,"game");Directory.CreateDirectory(game);
            var paths=Directory.GetFiles(runtime,"*",SearchOption.AllDirectories).Where(p=>Path.GetExtension(p)!=".ncmaui").ToArray();
            foreach(string path in paths){string relative=Path.GetRelativePath(runtime,path),to=Path.GetFullPath(Path.Combine(game,relative));
                pass(to.StartsWith(game+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"bounded runtime copy");Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(path,to);
                pass(SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(to))),"exact runtime copy "+relative);
            }
            pass(!Directory.GetFiles(game,"*.ncmaui",SearchOption.AllDirectories).Any(),"no author UI deployed");
            string evidence=Path.Combine(output,"relocated-"+kind);Directory.CreateDirectory(evidence);string request=Path.Combine(evidence,"request.json"),project=Path.Combine(game,"project.ncmaproject");
            File.WriteAllText(request,JsonSerializer.Serialize(new{probe=probeName,productRoot=dest,plugins=Path.Combine(dest,"plugins"),project,output=evidence,kind,relocated=true}));
            var pins=paths.Select(p=>new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.None)).ToArray();
            try{
                var probe=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(string arg in new[]{typeof(Program).Assembly.Location,"--environment-product-child",request})probe.ArgumentList.Add(arg);
                Run(probe,Path.Combine(evidence,"child.log"),0);
                var cli=new ProcessStartInfo(Path.Combine(dest,kind=="editor"?"NcmaEngine.exe":"NcmaPlayer.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(string arg in new[]{"--project",project})cli.ArgumentList.Add(arg);
                foreach(string arg in kind=="editor"?new[]{"--smoke-test","--frames","2"}:new[]{"--ticks","2","--report",Path.Combine(evidence,"apphost.json")})cli.ArgumentList.Add(arg);
                if(kind=="null")cli.ArgumentList.Add("--headless");Run(cli,Path.Combine(evidence,"apphost.log"),0);
                if(kind!="editor"){using var report=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(evidence,"apphost.json")));ulong tick=report.RootElement.GetProperty("tick").GetUInt64();pass(kind=="null"?tick==2:tick>=2,"actual Player apphost committed ticks");}
            }finally{foreach(var pin in pins)pin.Dispose();}
            using var loaded=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(evidence,"loaded-product-assemblies.json")));
            if(kind!="editor")pass(!loaded.RootElement.EnumerateArray().Any(a=>a.GetProperty("name").GetString() is {} n&&(n.StartsWith("Ncma.Editor",StringComparison.Ordinal)||n.Contains("Mcp",StringComparison.Ordinal)||n=="Ncma.Gui")),"Player stays independent of Editor/MCP/GUI");
            using var result=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(evidence,"probe.json")));pass(result.RootElement.GetProperty("checks").GetInt32()>=3,"product publication probe assertions");
        }
        void Run(ProcessStartInfo info,string logPath,int expected){using var child=Process.Start(info)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
            if(!child.WaitForExit(probeName=="scene-ui-joint"?120000:45000)){child.Kill(entireProcessTree:true);child.WaitForExit();throw new Exception("Owned publication product process timeout");}
            string log=stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult();File.WriteAllText(logPath,log);pass(child.ExitCode==expected,"publication product process "+log);
        }
    }
}
