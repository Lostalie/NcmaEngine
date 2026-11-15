using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Ncma.Editor.Services;

// Private hidden preparer, non-breakaway Job assigned at process creation.
// No terminate/KILL_ON_JOB_CLOSE/process sweep/uncontained fallback.
// https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute
internal sealed class WorkflowWindowsProcess
{
    private SafeKernelHandle? job, process;
    private bool attempted;
    internal uint ProcessId { get; private set; }
    internal bool Started => process is not null;
    internal static string Quote(string value)
    {
        if (value.Any(char.IsControl)) throw new IOException("workflow_argument_control");
        var result = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value) {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); result.Append(c); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
    internal void Start(string runner, string worker, string pipe, string scratch)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10) || !System.Environment.Is64BitProcess) throw new PlatformNotSupportedException("workflow_windows64_required");
        if (attempted) throw new IOException("workflow_process_single_use"); attempted = true;
        job = CreateJobObjectW(0, null);
        if (job.IsInvalid) throw new IOException("workflow_job_create_failed");
        // Atomic job assignment: unsupported/nested-job policy errors fail closed.
        nint bytes = 0; _ = InitializeProcThreadAttributeList(0, 1, 0, ref bytes);
        if (bytes <= 0 || bytes > 65536) throw new IOException("workflow_attribute_size");
        nint attributes = Marshal.AllocHGlobal(bytes), handles = Marshal.AllocHGlobal(nint.Size), environment = 0;
        bool initialized = false;
        try {
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes)) throw new IOException("workflow_attribute_init");
            initialized = true; Marshal.WriteIntPtr(handles, job.DangerousGetHandle());
            if (!UpdateProcThreadAttribute(attributes, 0, 0x0002000D, handles, (nuint)nint.Size, 0, 0)) throw new IOException("workflow_job_attribute");
            string windows = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows);
            // Do not copy arbitrary API-key/proxy/SDK-hook environment variables.
            // Pinning the runner EXE does NOT establish its whole runtime closure.
            var env = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["SystemRoot"] = windows, ["WINDIR"] = windows, ["SystemDrive"] = Path.GetPathRoot(windows)![..2],
                ["TEMP"] = scratch, ["TMP"] = scratch,
                ["PATH"] = Path.GetDirectoryName(runner) + ";" + Path.Combine(windows, "System32"),
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["POWERSHELL_TELEMETRY_OPTOUT"] = "1", ["POWERSHELL_UPDATECHECK"] = "Off" };
            environment = Marshal.StringToHGlobalUni(string.Join('\0', env.Select(p => p.Key + "=" + p.Value)) + "\0\0");
            var startup = new StartupInfoEx { Info = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>(), Flags = 1, ShowWindow = 0 }, Attributes = attributes };
            var command = new StringBuilder(Quote(runner) + " -NoLogo -NoProfile -NonInteractive -File " + Quote(worker) + " -PipeName " + Quote(pipe));
            if (!CreateProcessW(runner, command, 0, 0, false, 0x08000000 | 0x00080000 | 0x00000400, environment, scratch, ref startup, out var info))
                throw new IOException("workflow_process_create_failed");
            process = new SafeKernelHandle(info.Process); ProcessId = info.ProcessId;
            using var thread = new SafeKernelHandle(info.Thread);
        } finally {
            if (environment != 0) Marshal.FreeHGlobal(environment);
            if (initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(handles); Marshal.FreeHGlobal(attributes);
        }
    }
    internal (bool RootExited, uint ActiveProcesses, int? ExitCode) Observe()
    {
        if (job is null || job.IsInvalid) return (!Started, 0, null);
        if (!QueryInformationJobObject(job, 1, out var accounting, (uint)Marshal.SizeOf<Accounting>(), 0)) throw new IOException("workflow_job_query_failed");
        if (process is null) return (true, accounting.ActiveProcesses, null);
        uint wait = WaitForSingleObject(process, 0);
        if (wait == 0x102) return (false, accounting.ActiveProcesses, null);
        if (wait != 0 || !GetExitCodeProcess(process, out uint code)) throw new IOException("workflow_process_query_failed");
        return (true, accounting.ActiveProcesses, unchecked((int)code));
    }
    internal SafeFileHandle DuplicateFile(long borrowed)
    {
        if (process is null || borrowed <= 0) throw new IOException("workflow_borrowed_handle");
        if (!DuplicateHandle(process, (nint)borrowed, GetCurrentProcess(), out var local, 0, false, 2)) throw new IOException("workflow_handle_duplicate_failed");
        try { WorkflowPhysicalPins.ValidateHandle(local); return local; } catch { local.Dispose(); throw; }
    }
    internal void CloseDrained()
    {
        var state = Observe();
        if (!state.RootExited || state.ActiveProcesses != 0) throw new IOException("workflow_process_not_drained");
        process?.Dispose(); job?.Dispose(); process = null; job = null;
    }
    private sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeKernelHandle() : base(true) { }
        internal SafeKernelHandle(nint value) : base(true) => SetHandle(value);
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfo { public int Size; public nint Reserved, Desktop, Title; public uint X, Y, XSize, YSize, XCount, YCount, Fill, Flags; public ushort ShowWindow, ReservedSize; public nint ReservedBytes, Input, Output, Error; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Info; public nint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public nint Process, Thread; public uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct Accounting { public long User, Kernel, PeriodUser, PeriodKernel; public uint PageFaults, TotalProcesses, ActiveProcesses, Terminated; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeKernelHandle CreateJobObjectW(nint security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitializeProcThreadAttributeList(nint list, int count, uint flags, ref nint bytes);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value, nuint bytes, nint previous, nint returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcessW(string application, StringBuilder command, nint processSecurity, nint threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, nint environment, string directory, ref StartupInfoEx startup, out ProcessInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryInformationJobObject(SafeKernelHandle job, int kind, out Accounting result, uint size, nint returned);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeKernelHandle process, uint timeout);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(SafeKernelHandle process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DuplicateHandle(SafeKernelHandle source, nint sourceHandle, nint target, out SafeFileHandle result, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}
