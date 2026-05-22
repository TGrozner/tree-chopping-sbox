# Headless self-test runner — parallèle par défaut.
#
# Spawns sbox-server.exe per seed with +tc_selftest 1. Chaque process redirect
# stdout/stderr en fichier. Poll periodically pour DONE marker.
#
# Validations par iteration :
#   1. Phase contract — chaque phase emit [TC_TEST] PHASE_OK <name>.
#   2. FAIL markers — toute ligne [TC_TEST] *FAIL* ou [TC_INV] FAIL.
#   3. Exception watchdog — Exception/FATAL/Unhandled hors allowlist.
#   4. PASS count — au moins 1 marker [TC_TEST] *PASS*.

[CmdletBinding()]
param(
    [string]$Sbox    = "C:\Program Files (x86)\Steam\steamapps\common\sbox\sbox-server.exe",
    [string]$Project = "C:\dev\tree-chopping-sbox\tree_chopping.sbproj",
    [int]   $TimeoutSeconds = 75,
    [int]   $Seeds = 1,
    [switch]$Sequential,
    [switch]$KeepLog
)

$ErrorActionPreference = "Stop"

if ( -not (Test-Path $Sbox) )    { Write-Error "sbox-server.exe missing at $Sbox"; exit 2 }
if ( -not (Test-Path $Project) ) { Write-Error "sbproj missing at $Project"; exit 2 }

function Get-ExpectedPhases {
    $repoRoot = Split-Path $PSScriptRoot -Parent
    $selfTestPath = Join-Path $repoRoot "Code\SelfTest.cs"
    if ( Test-Path $selfTestPath ) {
        $text = Get-Content $selfTestPath -Raw
        $match = [regex]::Match( $text, 'enum\s+Phase\s*\{(?<body>.*?)\}', [System.Text.RegularExpressions.RegexOptions]::Singleline )
        if ( $match.Success ) {
            $phases = @( $match.Groups['body'].Value -split ',' | ForEach-Object {
                ($_ -replace '//.*$', '').Trim()
            } | Where-Object { $_ -and $_ -ne 'Done' } )
            if ( $phases.Count -gt 0 ) { return $phases }
        }
    }

    @( 'Init', 'TestSpawnDistribution', 'Approach', 'Swing', 'Verify',
        'TestStump', 'TestSplit', 'TestBonusDrop', 'TestWoodPickup', 'TestPhysicsAutoSplit', 'TestStumpRespawn', 'TestCascadeDamage', 'TestCascadeCollision',
        'TestAxeTierGate', 'TestChopPowerScaling', 'TestImpactBelowMin', 'TestImpactZeroNoOp',
        'TestBackpackFull', 'TestSellFlush', 'TestSellStationEntry', 'TestPrestigeFormula', 'TestFallingImpactSplit', 'TestComboFinalDamage', 'TestMultiWoodTypes',
        'TestStatCounters', 'TestWoodCuttingLevel', 'TestPickupStackMerge', 'TestEnvWindSanity', 'TestStrictTooHard', 'TestTunablesValheimSanity',
        'TestImpactDamageScaling', 'TestWindDirRotation', 'TestRespawnJitterRange', 'TestWoodTypeDistribution', 'TestTreeShakeReset', 'TestCascadeShakeNoFell',
        'TestRollingLogsDamping', 'TestEnvWindDeterministic', 'TestWoodTypeMixSumsAll', 'TestHitDataDamage',
        'TestGameStateSanitize', 'TestStats', 'TestPrestige' )
}

$expectedPhases = Get-ExpectedPhases
$expectedPassCount = 1

$stderrNoiseAllowlist = @(
    'ERROR_FILEOPEN',
    'engine/R Error loading resource file',
    '^\s*$'
)

