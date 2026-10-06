using V3 = System.Numerics.Vector3;
namespace Ncma.Characters;

// Copied committed diagnostics only. No solver handles, borrowed memory or private failure text.
public sealed record CharacterDebugRow(Guid ObjectId, V3 Position, string Ground, int Contacts,
    bool RootEnabled, double Time, V3 Desired, V3 Accepted, string Action, ulong Instance,
    float? Health, float? MaximumHealth);
public sealed record CharacterDebugFrame(Guid PlaySessionId, Guid WorldId, ulong Tick, string State,
    bool SnapshotValid, string FaultCode, CharacterDebugRow[] Characters, CombatEvent[] Events);
