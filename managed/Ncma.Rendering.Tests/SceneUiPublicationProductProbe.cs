using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Player.App;

internal sealed class UiPublicationProductEvidence:IDisposable
{
    private readonly JsonDocument _request;
    internal readonly string Root,Project,Output,Plugins,Kind;
    internal int Checks;
    internal UiPublicationProductEvidence(string path){_request=JsonDocument.Parse(File.ReadAllBytes(path));var r=_request.RootElement;
        Root=r.GetProperty("productRoot").GetString()!;Project=r.GetProperty("project").GetString()!;Output=r.GetProperty("output").GetString()!;Plugins=r.GetProperty("plugins").GetString()!;Kind=r.GetProperty("kind").GetString()!;DeploymentManifest.Validate(Root);}
    internal void Pass(bool ok,string label){if(!ok)throw new Exception("Publication product: "+label);Checks++;}
    internal void Finish(string[] paths){using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root,DeploymentManifest.FileName)));
        foreach(string path in paths){var entry=manifest.RootElement.GetProperty("files").EnumerateArray().Single(e=>e.GetProperty("path").GetString()==Path.GetRelativePath(Root,path).Replace('\\','/'));
            Pass(entry.GetProperty("sha256").GetString()==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),"exact native hash");}
        Pass(EnvironmentProductEvidence.NativePaths(Root).Length==0,"native unload");File.WriteAllText(Path.Combine(Output,"probe.json"),JsonSerializer.Serialize(new{checks=Checks,native=paths,wholeD3Accepted=false}));}
    public void Dispose()=>_request.Dispose();
}
internal static class SceneUiPublicationPlayerProbe
{
    internal static void Run(string requestPath){using var e=new UiPublicationProductEvidence(requestPath);bool headless=e.Kind=="null";string[] native=[];
        var options=new PlayerOptions(e.Project,headless,headless?"null":"d3d11",2,1d/60,30,Path.Combine(e.Output,"player.json"),false,false);
        var result=headless?PlayerRunner.Run(options,pluginRoot:e.Plugins,visible:false):PlayerRunner.RunForVerification(options,e.Plugins,(pixels,w,h)=>{
            int at=checked((int)((20*w+20)*4));e.Pass(pixels[at]>253&&pixels[at+2]<2,"saved scene overrides default with red generation2");native=EnvironmentProductEvidence.NativePaths(e.Root);File.WriteAllBytes(Path.Combine(e.Output,"player.rgba"),pixels);});
        e.Pass(result.ExitCode==0&&(headless?result.Tick==2:result.Tick>=2)&&result.ShutdownErrors.Length==0,"product fixed steps");e.Pass(result.ValidationWarnings==0&&result.ValidationErrors==0,"API0/0");
        if(headless)e.Pass(result.Modules.Length==0&&!Directory.Exists(e.Plugins),"Null no native");e.Finish(native);
    }
}
