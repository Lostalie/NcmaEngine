using V3 = System.Numerics.Vector3;
using Ncma.Runtime;

namespace Ncma.Movement;

// Version 1 managed numerical contract; NOT a native ABI, native character implementation or Agent tool.
public readonly record struct MovementStepStamp(Guid SessionId, Guid WorldId, ulong FromTick, ulong Revision, ulong Sequence);
public readonly record struct MovementIntent(MovementStepStamp Stamp, Guid ObjectId, V3 Displacement, float YawRadians);
public readonly record struct NumericMovementInput(Guid ObjectId, TransformData Start, V3 Displacement, float YawRadians);
public readonly record struct NumericMovementResult(Guid ObjectId, TransformData Transform);
public readonly record struct NumericMovementReceipt(MovementStepStamp Stamp, int Count);
// Managed host composition only. Borrowed readonly results; no callback crosses the plugin ABI.
public delegate void StageMovementValues(MovementStepStamp stamp,ReadOnlySpan<NumericMovementResult> results);
public sealed record CoupledMovementStatus(Guid SessionId, Guid WorldId, ulong CommittedTick, ulong CommittedSequence,
    ulong AttemptTick, bool NumericalExecutionStarted, bool SnapshotValid, bool ResourcesOwned, bool Faulted);

// Trusted application factory. Preflight must not advance simulation. No World, script or module
// references cross this contract. Spans are borrowed only for the call. Dispose failure retains ownership.
public interface INumericMovementAdapter : IDisposable
{
    void Initialize(Guid sessionId, Guid worldId, ReadOnlySpan<NumericMovementInput> startup);
    void Preflight(MovementStepStamp stamp, double fixedDeltaSeconds, ReadOnlySpan<NumericMovementInput> inputs);
    NumericMovementReceipt Execute(MovementStepStamp stamp, double fixedDeltaSeconds,
        ReadOnlySpan<NumericMovementInput> inputs, Span<NumericMovementResult> results);
}
