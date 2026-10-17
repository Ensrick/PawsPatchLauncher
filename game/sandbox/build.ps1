# Builds the sandbox stand-in shfolder.dll and copies it to the launcher's embedded asset
# (src/PawsPatchLauncher/Assets/Sandbox/shfolder.dll). Never starts or installs the game.
#   -NativeCompiler   x86 TCC 0.9.27, as for the other native modules (the build is byte-for-byte reproducible)
param([Parameter(Mandatory = $true)][string]$NativeCompiler)
$ErrorActionPreference = 'Stop'
$output = Join-Path ([IO.Path]::GetTempPath()) ('k2-sandbox-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $output | Out-Null
try {
    $dll = Join-Path $output 'shfolder.dll'
    & $NativeCompiler -shared -Wall -Werror (Join-Path $PSScriptRoot 'sandbox.c') -o $dll -luser32
    if ($LASTEXITCODE -ne 0) { throw 'Sandbox DLL build failed.' }
    $asset = Join-Path $PSScriptRoot '..\..\src\PawsPatchLauncher\Assets\Sandbox\shfolder.dll'
    Copy-Item -LiteralPath $dll -Destination $asset -Force
    "built $asset ($((Get-Item -LiteralPath $asset).Length) bytes, SHA256 $((Get-FileHash -LiteralPath $asset).Hash))"
}
finally { Remove-Item -LiteralPath $output -Recurse -Force }
