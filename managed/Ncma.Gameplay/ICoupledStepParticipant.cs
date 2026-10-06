namespace Ncma.Gameplay;

// Trusted application composition only. No World/physics dependency and no public callback registration.
// ExecuteAndStage starts an irreversible external domain; failure is fail-stop, not rollback.
internal interface ICoupledStepParticipant
{
    void Start();
    void BeginStep();
    void Preflight();
    void ExecuteAndStage();
    void Committed();
    void Faulted();
    void Stop();
    void RebuildStartup();
}
