// Quiet startup integration. No Form or console window. Optional graphics
// diagnostics start an embedded background worker after patch installation.
// These routines are used only for the new game launched by this executable.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

internal static class ReleaseStartup
{
#if LOCAL_LARGE_MAPS || LARGE_MAPS
    internal const string TemplateDataHash = LargeMapsLocal.TemplateHash;
#else
    internal const string TemplateDataHash = "DFD1B5E15507D9FE70DA7CAEDE5874DD0FC2E420B467BD662C5DDDE8DF6655E8";
#endif
#if LOBBY_COMPATIBILITY
    internal const string Build = "beta." + PawLobbyCompatibility.Version + "-1372-city-policy23-transfer-r2-lobby1-quiet";
#elif FAST_SAVE_TRANSFER
    internal const string Build = "beta.0.3.0-beta.4-1372-city-policy23-transfer-r2-quiet";
#else
    internal const string Build = "release.0.2.1-1372-terrain-randommap-colors20-independent-quiet";
#endif
    private static int verifiedPid;
    private static DateTime verifiedStart;
    internal static IntPtr VerifiedImage;
#if LAIR_RECOVERY_TEST || PAW_PURE_EXTENDED
    // A local test EXE lives outside the installation. All readers must use
    // the exact verified game root, not the location of the helper binary.
    internal static string GameDataDirectory = AppDomain.CurrentDomain.BaseDirectory;
    internal static void VerifyLocalLaunchData(string root)
    {
        GameDataDirectory = Path.GetFullPath(root);
        PawGamePresentation.ValidateData(GameDataDirectory);
#if PAW_COLORS
        if (!File.Exists(Path.Combine(GameDataDirectory, @"data\UI\Menus\pcolors.tgi")))
            throw new FileNotFoundException("Missing player-color lobby layout");
        PawLobbyColorsNative.VerifyOffline();
#endif
        // Force catalog parsing before the game is started, too.
        string language = PawGameText.Language;
    }
#endif

