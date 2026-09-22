[CmdletBinding()]
param(
    [string]$CollectorRoot,
    [switch]$Launch,
    [switch]$EnableDevTools
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($CollectorRoot)) { $CollectorRoot = Join-Path $PSScriptRoot '..\artifacts\collector-overwolf' }
$collector = (Resolve-Path -LiteralPath $CollectorRoot).Path
$packageTest = Join-Path $PSScriptRoot 'Test-CollectorPackage.ps1'
& $packageTest -PackagePath $collector

$overwolf = 'C:\Program Files (x86)\Overwolf\Overwolf.exe'
if (-not (Test-Path -LiteralPath $overwolf -PathType Leaf)) {
    throw "Overwolf.exe not found at the supported default path: $overwolf"
}

$running = @(Get-Process -Name Overwolf -ErrorAction SilentlyContinue)
if ($Launch -and $running.Count -eq 0) {
    $arguments = if ($EnableDevTools) { '--ow-enable-features=enable-dev-tools' } else { '' }
    Start-Process -FilePath $overwolf -ArgumentList $arguments | Out-Null
    Write-Output 'OVERWOLF_LAUNCHED=1'
} elseif ($running.Count -gt 0) {
    Write-Output 'OVERWOLF_ALREADY_RUNNING=1'
} else {
    Write-Output 'OVERWOLF_LAUNCHED=0'
}

Write-Output "COLLECTOR_ROOT=$collector"
Write-Output 'NEXT_STEP=Overwolf Settings > About > Development Options > Load unpacked extension'
Write-Output "SELECT_PATH=$collector"
Write-Output 'NEXT_STEP_2=Open My Frame Collector Dev and start capture'
