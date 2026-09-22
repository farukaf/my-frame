param(
    [Parameter(Mandatory = $true)]
    [string]$ServerPath,
    [int]$StartupMilliseconds = 500
)

$ErrorActionPreference = 'Stop'
$server = (Resolve-Path -LiteralPath $ServerPath).Path
if (-not (Test-Path -LiteralPath $server -PathType Leaf)) { throw "MCP server not found: $ServerPath" }
$roots = @(
    (Join-Path ([IO.Path]::GetTempPath()) ("myframe-mcp-concurrent-" + [guid]::NewGuid().ToString('N'))),
    (Join-Path ([IO.Path]::GetTempPath()) ("myframe-mcp-concurrent-" + [guid]::NewGuid().ToString('N')))
)
$processes = @()
$stderrTasks = @()
try {
    for ($index = 0; $index -lt 2; $index++) {
        New-Item -ItemType Directory -Path $roots[$index] -Force | Out-Null
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $server
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.Environment['MYFRAME_DATA_ROOT'] = $roots[$index]
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $start
        if (-not $process.Start()) { throw "Unable to start MCP server $($index + 1)." }
        $processes += $process
        $stderrTasks += $process.StandardError.ReadToEndAsync()
    }

    Start-Sleep -Milliseconds $StartupMilliseconds
    $workingSets = @($processes | ForEach-Object { $_.WorkingSet64 })
    if (@($processes | Where-Object HasExited).Count -gt 0) { throw 'An MCP process exited before the concurrent measurement.' }

    foreach ($process in $processes) { $process.StandardInput.Close() }
    foreach ($process in $processes) {
        $stdout = $process.StandardOutput.ReadToEndAsync().GetAwaiter().GetResult()
        if (-not $process.WaitForExit(15000)) { $process.Kill($true); throw 'MCP did not terminate after stdin EOF.' }
        if ($process.ExitCode -ne 0) { throw "MCP exited with code $($process.ExitCode)." }
        if ($stdout.Length -ne 0) { throw 'MCP emitted bytes to stdout without a protocol request.' }
    }
    foreach ($root in $roots) {
        if (@(Get-ChildItem -LiteralPath $root -Force).Count -ne 0) { throw "MCP wrote to clean root: $root" }
    }

    Write-Output 'MCP_CONCURRENT_OK=1'
    Write-Output 'MCP_CONCURRENT_SERVERS=2'
    Write-Output "MCP_WORKING_SET_BYTES=$([long](($workingSets | Measure-Object -Sum).Sum))"
    Write-Output "MCP_WORKING_SET_MAX_BYTES=$([long](($workingSets | Measure-Object -Maximum).Maximum))"
}
finally {
    foreach ($process in $processes) {
        if ($process -and -not $process.HasExited) { $process.Kill($true) }
        if ($process) { $process.Dispose() }
    }
    foreach ($root in $roots) {
        if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
