using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ncma.Modules;

/// <summary>Owned structural JSON, NOT schema approval or executable instructions.</summary>
public sealed class ModulePayload
{
    public const int MaximumBytes = 65_536;
    private readonly byte[] bytes;
    public int Length => bytes.Length;
    public string Sha256 { get; }

    public ModulePayload(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length is < 2 or > MaximumBytes) throw new ArgumentOutOfRangeException(nameof(utf8));
        bytes = utf8.ToArray();
        // JsonDocument may defer decoding string values: validate the entire copied UTF-8 now.
        _ = new UTF8Encoding(false, true).GetCharCount(bytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Module payload must be an object.", nameof(utf8));
        Validate(document.RootElement);
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
    }

    public byte[] CopyBytes() => (byte[])bytes.Clone();

    private static void Validate(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ArgumentException("Duplicate module payload member.");
                Validate(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) Validate(child);
    }
}

/// <summary>V2 exact authoring source; identities are not observation or execution grants.</summary>
public sealed record ModuleContext(Guid ProjectId, Guid HostSessionId, Guid EditSessionId, Guid DocumentSessionId,
    ulong DocumentGeneration, ulong Revision, Guid BehaviourCatalogGeneration, ulong ReviewEpoch,
    Guid? Selection, Guid EndpointId, ulong AudienceRevision)
{
    // Shared by the inbox and optional worker host. No parallel context validation contract.
    public void Validate()
    {
        if (ProjectId == Guid.Empty || HostSessionId == Guid.Empty || EditSessionId == Guid.Empty ||
            DocumentSessionId == Guid.Empty || DocumentGeneration == 0 || ReviewEpoch == 0 ||
            Selection == Guid.Empty || (EndpointId == Guid.Empty && AudienceRevision != 0))
            throw new ArgumentException("Invalid module context.");
    }
}

/// <summary>Trusted startup registration. Not an editor capability grant.</summary>
public sealed record ModuleOperation(Guid ModuleId, string ModuleVersion, Guid ConfigurationId,
    string Name, Guid InputSchema, Guid OutputSchema, int MaximumInputBytes = 65_536,
    int MaximumOutputBytes = 65_536, int TimeoutMilliseconds = 10_000)
{
    internal void Validate()
    {
        if (ModuleId == Guid.Empty || ConfigurationId == Guid.Empty || InputSchema == Guid.Empty ||
            OutputSchema == Guid.Empty || !Token(Name, 96) || !Token(ModuleVersion, 32) ||
            MaximumInputBytes is < 2 or > ModulePayload.MaximumBytes ||
            MaximumOutputBytes is < 2 or > ModulePayload.MaximumBytes || TimeoutMilliseconds is < 1 or > 60_000)
            throw new ArgumentException("Invalid module operation registration.");
    }

    private static bool Token(string? value, int maximum) => value is { Length: > 0 } &&
        value.Length <= maximum && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}

public sealed class ModuleCatalog
{
    private readonly Dictionary<string, ModuleOperation> operations = new(StringComparer.Ordinal);
    public ModuleCatalog(IEnumerable<ModuleOperation> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        foreach (var operation in registrations)
        {
            ArgumentNullException.ThrowIfNull(operation);
            operation.Validate();
            if (operations.Count == 64 || !operations.TryAdd(operation.Name, operation))
                throw new ArgumentException("Duplicate or excessive module registrations.");
        }
        if (operations.Count == 0) throw new ArgumentException("Empty module catalog.");
    }

    internal ModuleOperation Get(string name) => operations.TryGetValue(name, out var operation)
        ? operation : throw new ArgumentException("Unregistered module operation.", nameof(name));
}

public sealed class ModuleRequest
{
    public const int ContractVersion = 2;
    public int Version => ContractVersion;
    public Guid RequestId { get; }
    public Guid SessionId { get; }
    public long Epoch { get; }
    public ModuleContext Context { get; }
    public ModuleOperation Operation { get; }
    public ModulePayload Observation { get; }
    public string Fingerprint { get; }

    internal ModuleRequest(Guid requestId, Guid sessionId, long epoch, ModuleContext context,
        ModuleOperation operation, ModulePayload observation)
    {
        RequestId = requestId;
        SessionId = sessionId;
        Epoch = epoch;
        Context = context;
        Operation = operation;
        Observation = observation;
        // Versioned fixed-property canonical input; binds operation/config/schema/context AND exact bytes.
        Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version, RequestId, SessionId, Epoch, Context, Operation, Observation.Sha256
        })));
    }
}

public enum ModuleReplyStatus { Proposal = 1, Unavailable = 2, Rejected = 3 }

public sealed record ModuleReply(int Version, Guid RequestId, Guid SessionId, long Epoch,
    string Fingerprint, Guid OutputSchema, ModuleReplyStatus Status, ModulePayload? Proposal);

/// <summary>Trusted adapter; called only on background workers. No host state is provided.</summary>
public interface IModuleAdapter
{
    // Queue and ThreadPool dispatch time have already been deducted. A transport must also
    // deduct its own preparation time; the original operation timeout is not a new RPC budget.
    Task<ModuleReply> InvokeAsync(ModuleRequest request, TimeSpan remainingBudget, CancellationToken cancellation);
}

public enum ModuleOutcomeStatus
{
    NeedsDomainValidation, Unavailable, Rejected, InvalidReply, AdapterFault,
    Cancelled, TimedOut, Stale
}

/// <summary>A copied proposal is never permission to execute a command.</summary>
public sealed record ModuleOutcome(Guid RequestId, string Fingerprint, ModuleOutcomeStatus Status,
    ModulePayload? Proposal);

public enum ModuleAdmission { Accepted, Backpressure, Duplicate, SessionBudget, Stopped }
