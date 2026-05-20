# Headless self-test runner.
#
# Spawns sbox-server.exe with TREE_CHOPPING_SELFTEST=1; SceneStarter spawns a
# SelfTest Component which drives the full chop → log → chunk → wood pipeline
# without any input. We tail stdout, grep for [TC_TEST] lines, kill the process
# once we see DONE (or hit the timeout), and exit 0/1.
#
# Use this any time you change Tree / LogPiece / WoodChunk / WoodInventory /
# BeaverController-collection logic to confirm nothing regressed end-to-end.

[CmdletBinding()]
param(
    [string]$Sbox    = "C:\Program Files (x86)\Steam\steamapps\common\sbox\sbox-server.exe",
    [string]$Project = "C:\dev\tree-chopping-sbox\tree_chopping.sbproj",
    [int]   $TimeoutSeconds = 75,
    [switch]$KeepLog
)

$ErrorActionPreference = "Stop"

if ( -not (Test-Path $Sbox) )    { Write-Error "sbox-server.exe missing at $Sbox"; exit 2 }
if ( -not (Test-Path $Project) ) { Write-Error "sbproj missing at $Project"; exit 2 }

$stamp  = (Get-Date).ToString("yyyyMMdd-HHmmss")
$logOut = Join-Path $env:TEMP "tc-selftest-$stamp.stdout.log"
$logErr = Join-Path $env:TEMP "tc-selftest-$stamp.stderr.log"
Remove-Item $logOut,$logErr -ErrorAction SilentlyContinue

# SelfTest is enabled via the engine ConVar "tc_selftest" — set on the command
# line. System.Environment.* is on s&box's compiler whitelist deny-list, so env
# vars can't be read from game code; ConVar is the sandbox-friendly equivalent.
Write-Host "[harness] launching sbox-server (timeout ${TimeoutSeconds}s)"
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName  = $Sbox
$psi.Arguments = "+game `"$Project`" +maxplayers 1 +tc_selftest 1"
$psi.UseShellExecute        = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError  = $true
$psi.CreateNoWindow         = $true

$proc = New-Object System.Diagnostics.Process
$proc.StartInfo = $psi

# Hook both streams to file + buffer in memory so we can grep.
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
    # Snapshot to avoid "Collection was modified" while the event handler appends.
    $snapshot = @($collectedOut)
    for ( $i = $cursor; $i -lt $snapshot.Count; $i++ ) {
        $line = $snapshot[$i]
        if ( -not $sawSpawn -and $line -match '\[TC_TEST\] SelfTest spawned' ) {
            $sawSpawn = $true
            Write-Host "[harness] SelfTest spawned, watching for DONE"
        }
        if ( $line -match '\[TC_TEST\] DONE' ) {
            $sawDone = $true
            break
        }
    }
    $cursor = $snapshot.Count
    if ( $sawDone ) { break }
}

# Polite shutdown first, force after a beat.
if ( -not $proc.HasExited ) {
    try { $proc.CloseMainWindow() | Out-Null } catch { }
    Start-Sleep -Milliseconds 300
    if ( -not $proc.HasExited ) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}

# Drain a moment so any tail-end events flush.
Start-Sleep -Milliseconds 200
Get-EventSubscriber | Unregister-Event

# Merge stdout + stderr — Log.Error lands on stderr and previously hid Run2
# failures from the PASS/FAIL gate. Filter to the structured tags only; raw
# stderr noise (asset loader misses, etc.) stays in the log file.
$mergedLines = @($collectedOut) + @($collectedErr)
$tcLines = $mergedLines | Where-Object { $_ -match '\[TC_TEST\]' -or $_ -match '\[SceneStarter\]' }

Write-Host ""
Write-Host "------ [TC_TEST] / [SceneStarter] lines ------"
$tcLines | ForEach-Object { Write-Host $_ }
Write-Host "-----------------------------------------------"

# FAIL marker can appear as "FAIL", "RUN2_FAIL_*", "_FAIL_*" — any token
# containing FAIL on a [TC_TEST] line should fail the run. PASS stays strict
# ("[TC_TEST] PASS ...") so an informational mention of "pass" doesn't flip
# the result.
$pass = ($tcLines | Where-Object { $_ -match '\[TC_TEST\] PASS' }).Count -gt 0
$fail = ($tcLines | Where-Object { $_ -match '\[TC_TEST\].*FAIL' }).Count -gt 0

if ( -not $KeepLog ) {
    Write-Host "[harness] full stdout: $logOut"
} else {
    Write-Host "[harness] keep --> $logOut"
}

if ( $pass -and -not $fail ) {
    Write-Host "[harness] RESULT: PASS"
    exit 0
}

if ( $fail ) {
    Write-Host "[harness] RESULT: FAIL"
    exit 1
}

if ( -not $sawDone ) {
    Write-Host "[harness] RESULT: TIMEOUT after ${TimeoutSeconds}s (DONE not seen)"
    exit 3
}

Write-Host "[harness] RESULT: INDETERMINATE (DONE seen but no PASS/FAIL)"
exit 4
