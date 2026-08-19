param(
    [string]$Configuration = "Release",
    [string]$ActPath = "C:\Program Files (x86)\Advanced Combat Tracker\Advanced Combat Tracker.exe"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "src\TargetMarkerOverlay\TargetMarkerOverlay.csproj"
if (-not (Test-Path -LiteralPath $ActPath)) {
    throw "ACT was not found: $ActPath`nSpecify Advanced Combat Tracker.exe with -ActPath."
}

dotnet build $project -c $Configuration -p:ActPath="$ActPath"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$output = Join-Path $PSScriptRoot "src\TargetMarkerOverlay\bin\$Configuration\net48\TargetMarkerOverlay.dll"
Write-Host "Build complete: $output"
