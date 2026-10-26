using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Reticle.OverlayHost;

internal static class HostSecurity
{
    internal static bool GetUiAccess()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out SafeAccessTokenHandle token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        {
            if (!GetTokenInformation(token, 26, out int access, sizeof(int), out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return access != 0;
        }
    }

    internal static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static Process OpenEditor(HostOptions options)
    {
        var process = Process.GetProcessById(options.EditorPid);
        try
        {
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != options.EditorStartTicks || process.SessionId != Process.GetCurrentProcess().SessionId)
                throw new InvalidOperationException("编辑器进程身份已改变，请重新开启独立全屏模式。");
            return process;
        }
        catch { process.Dispose(); throw; }
    }

    internal static void CheckPipeServer(NamedPipeClientStream pipe, int expectedPid)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint pid)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (pid != expectedPid) throw new InvalidOperationException("覆盖连接不是由指定的编辑器建立。");
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int infoClass, out int information, int length, out int returnedLength);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverPid);
}
