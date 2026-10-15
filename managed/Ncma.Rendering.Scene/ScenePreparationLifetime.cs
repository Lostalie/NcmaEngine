namespace Ncma.Rendering.Scene;

public enum ScenePreparationPhase { Empty, Preparing, Ready, Failed, Closing, Closed }

// Constructors allocate only managed bookkeeping. Callers retain the owner before
// Prepare; failures retain acquired resources until an explicit successful close.
internal sealed class ScenePreparationLifetime
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private bool _rejected;
    private ScenePreparationPhase _phase;
    internal ScenePreparationPhase Phase { get { VerifyOwner(); return _phase; } }
    internal void VerifyOwner()
    { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Scene preparation requires owner thread."); }
    internal void Begin()
    {
        VerifyOwner();
        if (_phase == ScenePreparationPhase.Preparing) _rejected = true;
        if (_phase != ScenePreparationPhase.Empty) throw new InvalidOperationException("Scene preparation is single-use; retain and close a failed owner.");
        _phase = ScenePreparationPhase.Preparing;
    }
    internal void CheckCallback()
    { VerifyOwner(); if (_rejected) throw new InvalidOperationException("Preparation callback violated scene lifetime."); }
    internal void Complete() { CheckCallback(); _phase = ScenePreparationPhase.Ready; }
    internal void Fail() { _phase = ScenePreparationPhase.Failed; }
    internal void RequireReady()
    {
        VerifyOwner();
        if (_phase == ScenePreparationPhase.Preparing) _rejected = true;
        if (_phase != ScenePreparationPhase.Ready) throw new InvalidOperationException("Scene resources are not ready.");
    }
    internal bool BeginClose()
    {
        VerifyOwner();
        if (_phase == ScenePreparationPhase.Preparing) {
            _rejected = true;
            throw new InvalidOperationException("Cannot close during scene preparation.");
        }
        if (_phase == ScenePreparationPhase.Closed) return false;
        _phase = ScenePreparationPhase.Closing; return true;
    }
    internal void Closed() { _phase = ScenePreparationPhase.Closed; }
}