function Start-SelftestProcess {
    param([int]$Seed)

    $stamp = (Get-Date).ToString("yyyyMMdd-HHmmss-fff")
    $logOut = Join-Path $env:TEMP "tc-selftest-$stamp-s${Seed}.stdout.log"
    $logErr = Join-Path $env:TEMP "tc-selftest-$stamp-s${Seed}.stderr.log"
    Remove-Item $logOut, $logErr -ErrorAction SilentlyContinue

    $argList = @( '+game', "`"$Project`"", '+maxplayers', '1', '+tc_selftest', '1', '+tc_selftest_quick', '1' )
    if ( $Seed -gt 0 ) { $argList += @( '+tc_selftest_seed', "$Seed" ) }

    $proc = Start-Process -FilePath $Sbox -ArgumentList $argList -PassThru -NoNewWindow `
        -RedirectStandardOutput $logOut -RedirectStandardError $logErr

    [pscustomobject]@{
        Seed = $Seed
        Process = $proc
        StdoutPath = $logOut
        StderrPath = $logErr
        SawDone = $false
        Started = (Get-Date)
        DoneAt = $null
    }
}

function Wait-AllForDone {
    param([array]$Procs, [int]$TimeoutSeconds)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ( (Get-Date) -lt $deadline ) {
        $allDone = $true
        foreach ( $p in $Procs ) {
            if ( $p.SawDone ) { continue }
            if ( $p.Process.HasExited ) {
                $p.SawDone = $true
                $p.DoneAt = (Get-Date)
                continue
            }
            if ( Test-Path $p.StdoutPath ) {
                # sbox-server holds the stdout file open in write mode — we need
                # explicit FileShare.ReadWrite to read it concurrently. ReadAllText
                # uses FileShare.Read which would IOException here.
                $content = $null
                try {
                    $fs = [System.IO.File]::Open( $p.StdoutPath, [System.IO.FileMode]::Open,
                        [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite )
                    $reader = New-Object System.IO.StreamReader( $fs )
                    $content = $reader.ReadToEnd()
                    $reader.Close()
                    $fs.Close()
                } catch {}
                if ( $content -and $content -match '\[TC_TEST\] DONE' ) {
                    $p.SawDone = $true
                    $p.DoneAt = (Get-Date)
                    try { $p.Process.CloseMainWindow() | Out-Null } catch {}
                }
            }
            if ( -not $p.SawDone ) { $allDone = $false }
        }
        if ( $allDone ) { break }
        Start-Sleep -Milliseconds 400
    }

    # Force-kill anything still running.
    foreach ( $p in $Procs ) {
        if ( -not $p.Process.HasExited ) {
            try { Stop-Process -Id $p.Process.Id -Force -ErrorAction SilentlyContinue } catch {}
        }
    }

    # Give the kill a beat to flush stdout/stderr to file.
    Start-Sleep -Milliseconds 200
}

function Test-SelftestIteration {
    param([pscustomobject]$Proc)

    $stdout = @()
    $stderr = @()
    if ( Test-Path $Proc.StdoutPath ) { $stdout = Get-Content $Proc.StdoutPath -ErrorAction SilentlyContinue }
    if ( Test-Path $Proc.StderrPath ) { $stderr = Get-Content $Proc.StderrPath -ErrorAction SilentlyContinue }
    $merged = @($stdout) + @($stderr)
    $tcLines = $merged | Where-Object { $_ -match '\[TC_TEST\]' -or $_ -match '\[SceneStarter\]' -or $_ -match '\[TC_INV\]' }

    $observedPhases = @()
    foreach ( $line in $tcLines ) {
        if ( $line -match '\[TC_TEST\] PHASE_OK (\w+)' ) {
            $observedPhases += $Matches[1]
        }
    }
    $missingPhases = @( $expectedPhases | Where-Object { $observedPhases -notcontains $_ } )
    $phaseContractOk = ($missingPhases.Count -eq 0)

    $tcFails = @( $tcLines | Where-Object { $_ -match '\[TC_TEST\].*FAIL' } )
    $invFails = @( $tcLines | Where-Object { $_ -match '\[TC_INV\] FAIL' } )

    $allowRegex = ($stderrNoiseAllowlist -join '|')
    $exceptionLines = @( $stderr | Where-Object { $_ -notmatch $allowRegex } |
        Where-Object { $_ -match 'Exception|Unhandled|FATAL|StackTrace|System\.NullReferenceException|InvalidOperationException' } )

    $passCount = @( $tcLines | Where-Object { $_ -match '\[TC_TEST\].*PASS' } ).Count

    $elapsed = if ( $Proc.DoneAt ) { ($Proc.DoneAt - $Proc.Started).TotalSeconds } else { -1 }

    $ok = $Proc.SawDone -and $phaseContractOk -and ($tcFails.Count -eq 0) `
        -and ($invFails.Count -eq 0) -and ($exceptionLines.Count -eq 0) `
        -and ($passCount -ge $expectedPassCount)

    [pscustomobject]@{
        Seed = $Proc.Seed
        Ok = $ok
        SawDone = $Proc.SawDone
        PhaseContractOk = $phaseContractOk
        MissingPhases = $missingPhases
        TcFailCount = $tcFails.Count
        InvFailCount = $invFails.Count
        ExceptionCount = $exceptionLines.Count
        PassCount = $passCount
        ElapsedSeconds = $elapsed
        StdoutPath = $Proc.StdoutPath
        StderrPath = $Proc.StderrPath
        TcLines = $tcLines
        FirstException = if ( $exceptionLines.Count -gt 0 ) { $exceptionLines[0] } else { '' }
    }
}

# Seed list — same convention que l'ancienne version.
$seedList = @()
if ( $Seeds -le 1 ) {
    $seedList = @(0)
} else {
    for ( $i = 0; $i -lt $Seeds; $i++ ) {
        $seedList += (51800 + $i)
    }
}

$parallelLabel = if ( $Sequential -or $seedList.Count -eq 1 ) { 'sequential' } else { 'parallel' }
Write-Host "[harness] sbox-server: $Sbox"
Write-Host "[harness] project:     $Project"
Write-Host "[harness] mode:        $parallelLabel, $($seedList.Count) iter, $TimeoutSeconds`s timeout each"
Write-Host "[harness] seeds:       $($seedList -join ', ')"

$globalStart = (Get-Date)
$results = @()

if ( $Sequential -or $seedList.Count -eq 1 ) {
    foreach ( $seed in $seedList ) {
        $proc = Start-SelftestProcess -Seed $seed
        Wait-AllForDone -Procs @($proc) -TimeoutSeconds $TimeoutSeconds
        $results += (Test-SelftestIteration -Proc $proc)
    }
} else {
    # Parallel : spawn all, wait all, analyze all.
    Write-Host "[harness] spawning $($seedList.Count) processes in parallel..."
    $procs = @()
    foreach ( $seed in $seedList ) {
        $procs += (Start-SelftestProcess -Seed $seed)
    }
    Wait-AllForDone -Procs $procs -TimeoutSeconds $TimeoutSeconds
    foreach ( $p in $procs ) {
        $results += (Test-SelftestIteration -Proc $p)
    }
}

$globalElapsed = ((Get-Date) - $globalStart).TotalSeconds

# Per-iter dump.
foreach ( $r in $results ) {
    Write-Host ""
    Write-Host "================ [harness] seed=$($r.Seed) ================"
    if ( $r.TcLines.Count -gt 0 ) {
        $r.TcLines | ForEach-Object { Write-Host $_ }
    } else {
        Write-Host "(no [TC_TEST]/[TC_INV]/[SceneStarter] lines — check stdout : $($r.StdoutPath))"
    }
    Write-Host "---------------------------------------------------------"
    Write-Host "[harness] seed=$($r.Seed) summary:"
    Write-Host "  saw DONE          : $($r.SawDone)"
    Write-Host "  elapsed (s)       : $($r.ElapsedSeconds.ToString('F1'))"
    Write-Host "  phase contract    : $(if ($r.PhaseContractOk) { 'OK' } else { 'MISSING ' + ($r.MissingPhases -join ',') })"
    Write-Host "  TC_TEST FAILs     : $($r.TcFailCount)"
    Write-Host "  TC_INV  FAILs     : $($r.InvFailCount)"
    Write-Host "  exception lines   : $($r.ExceptionCount)"
    Write-Host "  PASS markers      : $($r.PassCount) (need $expectedPassCount)"
    if ( $r.FirstException ) { Write-Host "  first exception   : $($r.FirstException)" }
    Write-Host "  stdout log        : $($r.StdoutPath)"
}

Write-Host ""
Write-Host "================ [harness] overall result ================"
foreach ( $r in $results ) {
    $tag = if ( $r.Ok ) { 'PASS' } else { 'FAIL' }
    Write-Host ("  seed={0,-6} {1}  ({2:F1}s)" -f $r.Seed, $tag, $r.ElapsedSeconds)
}
Write-Host ("[harness] wall time: {0:F1}s ({1})" -f $globalElapsed, $parallelLabel)

$allOk = ($results | Where-Object { -not $_.Ok }).Count -eq 0
if ( $allOk ) {
    Write-Host "[harness] RESULT: PASS ($($results.Count) iterations)"
    exit 0
}

$timeoutOnly = ($results | Where-Object { -not $_.Ok -and -not $_.SawDone }).Count -eq $results.Count
if ( $timeoutOnly ) {
    Write-Host "[harness] RESULT: TIMEOUT (no iteration reached DONE within ${TimeoutSeconds}s)"
    exit 3
}

Write-Host "[harness] RESULT: FAIL"
exit 1
