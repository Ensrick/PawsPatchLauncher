# Shared by Run-Rig.ps1 and Probe-Precedence.ps1: starts a copy of the game for the rig (SteamAppId in its environment,
# shown without activation) and parks its windows past the right edge of the desktop. Not minimized: a minimized
# windowed game gets a 0x0 client area and its renderer fails to start.
if (-not ('PawsRig.Launcher' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections;
using System.Collections.Generic;
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
    [StructLayout(LayoutKind.Sequential)] struct BasicLimits { public long PerProcess, PerJob; public uint Flags; public UIntPtr Min, Max; public uint Active; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] struct Limits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob; }
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attrs, string name);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job, int type, ref Limits limits, uint size);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] static extern bool TerminateJobObject(IntPtr job, uint exit);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process, uint exit);
    [DllImport("kernel32.dll")] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    static IntPtr job;
    static readonly Dictionary<int,IntPtr> owned = new Dictionary<int,IntPtr>();
    static void EnsureJob() {
        if(job != IntPtr.Zero) return;
        job=CreateJobObject(IntPtr.Zero,null);
        var limits=new Limits(); limits.Basic.Flags=0x2000; // KILL_ON_JOB_CLOSE
        if(job==IntPtr.Zero || !SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf(typeof(Limits))))
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot establish process ownership");
    }
    public static int[] Running() {
        var ids=new List<int>(); foreach(var p in owned) if(WaitForSingleObject(p.Value,0)==258) ids.Add(p.Key); return ids.ToArray();
    }
    public static void Stop() {
        if(job!=IntPtr.Zero) TerminateJobObject(job,125);
        foreach(var p in owned) { WaitForSingleObject(p.Value,5000); CloseHandle(p.Value); }
        owned.Clear(); if(job!=IntPtr.Zero) CloseHandle(job); job=IntPtr.Zero;
    }
    // SteamAppId in the environment (Steam must run); shown without activation, so it never takes focus.
    public static int Start(string exe, string dir, string args, string extraEnv) {
        EnsureJob();
        var vars=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(DictionaryEntry e in Environment.GetEnvironmentVariables()) vars[(string)e.Key]=(string)e.Value;
        foreach(var k in new[]{"SteamAppId","SteamGameId","K2_RIG_DOCUMENTS","K2_RIG_JOIN","PAWS_TEST_DOCUMENTS"}) vars.Remove(k);
        vars["SteamAppId"]="97130"; vars["SteamGameId"]="97130";
        foreach(var entry in extraEnv.Split(new[]{'\0'},StringSplitOptions.RemoveEmptyEntries)) { int split=entry.IndexOf('='); if(split<1) throw new ArgumentException("Invalid environment"); vars[entry.Substring(0,split)]=entry.Substring(split+1); }
        var env=new StringBuilder(); foreach(var e in vars) env.Append(e.Key).Append('=').Append(e.Value).Append('\0'); env.Append('\0');
        var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)), dwFlags = 1, wShowWindow = 4 };
        var cmd = new StringBuilder("\"" + exe + "\"" + (args.Length > 0 ? " " + args : ""));
        PROCESS_INFORMATION pi;
        if (!CreateProcess(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, 0x404, env.ToString(), dir, ref si, out pi))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess failed");
        if(!AssignProcessToJobObject(job,pi.hProcess)) {
            int error=Marshal.GetLastWin32Error(); TerminateProcess(pi.hProcess,125); CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
            throw new Win32Exception(error,"Cannot own test process");
        }
        owned.Add(pi.dwProcessId,pi.hProcess);
        if(ResumeThread(pi.hThread)==0xffffffff) { CloseHandle(pi.hThread); Stop(); throw new Win32Exception("Cannot resume test process"); }
        CloseHandle(pi.hThread); return pi.dwProcessId;
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
# Retained process handles and a kill-on-close job own only our test copies.
# If this PowerShell process is interrupted or crashes, Windows terminates the job.
function Start-RigGame([string] $Game, [string] $Arguments, [string] $Environment) {
    $id = [PawsRig.Launcher]::Start((Join-Path $Game 'k2.exe'), $Game, $Arguments, $Environment)
    foreach ($i in 1..60) { if ([PawsRig.Launcher]::Park($id) -gt 0) { break }; Start-Sleep -Milliseconds 500 }
    $id
}
function Get-RigCopies { [PawsRig.Launcher]::Running() }
function Stop-RigCopies { [PawsRig.Launcher]::Stop() }
