using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;

internal static class PhysicsPackageChecks
{
    internal static void Run(JsonElement index,string output)
    {
        foreach(bool headless in new[]{true,false}){
            string key=headless?"playerNullPhysics":"playerDx11Physics";
            string source=index.GetProperty(key).GetString()!,basis=index.GetProperty(headless?"playerNull":"playerDx11").GetString()!;
            DeploymentManifest.Validate(source);DeploymentManifest.Validate(basis);
            Check(!File.Exists(Path.Combine(basis,"plugins/NcmaPhysics.dll")),"Default Player gained mandatory physics");
            using var baseManifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(basis,DeploymentManifest.FileName)));
            foreach(var entry in baseManifest.RootElement.GetProperty("files").EnumerateArray()){
                string relative=entry.GetProperty("path").GetString()!;
                Check(entry.GetProperty("sha256").GetString()==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(source,relative)))),"Physics variant changed shared file "+relative);
            }
            using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(source,DeploymentManifest.FileName)));
            var module=manifest.RootElement.GetProperty("modules").EnumerateArray().Single(m=>m.GetProperty("id").GetString()=="ncma.physics");
            Check(module.GetProperty("abiMajor").GetUInt32()==1&&module.GetProperty("abiMinor").GetUInt32()==2&&module.GetProperty("capabilities").GetUInt64()==255,"Physics metadata ABI mismatch");
            if(headless)Check(Directory.GetFiles(Path.Combine(source,"plugins")).Select(Path.GetFileName).SequenceEqual(new[]{"NcmaPhysics.dll"}),"Physics Null deployed unrelated native dependencies");
            Check(!Directory.GetFiles(source,"*",SearchOption.AllDirectories).Any(p=>Path.GetFileName(p).StartsWith("Ncma.Editor",StringComparison.Ordinal)||Path.GetFileName(p).StartsWith("Ncma.Gui",StringComparison.Ordinal)||Path.GetFileName(p)=="NcmaNative.dll"),"Physics variant imported Editor/GUI/FBX");
            string moved=Path.Combine(output,key+"-moved");Directory.CreateDirectory(moved);
            foreach(string path in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){string to=Path.Combine(moved,Path.GetRelativePath(source,path));Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(path,to);}
            DeploymentManifest.Validate(moved);
            string project=Path.Combine(moved,"sample/sample.ncmaproject");var options=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
            var config=JsonSerializer.Deserialize<ProjectConfiguration>(File.ReadAllBytes(project),options)!;
            // Sample files are manifested. Use a distinct test-owned runtime project instead
            // of silently changing the checked deployment's manifest or sample assets.
            string game=Path.Combine(moved,"physics-project.ncmaproject");File.WriteAllBytes(game,JsonSerializer.SerializeToUtf8Bytes(config with{PhysicsEnabled=true,StartupScene="sample/"+config.StartupScene,GameplayAssembly="sample/"+config.GameplayAssembly},options));
            string report=Path.Combine(output,key+".json");
            Check(Launch(moved,game,report,headless)==0,"Checked physical Player apphost failed");
            using var result=JsonDocument.Parse(File.ReadAllBytes(report));
            Check(result.RootElement.GetProperty("modules").EnumerateArray().Any(m=>m.GetProperty("id").GetString()=="ncma.physics"),"Explicit solver not loaded");
            Check(headless?result.RootElement.GetProperty("tick").GetUInt64()==2:result.RootElement.GetProperty("tick").GetUInt64()>=2,"Physical Player tick contract");
            byte[] bytes=File.ReadAllBytes(Path.Combine(moved,"plugins/NcmaPhysics.dll"));bytes[0]^=1;File.WriteAllBytes(Path.Combine(moved,"plugins/NcmaPhysics.dll"),bytes);
            Check(Launch(moved,game,Path.Combine(output,key+"-tampered.json"),headless)==3,"Tampered solver was not rejected by the actual apphost");
        }
        Console.WriteLine("PASS explicit relocated Null/DX11 physics variants, unchanged base bytes, optional solver and tamper rejection");
    }
    private static int Launch(string root,string project,string report,bool headless){
        var start=new ProcessStartInfo(Path.Combine(root,"NcmaPlayer.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in new[]{"--project",project,"--ticks","2","--report",report})start.ArgumentList.Add(arg);if(headless)start.ArgumentList.Add("--headless");
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(25000)){process.Kill(true);throw new Exception("Owned physics package watchdog");}
        Console.WriteLine(stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult());return process.ExitCode;
    }
    private static void Check(bool value,string label){if(!value)throw new Exception(label);}
}
