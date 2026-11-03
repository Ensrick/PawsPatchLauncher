using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

// Test rig only: installs the multiplayer saves module into one running copy of the game, with exactly the code
// the pure helper uses (game/multiplayer-saves/MultiplayerSaves.cs, module embedded the same way).
//   RigInject.exe <process id> <rig folder of this copy> <autosave minutes>
// The module and its log (<rig folder>\Logs\paws_recovery.log) go to <rig folder>. Its save-data directory is the
// copy's own <rig folder>\Documents\Kohan2\data, through the private-profile mechanism Prepare reads
// (reconnect-test.marker in <rig folder> plus PAWS_TEST_DOCUMENTS); exit code 0 when it is installed.
internal static class RigInject
{
    const string GameHash = "1EB79BBB678668BE5A05F8C98103CD9988048490CE7C53C74C9A1D0813E9CD45";

    static int Main(string[] args)
    {
        int id; uint minutes; long started;
        if (args.Length != 5 || !int.TryParse(args[0], out id) || !uint.TryParse(args[2], out minutes) || minutes < 1 || minutes > 5 || !long.TryParse(args[4], out started))
        {
            Console.Error.WriteLine("usage: RigInject.exe <process id> <rig folder> <autosave minutes> <expected exe> <start ticks>");
            return 2;
        }
        try
        {
            using (var game = Process.GetProcessById(id))
            {
                string exe = game.MainModule.FileName;
                if (!String.Equals(Path.GetFullPath(args[3]), exe, StringComparison.OrdinalIgnoreCase) || game.StartTime.ToUniversalTime().Ticks != started)
                    throw new InvalidDataException("Test process identity changed");
                if (!String.Equals(Path.GetFileName(exe), "k2.exe", StringComparison.OrdinalIgnoreCase) || Hash(exe) != GameHash)
                    throw new InvalidDataException("process " + id + " is not Kohan II 1.3.72");
                var until = DateTime.UtcNow.AddSeconds(60);
                while (true)
                {
                    game.Refresh();
                    if (game.HasExited) throw new InvalidOperationException("the game exited");
                    if (game.MainWindowHandle != IntPtr.Zero) break;
                    if (DateTime.UtcNow > until) throw new TimeoutException("no game window after 60 s");
                    Thread.Sleep(250);
                }
                string folder = Path.GetFullPath(args[1]);
                File.WriteAllText(Path.Combine(folder, "reconnect-test.marker"), "Paw's multiplayer test rig: this copy's private Documents\r\n");
                Environment.SetEnvironmentVariable("PAWS_TEST_DOCUMENTS", Path.Combine(folder, "Documents"));
                MultiplayerSaves.Prepare(folder);
                bool ready = false;
                MultiplayerSaves.Install(game, game.MainModule.BaseAddress, delegate(string line)
                {
                    Console.WriteLine(line);
                    if (line.StartsWith("MULTIPLAYER_SAVES_READY", StringComparison.Ordinal)) ready = true;
                }, minutes);
#if PAW_TASK_STATUS
                using (var memory = new PawPureFixes.NativeMemory(id, exe, game.StartTime.ToUniversalTime()))
                {
                    memory.Suspend();
                    TaskStatusPatch.Install(memory, unchecked((uint)game.MainModule.BaseAddress.ToInt32()), delegate(string line)
                    {
                        Console.WriteLine(line);
                        File.AppendAllText(Path.Combine(folder, "Logs", "task-status.log"), line + Environment.NewLine);
                    });
                }
#endif
                return ready ? 0 : 1;
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("RigInject: " + error.Message);
            return 1;
        }
    }

    static string Hash(string path)
    {
        using (var f = File.OpenRead(path)) using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
    }
}
