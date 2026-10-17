using System.IO.Compression;
using System.Text.Json;
using PawsPatchLauncher;

internal static class LauncherEvolutionTests
{
    public static async Task<int> RunAsync(string root)
    {
        int count = 0;
        void Check(bool ok, string reason) { count++; if (!ok) throw new Exception("Launcher evolution: " + reason); }
        var oldHash = new string('A',64); var newHash = new string('B',64);
        ChannelManifest Release(string hash) => new() { Channel = "stable", Game = new() { K2ExeSha256 = [hash] },
            Packages = [ new() { Id = "arcane-wars", Version = "1", Sha256 = oldHash }, new() { Id = "immortals", Version = "1", Sha256 = oldHash },
                new() { Id = "pure-fixes-runtime", Version = "1", Sha256 = oldHash }, new() { Id = "pure-fixes-data", Version = "1", Sha256 = oldHash, ExecutableIndependent = true } ] };
        var previous = Release(oldHash); var latest = Release(newHash);
        foreach (var activeMod in new[] { GameMod.Vanilla, GameMod.Immortals, GameMod.ArcaneWars })
        foreach (var chosenMod in new[] { GameMod.Vanilla, GameMod.Immortals, GameMod.ArcaneWars })
        {
            var active = new InstallState { AppliedSettings = new() { Mod = activeMod, Channel = "stable" }, BaseGameSha256 = oldHash,
                Modules = new() { ["runtime"] = new() { Enabled = true, Files = [new() { Path = "runtime.exe" }] } } };
            var selected = new UserSettings { Mod = chosenMod, Channel = "stable" };
            Check(GameCompatibilityPolicy.SelectedRuntimeNeedsUpdate(active,selected,newHash,null,true) == (activeMod==chosenMod),"Active runtime leaked into a different mod");
            Check(GameCompatibilityPolicy.CanOfferUpdate(true,true,true,previous,latest,chosenMod,newHash),"Stored incompatible mod missed its compatible update");
            Check(!GameCompatibilityPolicy.CanOfferUpdate(false,true,true,previous,latest,chosenMod,newHash),"Uninstalled mod received an update prompt");
            Check(!GameCompatibilityPolicy.CanOfferUpdate(true,true,false,latest,latest,chosenMod,newHash),"Already compatible mod retained update prompt");
            Check(!GameCompatibilityPolicy.CanOfferUpdate(true,true,true,previous,latest,chosenMod,oldHash),"Update does not support installed game");
            Check(!GameCompatibilityPolicy.CanOfferUpdate(true,true,true,previous,null,chosenMod,newHash),"Offline invented update");
        }
        var dir=Path.Combine(root,"bounded-action-journal"); var writer=new ActionJournalWriter(dir,1024);
        for (var i=0;i<50;i++) { writer.Write("ui.click","ApplySettingsButton"); await writer.FlushAsync(); }
        writer.Write("ui.click","secret@example.test\npassword"); await writer.FlushAsync();
        var logs=Directory.GetFiles(dir,"*.jsonl");
        Check(logs.Length==3,"Journal rotation must retain exactly three files");
        foreach(var file in logs) {
            Check(new FileInfo(file).Length<1600,"Journal file grew beyond a bounded record");
            foreach(var line in File.ReadAllLines(file)) {
                using var parsed=JsonDocument.Parse(line); Check(parsed.RootElement.TryGetProperty("utc",out _),"Missing action timestamp");
                Check(!line.Contains("secret@example")&&!line.Contains("password"),"Unsafe free text in journal");
            }
        }
        Check(File.ReadAllText(Path.Combine(dir,"user-actions.jsonl")).Contains("redacted"),"Unsafe journal field not redacted");
        if (ActivityStore.IsSmokeTest)
        {
            ActionJournal.Record("test.archive");
            var zip=Path.Combine(root,"launcher-diagnostics.zip");
            await DiagnosticsCollector.CreateLauncherOnlyAsync(zip);
            using var archive=ZipFile.OpenRead(zip);
            Check(archive.GetEntry("system-information.json") is not null,"Hardware info missing without a selected game");
            Check(archive.GetEntry("launcher/user-actions.jsonl") is not null,"Action journal omitted from diagnostics");
            Check(archive.GetEntry("files-sha256.txt") is not null,"Archive integrity manifest missing");
            Check(!archive.Entries.Any(e=>e.FullName.Contains("account",StringComparison.OrdinalIgnoreCase)||e.FullName.Contains("outbox",StringComparison.OrdinalIgnoreCase)),"Account store included in diagnostics");
        }
        return count;
    }
}
