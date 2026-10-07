using Ncma.Assets.Authoring.Storage;
using Ncma.Ui;

namespace Ncma.Assets.Authoring;

// Explicit trusted local read only. No scan, approval, capability or source path in game UI files.
// Reuses canonical path/parent leases, exact handle identity and hardlink/reparse rejection.
public static class UiAuthoringSource
{
    public static byte[] Read(string root,string relative,int maximum)
    {
        if(maximum is <1 or >64*1024*1024)throw new ArgumentException("UI source byte budget.");
        var paths=new AssetProjectPaths(root);relative=AssetPaths.Validate(relative);
        using var parents=new AssetDirectoryLease(paths,[relative]);
        using var source=WindowsAssetFile.OpenReadLease(paths.Resolve(relative,requireFile:true));return source.Read(maximum);
    }
    public static UiDefinition Open(string root,string relative)
    {
        ValidatePath(relative);var paths=new AssetProjectPaths(root);
        if(File.Exists(paths.Resolve(relative+".journal"))||Directory.Exists(paths.Resolve(relative+".journal")))throw new IOException("UI transaction recovery required.");
        return UiCodec.Decode(Read(root,relative,UiCodec.MaxBytes));
    }
    public static string ValidatePath(string relative)
    {
        relative=AssetPaths.Validate(relative);if(!relative.EndsWith(".ncmaui",StringComparison.Ordinal))throw new ArgumentException("Strict .ncmaui required.");return relative;
    }
}
