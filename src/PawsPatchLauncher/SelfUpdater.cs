using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text;

namespace PawsPatchLauncher;

public static class SelfUpdater
{
    public static Version CurrentVersion
        => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Off in this personal build: Paw's releases reach it by merging into the branch, never by replacing the exe.</summary>
    public static bool Enabled => false;

    public static bool IsNewer(string candidate)
        => Enabled && Version.TryParse(candidate, out var parsed) && parsed > CurrentVersion;

    public static bool IsBlocked(string hash) => File.Exists(BlockedPath) && File.ReadAllText(BlockedPath).Trim().Equals(hash, StringComparison.OrdinalIgnoreCase);
    public static string BlockedPath => Path.Combine(ActivityStore.Root, "failed-launcher-sha256.txt");

    /// <summary>The caller saves its settings and placement, then closes only after this handshake succeeds.</summary>
    public static async Task RestartThroughStartupAsync(CancellationToken cancellationToken = default)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The launcher executable path is unavailable.");
        using var current = Process.GetCurrentProcess();
        var token = Guid.NewGuid().ToString("N");
        using var ready = new NamedPipeServerStream(RestartPipeName(token), PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var next = Process.Start(BuildRestartStartInfo(executable, current.Id, current.StartTime.ToUniversalTime().Ticks,
            token, ActivityStore.LocalTestProfile)) ?? throw new IOException("Cannot restart the launcher.");
        try
        {
            var connection = ready.WaitForConnectionAsync(timeout.Token);
            var exited = next.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(connection, exited) == exited)
            {
                await exited;
                throw new IOException("The restarted launcher exited before it was ready.");
            }
            await connection;
            var response = new byte[1];
            if (await ready.ReadAsync(response, timeout.Token) != 1 || response[0] != 0x52 || next.HasExited)
                throw new IOException("The restarted launcher did not confirm readiness.");
        }
        catch (Exception error)
        {
            // This is only the child we just started. The original launcher remains open on failure.
            try { if (!next.HasExited) next.Kill(); } catch { }
            if (error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                throw new IOException("The restarted launcher did not become ready in time.", error);
            throw;
        }
        finally { timeout.Cancel(); }
    }

    public static ProcessStartInfo BuildRestartStartInfo(string executable, int parentId, long parentStartTicks,
        string token, string? testProfile)
    {
        if (parentId <= 0 || parentStartTicks <= 0 || !Guid.TryParseExact(token, "N", out _))
            throw new ArgumentException("Invalid launcher restart identity.");
        var path = Path.GetFullPath(executable);
        var start = new ProcessStartInfo(path)
        {
            WorkingDirectory = Path.GetDirectoryName(path)!, UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add("--restart-parent=" + parentId + ":" + parentStartTicks);
        start.ArgumentList.Add("--restart-token=" + token);
        if (!string.IsNullOrWhiteSpace(testProfile)) start.ArgumentList.Add("--test-profile=" + Path.GetFullPath(testProfile));
        // In particular, never forward --skip-startup-update or an obsolete --update-health token.
        return start;
    }

    /// <summary>Runs before the single-instance mutex so the replacement can wait for its predecessor.</summary>
    public static async Task<bool> WaitForRestartHandoffAsync(string[] arguments)
    {
        var parentArgument = arguments.FirstOrDefault(a => a.StartsWith("--restart-parent=", StringComparison.Ordinal));
        var tokenArgument = arguments.FirstOrDefault(a => a.StartsWith("--restart-token=", StringComparison.Ordinal));
        if (parentArgument is null && tokenArgument is null) return true;
        var identity = parentArgument?["--restart-parent=".Length..].Split(':');
        var token = tokenArgument?["--restart-token=".Length..];
        if (identity?.Length != 2 || !int.TryParse(identity[0], out var parentId) || parentId <= 0
            || !long.TryParse(identity[1], out var parentTicks) || parentTicks <= 0 || token is null
            || !Guid.TryParseExact(token, "N", out _))
            throw new InvalidDataException("Invalid launcher restart handoff.");
        using var parent = Process.GetProcessById(parentId);
        if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != parentTicks ||
            !string.Equals(Path.GetFullPath(parent.MainModule!.FileName), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The launcher restart predecessor does not match.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var ready = new NamedPipeClientStream(".", RestartPipeName(token), PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await ready.ConnectAsync(10000, timeout.Token);
        await ready.WriteAsync(new byte[] { 0x52 }, timeout.Token);
        await ready.FlushAsync(timeout.Token);
        await parent.WaitForExitAsync(timeout.Token);
        return true;
    }

    private static string RestartPipeName(string token) => "PawsPatchLauncher-Restart-" + token;

    public static void AcknowledgeStartup()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--update-health");
        if (index < 0 || index + 1 >= args.Length || !Guid.TryParseExact(args[index + 1], "N", out _)) return;
        var token = args[index + 1];
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, ".paw-update-" + token + ".ok"), token);
    }

