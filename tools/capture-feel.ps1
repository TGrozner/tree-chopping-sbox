[CmdletBinding()]
param(
	[string]$OutputRoot = "C:\dev\tree-chopping-sbox\_captures",
	[string]$SboxLog = "C:\Program Files (x86)\Steam\steamapps\common\sbox\logs\sbox-dev.log",
	[string]$ScreenshotDir = "C:\Program Files (x86)\Steam\steamapps\common\sbox\screenshots",
	[int]$DurationSeconds = 8,
	[int]$FrameIntervalMs = 350,
	[int]$VideoFps = 8,
	[int]$SheetColumns = 6,
	[int]$SheetThumbWidth = 320,
	[string]$TargetKind = "Sapling",
	[switch]$ForceTooHard,
	[switch]$AudioLog,
	[switch]$NoRestart,
	[switch]$DryRun
)

$ErrorActionPreference = "Stop"

function Resolve-Tool {
	param( [string]$Name )
	$cmd = Get-Command $Name -ErrorAction SilentlyContinue
	if ( -not $cmd ) {
		Write-Error "$Name not found on PATH"
		exit 2
	}
	return $cmd.Source
}

function New-CaptureDir {
	param( [string]$Root )
	$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
	$dir = Join-Path $Root $stamp
	New-Item -ItemType Directory -Path $dir -Force | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $dir "frames") -Force | Out-Null
	return $dir
}

function Read-SharedLines {
	param( [string]$Path )
	if ( -not (Test-Path $Path) ) { return @() }

	$stream = [System.IO.File]::Open( $Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite )
	try {
		$reader = [System.IO.StreamReader]::new( $stream )
		try {
			$text = $reader.ReadToEnd()
		} finally {
			$reader.Dispose()
		}
	} finally {
		$stream.Dispose()
	}

	if ( [string]::IsNullOrEmpty( $text ) ) { return @() }
	return $text -split "`r?`n"
}

function Invoke-Bridge {
	param(
		[string]$Command,
		[hashtable]$Params = @{},
		[int]$TimeoutMs = 5000
	)

	$ipc = Join-Path $env:TEMP "sbox-bridge-ipc"
	if ( -not (Test-Path $ipc) ) {
		throw "Bridge IPC dir missing: $ipc"
	}

	$id = [guid]::NewGuid().ToString( "N" )
	$reqPath = Join-Path $ipc "req_$id.json"
	$resPath = Join-Path $ipc "res_$id.json"
	$payload = @{
		id = $id
		command = $Command
		params = $Params
	} | ConvertTo-Json -Depth 8 -Compress

	Set-Content -Path $reqPath -Value $payload -NoNewline -Encoding UTF8

	$deadline = (Get-Date).AddMilliseconds( $TimeoutMs )
	while ( (Get-Date) -lt $deadline -and -not (Test-Path $resPath) ) {
		Start-Sleep -Milliseconds 35
	}

	if ( -not (Test-Path $resPath) ) {
		throw "Bridge command timed out: $Command"
	}

	$response = Get-Content $resPath -Raw | ConvertFrom-Json
	Remove-Item $resPath -Force -ErrorAction SilentlyContinue

	if ( -not $response.success ) {
		throw "Bridge command failed: $Command :: $($response.error)"
	}

	return $response.data
}

function Find-FilmStripId {
	param( $Nodes )

	foreach ( $node in $Nodes ) {
		if ( $node.components -contains "FilmStrip" ) {
			return $node.id
		}
		if ( $node.children ) {
			$found = Find-FilmStripId $node.children
			if ( $found ) { return $found }
		}
	}

	return $null
}

function Get-LatestScreenshotAfter {
	param(
		[datetime]$After,
		[string]$Dir
	)

	if ( -not (Test-Path $Dir) ) { return $null }
	return Get-ChildItem $Dir -Filter "*.png" |
		Where-Object { $_.LastWriteTime -ge $After } |
		Sort-Object LastWriteTime -Descending |
		Select-Object -First 1
}

function Copy-NewScreenshot {
	param(
		[string]$Destination,
		[string]$Dir,
		[datetime]$Before
	)

	$deadline = (Get-Date).AddSeconds( 3 )
	do {
		$shot = Get-LatestScreenshotAfter -After $Before -Dir $Dir
		if ( $shot ) {
			Copy-Item -LiteralPath $shot.FullName -Destination $Destination -Force
			return $true
		}
		Start-Sleep -Milliseconds 60
	} while ( (Get-Date) -lt $deadline )

	return $false
}

$ffmpeg = Resolve-Tool "ffmpeg"
$ffprobe = Get-Command ffprobe -ErrorAction SilentlyContinue
$captureDir = New-CaptureDir $OutputRoot
$framesDir = Join-Path $captureDir "frames"
$video = Join-Path $captureDir "feel.mp4"
$sheet = Join-Path $captureDir "contact-sheet.jpg"
$events = Join-Path $captureDir "events.log"
$meta = Join-Path $captureDir "capture.json"

$logStartLine = 0
if ( Test-Path $SboxLog ) {
	$logStartLine = (Read-SharedLines $SboxLog).Length
}

$metaObj = [ordered]@{
	createdAt = (Get-Date).ToString( "o" )
	mode = "sbox-bridge-screenshot-video"
	durationSeconds = $DurationSeconds
	frameIntervalMs = $FrameIntervalMs
	videoFps = $VideoFps
	audioLog = [bool]$AudioLog
	targetKind = $TargetKind
	forceTooHard = [bool]$ForceTooHard
	video = $video
	contactSheet = $sheet
	events = $events
	frames = $framesDir
}
$metaObj | ConvertTo-Json -Depth 3 | Set-Content -Path $meta

