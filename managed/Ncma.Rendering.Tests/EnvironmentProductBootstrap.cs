using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

// Test-only process boundary. Production Ncma assemblies MUST resolve from the checked
// relocated product; a missing assembly never falls back to the repository test output.
internal static class EnvironmentProductBootstrap
{
    private sealed class ProductContext(string root):AssemblyLoadContext("checked-environment-product",isCollectible:false)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            if(!name.Name!.StartsWith("Ncma",StringComparison.Ordinal))return null;
            string path=Path.Combine(root,name.Name+".dll");
            if(!File.Exists(path))throw new FileNotFoundException("No product assembly fallback: "+name.Name);
            return LoadFromAssemblyPath(path);
        }
    }
    internal static int Run(string requestPath)
    {
        using var request=JsonDocument.Parse(File.ReadAllBytes(requestPath));var value=request.RootElement;
        string root=Path.GetFullPath(value.GetProperty("productRoot").GetString()!);var context=new ProductContext(root);
        var probe=context.LoadFromAssemblyPath(typeof(EnvironmentProductBootstrap).Assembly.Location);
        // Process-local test setup, not a product switch/CLI. Assert the actual default resource root.
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY",root+Path.DirectorySeparatorChar);
        if(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)!=Path.TrimEndingDirectorySeparator(root))throw new Exception("Product base-directory override failed.");
        string kind=value.GetProperty("kind").GetString()!;
        string probeType=value.TryGetProperty("probe",out var selected)&&selected.GetString()=="pipeline"?"PipelineHostProbe":kind=="editor"?"EnvironmentEditorProductProbe":"EnvironmentPlayerProductProbe";
        var type=probe.GetType(probeType,true)!;
        try { type.GetMethod("Run",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[requestPath]); }
        catch(TargetInvocationException e){throw new Exception("Checked product probe failed.",e.InnerException);}
        var assemblies=context.Assemblies.Where(a=>a.GetName().Name!.StartsWith("Ncma",StringComparison.Ordinal)&&a!=probe).Select(a=>new{name=a.GetName().Name,path=a.Location,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location)))}).ToArray();
        if(assemblies.Length<5||assemblies.Any(a=>!Path.GetFullPath(a.path).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new Exception("Build assembly escaped product isolation.");
        using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"deployment-manifest.json")));var entries=manifest.RootElement.GetProperty("files").EnumerateArray().ToArray();
        foreach(var assembly in assemblies){string relative=Path.GetRelativePath(root,assembly.path).Replace('\\','/');var entry=entries.Single(e=>e.GetProperty("path").GetString()==relative);if(entry.GetProperty("sha256").GetString()!=assembly.sha256)throw new Exception("Loaded assembly is not checked product content.");}
        File.WriteAllText(Path.Combine(value.GetProperty("output").GetString()!,"loaded-product-assemblies.json"),JsonSerializer.Serialize(assemblies));return 0;
    }
}
