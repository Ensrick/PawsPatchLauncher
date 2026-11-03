// Exercise the actual guard in compiled production helpers without starting a game.
using System;
using System.IO;
using System.Reflection;

internal static class StartupGuardTests
{
    private static void Check(MethodInfo guard, string root, bool expected)
    {
        bool accepted = true;
        try { guard.Invoke(null, new object[] { root }); }
        catch (TargetInvocationException ex)
        {
            if (!(ex.InnerException is InvalidDataException)) throw;
            accepted = false;
        }
        if (accepted != expected) throw new Exception("Unexpected terrain guard result");
    }

    private static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "PawsTerrainGuard-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "data"));
            byte[] valid = File.ReadAllBytes(args[0]);
            string target = Path.Combine(root, "data", "UVars_k2.tgi");
            int checks = 0;
            for (int i = 1; i < args.Length; i++)
            {
                MethodInfo guard = Assembly.LoadFrom(Path.GetFullPath(args[i]))
                    .GetType("ReleaseStartup", true).GetMethod("ValidateTerrainTexturePool", BindingFlags.Static | BindingFlags.NonPublic);
                if (guard == null) throw new Exception("Missing compiled startup guard");
                File.Delete(target);
                Check(guard, root, false); checks++;
                File.WriteAllBytes(target, valid);
                Check(guard, root, true); checks++;
                byte[] changed = (byte[])valid.Clone(); changed[changed.Length - 1] ^= 1;
                File.WriteAllBytes(target, changed);
                Check(guard, root, false); checks++;
            }
            if (checks == 0) throw new Exception("No helpers supplied");
            Console.WriteLine("TERRAIN_STARTUP_GUARDS_PASS " + checks + "; no game launched");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
