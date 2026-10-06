using Ncma.Samples;
using Ncma.Player.App;
using Ncma.Application;
using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Characters;
using System.Text.Json;
internal static class ActionSampleChecks
{
    internal static void Run(string repository,string output,string plugins)
    {
        foreach(int count in new[]{0,1}) {
            var sample=ActionSample.Create(Path.Combine(output,"launchable-action-sample"),Path.Combine(repository,"out/managed/Ncma.Gameplay.Sample.dll"),count);
            var doc=new SceneDocument("sample",CharacterComponents.Register(RenderComponentRegistry.CreateRegistry()),CharacterComponents.RequireComposition);SceneDocumentFiles.Load(doc,Path.Combine(sample.Root,"start.ncmascene"));
            byte[] before=doc.CaptureBytes();
            using(var assets=SceneAssetPreparation.Prepare(sample.Root,sample.ProjectId,doc.CaptureSnapshot(),true,"assets/action.ncpak")) {
                if(count!=0&&assets.Diagnostics.Any(d=>d.Code is not ("source_missing" or "imported_material_slots_only")))throw new Exception("Unexpected sample diagnostics: "+string.Join(",",assets.Diagnostics.Select(d=>d.Code)));
            }
            foreach(bool headless in new[]{true,false}) {
                var flags=new List<string>{"--project",sample.Project,"--ticks","40","--report",Path.Combine(sample.Root,$"{headless}.json")};if(headless)flags.Add("--headless");
                var report=PlayerRunner.Run(PlayerOptions.Parse(flags.ToArray()),pluginRoot:plugins,visible:false,trustedInput:headless&&count!=0?(p,t)=>{if(t==1){CharacterHostChecks.Input(p,1,key:-1);CharacterHostChecks.Input(p,2,key:74,pressed:true);}}:null);
                if(report.ExitCode!=0||report.Tick<40||report.ShutdownErrors.Length!=0||report.ValidationErrors!=0||report.ValidationWarnings!=0)throw new Exception("Action sample Player failure: "+report.Reason);
                if(headless&&report.Modules.Any(m=>m.Id is "ncma.renderer" or "ncma.platform"))throw new Exception("Headless sample initialized graphics");
                if(count==0&&report.Modules.Any(m=>m.Id=="ncma.physics"))throw new Exception("Empty sample initialized unused physics");
                if(!before.SequenceEqual(File.ReadAllBytes(Path.Combine(sample.Root,"start.ncmascene"))))throw new Exception("Player changed sample authoring scene");
            }
            string data=File.ReadAllText(sample.Project);if(JsonDocument.Parse(data).RootElement.GetProperty("physicsEnabled").GetBoolean()!=(count!=0))throw new Exception("Explicit sample physics opt-in");
        }
    }
}
