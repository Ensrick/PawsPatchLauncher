using System;
using System.Text;

// The configuration block the module reads (multiplayer_saves.c, Config): layout and the limits PawInstall
// enforces, so the helper never sends a block the module would reject. No game process is touched.
internal static class MultiplayerSavesInstallerTests
{
    static int checks;
    static void Check(bool ok, string what) { checks++; if (!ok) throw new Exception("Multiplayer saves: " + what); }
    static bool Rejected(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
    }
    internal static int Main()
    {
        const string log = "C:\\Games\\Kohan II\\" + MultiplayerSaves.LogFile;
        const string data = "C:\\Private Profile\\Kohan2\\data";
        byte[] b = MultiplayerSaves.Configuration(MultiplayerSaves.AllFeatures, 1234, 5, log, data);
        Check(b.Length == 16 + 260 * 4, "size matches PMS2: four integers and two UTF-16 paths");
        Check(BitConverter.ToUInt32(b, 0) == 0x32534d50 && BitConverter.ToUInt32(b, 4) == 3 && BitConverter.ToUInt32(b, 8) == 1234
              && BitConverter.ToUInt32(b, 12) == 5, "fields in order");
        Check(Encoding.Unicode.GetString(b, 16, log.Length * 2) == log && b[16 + log.Length * 2] == 0 && b[17 + log.Length * 2] == 0,
              "log path, NUL terminated");
        Check(Encoding.Unicode.GetString(b, 536, data.Length * 2) == data && b[536 + data.Length * 2] == 0, "save data path, NUL terminated");
        Check(Rejected(() => MultiplayerSaves.Configuration(1, 1, 5, log, new string('x', 260))), "save path too long");
        Check(Rejected(() => MultiplayerSaves.Configuration(1, 1, 5, log, "a\0b")), "save path embedded NUL");
        Check(MultiplayerSaves.AllFeatures == (MultiplayerSaves.SaveCopies | MultiplayerSaves.Autosave), "feature bits");
        Check(Rejected(() => MultiplayerSaves.Configuration(4, 1, 5, log)), "unknown feature bit");
        Check(Rejected(() => MultiplayerSaves.Configuration(1, 0, 5, log)), "no main thread");
        Check(Rejected(() => MultiplayerSaves.Configuration(1, 1, 0, log)), "autosave below 1 minute");
        Check(Rejected(() => MultiplayerSaves.Configuration(1, 1, 121, log)), "autosave above 120 minutes");
        Check(Rejected(() => MultiplayerSaves.Configuration(1, 1, 5, new string('x', 260))), "log path without room for NUL");
        Check(Rejected(() => MultiplayerSaves.Configuration(1, 1, 5, "a\0b")), "embedded NUL");
        Check(!Rejected(() => MultiplayerSaves.Configuration(1, 1, 1, new string('x', 259))) &&
              !Rejected(() => MultiplayerSaves.Configuration(0, 1, 120, "")), "limits inclusive");
        Console.WriteLine("MULTIPLAYER_SAVES_INSTALLER_PASS " + checks + " checks; configuration layout and limits; no game process");
        return 0;
    }
}
