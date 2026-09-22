[CmdletBinding()]
param(
    [string]$SyncPath,
    [string]$ServerPath,
    [string]$FixturePath
)

$ErrorActionPreference = 'Stop'
$artifactRoot = Join-Path $PSScriptRoot '..\artifacts'
if ([string]::IsNullOrWhiteSpace($SyncPath) -or [string]::IsNullOrWhiteSpace($ServerPath)) {
    $latest = Get-ChildItem -LiteralPath $artifactRoot -Directory -Filter 'MyFrame-*-win-x64' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $latest) { throw 'MCP_DISTRIBUTION_NOT_FOUND' }
    if ([string]::IsNullOrWhiteSpace($SyncPath)) { $SyncPath = Join-Path $latest.FullName 'MyFrame.Sync.exe' }
    if ([string]::IsNullOrWhiteSpace($ServerPath)) { $ServerPath = Join-Path $latest.FullName 'MyFrame.Mcp.exe' }
}
if ([string]::IsNullOrWhiteSpace($FixturePath)) { $FixturePath = Join-Path $PSScriptRoot '..\docs\fixtures\public-export-minimal.json' }
$sync = (Resolve-Path -LiteralPath $SyncPath).Path
$server = (Resolve-Path -LiteralPath $ServerPath).Path
$fixture = (Resolve-Path -LiteralPath $FixturePath).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ("my-frame-public-export-smoke-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $syncResult = & $sync --public-export-file $fixture --data-root $root | ConvertFrom-Json
    if ($syncResult.state -ne 'published') { throw "Public Export import failed: $($syncResult | ConvertTo-Json -Compress)" }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $server
    $start.WorkingDirectory = Split-Path $server
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['MYFRAME_DATA_ROOT'] = $root
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    if (-not $process.Start()) { throw 'MCP server did not start.' }
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"public-export-offline-gate","version":"1"}}}')
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}')
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"search_public_export","arguments":{"text":"Test Weapon"}}}')
    $line = $null
    while (-not $process.StandardOutput.EndOfStream) {
        $candidate = $process.StandardOutput.ReadLine()
        if ($candidate -match '"id"\s*:\s*2') { $line = $candidate; break }
    }
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(15000)) { $process.Kill(); throw 'MCP server did not terminate.' }
    if ($process.ExitCode -ne 0) { throw "MCP server failed: $($process.StandardError.ReadToEnd())" }
    if (-not $line) { throw 'MCP returned no tools/call result.' }
    $payload = $line | ConvertFrom-Json
    if ($payload.error -or $payload.result.isError) { throw "MCP returned an error: $line" }
    $structured = $payload.result.structuredContent
    $item = $structured.items | Select-Object -First 1
    if ($structured.state -ne 'published' -or $structured.items.Count -ne 1) { throw "Unexpected MCP result: $($structured | ConvertTo-Json -Compress)" }
    [pscustomobject]@{
        importState = $syncResult.state
        importedRecords = $syncResult.records
        mcpState = $structured.state
        mcpItems = $structured.items.Count
        itemName = $item.name
        localizedNames = $structured.coverage.localizedNames
        technicalMetadata = $structured.coverage.technicalMetadata
        activeRevision = $structured.activeRevisionId
    }
}
finally { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } }
