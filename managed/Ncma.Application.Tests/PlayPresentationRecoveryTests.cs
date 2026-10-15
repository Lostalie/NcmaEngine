using Ncma.Gameplay;
using Ncma.Scene;

internal static class PlayPresentationRecoveryTests
{
    internal static void Run()
    {
        int count=0;void Check(bool value){if(!value)throw new Exception("Play presentation recovery invariant");count++;}
        void Reject(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("Expected Play recovery rejection");}
        foreach(bool paused in new[]{false,true}){
            using var play=new PlaySession(new SceneDocument("Recovery"));play.Start(_=>throw new Exception("No scripts"));play.AdvanceFrame(.025);if(paused)play.Pause();
            var status=play.Status;var revision=play.Document.Revision;play.CancelPresentationInput();Check(play.Status==status);
            using(var clean=play.BeginPresentationRecovery()){clean.Validate();Check(play.Status==status);}
            foreach(Action operation in new Action[]{()=>play.Pause(),()=>play.Resume(),()=>play.Step(),()=>play.AdvanceFrame(0),()=>play.CancelPresentationInput(),()=>play.Stop(),play.Dispose}) {
                using(var scope=play.BeginPresentationRecovery()){
                    Reject(operation);Reject(scope.Validate);Check(play.Status==status&&play.Document.Revision==revision);
                }
            }
            using(var scope=play.BeginPresentationRecovery()){
                Task.Run(()=>Reject(()=>play.Pause())).GetAwaiter().GetResult();scope.Validate();Check(play.Status==status);
            }
            Check(play.State==(paused?PlayState.Paused:PlayState.Running));
        }
        Console.WriteLine($"PASS Play presentation recovery {count} checks; clocks retained/control freeze/reentry");
    }
}
