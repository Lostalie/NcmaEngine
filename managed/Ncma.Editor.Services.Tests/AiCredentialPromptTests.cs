#if NCMA_AI
using System.Runtime.InteropServices;
using Ncma.AI;
using Ncma.Editor.App;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;

// Native boundary replacement only. No real dialog, credential store or user secret.
internal static unsafe class AiCredentialPromptTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Credential boundary assertion"); }
    private static void Reject(Action action, string? code = null) {
        try { action(); } catch (Exception e) when (e is InvalidOperationException or ArgumentException) {
            if (code is not null) Check(e.Message == code); return;
        }
        throw new Exception("Credential boundary accepted invalid input");
    }
    private static AiConnectionProfile Remote() => new(Guid.NewGuid(), 1, AiProvider.DeepSeek, AiDeployment.RemoteHttps,
        AiProvider.DeepSeek.RemoteEndpoint, "fixture-model", new(Guid.NewGuid(), 1));
    private sealed class Api : WindowsAiCredentialPrompt.NativeApi
    {
        internal int ParentCalls, Prompts, Unpacks, Frees;
        internal nint Window = 42;
        internal uint Result;
        internal bool ThrowPrompt, ThrowUnpack, FailUnpack, Unterminated, EmptyBlob;
        internal string User = "deepseek", Domain = "", Password = "fixture-not-a-user-key";
        internal Action? Reenter;
        internal WindowsAiCredentialPrompt.PromptInfo Info;
        internal override nint Parent() { ParentCalls++; return Window; }
        internal override uint Prompt(ref WindowsAiCredentialPrompt.PromptInfo info, out nint blob, out uint bytes) {
            Info = info; Prompts++; bytes = EmptyBlob ? 0u : 64u; blob = EmptyBlob ? 0 : Marshal.AllocCoTaskMem((int)bytes);
            if (blob != 0) new Span<byte>((void*)blob, (int)bytes).Fill(0x5a);
            Reenter?.Invoke(); if (ThrowPrompt) throw new InvalidOperationException("fixture_prompt_failure"); return Result;
        }
        internal override bool Unpack(nint blob, uint bytes, Span<char> user, Span<char> domain, Span<char> password) {
            Unpacks++; Check(blob != 0 && bytes == 64 && user.Length == 4097 && domain.Length == 4097 && password.Length == 4097);
            Check(user.IndexOfAnyExcept('\0') < 0 && domain.IndexOfAnyExcept('\0') < 0 && password.IndexOfAnyExcept('\0') < 0);
            User.AsSpan().CopyTo(user); Domain.AsSpan().CopyTo(domain); Password.AsSpan().CopyTo(password);
            if (Unterminated) password.Fill('x');
            if (ThrowUnpack) throw new InvalidOperationException("fixture_unpack_failure"); return !FailUnpack;
        }
        internal override void Free(nint blob) {
            try { Check(new ReadOnlySpan<byte>((void*)blob, 64).IndexOfAnyExcept((byte)0) < 0); Frees++; }
            finally { Marshal.FreeCoTaskMem(blob); }
        }
    }
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("M8.4-D credential native boundary exact recipient empty buffers zeroed blob once", () => {
            var api = new Api(); var prompt = new WindowsAiCredentialPrompt(api); var profile = Remote();
            for (int i = 0; i < 16; i++) Check(prompt.Acquire(profile) == "fixture-not-a-user-key");
            Check(api.Prompts == 16 && api.Unpacks == 16 && api.Frees == 16 && api.Info.Parent == 42);
            Check(api.Info.Size == (uint)Marshal.SizeOf<WindowsAiCredentialPrompt.PromptInfo>() && api.Info.Banner == 0);
            Check(api.Info.Message.Contains(profile.Endpoint) && api.Info.Message.Contains(profile.Model) && !api.Info.Message.Contains(api.Password));
        });
        yield return ("M8.4-D credential cancellation native error and throwing prompt always clear returned blob", () => {
            foreach (uint code in new uint[] { 1223, 5 }) {
                var api = new Api { Result = code }; var prompt = new WindowsAiCredentialPrompt(api);
                if (code == 1223) Check(prompt.Acquire(Remote()) is null); else Reject(() => prompt.Acquire(Remote()), "ai_credential_prompt_failed");
                Check(api.Unpacks == 0 && api.Frees == 1);
            }
            var throwing = new Api { ThrowPrompt = true }; var p = new WindowsAiCredentialPrompt(throwing);
            Reject(() => p.Acquire(Remote()), "fixture_prompt_failure"); Check(throwing.Frees == 1);
            throwing.ThrowPrompt = false; Check(p.Acquire(Remote()) == throwing.Password && throwing.Frees == 2);
            var empty = new Api { EmptyBlob = true }; Reject(() => new WindowsAiCredentialPrompt(empty).Acquire(Remote()), "ai_credential_prompt_failed");
            Check(empty.Unpacks == 0 && empty.Frees == 0);
        });
        yield return ("M8.4-D credential unpack failure wrong username domain and unterminated password reject", () => {
            foreach (var api in new[] { new Api { FailUnpack = true }, new Api { ThrowUnpack = true }, new Api { User = "windows-user" },
                new Api { Domain = "machine" }, new Api { Password = "" }, new Api { Unterminated = true } }) {
                Reject(() => new WindowsAiCredentialPrompt(api).Acquire(Remote())); Check(api.Unpacks == 1 && api.Frees == 1);
            }
            var max = new Api { Password = new string('k', 4096) };
            Check(new WindowsAiCredentialPrompt(max).Acquire(Remote())!.Length == 4096 && max.Frees == 1);
        });
        yield return ("M8.4-D credential profile parent owner and synchronous reentry rejected before native prompt", () => {
            var api = new Api(); var prompt = new WindowsAiCredentialPrompt(api); var profile = Remote();
            var local = new AiConnectionProfile(Guid.NewGuid(), 1, AiProvider.DeepSeek, AiDeployment.LocalLoopback,
                "http://127.0.0.1:8000/v1/chat/completions", "fixture", null);
            Reject(() => prompt.Acquire(local), "ai_credential_prompt_profile_invalid"); Check(api.ParentCalls == 0);
            Task.Run(() => Reject(() => prompt.Acquire(profile), "ai_credential_prompt_owner_or_reentry")).GetAwaiter().GetResult();
            Check(api.ParentCalls == 0); api.Window = 0;
            Reject(() => prompt.Acquire(profile), "ai_credential_prompt_window_required"); Check(api.Prompts == 0);
            api.Window = 42; bool blocked = false;
            api.Reenter = () => { Reject(() => prompt.Acquire(profile), "ai_credential_prompt_owner_or_reentry"); blocked = true; };
            Check(prompt.Acquire(profile) == api.Password && blocked && api.Prompts == 1 && api.Frees == 1);
        });
        yield return ("M8.4-D credential shared cleanup clears entire user domain password spans", () => {
            char[] user = Enumerable.Repeat('u', 4097).ToArray(), domain = Enumerable.Repeat('d', 4097).ToArray(), password = Enumerable.Repeat('p', 4097).ToArray();
            WindowsAiCredentialPrompt.ClearSensitive(user, domain, password);
            Check(user.All(c => c == 0) && domain.All(c => c == 0) && password.All(c => c == 0));
        });
        yield return ("M8.4-D credential actual controller replacement canceled or invalid key leaves disconnected", () => {
            using var owner = new EditorSessionOwner("Credential boundary"); owner.ConfigureDomainProposals(Guid.NewGuid());
            var workspace = new AiAuthoringWorkspace(owner.DomainProposals!); var api = new Api(); var prompt = new WindowsAiCredentialPrompt(api);
            try {
                Check(workspace.ConfigureRemote("fixture-model", prompt.Acquire)); Guid old = workspace.Profile!.ConfigurationId;
                api.Result = 1223; Check(!workspace.ConfigureRemote("fixture-model", prompt.Acquire) && workspace.Profile is null);
                api.Result = 0; api.Password = "invalid\nkey"; Reject(() => workspace.ConfigureRemote("fixture-model", prompt.Acquire));
                Check(workspace.Profile is null && workspace.Status == "unconfigured");
                api.Password = "fixture-not-a-user-key"; Check(workspace.ConfigureRemote("fixture-model", prompt.Acquire));
                Check(workspace.Profile!.ConfigurationId != old && workspace.Diagnostic is null && owner.DomainProposals!.RegisteredModules.Length == 0 && api.Frees == 4);
            }
            finally { Check(workspace.TryClose()); }
        });
    }
}
#endif
