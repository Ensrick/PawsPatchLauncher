using System;
using System.Text;

// The configuration block the module reads (multiplayer_recovery.c, Config): layout and the limits PawInstall
// enforces, so the helper never sends a block the module would reject. No game process is touched.
internal static class MultiplayerRecoveryInstallerTests
{
    static int checks;
    static void Check(bool ok, string what) { checks++; if (!ok) throw new Exception("Multiplayer recovery: " + what); }
    static bool Rejected(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
    }
    internal static int Main()
    {
        const string log = "C:\\Games\\Kohan II\\" + MultiplayerRecovery.LogFile;
        byte[] b = MultiplayerRecovery.Configuration(MultiplayerRecovery.AllFeatures, 1234, 5, 20, log);
        Check(b.Length == 20 + 260 * 2, "size matches the C struct (5 x uint32 + wchar_t[260])");
        Check(BitConverter.ToUInt32(b, 0) == 0x31524d50 && BitConverter.ToUInt32(b, 4) == 31 && BitConverter.ToUInt32(b, 8) == 1234
              && BitConverter.ToUInt32(b, 12) == 5 && BitConverter.ToUInt32(b, 16) == 20, "fields in order");
        Check(Encoding.Unicode.GetString(b, 20, log.Length * 2) == log && b[20 + log.Length * 2] == 0 && b[21 + log.Length * 2] == 0,
              "log path, NUL terminated");
        Check(MultiplayerRecovery.AllFeatures == (MultiplayerRecovery.SaveCopies | MultiplayerRecovery.Autosave
              | MultiplayerRecovery.DropRecovery | MultiplayerRecovery.LobbyStep | MultiplayerRecovery.Vote), "feature bits");
        Check(Rejected(() => MultiplayerRecovery.Configuration(32, 1, 5, 20, log)), "unknown feature bit");
        Check(Rejected(() => MultiplayerRecovery.Configuration(1, 0, 5, 20, log)), "no main thread");
        Check(Rejected(() => MultiplayerRecovery.Configuration(1, 1, 0, 20, log)), "autosave below 1 minute");
        Check(Rejected(() => MultiplayerRecovery.Configuration(1, 1, 121, 20, log)), "autosave above 120 minutes");
        Check(Rejected(() => MultiplayerRecovery.Configuration(1, 1, 5, 4, log)), "vote below 5 s");
        Check(Rejected(() => MultiplayerRecovery.Configuration(1, 1, 5, 601, log)), "vote above 600 s");
        Check(Rejected(() => MultiplayerRecovery.Configuration(1, 1, 5, 20, new string('x', 260))), "log path without room for NUL");
        Check(Rejected(() => MultiplayerRecovery.Configuration(1, 1, 5, 20, "a\0b")), "embedded NUL");
        Check(!Rejected(() => MultiplayerRecovery.Configuration(1, 1, 1, 5, new string('x', 259))) &&
              !Rejected(() => MultiplayerRecovery.Configuration(0, 1, 120, 600, "")), "limits inclusive");
        Console.WriteLine("MULTIPLAYER_RECOVERY_INSTALLER_PASS " + checks + " checks; configuration layout and limits; no game process");
        return 0;
    }
}
