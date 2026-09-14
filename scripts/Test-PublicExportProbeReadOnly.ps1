[CmdletBinding()]
param(
    [string]$ProjectPath,
    [string]$DataRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectPath)) { $ProjectPath = Join-Path $PSScriptRoot '..\MyFrame.Sync\MyFrame.Sync.csproj' }
$temporaryRoot = $false
if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    $DataRoot = Join-Path ([IO.Path]::GetTempPath()) "myframe-public-export-probe-$([Guid]::NewGuid().ToString('N'))"
    $temporaryRoot = $true
}

$resolvedProject = (Resolve-Path -LiteralPath $ProjectPath).Path
$resolvedDataRoot = [IO.Path]::GetFullPath($DataRoot)
$existedBefore = Test-Path -LiteralPath $resolvedDataRoot
$before = if ($existedBefore) {
    @(Get-ChildItem -LiteralPath $resolvedDataRoot -Force -Recurse -File -ErrorAction Stop |
        ForEach-Object { '{0}|{1}|{2}' -f $_.FullName, $_.Length, $_.LastWriteTimeUtc.Ticks })
} else { @() }

$output = @(& dotnet run --project $resolvedProject --no-build -- --public-export-probe --data-root $resolvedDataRoot 2>&1)
$exitCode = $LASTEXITCODE
$jsonLine = $output | Where-Object { $_ -match '^\{' } | Select-Object -Last 1
if ([string]::IsNullOrWhiteSpace($jsonLine)) { throw "Probe did not return JSON. Output: $($output -join ' ')" }
$result = $jsonLine | ConvertFrom-Json
if ($result.source -ne 'public-export' -or $result.state -notin @('reachable', 'unreachable')) {
    throw "Unexpected probe result: $jsonLine"
}
if ($result.state -eq 'reachable' -and $exitCode -ne 0) { throw "Reachable probe exited with $exitCode" }
if ($result.state -eq 'unreachable' -and $exitCode -eq 0) { throw 'Unreachable probe unexpectedly exited with zero.' }

$afterExists = Test-Path -LiteralPath $resolvedDataRoot
$after = if ($afterExists) {
    @(Get-ChildItem -LiteralPath $resolvedDataRoot -Force -Recurse -File -ErrorAction Stop |
        ForEach-Object { '{0}|{1}|{2}' -f $_.FullName, $_.Length, $_.LastWriteTimeUtc.Ticks })
} else { @() }
if ($temporaryRoot -and $afterExists) { throw "Probe created data root: $resolvedDataRoot" }
$differences = @(Compare-Object $before $after -SyncWindow 0)
if (-not $temporaryRoot -and (($existedBefore -ne $afterExists) -or $differences.Count -ne 0)) {
    throw "Probe changed data root: $resolvedDataRoot"
}

Write-Output "PROBE_STATE=$($result.state)"
Write-Output "PROBE_EXIT_CODE=$exitCode"
Write-Output 'PUBLIC_EXPORT_PROBE_READONLY=1'
exit 0
