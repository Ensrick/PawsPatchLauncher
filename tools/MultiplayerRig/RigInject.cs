using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

// Test rig only: installs the multiplayer recovery module into one running copy of the game, with exactly the code
// the pure helper uses (game/multiplayer-recovery/MultiplayerRecovery.cs, module embedded the same way).
//   RigInject.exe <process id> <rig folder of this copy> <autosave minutes>
// The module and its log go to <rig folder>; exit code 0 when it is installed.
internal static class RigInject
{
    const string GameHash = "1EB79BBB678668BE5A05F8C98103CD9988048490CE7C53C74C9A1D0813E9CD45";

    static int Main(string[] args)
    {
        int id; uint minutes;
        if (args.Length != 3 || !int.TryParse(args[0], out id) || !uint.TryParse(args[2], out minutes))
        {
            Console.Error.WriteLine("usage: RigInject.exe <process id> <rig folder> <autosave minutes>");
            return 2;
        }
        try
        {
            using (var game = Process.GetProcessById(id))
            {
                string exe = game.MainModule.FileName;
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
                MultiplayerRecovery.Prepare(args[1]);
                bool ready = false;
                MultiplayerRecovery.Install(game, game.MainModule.BaseAddress, delegate(string line)
                {
                    Console.WriteLine(line);
                    if (line.StartsWith("MULTIPLAYER_RECOVERY_READY", StringComparison.Ordinal)) ready = true;
                }, minutes);
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
