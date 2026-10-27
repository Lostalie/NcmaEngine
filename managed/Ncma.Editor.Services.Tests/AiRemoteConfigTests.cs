#if NCMA_AI
using System.Text;
using Ncma.AI;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;
using Ncma.Gui;

internal static class AiRemoteConfigTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Remote configuration assertion"); }
    private sealed class Fixture : IDisposable
    {
        internal readonly EditorSessionOwner Owner = new("Remote configuration fixture");
        internal readonly AiAuthoringWorkspace Ai = new();
        internal readonly EditorPresenter View;
        internal string? Secret;
        internal int Takes, Clears;
        private ulong frame;
        internal Fixture() {
            View = new(new EditorWorkspace(Owner), null, workspaceStyle: true);
            View.AttachAiAuthoring(Ai);
            View.AttachAiSecretInput(f => { Check(f.Frame == View.Frame.Frame); Takes++; var s = Secret; Secret = null; return s; },
                () => { Clears++; Secret = null; });
            Build(); Click(14,24); Click(75,24); Click(20);
        }
        internal void Build() => View.Build(++frame,1440,900);
        internal GuiItem Item(ulong low) => View.Items.ToArray().Single(i => i.WidgetHigh == 81 && i.WidgetLow == low);
        internal GuiEvent Event(ulong low, ulong high=81, int length=0) {
            var i=View.Items.ToArray().Single(i=>i.WidgetHigh==high&&i.WidgetLow==low); var f=View.Frame;
            return new(){Kind=i.Kind,WidgetHigh=high,WidgetLow=low,Phase=3,Frame=f.Frame,ViewGeneration=f.ViewGeneration,
                DocumentGeneration=f.DocumentGeneration,Revision=f.Revision,TextLength=(uint)length};
        }
        internal void Click(ulong low,ulong high=81) { View.Apply([Event(low,high)],[]); Build(); }
        internal void Model(string value) { var bytes=Encoding.UTF8.GetBytes(value);View.Apply([Event(2,length:bytes.Length)],bytes);Build(); }
        public void Dispose() { Check(Ai.TryClose());Owner.Dispose(); }
    }
    internal static IEnumerable<(string,Action)> Cases()
    {
        yield return ("Remote form provider selector password channel and advanced model",()=>{
            using var f=new Fixture();
            Check(f.Item(32).Kind==(uint)GuiItemKind.Choice&&f.Item(30).Kind==(uint)GuiItemKind.SecretText);
            Check(!f.View.Items.ToArray().Any(i=>i.WidgetHigh==81&&i.WidgetLow==2));
            f.Secret="fake-remote-only-token";f.Build();
            Check(!Encoding.UTF8.GetString(f.View.Text).Contains(f.Secret));
            f.Click(3);Check(f.Takes==1&&f.Secret is null&&f.Ai.Profile?.Deployment==AiDeployment.RemoteHttps);
            Check(f.Ai.Profile!.ProviderId==AiProvider.DeepSeek.Id&&f.Ai.Profile.Model=="deepseek-flash"&&f.Ai.Diagnostic is null);
            f.Click(31);Check(f.Item(2).Kind==(uint)GuiItemKind.Text);
        });
        yield return ("Remote blank key retains exact session only and changed model rejects",()=>{
            using var f=new Fixture(); f.Click(3); Check(f.Ai.Profile is null);
            f.Secret="fake-remote-only-token";f.Click(3);var original=f.Ai.Profile!.ConfigurationId;
            f.Click(3);Check(f.Ai.Profile.ConfigurationId==original);
            f.Click(31);f.Model("other-model");f.Click(3);
            Check(f.Ai.Profile.ConfigurationId==original&&f.Ai.Profile.Model=="deepseek-flash");
            f.Secret="fake-replacement-token";f.Click(3);Check(f.Ai.Profile.Model=="other-model"&&f.Ai.Profile.ConfigurationId!=original);
        });
        yield return ("Remote cancel discards secret draft but retains applied session",()=>{
            using var f=new Fixture();f.Secret="fake-current-token";f.Click(3);var id=f.Ai.Profile!.ConfigurationId;
            f.Click(31);f.Model("unapplied-model");f.Secret="fake-discard-token";var clears=f.Clears;f.Click(33);
            Check(f.Secret is null&&f.Clears>clears&&f.Ai.Profile.ConfigurationId==id);
            f.Click(3);Check(f.Ai.Profile.ConfigurationId==id);
            Check(!Encoding.UTF8.GetString(f.View.Text).Contains("fake-discard-token"));
        });
        yield return ("Remote stale save cannot consume credential and mode close disconnect clear drafts",()=>{
            using var f=new Fixture();var stale=f.Event(3);f.Build();f.Secret="fake-stale-token";
            f.View.Apply([stale],[]);Check(f.Takes==0&&f.Secret is not null);
            f.Click(19);Check(f.Secret is null);f.Click(20);
            f.Secret="fake-disconnect-token";f.Click(4);Check(f.Secret is null&&f.Ai.Profile is null);
            f.Secret="fake-close-token";f.Click(18);Check(f.Secret is null&&f.Takes==0);
        });
        yield return ("Remote configuration service blank key has no implicit provider or model rebinding",()=>{
            var ai=new AiAuthoringWorkspace();
            try {
                bool rejected=false;try{ai.ConfigureRemoteInput("model",null);}catch(EditRejectedException){rejected=true;}Check(rejected);
                Check(ai.ConfigureRemoteInput("model","fake-service-token"));var id=ai.Profile!.ConfigurationId;
                Check(ai.ConfigureRemoteInput("model",null)&&ai.Profile.ConfigurationId==id);
                rejected=false;try{ai.ConfigureRemoteInput("changed",null);}catch(EditRejectedException){rejected=true;}
                Check(rejected&&ai.Profile.ConfigurationId==id&&ai.Diagnostic is null);
                Check(ai.Disconnect());rejected=false;try{ai.ConfigureRemoteInput("model",null);}catch(EditRejectedException){rejected=true;}Check(rejected);
            }finally{Check(ai.TryClose());}
        });
    }
    internal static IEnumerable<(string,Action)> HostCases(string plugins)
    {
        foreach(bool cpu in new[]{true,false}) {
            bool cpuOnly=cpu;
            yield return ($"Remote real {(cpuOnly?"CPU":"DX11")} host ABI1.8 empty secret save and close",()=>{
                using var host=new CandidatePresentation(plugins,null,true,cpuOnly,false);host.Start();
                var view=host.VerificationPresenter;var ai=host.VerificationAi;ulong frame=0;
                void Pump(){var f=new FrameContext(++frame,1.0/60);host.PumpPlatform();host.ApplyEditorIntents(f);host.PumpAgent(f);host.BeginPresentation(f);host.SubmitInput(f);host.AdvancePlay(f);host.Present(f);}
                void Click(ulong high,ulong low){var i=view.Items.ToArray().Single(x=>x.WidgetHigh==high&&x.WidgetLow==low);var f=view.Frame;
                    view.Apply([new GuiEvent{Kind=i.Kind,WidgetHigh=high,WidgetLow=low,Phase=3,Frame=f.Frame,ViewGeneration=f.ViewGeneration,DocumentGeneration=f.DocumentGeneration,Revision=f.Revision}],[]);}
                // Hidden fixture windows correctly cancel ordinary menus when unfocused.
                // Build local UI actions together, then render the persistent modal through the real host.
                view.Build(++frame,1440,900);Click(24,14);view.Build(++frame,1440,900);Click(24,75);
                view.Build(++frame,1440,900);Click(81,20);Pump();
                Check(view.Items.ToArray().Any(i=>i.Kind==(uint)GuiItemKind.SecretText&&i.Enabled!=0));
                var native=(GuiSession)host.VerificationAiLifetime.Gui!;
                Check(native.SupportsSecretInput&&native.TakeSecret(view.Frame,81,30) is null);
                Pump(); // Fresh empty draft for the actual Save dispatch.
                Click(81,3);Pump();
                // Native ABI checked separately above; presenter intentionally uses a content-free rejection.
                Check(Encoding.UTF8.GetString(view.Text).Contains("ai_action_rejected_refresh_or_cancel"));
                Check(host.VerificationAi.Profile is null&&host.VerificationAi.Diagnostic is null);
                Click(81,18);Pump();if(!cpuOnly)Check(host.VerificationStats.ValidationErrors==0);
                host.Dispose();Check(ai.Status=="closed"&&host.VerificationAiLifetime.Modules==0);
            });
        }
    }
}
#endif
