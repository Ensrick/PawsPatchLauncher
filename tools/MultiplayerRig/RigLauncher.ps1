# Shared by Run-Rig.ps1 and Probe-Precedence.ps1: starts a copy of the game for the rig (SteamAppId in its environment,
# shown without activation) and parks its windows past the right edge of the desktop. Not minimized: a minimized
# windowed game gets a 0x0 client area and its renderer fails to start.
if (-not ('PawsRig.Launcher' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
namespace PawsRig {
public static class Launcher {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO { public int cb; public string lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
    [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, string env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    // SteamAppId in the environment (Steam must run); shown without activation, so it never takes focus.
    public static int Start(string exe, string dir, string args, string extraEnv) {
        var env = new StringBuilder();
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables()) {
            var k = (string)e.Key; if (k == "SteamAppId" || k == "SteamGameId") continue;
            env.Append(k).Append('=').Append(e.Value).Append('\0');
        }
        env.Append("SteamAppId=97130\0SteamGameId=97130\0").Append(extraEnv).Append('\0');
        var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)), dwFlags = 1, wShowWindow = 4 };
        var cmd = new StringBuilder("\"" + exe + "\"" + (args.Length > 0 ? " " + args : ""));
        PROCESS_INFORMATION pi;
        if (!CreateProcess(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, 0x400, env.ToString(), dir, ref si, out pi))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess failed");
        CloseHandle(pi.hThread); CloseHandle(pi.hProcess); return pi.dwProcessId;
    }
    // Moves the process's visible windows past the right edge of the desktop without activating them. Not minimized:
    // a minimized windowed game gets a 0x0 client area and its renderer fails to start.
    public static int Park(int pid) {
        int moved = 0, right = GetSystemMetrics(76) + GetSystemMetrics(78);
        EnumWindows((hwnd, l) => { int owner; GetWindowThreadProcessId(hwnd, out owner);
            if (owner == pid && IsWindowVisible(hwnd) && SetWindowPos(hwnd, IntPtr.Zero, right + 200, 0, 0, 0, 0x15)) moved++;
            return true; }, IntPtr.Zero);
        return moved;
    }
}
}
'@
}
function Start-RigGame([string] $Game, [string] $Arguments, [string] $Environment) {
    $id = [PawsRig.Launcher]::Start((Join-Path $Game 'k2.exe'), $Game, $Arguments, $Environment)
    foreach ($i in 1..60) { if ([PawsRig.Launcher]::Park($id) -gt 0) { break }; Start-Sleep -Milliseconds 500 }
    $id
}
