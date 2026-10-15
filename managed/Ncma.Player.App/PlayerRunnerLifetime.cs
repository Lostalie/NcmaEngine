using System.Runtime.ExceptionServices;
using Ncma.Application.Runtime;
using Ncma.Interop;
using Ncma.Physics;
using Ncma.Rendering;

namespace Ncma.Player.App;

public enum PlayerRunnerPhase { Idle, Running, Closing, Retained, Disposed }
public readonly record struct PlayerRunnerStatus(PlayerRunnerPhase Phase, ulong Generation, uint ShutdownAttempts,
    string? FailedService, PluginResult? NativeResult, bool PresentationRetained, bool RuntimeRetained,
    bool PhysicsRetained, bool AssetPinsRetained, bool ProgramPinsRetained);

// Explicit process-local application owner; not a World/Agent authority. No finalizer,
// implicit retries, resource registry or forced plugin unload after a failed close.
public sealed partial class PlayerRunner : IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private PlayerRunnerPhase _phase;
    private bool _runActive, _runRejected, _closing;
    private ulong _generation;
    private uint _shutdownAttempts;
    private string? _failedService;
    private Exception? _shutdownFailure;
    private RuntimeSessionOwner? owner;
    private PhysicsService? physics;
    private PlayerPresentation? presentation;
    private Ncma.Scene.Rendering.PreparedSceneAssetLease? renderAssets;
    private RuntimeShaderFileSet? shaderFiles;
    private RuntimeShadowLibrary? shadowLibrary;
    private Ncma.Ui.Rendering.RuntimeUiPackage? uiPackage;
    private RuntimePostProcessLibrary? postLibrary;

    public PlayerRunnerStatus Status {
        get {
            VerifyThread();
            return new(_phase,_generation,_shutdownAttempts,_failedService,(_shutdownFailure as PluginException)?.Result,
                presentation is not null,owner is not null,physics is not null,renderAssets is not null||uiPackage is not null,
                shaderFiles is not null||shadowLibrary is not null||postLibrary is not null);
        }
    }
    public PlayerReport? LastReport { get; private set; }
    public RenderWindowFault? LastPresentationFailure { get; private set; }
    public NativeFailureDiagnostic? LastNativeFailureDiagnostic { get; private set; }
    private void VerifyThread()
    { if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Player runner requires its owner thread."); }
    private void RejectControlReentry()
    {
        VerifyThread();
        if (_runActive || _closing) {
            _runRejected = true;
            throw new InvalidOperationException("Cannot run or close recursively inside a Player callback.");
        }
    }
    private void BeginRun()
    {
        RejectControlReentry();
        if (_phase != PlayerRunnerPhase.Idle) throw new InvalidOperationException("Close retained Player resources before another run; disposed owners cannot restart.");
        _generation=checked(_generation+1);_shutdownAttempts=0;_failedService=null;_shutdownFailure=null;
        LastReport=null;LastPresentationFailure=null;LastNativeFailureDiagnostic=null;_runRejected=false;_runActive=true;_phase=PlayerRunnerPhase.Running;
    }
    private void CheckRun()
    {
        VerifyThread();
        if (!_runActive || _closing || _runRejected) throw new InvalidOperationException("Player callback invalidated the active run.");
    }
    private bool TryShutdown()
    {
        VerifyThread();
        if (_closing) throw new InvalidOperationException("Nonreentrant Player shutdown.");
        uint attempt=checked(_shutdownAttempts+1);
        _phase=PlayerRunnerPhase.Closing;_closing=true;_shutdownAttempts=attempt;
        _failedService=null;_shutdownFailure=null;
        string service="presentation_shutdown_failed";
        try {
            LastNativeFailureDiagnostic??=presentation?.FirstNativeFailure;
            presentation?.Dispose();presentation=null;
            service="runtime_shutdown_failed";owner?.Dispose();owner=null;
            service="physics_shutdown_failed";physics?.Dispose();physics=null;
            service="render_asset_shutdown_failed";renderAssets?.Dispose();renderAssets=null;
            service="ui_package_shutdown_failed";uiPackage?.Dispose();uiPackage=null;
            service="shadow_library_shutdown_failed";shadowLibrary?.Dispose();shadowLibrary=null;
            service="post_library_shutdown_failed";postLibrary?.Dispose();postLibrary=null;
            service="shader_files_shutdown_failed";shaderFiles?.Dispose();shaderFiles=null;
            _phase=PlayerRunnerPhase.Idle;return true;
        } catch(Exception error) {
            _failedService=service;_shutdownFailure=error;_phase=PlayerRunnerPhase.Retained;return false;
        } finally { _closing=false; }
    }
    // Only closes. Does not pump input, present an incomplete frame, advance gameplay,
    // restore a lost device, rewrite the original report or authorize new callbacks.
    public void RetryShutdown()
    {
        RejectControlReentry();
        if (_phase != PlayerRunnerPhase.Retained) throw new InvalidOperationException("No retained Player shutdown to retry.");
        if (!TryShutdown()) ExceptionDispatchInfo.Capture(_shutdownFailure!).Throw();
    }
    // Explicit opt-in after a graphical shutdown failure. No incomplete image is
    // presented; query17 makes the retained renderer terminal before normal close.
    public void AbandonFrameAndRetryShutdown()
    {
        RejectControlReentry();
        if(_phase!=PlayerRunnerPhase.Retained||presentation is null)throw new InvalidOperationException("No retained graphical Player shutdown.");
        try{presentation.AbandonFrameForShutdown();}
        catch(Exception error){_failedService="presentation_abandon_failed";_shutdownFailure=error;throw;}
        if(!TryShutdown())ExceptionDispatchInfo.Capture(_shutdownFailure!).Throw();
    }
    public void Dispose()
    {
        RejectControlReentry();
        if (_phase==PlayerRunnerPhase.Disposed)return;
        if (_phase==PlayerRunnerPhase.Retained&&!TryShutdown())ExceptionDispatchInfo.Capture(_shutdownFailure!).Throw();
        _phase=PlayerRunnerPhase.Disposed;
    }
}
