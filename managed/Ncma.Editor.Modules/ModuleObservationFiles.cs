using Ncma.Editor.Services;
using Ncma.Modules;

namespace Ncma.Editor.Modules;

// Explicit trusted C# API only. No presenter/MCP/model-supplied paths, implicit export or installation.
public static class ModuleObservationFiles
{
    public static DiagnosticEvidenceFileReceipt ExportNew(string destination, ModuleObservationReport report,
        int maximumBytes = ModuleObservationEvidence.MaximumBytes)
    {
        byte[] bytes = ModuleObservationEvidence.Encode(report, maximumBytes);
        return DiagnosticEvidenceFiles.WriteNew(destination, bytes);
    }
}
