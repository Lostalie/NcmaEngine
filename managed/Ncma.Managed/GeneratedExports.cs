namespace Ncma;

// Opt-in build contract. Names remain the existing persisted Export identities.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GenerateExportsAttribute : Attribute { }

// Trusted bootstrap adapter, not an inspection/write capability for live scripts.
// Owned by the collectible catalog; never install these delegates in a global cache.
public sealed record GeneratedExportBinding(string Name, uint Kind, string DisplayName, string Category,
    Func<Behaviour, double> Read, Action<Behaviour, double> Write);

public interface IGeneratedExportSource
{
    GeneratedExportBinding[] DescribeGeneratedExports();
}

public static class ExportNumbers
{
    // Shared by generated setters and the original reflective catalog path.
    public static double Require(double number, uint kind)
    {
        if (!double.IsFinite(number)) throw new ArgumentException("Export value must be finite.");
        return kind switch {
            1 when float.IsFinite((float)number) => (float)number,
            2 => number,
            3 when number == Math.Truncate(number) && number >= int.MinValue && number <= int.MaxValue => number,
            4 when number == 0 || number == 1 => number,
            _ => throw new ArgumentException("Invalid exported property value.")
        };
    }
}
