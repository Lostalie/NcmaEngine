#if NCMA_AI
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ncma.AI;

namespace Ncma.Editor.App;

// Not a secure-desktop dialog. Keeps secrets out of GUI packets/events/draft caches.
// No persistence, credential-manager/environment/clipboard lookup or HTTP validation.
internal sealed unsafe class WindowsAiCredentialPrompt
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly NativeApi native;
    private bool busy;
    internal WindowsAiCredentialPrompt() : this(new NativeApi()) { }
    // Trusted in-assembly/test composition only; never supplied by a model or GUI event.
    internal WindowsAiCredentialPrompt(NativeApi native) => this.native = native ?? throw new ArgumentNullException(nameof(native));
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PromptInfo {
        public uint Size; public nint Parent;
        public string Message, Caption; public nint Banner;
    }
    [DllImport("credui.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CredUIPromptForWindowsCredentialsW(ref PromptInfo info, uint error,
        ref uint package, nint input, uint inputSize, out nint output, out uint outputSize, nint save, uint flags);
    [DllImport("credui.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredUnPackAuthenticationBufferW(uint flags, nint buffer, uint size,
        char* user, ref uint userSize, char* domain, ref uint domainSize, char* password, ref uint passwordSize);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern nint GetActiveWindow();
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern uint GetCurrentThreadId();

    internal class NativeApi
    {
        internal virtual nint Parent() {
            nint window = GetActiveWindow();
            return window != 0 && GetWindowThreadProcessId(window, out uint process) == GetCurrentThreadId() && process == (uint)Environment.ProcessId ? window : 0;
        }
        internal virtual uint Prompt(ref PromptInfo info, out nint blob, out uint bytes) {
            uint package = 0;
            // GENERIC only, empty input, no Save checkbox or credential persistence.
            return CredUIPromptForWindowsCredentialsW(ref info, 0, ref package, 0, 0, out blob, out bytes, 0, 1);
        }
        internal virtual bool Unpack(nint blob, uint bytes, Span<char> user, Span<char> domain, Span<char> password) {
            uint u = (uint)user.Length, d = (uint)domain.Length, p = (uint)password.Length;
            fixed (char* up = user, dp = domain, pp = password)
                return CredUnPackAuthenticationBufferW(0, blob, bytes, up, ref u, dp, ref d, pp, ref p);
        }
        internal virtual void Free(nint blob) => Marshal.FreeCoTaskMem(blob);
    }

    internal static void ClearSensitive(Span<char> user, Span<char> domain, Span<char> password) {
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(user));
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(domain));
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password));
    }

    internal string? Acquire(AiConnectionProfile profile)
    {
        if (owner != Environment.CurrentManagedThreadId || busy) throw new InvalidOperationException("ai_credential_prompt_owner_or_reentry");
        ArgumentNullException.ThrowIfNull(profile);
        busy = true;
        try { return AcquireCore(profile); }
        finally { busy = false; }
    }
    private string? AcquireCore(AiConnectionProfile profile)
    {
        if (!OperatingSystem.IsWindows() || profile.Deployment != AiDeployment.RemoteHttps ||
            profile.ProviderId != AiProvider.DeepSeek.Id || profile.Endpoint != AiProvider.DeepSeek.RemoteEndpoint || profile.Credential is null)
            throw new InvalidOperationException("ai_credential_prompt_profile_invalid");
        nint parent = native.Parent();
        if (parent == 0)
            throw new InvalidOperationException("ai_credential_prompt_window_required");
        var info = new PromptInfo { Size = (uint)Marshal.SizeOf<PromptInfo>(), Parent = parent,
            Caption = "Ncma · DeepSeek API 密钥（仅本次会话）",
            Message = $"接收方 {profile.Endpoint}\n模型 {profile.Model}\n用户名填写 deepseek；密码栏仅填写 API 密钥，不要填写 Windows 密码。取消将保持断开。不保存、不验证连接；外发另行批准。" };
        nint blob = 0; uint bytes = 0;
        const int capacity = 4097;
        Span<char> user = stackalloc char[capacity], domain = stackalloc char[capacity], password = stackalloc char[capacity];
        user.Clear(); domain.Clear(); password.Clear();
        try {
            uint result = native.Prompt(ref info, out blob, out bytes);
            if (result == 1223) return null; // ERROR_CANCELLED, not a connection failure.
            if (result != 0 || blob == 0 || bytes == 0) throw new InvalidOperationException("ai_credential_prompt_failed");
            if (!native.Unpack(blob, bytes, user, domain, password))
                throw new InvalidOperationException("ai_credential_unpack_failed");
            int userEnd = user.IndexOf('\0'), domainEnd = domain.IndexOf('\0'), passwordEnd = password.IndexOf('\0');
            if (userEnd != 8 || !user[..userEnd].SequenceEqual("deepseek") || domainEnd != 0 || passwordEnd is < 1 or > 4096)
                throw new InvalidOperationException("ai_credential_prompt_fields_invalid");
            // Managed string / HttpClient copies cannot promise physical memory erasure.
            return new string(password[..passwordEnd]);
        }
        finally {
            ClearSensitive(user, domain, password);
            if (blob != 0) { NativeMemory.Clear((void*)blob, bytes); native.Free(blob); }
        }
    }
}
#endif