Write-Host "[capture-feel] output: $captureDir" -ForegroundColor Cyan
Write-Host "[capture-feel] mode: bridge screenshots -> video/contact sheet" -ForegroundColor Cyan

if ( $DryRun ) {
	Invoke-Bridge "get_bridge_status" | Out-Null
	Write-Host "[capture-feel] dry-run only"
	exit 0
}

Invoke-Bridge "get_bridge_status" | Out-Null

if ( -not $NoRestart ) {
	try { Invoke-Bridge "stop_play" | Out-Null } catch { }
	Invoke-Bridge "start_play" | Out-Null
	Start-Sleep -Seconds 3
}

$hierarchy = Invoke-Bridge "get_scene_hierarchy" @{ maxDepth = 2 } 12000
$filmId = Find-FilmStripId $hierarchy.hierarchy
if ( -not $filmId ) {
	throw "FilmStrip GameObject not found"
}

Write-Host "[capture-feel] FilmStrip: $filmId" -ForegroundColor Cyan
Invoke-Bridge "set_runtime_property" @{ id = $filmId; component = "FilmStrip"; property = "Active"; value = "false" } | Out-Null
Invoke-Bridge "set_runtime_property" @{ id = $filmId; component = "FilmStrip"; property = "TargetKind"; value = $TargetKind } | Out-Null
Invoke-Bridge "set_runtime_property" @{ id = $filmId; component = "FilmStrip"; property = "ForceTooHard"; value = ([string][bool]$ForceTooHard).ToLowerInvariant() } | Out-Null
Invoke-Bridge "set_runtime_property" @{ id = $filmId; component = "FilmStrip"; property = "AudioLog"; value = ([string][bool]$AudioLog).ToLowerInvariant() } | Out-Null
Start-Sleep -Milliseconds 120
Invoke-Bridge "set_runtime_property" @{ id = $filmId; component = "FilmStrip"; property = "Active"; value = "true" } | Out-Null

$frame = 0
$phase = ""
$started = Get-Date
$deadline = $started.AddSeconds( $DurationSeconds )

while ( (Get-Date) -lt $deadline ) {
	$beforeShot = Get-Date
	Invoke-Bridge "take_screenshot" @{ path = "screenshots/capture-feel.png" } 8000 | Out-Null
	$dest = Join-Path $framesDir ( "frame_{0:D3}.png" -f $frame )
	if ( Copy-NewScreenshot -Destination $dest -Dir $ScreenshotDir -Before $beforeShot ) {
		$frame++
	}

	$phaseData = Invoke-Bridge "get_runtime_property" @{ id = $filmId; component = "FilmStrip"; property = "Phase" } 5000
	$phase = [string]$phaseData.value
	Write-Host "[capture-feel] frame=$frame phase=$phase"

	if ( $phase -eq "Done" -and $frame -ge 3 ) {
		break
	}

	Start-Sleep -Milliseconds $FrameIntervalMs
}

Invoke-Bridge "set_runtime_property" @{ id = $filmId; component = "FilmStrip"; property = "Active"; value = "false" } | Out-Null
Invoke-Bridge "set_runtime_property" @{ id = $filmId; component = "FilmStrip"; property = "AudioLog"; value = "false" } | Out-Null

$frameCount = (Get-ChildItem $framesDir -Filter "frame_*.png").Count
if ( $frameCount -lt 1 ) {
	throw "No frames captured"
}

Write-Host "[capture-feel] captured frames: $frameCount" -ForegroundColor Cyan
& $ffmpeg -hide_banner -loglevel warning -y `
	-framerate $VideoFps -i (Join-Path $framesDir "frame_%03d.png") `
	-c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p `
	$video
if ( $LASTEXITCODE -ne 0 -or -not (Test-Path $video) ) {
	Write-Error "[capture-feel] video build failed"
	exit 2
}

$sheetRows = [Math]::Max( 1, [Math]::Ceiling( $frameCount / [double]$SheetColumns ) )
$vf = "scale=${SheetThumbWidth}:-1,tile=${SheetColumns}x${sheetRows}"
& $ffmpeg -hide_banner -loglevel warning -y `
	-framerate $VideoFps -i (Join-Path $framesDir "frame_%03d.png") `
	-frames:v 1 -vf $vf -q:v 2 -update 1 $sheet
if ( $LASTEXITCODE -ne 0 -or -not (Test-Path $sheet) ) {
	Write-Error "[capture-feel] contact sheet failed"
	exit 2
}

if ( Test-Path $SboxLog ) {
	$lines = Read-SharedLines $SboxLog
	$newLines = if ( $logStartLine -lt $lines.Length ) {
		$lines[$logStartLine..($lines.Length - 1)]
	} else {
		@()
	}
	$newLines |
		Where-Object { $_ -match 'TC_FILM|TC_FEEL|TC_SFX|SceneStarter|Exception|FATAL|Error' } |
		Set-Content -Path $events
} else {
	"Missing sbox log: $SboxLog" | Set-Content -Path $events
}

if ( $ffprobe ) {
	$duration = & $ffprobe.Source -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 $video 2>$null
	Write-Host "[capture-feel] video duration: $duration"
}

Write-Host "[capture-feel] video:   $video" -ForegroundColor Green
Write-Host "[capture-feel] sheet:   $sheet" -ForegroundColor Green
Write-Host "[capture-feel] events:  $events" -ForegroundColor Green
