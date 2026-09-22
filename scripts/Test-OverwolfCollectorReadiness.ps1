[CmdletBinding()]
param(
    [string]$CollectorRoot,
    [string]$CaptureDirectory
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($CollectorRoot)) { $CollectorRoot = Join-Path $PSScriptRoot '..\artifacts\collector-overwolf' }
if ([string]::IsNullOrWhiteSpace($CaptureDirectory)) { $CaptureDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MyFrame\captures' }
$resolvedCollector = if (Test-Path -LiteralPath $CollectorRoot) { (Resolve-Path -LiteralPath $CollectorRoot).Path } else { $null }
$overwolf = @(Get-Process -Name Overwolf -ErrorAction SilentlyContinue)
$warframe = @(Get-Process -Name Warframe.x64 -ErrorAction SilentlyContinue)
$manifest = $null -ne $resolvedCollector -and (Test-Path -LiteralPath (Join-Path $resolvedCollector 'manifest.json'))
$markers = if (Test-Path -LiteralPath $CaptureDirectory) {
    @(Get-ChildItem -LiteralPath $CaptureDirectory -Filter '*.ready.json' -File -ErrorAction SilentlyContinue)
} else { @() }
$heartbeat = $false
$heartbeatPath = Join-Path $CaptureDirectory 'collector-status.json'
if (Test-Path -LiteralPath $heartbeatPath -PathType Leaf) {
    try {
        $status = Get-Content -LiteralPath $heartbeatPath -Raw | ConvertFrom-Json
        $timestamp = [DateTimeOffset]::Parse([string]$status.timestampUtc, [Globalization.CultureInfo]::InvariantCulture)
        $heartbeat = $status.schemaVersion -eq 1 -and $status.kind -eq 'my-frame-collector' -and
            $status.state -eq 'started' -and (Get-Date).ToUniversalTime() - $timestamp.UtcDateTime -lt [TimeSpan]::FromHours(1)
    } catch { $heartbeat = $false }
}
$overwolfLogRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Overwolf\Log'
$loaded = $false
if (Test-Path -LiteralPath $overwolfLogRoot) {
    $loaded = @(Get-ChildItem -LiteralPath $overwolfLogRoot -Filter '*.log' -File -Recurse -ErrorAction SilentlyContinue |
        Select-String -SimpleMatch -Pattern 'My Frame Collector Dev' -ErrorAction SilentlyContinue).Count -gt 0
}

Write-Output "OVERWOLF_PROCESS=$([int]($overwolf.Count -gt 0))"
Write-Output "WARFRAME_PROCESS=$([int]($warframe.Count -gt 0))"
Write-Output "COLLECTOR_MANIFEST=$([int]$manifest)"
Write-Output "COLLECTOR_EXTENSION_LOGGED=$([int]$loaded)"
Write-Output "COLLECTOR_HEARTBEAT=$([int]$heartbeat)"
Write-Output "COLLECTOR_RUNTIME_EVIDENCE=$([int]($loaded -or $heartbeat))"
Write-Output "CAPTURE_MARKERS=$($markers.Count)"
Write-Output "COLLECTOR_ROOT=$resolvedCollector"
Write-Output "CAPTURE_DIRECTORY=$CaptureDirectory"

if ($overwolf.Count -gt 0 -and $warframe.Count -gt 0 -and $manifest -and ($loaded -or $heartbeat) -and $markers.Count -gt 0) {
    Write-Output 'OVERWOLF_COLLECTOR_READY=1'
    exit 0
}
Write-Output 'OVERWOLF_COLLECTOR_READY=0'
if (-not $manifest) {
    Write-Output 'OVERWOLF_ACTION=build-collector-package'
} elseif (-not ($loaded -or $heartbeat)) {
    Write-Output 'OVERWOLF_ACTION=load-unpacked-extension'
    Write-Output "OVERWOLF_EXTENSION_PATH=$resolvedCollector"
} elseif ($markers.Count -eq 0) {
    Write-Output 'OVERWOLF_ACTION=exercise-warframe-event-and-wait-for-marker'
}
exit 1
