$ErrorActionPreference = 'Stop'
function Assert-RigFixture([string]$Game) {
    $full = [IO.Path]::GetFullPath($Game).TrimEnd('\')
    $marker = Join-Path $full '.paws-multiplayer-fixture.json'
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) { throw 'Use Prepare-Rig.ps1 first; installed games are never accepted.' }
    $meta = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
    if ($meta.schema -ne 1 -or $meta.game -ne $full -or $meta.source -eq $full) { throw 'Invalid fixture identity.' }
    if ((Get-FileHash -LiteralPath (Join-Path $full 'k2.exe')).Hash -ne '1EB79BBB678668BE5A05F8C98103CD9988048490CE7C53C74C9A1D0813E9CD45') { throw 'Unsupported fixture executable.' }
    # Parent workspace junctions are allowed. Redirects inside our fixture are not.
    $pending = [Collections.Generic.Queue[string]]::new(); $pending.Enqueue($full)
    while ($pending.Count) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Dequeue() -Force) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Redirect inside fixture: $($entry.FullName)" }
            if ($entry.PSIsContainer) { $pending.Enqueue($entry.FullName) }
        }
    }
}
function Assert-FreshRigRun([string]$Game,[string]$RunDirectory) {
    $root = Join-Path (Split-Path ([IO.Path]::GetFullPath($Game))) 'runs'
    $run = [IO.Path]::GetFullPath($RunDirectory).TrimEnd('\')
    if ((Split-Path $run) -ne $root -or (Test-Path -LiteralPath $run)) { throw 'Each run requires a NEW direct child of the fixture runs directory.' }
    if ((Test-Path -LiteralPath $root) -and ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Redirected runs directory.' }
    New-Item -ItemType Directory -Path $root -Force | Out-Null
}
function Assert-RigResources {
    $os = Get-CimInstance Win32_OperatingSystem
    if ($os.FreePhysicalMemory -lt 4MB -or $os.FreeVirtualMemory -lt 6MB) { throw 'Need at least 4 GiB free RAM and 6 GiB available commit for two test copies.' }
    if (Get-Process k2 -ErrorAction SilentlyContinue) { throw 'Kohan II is already running; it will not be touched.' }
}
