namespace Ncma.Gameplay;

public sealed partial class PlaySession
{
    private PresentationRecoveryScope? _presentationRecovery;
    // Trusted application lifecycle only, not a gameplay command. Revoke pending observations
    // without Pause/Resume, clearing simulation debt, rerunning scripts or changing their state.
    public void CancelPresentationInput()
    {
        Control();if(State is not (PlayState.Running or PlayState.Paused))throw new InvalidOperationException("Active Play required.");
        _input.Clear();CancelUiInputCore();
    }
    public PresentationRecoveryScope BeginPresentationRecovery()
    {
        Control();if(State is not (PlayState.Running or PlayState.Paused))throw new InvalidOperationException("Only healthy active Play permits presentation recovery.");
        var scope=new PresentationRecoveryScope(this);_presentationRecovery=scope;return scope;
    }
    public sealed class PresentationRecoveryScope:IDisposable
    {
        private readonly PlaySession _play;
        private readonly PlayStatus _status;
        private readonly ulong _revision;
        private readonly IDisposable _read;
        private bool _rejected,_closed;
        internal PresentationRecoveryScope(PlaySession play){_play=play;_status=play.Status;_revision=play.Scene.Revision;_read=play.Scene.World.ReadOnly();}
        internal void Reject()=>_rejected=true;
        public void Validate()
        {
            _play.Verify();if(_closed||!ReferenceEquals(_play._presentationRecovery,this)||_rejected||_play.Status!=_status||_play.Scene.Revision!=_revision)
                throw new InvalidOperationException("Play changed or a control reentry was rejected during presentation recovery.");
        }
        public void Dispose()
        {
            _play.Verify();if(_closed)return;
            if(!ReferenceEquals(_play._presentationRecovery,this))throw new InvalidOperationException("Foreign presentation recovery scope.");
            _read.Dispose();_play._presentationRecovery=null;_closed=true;
        }
    }
}