    public static void ScheduleReplacement(string downloadedExecutable, string hash)
    {
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("The launcher executable path is unavailable.");
        if (!File.Exists(downloadedExecutable)) throw new FileNotFoundException("The downloaded launcher is missing.", downloadedExecutable);
        var currentDirectory = Path.GetDirectoryName(current) ?? throw new InvalidOperationException("The launcher directory is unavailable.");
        var staged = Path.Combine(currentDirectory, Path.GetFileName(current) + ".new");
        File.Copy(downloadedExecutable, staged, true);

        Directory.CreateDirectory(ActivityStore.Root);
        var profile = ActivityStore.LocalTestProfile;
        var profilePath = profile is null ? null : Path.GetFullPath(profile);
        var arguments = profilePath is null ? "" : " --test-profile=\"" + profilePath + (profilePath.EndsWith('\\') ? "\\" : "") + "\"";
        var script = BuildScript(current, staged, Environment.ProcessId, hash, ActivityStore.Root, Guid.NewGuid().ToString("N"), startupArguments: arguments);
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        _ = Process.Start(start) ?? throw new IOException("Cannot start the update recovery helper.");
    }

    public static string BuildScript(string current, string staged, int pid, string hash, string logRoot, string token, int timeoutSeconds = 60, string startupArguments = "")
    {
        static string Q(string value) => "'" + value.Replace("'", "''") + "'";
        // Windows PowerShell uses .NET Framework: File.Exists silently returns
        // false past MAX_PATH. Keep native launch paths normal, extend all I/O paths.
        static string Extended(string value)
        {
            var path = Path.GetFullPath(value);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
            return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
        }
        return $$"""
        $ErrorActionPreference = 'Stop'
        $launchTarget = {{Q(Path.GetFullPath(current))}}
        $launchFolder = [IO.Path]::GetDirectoryName($launchTarget)
        $target = {{Q(Extended(current))}}
        $staged = {{Q(Extended(staged))}}
        $folder = [IO.Path]::GetDirectoryName($target)
        $backup = $target + '.previous'
        $failed = $target + '.failed'
        $ack = [IO.Path]::Combine($folder, '.paw-update-{{token}}.ok')
        $logRoot = {{Q(Extended(logRoot))}}
        $replaced = $false
        $candidate = $null
        function Start-Launcher([string]$arguments) {
            $info = New-Object System.Diagnostics.ProcessStartInfo
            $info.FileName = $launchTarget
            $info.WorkingDirectory = $launchFolder
            $info.Arguments = $arguments + {{Q(" --skip-startup-update" + startupArguments)}}
            $info.UseShellExecute = $false
            $info.CreateNoWindow = $true
            return [Diagnostics.Process]::Start($info)
        }
        try {
            $old = $null
            try { $old = [Diagnostics.Process]::GetProcessById({{pid}}) } catch [ArgumentException] { }
            if ($null -ne $old -and -not $old.WaitForExit(60000)) { throw 'The original launcher did not exit.' }
            if ([IO.File]::Exists($ack)) { [IO.File]::Delete($ack) }
            $sha = [Security.Cryptography.SHA256]::Create()
            $stream = [IO.File]::OpenRead($staged)
            try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } finally { $stream.Dispose(); $sha.Dispose() }
            if ($actual -ne {{Q(hash)}}) { throw 'Staged launcher hash mismatch.' }
            if ([IO.File]::Exists($backup)) { [IO.File]::Delete($backup) }
            [IO.File]::Replace($staged, $target, $backup)
            $replaced = $true
            $candidate = Start-Launcher '--update-health {{token}}'
            $until = [DateTime]::UtcNow.AddSeconds({{timeoutSeconds}})
            $healthy = $false
            while ([DateTime]::UtcNow -lt $until) {
                $candidate.Refresh()
                if ($candidate.HasExited) { break }
                if ([IO.File]::Exists($ack) -and [IO.File]::ReadAllText($ack) -eq '{{token}}') { $healthy = $true; break }
                [Threading.Thread]::Sleep(250)
            }
            if (-not $healthy) { throw 'The new launcher did not confirm a working window.' }
            [IO.File]::Delete($ack)
            [IO.File]::WriteAllText([IO.Path]::Combine($logRoot, 'self-update.log'), 'Update confirmed: ' + {{Q(hash)}})
        } catch {
            [void][IO.Directory]::CreateDirectory($logRoot)
            [IO.File]::WriteAllText([IO.Path]::Combine($logRoot, 'self-update.log'), $_.ToString())
            if ($replaced) {
                if ($null -ne $candidate -and -not $candidate.HasExited) { $candidate.Kill(); $candidate.WaitForExit() }
                if ([IO.File]::Exists($failed)) { [IO.File]::Delete($failed) }
                [IO.File]::Replace($backup, $target, $failed)
                [IO.File]::WriteAllText([IO.Path]::Combine($logRoot, 'failed-launcher-sha256.txt'), {{Q(hash)}})
                [IO.File]::WriteAllText([IO.Path]::Combine($logRoot, 'update-rollback.txt'), 'The launcher update failed. The previous executable was restored.')
                [void](Start-Launcher '')
            } else {
                # A staging/replacement failure must not leave the user without a launcher.
                [void](Start-Launcher '')
            }
        }
        """;
    }
}
