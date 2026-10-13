using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

// Loads paws_multiplayer_recovery.dll into the fresh, verified 1.3.72 game with the same handshake as the lobby
// compatibility module: system loader identity check, a worker that reports ready, the whole process paused while only
// the worker installs, the result read back before anything resumes. The module checks every byte it relies on itself
// and leaves a feature off when its code differs. A failure here is logged and the game keeps running unchanged.
internal static class MultiplayerRecovery
{
    internal const string ModuleId = "pure-multiplayer-recovery";
    internal const string LogFile = "paws_multiplayer_recovery_1372.log";
    internal const uint SaveCopies = 1, Autosave = 2, DropRecovery = 4, LobbyStep = 8, Vote = 16, AllFeatures = 31;
    internal const uint AutosaveMinutes = 5, VoteAfterSeconds = 20;
    private const uint Magic = 0x31524d50; // "PMR1"
    private const int PathChars = 260;
    // Call sites the module redirects (RVA, current target) and the import entry it replaces; unchanged before install.
    private static readonly uint[] Sites = {0x162b94, 0x162c3a, 0x15708c, 0x1503ea};
    private static readonly uint[] Originals = {0x14c9a6, 0x16a9f6, 0x14b7c5, 0x151cb5};
    private static string nativePath, logPath;

    // Config: magic, features, main thread id, autosave minutes, vote seconds, UTF-16 log path (260 characters).
    internal static byte[] Configuration(uint features, uint mainThread, uint minutes, uint voteSeconds, string log)
    {
        if ((features & ~AllFeatures) != 0 || mainThread == 0 || minutes < 1 || minutes > 120 || voteSeconds < 5 || voteSeconds > 600)
            throw new ArgumentException("Invalid multiplayer recovery settings.");
        if (log.Length >= PathChars || log.IndexOf('\0') >= 0) throw new ArgumentException("Log path is too long.");
        var b = new byte[20 + PathChars * 2];
        BitConverter.GetBytes(Magic).CopyTo(b, 0); BitConverter.GetBytes(features).CopyTo(b, 4);
        BitConverter.GetBytes(mainThread).CopyTo(b, 8); BitConverter.GetBytes(minutes).CopyTo(b, 12);
        BitConverter.GetBytes(voteSeconds).CopyTo(b, 16); Encoding.Unicode.GetBytes(log).CopyTo(b, 20);
        return b;
    }

    internal static void Prepare(string root)
    {
        byte[] payload;
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("PawMultiplayerRecoveryNative"))
        {
            if (s == null || s.Length < 1024 || s.Length > 1024 * 1024) throw new InvalidDataException("Missing multiplayer recovery resource.");
            using (var output = new MemoryStream()) { s.CopyTo(output); payload = output.ToArray(); }
        }
        string digest = Hex(SHA256.Create().ComputeHash(payload));
        // Next to this game's managed installation, as the lobby module (some Steam compatibility environments cannot
        // load from AppData).
        string folder = Path.Combine(root, ".pawpatch", "native", digest);
        Directory.CreateDirectory(folder);
        nativePath = Path.Combine(folder, "paws_multiplayer_recovery.dll");
        if (!File.Exists(nativePath))
        {
            string temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
            try { File.WriteAllBytes(temporary, payload); try { File.Move(temporary, nativePath); } catch (IOException) { if (!File.Exists(nativePath)) throw; } }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        if (FileHash(nativePath) != digest) throw new InvalidDataException("Multiplayer recovery resource integrity check failed.");
        logPath = Path.Combine(Path.GetFullPath(root), LogFile);
    }

    // autosaveMinutes: the helper uses the default; the test rig (tools/MultiplayerRig) passes a shorter interval.
    internal static void Install(Process game, IntPtr image, Action<string> log, uint autosaveMinutes = AutosaveMinutes)
    {
        if (nativePath == null) throw new InvalidOperationException("Multiplayer recovery was not prepared.");
        try
        {
            game.Refresh();
            uint process, thread = GetWindowThreadProcessId(game.MainWindowHandle, out process);
            if (thread == 0 || process != (uint)game.Id) throw new IOException("Game window thread is unavailable.");
            byte[] config = Configuration(AllFeatures, thread, autosaveMinutes, VoteAfterSeconds, logPath);
            uint on;
            using (var native = new Installer(game, image)) on = native.Install(nativePath, config);
            log("MULTIPLAYER_RECOVERY_READY revision=1 saveCopies=" + ((on & SaveCopies) != 0) + " autosave=" + ((on & Autosave) != 0)
                + "/" + autosaveMinutes + "min dropRecovery=" + ((on & DropRecovery) != 0) + " lobbyStep=" + ((on & LobbyStep) != 0)
                + " vote=" + ((on & Vote) != 0) + "/" + VoteAfterSeconds + "s log=" + LogFile);
        }
        catch (Exception error)
        {
            if (game.HasExited) throw;
            log("MULTIPLAYER_RECOVERY_OFF " + error.Message);
        }
    }

