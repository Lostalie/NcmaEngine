using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Application;

namespace Ncma.Editor.Services;

// Explicit local request, not an approval ticket. Only CPU idle is implemented by the candidate host.
public sealed record MeasurementCandidateRequest(int Version, ProcessMeasurementIdentity Identity, int DurationMilliseconds,
    int IntervalMilliseconds, int Capacity, ulong MaximumFrames, string CaptureManifestPath, string ProductDirectory, string WorkloadDirectory)
{
    public const int MaximumBytes = 16384;
    internal static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public MeasurementInputsPlan Plan() => new(Identity,DurationMilliseconds,IntervalMilliseconds,Capacity,MaximumFrames);
    public void Validate()
    {
        if (Version != 1) throw new ArgumentException("measurement_request_version");
        _ = Plan();
        _ = WorkflowPhysicalPins.Absolute(CaptureManifestPath);
        _ = WorkflowPhysicalPins.Absolute(ProductDirectory);
        _ = WorkflowPhysicalPins.Absolute(WorkloadDirectory);
    }
    public static byte[] Encode(MeasurementCandidateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); request.Validate();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(request,JsonOptions);
        if(bytes.Length > MaximumBytes) throw new ArgumentException("measurement_request_budget");
        return bytes;
    }
    public static MeasurementCandidateRequest Decode(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length is < 1 or > MaximumBytes) throw new ArgumentException("measurement_request_budget");
        try {
            var result=JsonSerializer.Deserialize<MeasurementCandidateRequest>(bytes,JsonOptions) ?? throw new ArgumentException("measurement_request_null");
            // Duplicate, omitted, reordered or noncanonical fields cannot be silently accepted.
            if(!bytes.SequenceEqual(Encode(result))) throw new ArgumentException("measurement_request_noncanonical");
            return result;
        } catch(JsonException) { throw new ArgumentException("measurement_request_json"); }
    }
    public static MeasurementCandidateRequest Read(string path,string expectedSha256)
        => Decode(ReadLocalBytes(path,expectedSha256));
    // Shared bounded local request reader. Bytes are data, never approval or executable authority.
    public static byte[] ReadLocalBytes(string path,string expectedSha256)
    {
        WorkflowEvidenceRules.Hash(expectedSha256);
        using var pins=new WorkflowPhysicalPins();
        var actual=pins.Observe(path,MaximumBytes);
        if(actual.Sha256!=expectedSha256) throw new IOException("measurement_request_changed");
        return pins.ReadFile(path,expectedSha256,actual.Bytes,MaximumBytes);
    }
    public MeasurementInputLease OpenInputs() { Validate(); return new(Plan(),CaptureManifestPath,ProductDirectory,WorkloadDirectory); }
    public void ValidateOutput(string path)
    {
        path=WorkflowPhysicalPins.Absolute(path);
        foreach(string root in new[]{WorkflowPhysicalPins.Absolute(ProductDirectory),WorkflowPhysicalPins.Absolute(WorkloadDirectory),Path.GetDirectoryName(WorkflowPhysicalPins.Absolute(CaptureManifestPath))!})
            if(path.Equals(root,StringComparison.OrdinalIgnoreCase)||path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new IOException("measurement_output_input_overlap");
        if(File.Exists(path)||Directory.Exists(path)) throw new IOException("measurement_output_exists");
        // Validate the same destination grammar/ancestors before startup without creating the file.
        DiagnosticEvidenceFiles.ValidateNewDestination(path);
    }
}
