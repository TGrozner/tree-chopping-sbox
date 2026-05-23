[CmdletBinding()]
param(
	[string[]]$Kinds = @( "Sapling", "Normal", "Veteran", "Brittle" ),
	[int]$DurationSeconds = 24,
	[int]$FrameIntervalMs = 240,
	[string]$OutputRoot = "",
	[switch]$AudioLog
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
if ( [string]::IsNullOrWhiteSpace( $OutputRoot ) ) {
	$OutputRoot = Join-Path $root "_captures"
}
$captureFeel = Join-Path $PSScriptRoot "capture-feel.ps1"
if ( -not (Test-Path $captureFeel) ) {
	throw "Missing capture-feel.ps1 at $captureFeel"
}

function Get-LastCounter {
	param(
		[string]$Text,
		[string]$Name
	)
	$matches = [regex]::Matches( $Text, "$Name=(\d+)" )
	if ( $matches.Count -eq 0 ) { return 0 }
	return [int]$matches[$matches.Count - 1].Groups[1].Value
}

function Get-LastFloat {
	param(
		[string]$Text,
		[string]$Name
	)
	$matches = [regex]::Matches( $Text, "$Name=(-?\d+(?:\.\d+)?)" )
	if ( $matches.Count -eq 0 ) { return 0.0 }
	return [double]::Parse( $matches[$matches.Count - 1].Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture )
}

function Get-SummaryText {
	param(
		[string]$Text
	)
	$summaryLines = @( $Text -split "`r?`n" | Where-Object { $_ -match '\[TC_FEEL_SUMMARY\]' } )
	if ( $summaryLines.Count -eq 0 ) { return $Text }
	return $summaryLines[$summaryLines.Count - 1]
}

$results = @()
foreach ( $kind in $Kinds ) {
	Write-Host "[feel-suite] capture $kind" -ForegroundColor Cyan
	$captureParams = @{
		TargetKind = $kind
		DurationSeconds = $DurationSeconds
		FrameIntervalMs = $FrameIntervalMs
		OutputRoot = $OutputRoot
	}
	if ( $AudioLog ) { $captureParams.AudioLog = $true }

	$beforeCapture = Get-Date
	$output = & $captureFeel @captureParams 2>&1
	$exitCode = $LASTEXITCODE
	$output | ForEach-Object { Write-Host $_ }
	if ( $exitCode -ne 0 ) {
		throw "capture-feel failed for $kind (exit $exitCode)"
	}

	$captureDir = Get-ChildItem -Path $OutputRoot -Directory |
		Where-Object { $_.LastWriteTime -ge $beforeCapture.AddSeconds( -2 ) } |
		Sort-Object LastWriteTime -Descending |
		Select-Object -First 1
	if ( -not $captureDir ) {
		throw "Could not parse capture directory for $kind"
	}
	$captureDir = $captureDir.FullName
	$events = Join-Path $captureDir "events.log"
	if ( -not (Test-Path $events) ) {
		throw "Missing events.log for $kind at $events"
	}

	$eventText = Get-Content -Path $events -Raw
	$summaryText = Get-SummaryText $eventText
	$hits = Get-LastCounter $eventText "hits"
	$misses = Get-LastCounter $eventText "misses"
	$physicsAnomalies = Get-LastCounter $summaryText "physAnom"
	$worstPen = Get-LastFloat $summaryText "worstPen"
	$worstFloat = Get-LastFloat $summaryText "worstFloat"
	$worstUpDot = Get-LastFloat $summaryText "worstUpDot"
	$maxLogSpeed = Get-LastFloat $summaryText "maxSpeed"
	$maxLogAngular = Get-LastFloat $summaryText "maxAng"
	$done = $eventText -match 'phase=Done'
	$pickup = $eventText -match 'PICKUP COMPLETE'
	$bad = $eventText -match '\[TC_TEST\] FAIL|Exception|FATAL'
	$anomalyLines = @( $eventText -split "`r?`n" | Where-Object { $_ -match '\[TC_FEEL_ANOMALY\]' } )

	if ( -not $done -or -not $pickup -or $misses -ne 0 -or $bad -or $physicsAnomalies -gt 0 -or $anomalyLines.Count -gt 0 ) {
		$anomalySummary = if ( $anomalyLines.Count -gt 0 ) { ($anomalyLines | Select-Object -First 4) -join " | " } else { "" }
		throw "Feel capture failed for ${kind}: done=$done pickup=$pickup hits=$hits misses=$misses bad=$bad physAnom=$physicsAnomalies worstPen=$worstPen worstFloat=$worstFloat worstUpDot=$worstUpDot maxSpeed=$maxLogSpeed maxAng=$maxLogAngular dir=$captureDir $anomalySummary"
	}

	$results += [pscustomobject]@{
		Kind = $kind
		Hits = $hits
		Misses = $misses
		Phys = $physicsAnomalies
		Pen = $worstPen
		Float = $worstFloat
		UpDot = $worstUpDot
		Speed = $maxLogSpeed
		Ang = $maxLogAngular
		Capture = $captureDir
	}
}

Write-Host "[feel-suite] PASS" -ForegroundColor Green
$results | Format-Table -AutoSize
