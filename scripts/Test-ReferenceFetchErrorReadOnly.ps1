[CmdletBinding()]
param([string]$SyncPath)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SyncPath)) {
    $artifactRoot = Join-Path $PSScriptRoot '..\artifacts'
    $latest = Get-ChildItem -LiteralPath $artifactRoot -Directory -Filter 'MyFrame-*-win-x64' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $latest) { throw 'MCP_DISTRIBUTION_NOT_FOUND' }
    $SyncPath = Join-Path $latest.FullName 'MyFrame.Sync.exe'
}
$sync = (Resolve-Path -LiteralPath $SyncPath).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ('myframe-reference-fetch-' + [guid]::NewGuid().ToString('N'))
try {
    $output = @(& $sync --reference-url 'https://overframe.gg/build/123' --data-root $root 2>&1)
    $exitCode = $LASTEXITCODE
    $jsonLine = $output | Where-Object { $_ -match '^\{' } | Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($jsonLine)) { throw 'REFERENCE_PROBE_NO_JSON' }
    $result = $jsonLine | ConvertFrom-Json
    if ($result.state -ne 'failed' -or $result.errorCode -notmatch '^REFERENCE_[A-Z0-9_]+$') {
        throw "Unexpected reference failure: $jsonLine"
    }
    if ($exitCode -eq 0) { throw 'Reference fetch unexpectedly succeeded in the offline gate.' }
    if (($output -join "`n") -match '127\.0\.0\.1|No connection could be made|proxy details') {
        throw 'Reference fetch leaked transport details.'
    }
    if (Test-Path -LiteralPath $root) {
        $files = @(Get-ChildItem -LiteralPath $root -Force -Recurse -File -ErrorAction Stop)
        if ($files.Count -ne 0) { throw 'Reference fetch wrote files to the data root.' }
    }
    Write-Output "REFERENCE_STATE=$($result.state)"
    Write-Output "REFERENCE_ERROR_CODE=$($result.errorCode)"
    Write-Output "REFERENCE_EXIT_CODE=$exitCode"
    Write-Output 'REFERENCE_FETCH_READONLY=1'
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}
