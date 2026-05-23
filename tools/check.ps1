param(
	[string]$ProjectDir = "C:\dev\tree-chopping-sbox",
	[int]$Seeds = 1,
	[int]$MaxParallel = 2,
	[int]$TimeoutSeconds = 75,
	[int]$MinPassMarkers = 52,
	[switch]$FullSelftest,
	[switch]$PhysicsOnly,
	[switch]$SkipSelftest
)

$ErrorActionPreference = "Stop"

$csproj = Join-Path $ProjectDir "Code\tree_chopping.csproj"
$selftest = Join-Path $ProjectDir "tools\selftest.ps1"

if ( -not (Test-Path $csproj) ) {
	Write-Error "Missing csproj at $csproj. Open sbox-dev once to regenerate it."
	exit 2
}

Write-Host "[check] dotnet build" -ForegroundColor Cyan
dotnet build $csproj --nologo --verbosity minimal
if ( $LASTEXITCODE -ne 0 ) {
	Write-Error "[check] build failed"
	exit $LASTEXITCODE
}

if ( $SkipSelftest ) {
	Write-Host "[check] PASS (selftest skipped)" -ForegroundColor Green
	exit 0
}

if ( -not (Test-Path $selftest) ) {
	Write-Error "Missing selftest at $selftest"
	exit 2
}

if ( $PhysicsOnly -and $MinPassMarkers -eq 52 ) { $MinPassMarkers = 13 }

Write-Host "[check] selftest seeds=$Seeds maxParallel=$MaxParallel timeout=${TimeoutSeconds}s minPassMarkers=$MinPassMarkers profile=$(if ($PhysicsOnly) { 'physics' } elseif ($FullSelftest) { 'full' } else { 'quick' })" -ForegroundColor Cyan
$selftestArgs = @{
	Seeds = $Seeds
	MaxParallel = $MaxParallel
	TimeoutSeconds = $TimeoutSeconds
	MinPassMarkers = $MinPassMarkers
}
if ( $FullSelftest ) { $selftestArgs.Full = $true }
if ( $PhysicsOnly ) { $selftestArgs.PhysicsOnly = $true }
& $selftest @selftestArgs
if ( $LASTEXITCODE -ne 0 ) {
	Write-Error "[check] selftest failed"
	exit $LASTEXITCODE
}

Write-Host "[check] PASS" -ForegroundColor Green