    private static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", ""); }
    private static string FileHash(string p) { using (var f = File.OpenRead(p)) using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(f)); }

    private sealed class Installer : IDisposable
    {
        private IntPtr handle; private readonly Process game; private readonly uint image; private readonly DateTime started;
        internal Installer(Process p, IntPtr b)
        {
            game = p; image = U(b); started = p.StartTime.ToUniversalTime();
            handle = OpenProcess(0x00100c3a, false, p.Id);
            if (handle == IntPtr.Zero) throw new IOException("Cannot open the game for multiplayer recovery.");
        }
        private static uint U(IntPtr p) { return unchecked((uint)p.ToInt32()); }
        private static IntPtr P(uint p) { return new IntPtr(unchecked((int)p)); }
        private byte[] Read(uint address, int n)
        {
            var b = new byte[n]; IntPtr done;
            if (!ReadProcessMemory(handle, P(address), b, n, out done) || done.ToInt32() != n) throw new IOException("Multiplayer recovery read failed.");
            return b;
        }
        private IntPtr Allocate(byte[] data)
        {
            var p = VirtualAllocEx(handle, IntPtr.Zero, data.Length, 0x3000, 4); IntPtr n;
            if (p == IntPtr.Zero || !WriteProcessMemory(handle, p, data, data.Length, out n) || n.ToInt32() != data.Length)
                throw new IOException("Multiplayer recovery allocation failed.");
            return p;
        }
        private void Invoke(uint address, IntPtr data, uint timeout)
        {
            uint tid; var t = CreateRemoteThread(handle, IntPtr.Zero, 0, P(address), data, 0, out tid);
            if (t == IntPtr.Zero) throw new IOException("Multiplayer recovery thread failed.");
            try { if (WaitForSingleObject(t, timeout) != 0) throw new IOException("Multiplayer recovery loader timed out."); }
            finally { CloseHandle(t); }
        }
        private ProcessModule Module(string name)
        {
            game.Refresh();
            return game.Modules.Cast<ProcessModule>().Single(m => m.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        private uint LoadRemoteLibrary(uint load, string dll)
        {
            var path = Allocate(Encoding.Unicode.GetBytes(dll + "\0")); var result = Allocate(new byte[8]); byte[] code;
            using (var output = new MemoryStream()) using (var writer = new BinaryWriter(output))
            {
                // push path; mov eax, LoadLibraryW; call eax; mov [result], eax; result+4 = this thread's Win32 error
                writer.Write((byte)0x68); writer.Write(U(path)); writer.Write((byte)0xb8); writer.Write(load);
                writer.Write(new byte[] { 0xff, 0xd0, 0xa3 }); writer.Write(U(result));
                writer.Write(new byte[] { 0x64, 0x8b, 0x0d, 0x18, 0, 0, 0, 0x8b, 0x49, 0x34, 0x89, 0x0d }); writer.Write(U(result) + 4);
                writer.Write(new byte[] { 0x33, 0xc0, 0xc2, 4, 0 }); code = output.ToArray();
            }
            var stub = Allocate(code); uint old;
            if (!VirtualProtectEx(handle, stub, code.Length, 0x20, out old) || !FlushInstructionCache(handle, stub, code.Length))
                throw new IOException("Cannot prepare the system loader call.");
            Invoke(U(stub), IntPtr.Zero, 10000); var value = Read(U(result), 8);
            VirtualFreeEx(handle, path, 0, 0x8000); VirtualFreeEx(handle, result, 0, 0x8000); VirtualFreeEx(handle, stub, 0, 0x8000);
            uint module = BitConverter.ToUInt32(value, 0);
            if (module == 0) throw new IOException("The game could not load the multiplayer recovery module. Win32=" + BitConverter.ToUInt32(value, 4));
            return module;
        }
        private void Guard()
        {
            if (game.HasExited || game.StartTime.ToUniversalTime() != started || U(game.MainModule.BaseAddress) != image)
                throw new IOException("Fresh game process identity changed.");
            for (int i = 0; i < Sites.Length; i++)
            {
                var b = Read(image + Sites[i], 5);
                if (b[0] != 0xe8 || unchecked(image + Sites[i] + 5 + BitConverter.ToUInt32(b, 1)) != image + Originals[i])
                    throw new IOException("Multiplayer recovery guard failed at " + Sites[i].ToString("X"));
            }
        }
        // Returns the features the module turned on.
        internal uint Install(string dll, byte[] config)
        {
            Guard(); IntPtr local = LoadLibraryEx(dll, IntPtr.Zero, 1);
            if (local == IntPtr.Zero) throw new IOException("Cannot read the multiplayer recovery module.");
            try
            {
                IntPtr export = GetProcAddress(local, "PawInstallQueued"); if (export == IntPtr.Zero) export = GetProcAddress(local, "_PawInstallQueued@4");
                IntPtr status = GetProcAddress(local, "PawRecoveryFeatures");
                if (export == IntPtr.Zero || status == IntPtr.Zero) throw new IOException("Missing multiplayer recovery exports.");
                uint load = U(GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW"));
                ProcessModule owner = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Single(m => load >= U(m.BaseAddress) && load < U(m.BaseAddress) + (uint)m.ModuleMemorySize);
                var target = Module(owner.ModuleName);
                if (FileHash(owner.FileName) != FileHash(target.FileName)) throw new IOException("System loader identity differs.");
                uint module = LoadRemoteLibrary(U(target.BaseAddress) + load - U(owner.BaseAddress), dll);
                // {ready, go, result, config}
                byte[] request = new byte[config.Length + 12]; config.CopyTo(request, 12);
                IntPtr arg = Allocate(request); bool suspended = false, released = false; uint tid;
                var worker = CreateRemoteThread(handle, IntPtr.Zero, 0, P(module + U(export) - U(local)), arg, 0, out tid);
                if (worker == IntPtr.Zero) throw new IOException("Cannot start the multiplayer recovery worker.");
                try
                {
                    var timer = Stopwatch.StartNew();
                    while (BitConverter.ToInt32(Read(U(arg), 4), 0) != 1)
                    {
                        if (game.HasExited || WaitForSingleObject(worker, 0) == 0 || timer.ElapsedMilliseconds > 10000) throw new IOException("Multiplayer recovery worker did not initialize.");
                        System.Threading.Thread.Sleep(10);
                    }
                    if (NtSuspendProcess(handle) != 0) throw new IOException("Cannot pause the game for multiplayer recovery."); suspended = true; Guard();
                    // This worker already completed DLL thread notifications.
                    if (ResumeThread(worker) != 1) throw new IOException("Unexpected multiplayer recovery worker suspend count.");
                    IntPtr written; var go = BitConverter.GetBytes(1);
                    if (!WriteProcessMemory(handle, P(U(arg) + 4), go, 4, out written) || written.ToInt32() != 4) throw new IOException("Cannot release the multiplayer recovery worker.");
                    released = true; timer.Restart();
                    // The explicit result, before DLL_THREAD_DETACH, which may need a loader lock held by a paused thread.
                    while (BitConverter.ToInt32(Read(U(arg), 4), 0) != 2)
                    {
                        if (WaitForSingleObject(worker, 0) == 0 || timer.ElapsedMilliseconds > 5000) throw new IOException("Multiplayer recovery installation did not complete.");
                        System.Threading.Thread.Sleep(1);
                    }
                    uint result = BitConverter.ToUInt32(Read(U(arg) + 8, 4), 0);
                    if (result != 0) throw new IOException("Multiplayer recovery installation failed: " + result + " (see " + LogFile + ")");
                    uint on = BitConverter.ToUInt32(Read(module + U(status) - U(local), 4), 0);
                    VirtualFreeEx(handle, arg, 0, 0x8000);
                    return on;
                }
                finally
                {
                    // An aborted install still releases the waiting worker, with a config the module rejects before
                    // any change (wrong magic), so no thread is left polling in the game.
                    if (!released)
                    {
                        IntPtr ignored;
                        WriteProcessMemory(handle, P(U(arg) + 12), new byte[4], 4, out ignored);
                        WriteProcessMemory(handle, P(U(arg) + 4), BitConverter.GetBytes(1), 4, out ignored);
                    }
                    CloseHandle(worker);
                    if (suspended && NtResumeProcess(handle) != 0) throw new IOException("Cannot resume the game after multiplayer recovery.");
                }
            }
            finally { FreeLibrary(local); }
        }
        public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
    }

    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("kernel32", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32")] private static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, int n, out IntPtr read);
    [DllImport("kernel32")] private static extern bool WriteProcessMemory(IntPtr h, IntPtr a, byte[] b, int n, out IntPtr written);
    [DllImport("kernel32")] private static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr a, int n, uint type, uint protect);
    [DllImport("kernel32")] private static extern bool VirtualFreeEx(IntPtr h, IntPtr a, int n, uint type);
    [DllImport("kernel32")] private static extern bool VirtualProtectEx(IntPtr h, IntPtr a, int n, uint protect, out uint old);
    [DllImport("kernel32")] private static extern bool FlushInstructionCache(IntPtr h, IntPtr a, int n);
    [DllImport("kernel32")] private static extern IntPtr CreateRemoteThread(IntPtr h, IntPtr attr, uint stack, IntPtr fn, IntPtr arg, uint flags, out uint id);
    [DllImport("kernel32")] private static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32")] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32")] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("ntdll")] private static extern int NtSuspendProcess(IntPtr h);
    [DllImport("ntdll")] private static extern int NtResumeProcess(IntPtr h);
}
