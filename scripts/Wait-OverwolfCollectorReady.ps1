[CmdletBinding()]
param(
    [string]$ProbePath,
    [string]$CollectorRoot,
    [int]$TimeoutSeconds = 300,
    [int]$PollSeconds = 2
)

$ErrorActionPreference = 'Stop'
if ($TimeoutSeconds -lt 0) { throw 'TimeoutSeconds must be non-negative.' }
if ($PollSeconds -lt 1) { throw 'PollSeconds must be at least one second.' }
if ([string]::IsNullOrWhiteSpace($CollectorRoot)) { $CollectorRoot = Join-Path $PSScriptRoot '..\artifacts\collector-overwolf' }
if ([string]::IsNullOrWhiteSpace($ProbePath)) {
    $artifactRoot = Join-Path $PSScriptRoot '..\artifacts'
    $latest = Get-ChildItem -LiteralPath $artifactRoot -Directory -Filter 'MyFrame-*-win-x64' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $latest) { throw 'MCP_DISTRIBUTION_NOT_FOUND' }
    $ProbePath = Join-Path $latest.FullName 'MyFrame.Sync.exe'
}
$probe = (Resolve-Path -LiteralPath $ProbePath -ErrorAction Stop).Path
$collector = (Resolve-Path -LiteralPath $CollectorRoot -ErrorAction Stop).Path
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
$last = $null

do {
    $raw = & $probe --overwolf-inventory-probe
    $probeExit = $LASTEXITCODE
    try { $last = $raw | ConvertFrom-Json -ErrorAction Stop }
    catch { throw "Collector probe returned invalid JSON: $raw" }
    if ($probeExit -notin @(0, 1)) { throw "Collector probe failed with exit code $probeExit." }

    $summary = [ordered]@{
        state = $last.state
        overwolfRunning = $last.overwolfRunning
        warframeRunning = $last.warframeRunning
        heartbeatFresh = $last.heartbeatFresh
        validMarkers = $last.validMarkers
        invalidMarkers = $last.invalidMarkers
    }
    $summary | ConvertTo-Json -Compress

    if ($last.state -eq 'ready' -and $last.heartbeatFresh -eq $true -and [int]$last.validMarkers -gt 0) {
        'OVERWOLF_COLLECTOR_READY=1'
        exit 0
    }
    if ([DateTimeOffset]::UtcNow -ge $deadline) { break }
    Start-Sleep -Seconds $PollSeconds
} while ($true)

'OVERWOLF_COLLECTOR_READY=0'
'OVERWOLF_COLLECTOR_TIMEOUT=1'
'OVERWOLF_ACTION=load-unpacked-extension'
"OVERWOLF_EXTENSION_PATH=$collector"
exit 2
