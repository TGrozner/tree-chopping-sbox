# Filmstrip orchestrator — helper for visual capture cycle.
#
# Two usage modes :
#
# 1) Cold-start fully scripted (no editor open yet) :
#    .\tools\filmstrip.ps1 -ColdStart
#    Launches sbox-dev.exe with +tc_filmstrip 1 so the FilmStrip director
#    auto-activates on the first Play. Use when you want a capture cycle
#    from a fresh editor boot.
#
# 2) Live editor (sbox-dev already open with the bridge dock visible) :
#    .\tools\filmstrip.ps1 -PrintProcedure
#    Prints the step-by-step bridge MCP procedure that the current agent
#    session* should run to drive the capture without restarting the editor.
#    This is the everyday path during dev — flip Active=true, take ~12
#    screenshots at 0.3s intervals, stop play, read each PNG.
#
# Either way, the C# side is FilmStrip.cs in the runtime assembly — a
# Component spawned unconditionally by SceneStarter. When Active=true it
# walks the player through a deterministic
#   Setup → Ready(0.6s idle linger) → Swing → Falling → Landed(1.5s linger)
# sequence. Phase + Elapsed are exposed as runtime properties for polling.

[CmdletBinding()]
param(
    [switch]$ColdStart,
    [switch]$PrintProcedure,
    [string]$SboxDev = "C:\Program Files (x86)\Steam\steamapps\common\sbox\sbox-dev.exe",
    [string]$Project = "C:\dev\tree-chopping-sbox\tree_chopping.sbproj"
)

$ErrorActionPreference = "Stop"

function Print-Procedure {
    Write-Host ""
    Write-Host "=== FilmStrip — bridge MCP procedure (Codex session) ===" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Pre-flight :"
    Write-Host "  1. sbox-dev.exe open on tree_chopping.sbproj"
    Write-Host "  2. Bridge dock visible (otherwise tools time out)"
    Write-Host "  3. mcp__sbox__get_bridge_status -> connected:true"
    Write-Host ""
    Write-Host "Capture cycle :"
    Write-Host "  a. mcp__sbox__is_playing                            -> if false, mcp__sbox__start_play"
    Write-Host "  b. mcp__sbox__get_scene_hierarchy                   -> grab FilmStrip GameObject id"
    Write-Host "  c. mcp__sbox__set_runtime_property                  -> FilmStrip.Active = true"
    Write-Host "  d. Loop, with screenshot+phase-poll in a SINGLE PARALLEL tool block :"
    Write-Host "       mcp__sbox__take_screenshot     +     mcp__sbox__get_runtime_property Phase"
    Write-Host "       (sequential = bridge latency 500-800ms each, max 4-5 captures)"
    Write-Host "       Param 'path' is IGNORED by the bridge -- all PNGs land in"
    Write-Host "       'C:\Program Files (x86)\Steam\steamapps\common\sbox\screenshots\sbox.<ts>.png'."
    Write-Host "       Read each PNG via Read tool to inspect (multimodal)."
    Write-Host "       Stop the loop once Phase == Done."
    Write-Host "  e. mcp__sbox__set_runtime_property                  -> FilmStrip.Active = false"
    Write-Host "  f. mcp__sbox__stop_play"
    Write-Host ""
    Write-Host "Frame analysis (Codex is multimodal — actually look at each frame) :"
    Write-Host "  - frame 1-2  : idle pose (clean before-shot)"
    Write-Host "  - frame 3-5  : WindUp anticipation (axe rising, FOV punch)"
    Write-Host "  - frame 6    : Impact (chips + hit-stop freeze + cam shake)"
    Write-Host "  - frame 7-10 : tree falling (torque ramp + canopy shedding)"
    Write-Host "  - frame 11-13: landing (log break sfx, dust puff, wood banner)"
    Write-Host "  - frame 14-15: after (banner decaying)"
    Write-Host ""
    Write-Host "If a phase reads wrong (snap-flat fall, missing chips, weak banner),"
    Write-Host "that frame number maps cleanly to a Tunables constant — adjust, "
    Write-Host "build (post-edit hook), filmstrip again."
    Write-Host ""
    Write-Host "See AGENTS.md -> 'Visual cycle - filmstrip' for the full cookbook."
}

if ( $PrintProcedure ) {
    Print-Procedure
    exit 0
}

if ( $ColdStart ) {
    if ( -not (Test-Path $SboxDev) ) { Write-Error "sbox-dev.exe missing at $SboxDev"; exit 2 }
    if ( -not (Test-Path $Project) ) { Write-Error "sbproj missing at $Project"; exit 2 }
    Write-Host "[filmstrip] cold-start: launching sbox-dev with +tc_filmstrip 1..." -ForegroundColor Cyan
    Start-Process -FilePath $SboxDev -ArgumentList @( '+game', "`"$Project`"", '+tc_filmstrip', '1' )
    Write-Host "[filmstrip] editor launched. After 20-25s boot, hit Play F5 — FilmStrip auto-activates."
    Write-Host "[filmstrip] then have the Codex session take screenshots via mcp__sbox__take_screenshot."
    Print-Procedure
    exit 0
}

# Default : print procedure (matches the everyday "editor already open" path)
Print-Procedure
exit 0
