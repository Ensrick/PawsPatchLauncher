#Requires -Version 7.0
# Use the current .NET icon decoder; legacy Windows PowerShell decodes PNG ICOs differently.
param([Parameter(Mandatory=$true)][string]$BuildDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class LauncherWindowIcons {
 [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint message,IntPtr kind,IntPtr unused,uint flags,uint timeout,out IntPtr result);
}
'@
$build=[IO.Path]::GetFullPath($BuildDirectory)
$exe=Join-Path $build 'PawsPatchLauncher.exe'
$sourceIcon=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\PawsPatchLauncher\Assets\K2Launcher.ico'))
$process=Start-Process -FilePath $exe -WorkingDirectory $build -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
$marker=Join-Path ([IO.Path]::GetTempPath()) "PawsPatchLauncherSmoke\$($process.Id)\window-ready.txt"
try {
 $deadline=[DateTime]::UtcNow.AddSeconds(30)
 while (!(Test-Path -LiteralPath $marker) -and !$process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200; $process.Refresh() }
 if (!(Test-Path -LiteralPath $marker)) { throw 'No smoke-test startup acknowledgement.' }
 $process.Refresh()
 if ($process.MainWindowHandle -eq 0) { throw 'Cannot access the test window on this desktop.' }
 foreach ($kind in 0,1) {
  $handle=[IntPtr]::Zero
  $success=[LauncherWindowIcons]::SendMessageTimeout($process.MainWindowHandle,0x7F,[IntPtr]$kind,[IntPtr]::Zero,2,2000,[ref]$handle)
  if ($success -eq 0 -or $handle -eq 0) { throw "Missing native window icon: $kind" }
  $actual=[Drawing.Icon]::FromHandle($handle).ToBitmap()
  $expectedIcon=[Drawing.Icon]::new($sourceIcon,$actual.Width,$actual.Height)
  $expected=$expectedIcon.ToBitmap()
  try {
   $mismatch=0
   for ($y=0; $y -lt $actual.Height; $y++) { for ($x=0; $x -lt $actual.Width; $x++) {
    $a=$actual.GetPixel($x,$y); $b=$expected.GetPixel($x,$y)
    if ($a.A -eq 0 -and $b.A -eq 0) { continue }
    if ([Math]::Abs([int]$a.A-[int]$b.A) -gt 2 -or [Math]::Abs([int]$a.R-[int]$b.R) -gt 3 -or [Math]::Abs([int]$a.G-[int]$b.G) -gt 3 -or [Math]::Abs([int]$a.B-[int]$b.B) -gt 3) { $mismatch++ }
   } }
   $actual.Save((Join-Path $build "window-icon-$kind.png"),[Drawing.Imaging.ImageFormat]::Png)
   if ($mismatch -gt ($actual.Width*$actual.Height*0.03)) { throw "Window icon does not match the approved asset: $kind, $mismatch pixels" }
   Write-Output "WINDOW ICON PASS: kind=$kind size=$($actual.Width) mismatch=$mismatch"
  } finally { $actual.Dispose(); $expected.Dispose(); $expectedIcon.Dispose() }
 }
} finally {
 if (!$process.HasExited) { $null=$process.CloseMainWindow(); if (!$process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() } }
 $process.Dispose()
}
