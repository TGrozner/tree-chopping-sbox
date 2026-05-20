# Claude Code Stop hook
# Fires when Claude tries to end the turn. If any of the critical runtime
# files (Tree / SceneStarter / BeaverController / GameState / ShopArea)
# have been modified vs HEAD, run tools\selftest.ps1. Failure -> exit 2,
# blocks the stop and forces Claude to keep working.
#
# Enforces CLAUDE.md non-negotiable #2 ("relance le selftest apres TOUT
# changement dans Tree / GameState / SceneStarter.SpawnForest /
# BeaverController swing path") automatically.
#
# Escape hatch: stop_hook_active=true on the second Stop attempt -- after
# one block, Claude is allowed to stop on the next try so the user is not
# trapped if the selftest fails for an expected reason.

$ErrorActionPreference = "SilentlyContinue"

$stdinText = [Console]::In.ReadToEnd()
if ( -not $stdinText ) { exit 0 }

try {
    $payload = $stdinText | ConvertFrom-Json
} catch {
    exit 0
}

# Already blocked once; let Claude stop now. Avoids infinite loops when
# the selftest stays red for a reason Claude can't fix in-session.
if ( $payload.stop_hook_active -eq $true ) { exit 0 }

$projectDir = $env:CLAUDE_PROJECT_DIR
if ( -not $projectDir ) { $projectDir = "C:\dev\tree-chopping-sbox" }

# git status --porcelain reports modified+untracked files relative to
# the repo root. We check the staged AND unstaged columns. False positive
# possible if user had uncommitted critical-file changes before the session
# started -- acceptable given the user's phase-commit cadence.
$gitStatus = & git -C $projectDir status --porcelain 2>$null
if ( -not $gitStatus ) { exit 0 }

$critical = @('Tree.cs', 'SceneStarter.cs', 'BeaverController.cs', 'GameState.cs', 'ShopArea.cs')
$touched = $null
foreach ( $line in $gitStatus ) {
    foreach ( $f in $critical ) {
        if ( $line -match ('[\\/]' + [regex]::Escape($f) + '$') ) {
            $touched = $f
            break
        }
    }
    if ( $touched ) { break }
}

if ( -not $touched ) { exit 0 }

$selftest = Join-Path $projectDir "tools\selftest.ps1"
if ( -not (Test-Path $selftest) ) { exit 0 }

[Console]::Error.WriteLine("[hook:stop-selftest] $touched modified -- running selftest (up to 75s)...")

# -Seeds 1 keeps it to one iteration (~30-75s). Stop hook is meant to be
# a checkpoint, not a fuzz pass; the user can run -Seeds 5+ manually.
$out = & powershell -NoProfile -ExecutionPolicy Bypass -File $selftest -Seeds 1 -TimeoutSeconds 75 2>&1
$selfExit = $LASTEXITCODE

if ( $selfExit -eq 0 ) {
    [Console]::Error.WriteLine("[hook:stop-selftest] selftest PASS")
    exit 0
}

[Console]::Error.WriteLine("[hook:stop-selftest] selftest FAILED (exit $selfExit) -- $touched was modified, regression suspected. Fix before stopping, or call Stop again to override.")
[Console]::Error.WriteLine("--- selftest tail (last 40 lines) ---")
$out | Select-Object -Last 40 | ForEach-Object { [Console]::Error.WriteLine([string]$_) }
exit 2
