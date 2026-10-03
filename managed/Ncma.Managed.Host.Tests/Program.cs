using Ncma.ManagedHost;
var path = args.Length == 1 && args[0] != "--nologo" ? args[0] : Path.GetFullPath("out/managed/Ncma.Gameplay.Sample.dll");
for (int cycle = 0; cycle < 12; cycle++)
{
    var weak = NativeEntry.ProbeCollectibleCatalog(path);
    for (int pass = 0; pass < 10 && weak.IsAlive; pass++)
    { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    if (weak.IsAlive) throw new InvalidOperationException("Collectible gameplay catalog retained engine-owned references.");
}
Console.WriteLine("Host catalog: 12 load/unload cycles released with bounded GC; shared SDK identity passed.");
