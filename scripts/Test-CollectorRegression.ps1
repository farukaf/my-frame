[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$probeProject = Join-Path $repository 'MyFrame.Collector.Probe\MyFrame.Collector.Probe.csproj'
$probePath = Join-Path $repository "MyFrame.Collector.Probe\bin\$Configuration\net10.0\MyFrame.Collector.Probe.exe"
$tests = @(Get-ChildItem -LiteralPath (Join-Path $repository 'MyFrame.Collector.Overwolf\tests') -Filter '*.test.mjs' -File |
    Sort-Object Name | Select-Object -ExpandProperty FullName)
if ($tests.Count -eq 0) { throw 'No collector tests were found.' }

& dotnet build $probeProject --configuration $Configuration --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "Collector probe build failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $probePath -PathType Leaf)) { throw "Collector probe is missing: $probePath" }

$previousProbe = $env:MYFRAME_COLLECTOR_PROBE
try {
    $env:MYFRAME_COLLECTOR_PROBE = $probePath
    & node --test --test-isolation=none @tests
    if ($LASTEXITCODE -ne 0) { throw "Collector tests failed with exit code $LASTEXITCODE." }
} finally {
    if ($null -eq $previousProbe) {
        Remove-Item Env:MYFRAME_COLLECTOR_PROBE -ErrorAction SilentlyContinue
    } else {
        $env:MYFRAME_COLLECTOR_PROBE = $previousProbe
    }
}

& (Join-Path $PSScriptRoot 'Test-CollectorPackage.ps1')
Write-Output 'COLLECTOR_REGRESSION_OK=1'
