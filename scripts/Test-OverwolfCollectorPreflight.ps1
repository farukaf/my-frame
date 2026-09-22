[CmdletBinding()]
param(
    [string]$IconPath,
    [string]$CollectorRoot,
    [string]$CaptureDirectory
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($IconPath)) { $IconPath = Join-Path $PSScriptRoot '..\MyFrame.Collector.Overwolf\icon.png' }
if ([string]::IsNullOrWhiteSpace($CollectorRoot)) { $CollectorRoot = Join-Path $PSScriptRoot '..\artifacts\collector-overwolf' }
if ([string]::IsNullOrWhiteSpace($CaptureDirectory)) { $CaptureDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MyFrame\captures' }
$buildScript = Join-Path $PSScriptRoot 'Build-Collector.ps1'
$packageTest = Join-Path $PSScriptRoot 'Test-CollectorPackage.ps1'
$readinessTest = Join-Path $PSScriptRoot 'Test-OverwolfCollectorReadiness.ps1'

& $buildScript -IconPath $IconPath | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $CollectorRoot 'manifest.json') -PathType Leaf)) {
    throw 'COLLECTOR_BUILD_FAILED'
}
Write-Output 'PACKAGE_BUILD=1'

& $packageTest -PackagePath $CollectorRoot
Write-Output 'PACKAGE_VALID=1'

$readinessOutput = @(& $readinessTest -CollectorRoot $CollectorRoot -CaptureDirectory $CaptureDirectory 2>&1)
$readinessCode = $LASTEXITCODE
$readinessOutput | Write-Output
if ($readinessCode -eq 0) {
    Write-Output 'OVERWOLF_PREFLIGHT_READY=1'
    exit 0
}
Write-Output 'OVERWOLF_PREFLIGHT_READY=0'
exit 1
