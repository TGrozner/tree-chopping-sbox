# Headless test suite runner — runs TestSuite.cs which asserts on Tunables
# sanity, modifier metadata, milestone config, daily seed determinism,
# AudioBank no-throw, ChopParticles edge cases, camera config.
#
# Complements tools/selftest.ps1 (the cascade pipeline scenario). This runs
# the broader unit-style assertions. Exit codes:
#   0 = all PASS_<name>, no FAIL_<name>
#   1 = any FAIL_<name> observed
#   3 = TIMEOUT (DONE not seen within budget)
#   4 = INDETERMINATE (DONE seen but no PASS/FAIL emitted)

[CmdletBinding()]
param(
    [string]$Sbox    = "C:\Program Files (x86)\Steam\steamapps\common\sbox\sbox-server.exe",
    [string]$Project = "C:\dev\tree-chopping-sbox\tree_chopping.sbproj",
    [int]   $TimeoutSeconds = 45,
    [switch]$KeepLog
)

$ErrorActionPreference = "Stop"

if ( -not (Test-Path $Sbox) )    { Write-Error "sbox-server.exe missing at $Sbox"; exit 2 }
if ( -not (Test-Path $Project) ) { Write-Error "sbproj missing at $Project"; exit 2 }

$stamp  = (Get-Date).ToString("yyyyMMdd-HHmmss")
$logOut = Join-Path $env:TEMP "tc-testsuite-$stamp.stdout.log"
$logErr = Join-Path $env:TEMP "tc-testsuite-$stamp.stderr.log"
Remove-Item $logOut,$logErr -ErrorAction SilentlyContinue

Write-Host "[harness] launching sbox-server with tc_test_suite=1 (timeout ${TimeoutSeconds}s)"
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName  = $Sbox
$psi.Arguments = "+game `"$Project`" +maxplayers 1 +tc_test_suite 1"
$psi.UseShellExecute        = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError  = $true
$psi.CreateNoWindow         = $true

$proc = New-Object System.Diagnostics.Process
$proc.StartInfo = $psi

$collectedOut = New-Object System.Collections.Generic.List[string]
$collectedErr = New-Object System.Collections.Generic.List[string]
$outAction = {
    if ( $EventArgs.Data -ne $null ) {
        $line = [string]$EventArgs.Data
        Add-Content -Path $Event.MessageData.LogPath -Value $line
        $Event.MessageData.Buffer.Add($line) | Out-Null
    }
}
$errAction = $outAction
$null = Register-ObjectEvent -InputObject $proc -EventName OutputDataReceived `
    -Action $outAction `
    -MessageData ([pscustomobject]@{ LogPath = $logOut; Buffer = $collectedOut })
$null = Register-ObjectEvent -InputObject $proc -EventName ErrorDataReceived `
    -Action $errAction `
    -MessageData ([pscustomobject]@{ LogPath = $logErr; Buffer = $collectedErr })

[void]$proc.Start()
$proc.BeginOutputReadLine()
$proc.BeginErrorReadLine()

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$sawDone  = $false
$sawSpawn = $false
$cursor   = 0
while ( (Get-Date) -lt $deadline -and -not $proc.HasExited ) {
    Start-Sleep -Milliseconds 250
    $snapshot = @($collectedOut)
    for ( $i = $cursor; $i -lt $snapshot.Count; $i++ ) {
        $line = $snapshot[$i]
        if ( -not $sawSpawn -and $line -match '\[TC_TEST\] TestSuite spawned' ) {
            $sawSpawn = $true
            Write-Host "[harness] TestSuite spawned, watching for DONE"
        }
        if ( $line -match '\[TC_TEST\] DONE' ) {
            $sawDone = $true
            break
        }
    }
    $cursor = $snapshot.Count
    if ( $sawDone ) { break }
}

if ( -not $proc.HasExited ) {
    try { $proc.CloseMainWindow() | Out-Null } catch { }
    Start-Sleep -Milliseconds 300
    if ( -not $proc.HasExited ) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}

Start-Sleep -Milliseconds 200
Get-EventSubscriber | Unregister-Event

$tcLines = @($collectedOut) | Where-Object { $_ -match '\[TC_TEST\]' -or $_ -match '\[SceneStarter\]' }

Write-Host ""
Write-Host "------ [TC_TEST] / [SceneStarter] lines ------"
$tcLines | ForEach-Object { Write-Host $_ }
Write-Host "-----------------------------------------------"

$passCount = ($tcLines | Where-Object { $_ -match '\[TC_TEST\] PASS_' }).Count
$failCount = ($tcLines | Where-Object { $_ -match '\[TC_TEST\] FAIL_' }).Count
$suitePass = ($tcLines | Where-Object { $_ -match '\[TC_TEST\] SUITE_PASS' }).Count -gt 0
$suiteFail = ($tcLines | Where-Object { $_ -match '\[TC_TEST\] SUITE_FAIL' }).Count -gt 0

Write-Host "[harness] assertions: passed=$passCount failed=$failCount"

if ( -not $KeepLog ) {
    Write-Host "[harness] full stdout: $logOut"
} else {
    Write-Host "[harness] keep --> $logOut"
}

if ( $suitePass -and $failCount -eq 0 ) {
    Write-Host "[harness] RESULT: PASS"
    exit 0
}

if ( $suiteFail -or $failCount -gt 0 ) {
    Write-Host "[harness] RESULT: FAIL ($failCount assertion(s) failed)"
    exit 1
}

if ( -not $sawDone ) {
    Write-Host "[harness] RESULT: TIMEOUT after ${TimeoutSeconds}s (DONE not seen)"
    exit 3
}

Write-Host "[harness] RESULT: INDETERMINATE (DONE seen but no PASS/FAIL emitted)"
exit 4
