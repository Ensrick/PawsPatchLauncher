using System.Text;
using PawsPatchLauncher;

internal static class GameUserSettingsTests
{
    public static int Run()
    {
        var count = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Game settings: " + why); count++; }
        void Throws<T>(Action action, string why) where T : Exception
        {
            try { action(); } catch (T) { count++; return; }
            throw new Exception("Game settings: expected " + typeof(T).Name + ": " + why);
        }
        var root = Path.Combine(Path.GetTempPath(), "PawsGameSettingsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "UVars.tgi");
        const string source = ";; пользовательский профиль\r\n[Vars]\r\n{\r\n\tint ResolutionX = 2560 ;; display\r\n\tint ResolutionY = 1440\r\n\tfloat AudioMainVolume = 0.309220\r\n\tfloat Audio2DVolume = 80% // effect\r\n\tfloat FrameRateLimit = 240.000000\r\n\tflag ViewShowElapsedGameTime = true\r\n\tstring HotkeyConfiguration = visual_dvorak_config\r\n}\r\n";
        var resolution = new Dictionary<string, string> { ["ResolutionX"] = "1920", ["ResolutionY"] = "1080" };
        var timer = new Dictionary<string, string> { ["ViewShowElapsedGameTime"] = "false" };
        try
        {
            Check(GameResolution.WidescreenChoices.Count == 9 && GameResolution.WidescreenChoices.Distinct().Count() == 9, "complete unique preset list");
            foreach (var mode in GameResolution.WidescreenChoices)
            {
                Check(mode.Width <= 3840 && mode.Height <= 2160 && Math.Abs((double)mode.Width / mode.Height - 16d / 9) < .002, "16:9 up to 4K");
                GameUserSettings.Validate("ResolutionX", mode.Width.ToString());
                GameUserSettings.Validate("ResolutionY", mode.Height.ToString());
            }
            // Compare full bytes for every supported encoding, including legacy comments.
            foreach (var encoding in new Encoding[] { new UTF8Encoding(false), new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true), Encoding.Latin1 })
            foreach (var newline in new[] { "\r\n", "\n" })
            {
                byte[] Enc(string s) => [..encoding.GetPreamble(), ..encoding.GetBytes(s.Replace("\r\n", newline))];
                var original = Enc(source); File.WriteAllBytes(path, original);
                var doc = GameUserSettings.Load(path);
                Check(doc.Number("ResolutionX") == 2560 && doc.Number("ResolutionY") == 1440, "resolution read");
                Check(doc.Number("AudioMainVolume") == 0.309220 && doc.Number("Audio2DVolume") == 0.8, "precision/percentage read");
                Check(doc.Flag("ViewShowElapsedGameTime") == true && doc.Flag("MinimapColorByKingdom") is null, "flags read");
                Check(doc.Edit(new Dictionary<string, string>()).SequenceEqual(original), "no edits byte-identical");
                Check(doc.Edit(resolution).SequenceEqual(Enc(source.Replace("2560", "1920").Replace("1440", "1080"))), "only dimensions changed");
                var expected = source.Replace("0.309220", "0.25").Replace("true", "false")
                    .Replace("}\r\n", "\tflag\tMinimapColorByKingdom = true\r\n}\r\n");
                Check(doc.Edit(new Dictionary<string, string> { ["AudioMainVolume"] = "0.25", ["ViewShowElapsedGameTime"] = "false", ["MinimapColorByKingdom"] = "true" }).SequenceEqual(Enc(expected)), "mixed edit/insert preserves rest");
                var backup = doc.Save(resolution, () => false);
                Check(backup is not null && File.ReadAllBytes(backup).SequenceEqual(original), "exact backup");
                Check(File.ReadAllBytes(path).SequenceEqual(Enc(source.Replace("2560", "1920").Replace("1440", "1080"))), "atomic replacement");
                Check(!Directory.EnumerateFiles(root, ".paws-uvars-*").Any(), "temp removed");
            }
            File.WriteAllText(path, source);
            var known = GameUserSettings.Load(path);
            foreach (var (key, value) in new[]
            {
                ("ResolutionX", "799"), ("ResolutionY", "599"), ("ResolutionX", "16385"), ("ResolutionY", "16385"),
                ("ResolutionX", "1e3"), ("ResolutionY", "1080.0"), ("FrameRateLimit", "0"), ("FrameRateLimit", "1001"),
                ("FrameRateLimit", "NaN"), ("AudioMainVolume", "-0.01"), ("AudioMainVolume", "1.01"),
                ("AudioMainVolume", "Infinity"), ("AudioMainVolume", "0,5"), ("ViewShowElapsedGameTime", "1"),
                ("HotkeyConfiguration", "changed"), ("CameraScaleZoom", "1")
            }) Throws<ArgumentException>(() => GameUserSettings.Validate(key, value), key + "=" + value);
            Throws<ArgumentException>(() => known.Edit(new Dictionary<string, string> { ["ResolutionX"] = "1920" }), "half resolution");
            var before = File.ReadAllBytes(path);
            Throws<GameUserSettingsRunningException>(() => known.Save(resolution, () => true), "running before write");
            Check(File.ReadAllBytes(path).SequenceEqual(before), "running preserves file");
            var probes = 0;
            Throws<GameUserSettingsRunningException>(() => known.Save(resolution, () => ++probes > 1), "game starts during save");
            Check(probes == 2 && File.ReadAllBytes(path).SequenceEqual(before), "second check protected file");
            Check(!Directory.EnumerateFiles(root, ".paws-uvars-*").Any(), "failed save temp removed");
            File.AppendAllText(path, ";; changed by game\r\n");
            var changed = File.ReadAllBytes(path);
            Throws<GameUserSettingsChangedException>(() => known.Save(resolution, () => false), "external edit rejected");
            Check(File.ReadAllBytes(path).SequenceEqual(changed), "external edit retained");
            File.WriteAllText(path, source);
            known = GameUserSettings.Load(path); probes = 0;
            Throws<GameUserSettingsChangedException>(() => known.Save(timer, () =>
            {
                if (++probes == 2) File.AppendAllText(path, ";; late change\r\n");
                return false;
            }), "external edit during save");
            Check(File.ReadAllText(path) == source + ";; late change\r\n", "late external edit retained");
            File.WriteAllText(path, source); known = GameUserSettings.Load(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Throws<IOException>(() => known.Save(resolution, () => false), "locked by game");
            Check(File.ReadAllText(path) == source, "lock retained bytes");
            var empty = Path.Combine(root, "missing", "UVars.tgi");
            var fresh = GameUserSettings.Load(empty);
            Check(!fresh.Exists && fresh.Value("ResolutionX") is null, "missing file state");
            Check(fresh.Save(new Dictionary<string, string>(), () => false) is null && !File.Exists(empty), "opening empty makes no file");
            fresh.Save(resolution, () => false);
            var created = GameUserSettings.Load(empty);
            Check(created.Number("ResolutionX") == 1920 && created.Number("ResolutionY") == 1080 && created.Value("AudioMainVolume") is null, "create only selected keys");
            Throws<GameUserSettingsChangedException>(() => fresh.Save(timer, () => false), "new file cannot overwrite another creation");
            File.Delete(empty);
            Throws<GameUserSettingsChangedException>(() => created.Save(timer, () => false), "deletion cannot restore stale snapshot");
            foreach (var malformed in new[]
            {
                "", "[Other]\n{\n}\n", "[Vars]\n{\nint ResolutionX = 1\nint ResolutionX = 2\n}\n",
                "[Vars]\n{\nfloat ResolutionX = 1920\n}\n", "[Vars]\n{\n}\n[Vars]\n{\n}\n",
                "[Vars]\n{\n[Child]\n{\n}\n}\n"
            })
            {
                File.WriteAllText(path, malformed);
                Throws<InvalidDataException>(() => GameUserSettings.Load(path), "malformed file");
                Check(File.ReadAllText(path) == malformed, "malformed file untouched");
            }
            File.WriteAllBytes(path, new byte[1024 * 1024 + 1]);
            Throws<InvalidDataException>(() => GameUserSettings.Load(path), "bounded input");
            // Copying a friend's launcher configuration must never import local game preferences.
            File.WriteAllText(path, source);
            var local = new UserSettings();
            var code = ConfigurationCode.Create(local);
            GameUserSettings.Load(path).Save(resolution, () => false);
            var settingsBytes = File.ReadAllBytes(path);
            ConfigurationCode.Apply(ConfigurationCode.Parse(code), local);
            Check(ConfigurationCode.Create(local) == code, "code independent of game settings");
            Check(File.ReadAllBytes(path).SequenceEqual(settingsBytes), "configuration apply leaves UVars untouched");
            foreach (var lang in new[] { "uk", "cs", "de", "fr" })
                Check(UiLanguages.English(lang, "Game settings") != "Game settings", "game settings translation " + lang);
            Console.WriteLine($"GAME USER SETTINGS PASS {count}: byte preservation, encodings, backups, races, locks, validation, configuration isolation");
            return count;
        }
        finally { Directory.Delete(root, true); }
    }
}