    internal static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create()) return TerrainPatch.HexText(sha.ComputeHash(stream));
    }

    internal static string GameDirectory(string[] args)
    {
        if (args.Length == 2 && args[0] == "--game-dir") return Path.GetFullPath(args[1]);
        if (args.Length != 0) throw new ArgumentException("Unknown launch arguments.");
        string own = AppDomain.CurrentDomain.BaseDirectory;
        if (File.Exists(Path.Combine(own, "k2.exe"))) return own;
        // The local two-PC test can run directly from the existing shared folder.
        // Published helpers live next to k2.exe and use only the branch above.
        string[] roots = {
            @"D:\SteamLibrary\steamapps\common\Kohan II",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "Kohan II")
        };
        string[] found = roots.Where(p => File.Exists(Path.Combine(p, "k2.exe"))).ToArray();
        if (found.Length != 1) throw new InvalidOperationException("Не удалось однозначно выбрать папку игры. Укажите --game-dir и путь к Kohan II.");
        return found[0];
    }

    internal static void GuardData(string root)
    {
        string[][] files = {
            new[] { @"data\Game\world_rules_k2.tgi", "22282E6C584F37B697FC919EA16126FFE15327248B9A8E1B9965FE3568DD146C" },
            new[] { @"data\Templates\template_rmc_k2.tgi", TemplateDataHash },
            new[] { @"data\RandomMap\rmc_temperate03.tgi", "75C096D8095EA5EDB93D27B06E74F8BBFBB0D557A05D311D0CFCCD4889A3D7AB" }
        };
        foreach (string[] f in files)
            if (!File.Exists(Path.Combine(root, f[0])) || Hash(Path.Combine(root, f[0])) != f[1])
                throw new InvalidDataException("Нужны файлы текущего выпуска патча. Отличается " + f[0] + ". Выполните проверку файлов в лаунчере. Игра не запущена.");
        RandomMapPatch.GuardData(root);
#if AI_POLICY
        PawAiOptions.Enabled = PawAiOptions.Read(root);
        Console.WriteLine("AI_IMPROVEMENTS " + (PawAiOptions.Enabled ? "on" : "off"));
#endif
#if NIGHTMARE_DIFFICULTY
        NightmareDifficultyData.Validate(root, PawAiOptions.Enabled);
        Console.WriteLine("NIGHTMARE_DIFFICULTY " + (PawAiOptions.Enabled ? "on" : "off"));
#endif
    }

    internal static void InstallTerrainAndMap(Process game, IntPtr image, Action<string> log)
    {
        if (game.Id != verifiedPid || game.StartTime.ToUniversalTime() != verifiedStart)
            throw new InvalidOperationException("Процесс не принадлежит этому запуску.");
        uint address = unchecked((uint)image.ToInt32());
#if ENGINE_CRASH_FIXES
        uint addProbeHandler = EngineCrashFixesPatch.ResolveHandlerRegistration(game);
#endif
        using (NativeMemory memory = new NativeMemory(game.Id))
        {
            memory.Suspend();
            try
            {
                // Run before the optional sync bypass, while native guards are
                // still intact. The r8 terrain and random-map payloads are unchanged.
                TerrainPatch.Validate(memory, address);
                RandomMapPatch.Validate(memory, address);
#if LOCAL_LARGE_MAPS || LARGE_MAPS
                LargeMapsPatch.Validate(memory, address);
#endif
#if FOUNDATION_COUNTS
                FoundationCountsPatch.Validate(memory, address);
#endif
#if ENGINE_CRASH_FIXES
                EngineCrashFixesPatch.Validate(memory, address);
#endif
#if CAMERA_ZOOM_2
                CameraZoomPatch.Validate(memory, address);
#endif
#if COMPANY_POSITION_RECOVERY
                CompanyPositionPatch.Validate(memory, address);
#endif
#if EXHAUSTION_RECOVERY
                #if AI_POLICY
                if (PawAiOptions.Enabled)
#endif
                ExhaustionRecoveryPatch.Validate(memory, address);
#endif
#if AI_POLICY
                #if AI_POLICY
                if (PawAiOptions.Enabled)
#endif
                AiPolicyRuntime.Validate(memory, address);
#endif
#if BOT_LOBBY
                BotLobbyPatch.Validate(memory, address);
#endif
#if FRACTIONAL_KINGDOM_POINTS
                FractionalPointsPatch.Validate(memory, address);
#endif
#if SETTLEMENT_SLOTS
                SettlementSlotsPatch.Validate(memory, address);
#endif
#if ALLY_ECONOMY
                AllyEconomyPatch.Validate(memory, address);
#endif
                uint terrain = TerrainPatch.Install(memory, address, log);
                uint map = RandomMapPatch.Install(memory, address, log);
                TerrainPatch.Expect(memory, address + TerrainPatch.HookRva,
                    TerrainPatch.Call(address + TerrainPatch.HookRva, terrain));
                TerrainPatch.Expect(memory, terrain, TerrainPatch.Stub(address, terrain));
                RandomMapPatch.Verify(memory, address, map);
#if LOCAL_LARGE_MAPS || LARGE_MAPS
                LargeMapsPatch.Install(memory, address, log);
#endif
#if FOUNDATION_COUNTS
                FoundationCountsPatch.Install(memory, address, log);
#endif
#if ENGINE_CRASH_FIXES
                uint crashGuard = EngineCrashFixesPatch.Install(memory, address, addProbeHandler, log);
                EngineCrashFixesPatch.Monitor(game.Id, crashGuard, log);
#endif
#if LAIR_RECOVERY
                LairRecoveryPatch.Install(memory, address, log);
#endif
#if CAMERA_ZOOM_2
                CameraZoomPatch.Install(memory, address, log);
#endif
#if COMPANY_POSITION_RECOVERY
                CompanyPositionPatch.Install(memory, address, log);
#endif
#if EXHAUSTION_RECOVERY
                #if AI_POLICY
                if (PawAiOptions.Enabled)
#endif
                ExhaustionRecoveryPatch.Install(memory, address, log);
#endif
#if AI_POLICY
                #if AI_POLICY
                if (PawAiOptions.Enabled)
#endif
                AiPolicyRuntime.Install(memory, address, game.Id, Path.GetDirectoryName(game.MainModule.FileName), log);
#endif
#if BOT_LOBBY
#if AI_POLICY
                BotLobbyPatch.Install(memory, address, PawGameText.LanguageId, PawAiOptions.Enabled, log);
#else
                BotLobbyPatch.Install(memory, address, PawGameText.LanguageId, log);
#endif
#endif
#if FRACTIONAL_KINGDOM_POINTS
                FractionalPointsPatch.Install(memory, address, log);
#endif
#if SETTLEMENT_SLOTS
                SettlementSlotsPatch.Install(memory, address, log);
#endif
#if ALLY_ECONOMY
                AllyEconomyPatch.Install(memory, address, log);
#endif
            }
            finally { memory.Resume(); }
        }
    }

    // Restrict retry to reads of not-yet-materialized ProcessModule metadata.
    // In particular, do not swallow access denials or failures after patching.
    internal static bool TrySnapshot(Func<string> readPath, Func<IntPtr> readBase, out string path, out IntPtr image)
    {
        path = null; image = IntPtr.Zero;
        try
        {
            string p = readPath(); IntPtr b = readBase();
            if (String.IsNullOrWhiteSpace(p) || b == IntPtr.Zero) return false;
            path = Path.GetFullPath(p); image = b; return true;
        }
        catch (NullReferenceException) { return false; }
    }

    internal static Process WaitForGame(string expectedPath, DateTime launch, out IntPtr image)
    {
        image = IntPtr.Zero;
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 60000)
        {
            Process[] games = Process.GetProcessesByName("k2");
            if (games.Length > 1)
            {
                foreach (Process p in games) p.Dispose();
                throw new InvalidOperationException("Запущено несколько копий Kohan II. Исправления не применены.");
            }
            foreach (Process game in games)
            {
                bool keep = false;
                try
                {
                    if (game.HasExited) continue;
                    DateTime started = game.StartTime.ToUniversalTime();
                    if (started < launch) throw new InvalidOperationException("Нельзя применять исправления к уже запущенной игре.");
                    game.Refresh();
                    if (game.MainWindowHandle == IntPtr.Zero) continue;
                    ProcessModule module = game.MainModule;
                    if (module == null) continue;
                    string path; IntPtr address;
                    if (!TrySnapshot(delegate { return module.FileName; }, delegate { return module.BaseAddress; }, out path, out address)) continue;
                    if (!String.Equals(path, Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Steam запустил другую папку игры: " + path);
                    if (game.HasExited) continue;
                    image = address; VerifiedImage = address; verifiedPid = game.Id; verifiedStart = started;
                    keep = true; return game;
                }
                catch (Win32Exception e)
                {
                    // Access denial is never converted into a startup retry.
                    if (e.NativeErrorCode == 5 || !game.HasExited) throw;
                }
                finally { if (!keep) game.Dispose(); }
            }
            Thread.Sleep(50);
        }
        throw new InvalidOperationException("Steam не создал совместимый процесс игры за 60 секунд. Исправления не применены.");
    }

    internal static void StopIncompleteLaunch(Process game, Action<string> log)
    {
        // Only a process from this fresh launch with verified path/start identity.
        // Never find/attach to another process in the failure path.
        if (game == null || verifiedPid != game.Id || game.HasExited) return;
        if (game.StartTime.ToUniversalTime() != verifiedStart) throw new InvalidOperationException("Изменилась идентичность процесса игры.");
        game.Kill();
        if (!game.WaitForExit(5000)) throw new InvalidOperationException("Не удалось остановить запуск с неполными исправлениями.");
        log("INCOMPLETE_FRESH_LAUNCH_STOPPED pid=" + verifiedPid);
    }

    internal static int SelfTest()
    {
        int count = 0;
        Action<bool> check = delegate(bool ok) { count++; if (!ok) throw new Exception("Quiet startup regression failed #" + count); };
        string path; IntPtr image;
        string expected = Path.GetFullPath("k2.exe");
        int reads = 0;
        check(!TrySnapshot(delegate { throw new NullReferenceException(); }, delegate { throw new Exception(); }, out path, out image));
        check(path == null && image == IntPtr.Zero);
        check(!TrySnapshot(delegate { return expected; }, delegate { throw new NullReferenceException(); }, out path, out image));
        check(path == null && image == IntPtr.Zero);
        check(!TrySnapshot(delegate { return null; }, delegate { return new IntPtr(0x460000); }, out path, out image));
        check(!TrySnapshot(delegate { return expected; }, delegate { return IntPtr.Zero; }, out path, out image));
        bool denied = false;
        try { TrySnapshot(delegate { throw new Win32Exception(5); }, delegate { return IntPtr.Zero; }, out path, out image); }
        catch (Win32Exception e) { denied = e.NativeErrorCode == 5; }
        check(denied);
        Func<string> getter = delegate { if (++reads < 4) throw new NullReferenceException(); return expected; };
        for (int attempt = 0; attempt < 4; attempt++)
            check(TrySnapshot(getter, delegate { return new IntPtr(0x460000); }, out path, out image) == (attempt == 3));
        check(path == expected && image == new IntPtr(0x460000));
        // Accepted terrain bytes, rollback and random-map transactions in mock memory.
        foreach (uint b in new uint[] { 0x460000, 0x640000, 0x14000000 })
        {
            var m = new FakeMemory(b); uint cave = TerrainPatch.Install(m, b, delegate { }); int operations = m.Operations;
            check(m.Read(b + TerrainPatch.HookRva, 5).SequenceEqual(TerrainPatch.Call(b + TerrainPatch.HookRva, cave)));
            for (int at = 1; at <= operations; at++)
            {
                var bad = new FakeMemory(b) { FailAt = at }; bool failed = false;
                try { TerrainPatch.Install(bad, b, delegate { }); } catch (IOException) { failed = true; }
                bad.FailAt = -1;
                check(failed && !bad.Allocated && bad.Read(b + TerrainPatch.HookRva, 5).SequenceEqual(TerrainPatch.Original));
            }
        }
        count += RandomMapPatch.SelfTest();
        Console.WriteLine("QUIET_STARTUP_PASS " + count + " assertions; no game or UI started");
        return 0;
    }
}
