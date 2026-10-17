param([string]$Compiler,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $out -Force | Out-Null
& $Compiler -Wall -Werror (Join-Path $PSScriptRoot 'fixture.c') (Join-Path $PSScriptRoot 'debug-api.def') -o (Join-Path $out 'fixture.exe') -lkernel32
if($LASTEXITCODE -ne 0){throw 'Fixture build failed'}
& $Compiler -mwindows -Wall -Werror -DRECORDER_TEST (Join-Path $PSScriptRoot 'recorder.c') (Join-Path $PSScriptRoot 'debug-api.def') -o (Join-Path $out 'recorder-test.exe') -lkernel32 -lshell32
if($LASTEXITCODE -ne 0){throw 'Recorder test build failed'}
$results=@()
$unicode=Join-Path $out ('unicode-'+[char]0x416+[char]0x10d)
New-Item -ItemType Directory -Path $unicode -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $out 'fixture.exe') -Destination $unicode
foreach($case in @(
    @{Name='normal-no-gpu';Mode='normal';Module='missing-driver.dll';Dump=$false;Exit=0},
    @{Name='handled-outside';Mode='handled';Module='kernel32.dll';Dump=$false;Exit=0},
    @{Name='first-chance';Mode='handled';Module='fixture.exe';Dump=$true;Exit=0},
    @{Name='invalid-call';Mode='invalid-call';Module='missing-driver.dll';Dump=$true;Exit=0},
    @{Name='second-chance';Mode='unhandled';Module='kernel32.dll';Dump=$true;Exit=-1073741819},
    @{Name='original-filter';Mode='filter';Module='fixture.exe';Dump=$true;Exit=77},
    @{Name='recorder-exit';Mode='normal';Module='kernel32.dll';Dump=$false;Exit=0},
    @{Name='wrong-time';Mode='normal';Module='fixture.exe';Dump=$false;Exit=0;Reject=$true},
    @{Name='wrong-path';Mode='normal';Module='fixture.exe';Dump=$false;Exit=0;Reject=$true}
)){
    $fixturePath=Join-Path $unicode 'fixture.exe'
    $marker=Join-Path $out ($case.Name+'.marker')
    $fixture=Start-Process -FilePath $fixturePath -ArgumentList @($case.Mode,('"'+$marker+'"'),'2000') -WindowStyle Hidden -PassThru
    $stamp=$fixture.StartTime.ToUniversalTime().ToFileTimeUtc().ToString('X')
    if($case.Name -eq 'wrong-time'){$stamp='1'}
    $expected=$fixturePath
    if($case.Name -eq 'wrong-path'){$expected=Join-Path $out 'wrong.exe'}
    $stem=Join-Path $unicode ('log-'+$case.Name)
    $watcher=Start-Process -FilePath (Join-Path $out 'recorder-test.exe') -ArgumentList @($fixture.Id,$stamp,('"'+$expected+'"'),('"'+$stem+'"'),$case.Module) -WindowStyle Hidden -PassThru
    if($case.Name -eq 'recorder-exit'){
        $deadline=[datetime]::Now.AddSeconds(1.5)
        do{Start-Sleep -Milliseconds 30;$armed=(Test-Path -LiteralPath ($stem+'.log')) -and ((Get-Content -LiteralPath ($stem+'.log') -Raw) -match 'ARMED')}while(!$armed -and [datetime]::Now -lt $deadline)
        if(!$armed){throw 'Recorder did not arm'}
        Stop-Process -Id $watcher.Id
    }
    if(!$watcher.WaitForExit(15000)){throw ('Recorder timeout '+$case.Name)}
    if(!$fixture.WaitForExit(5000)){throw ('Fixture timeout '+$case.Name)}
    $dump=Test-Path -LiteralPath ($stem+'.dmp')
    if($dump -ne $case.Dump -or $fixture.ExitCode -ne $case.Exit){throw ('Wrong result '+$case.Name+' fixture='+$fixture.ExitCode)}
    if($case.Reject){if($watcher.ExitCode -ne 1 -or (Test-Path -LiteralPath ($stem+'.log'))){throw 'Identity guard failed'}}
    elseif($case.Name -ne 'recorder-exit' -and $watcher.ExitCode -ne 0){throw ('Recorder failure '+$case.Name)}
    if(!$case.Reject){
        $log=Get-Content -LiteralPath ($stem+'.log') -Raw
        if($case.Dump -and $log -notmatch 'DUMP result=1'){throw ('Dump failure '+$case.Name)}
        if($case.Name -eq 'second-chance' -and $log -notmatch 'first_chance=0'){throw 'Missing second chance'}
        if($case.Name -eq 'first-chance' -and $log -notmatch 'first_chance=1'){throw 'Missing first chance'}
        if($case.Name -eq 'invalid-call' -and ($log -notmatch 'INVALID_CALL address=00000062' -or $log -notmatch 'first_chance=1')){throw 'Missing invalid-call first chance'}
    }
    if($case.Name -eq 'original-filter' -and (Get-Content -LiteralPath $marker -Raw) -notmatch 'unhandled-filter'){throw 'Original crash handler suppressed'}
    $results+=@{test=$case.Name;passed=$true;targetExit=$fixture.ExitCode;dump=$dump;stem=$stem}
    Write-Output ('PASS '+$case.Name)
}
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $out 'verification.json') -Encoding utf8
