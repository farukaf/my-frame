[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ServerPath
)

$ErrorActionPreference = 'Stop'
$server = (Resolve-Path -LiteralPath $ServerPath).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ("myframe-mcp-failure-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$process = $null
try {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $server
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['MYFRAME_DATA_ROOT'] = $root
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    if (-not $process.Start()) { throw 'Unable to start MCP server.' }

    $connections = @()
    if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
        Start-Sleep -Milliseconds 200
        $connections = @(Get-NetTCPConnection -OwningProcess $process.Id -ErrorAction SilentlyContinue |
            Where-Object State -in @('Listen', 'SynSent', 'SynReceived', 'Established', 'FinWait1', 'FinWait2', 'CloseWait'))
    }
    if ($connections.Count -gt 0) { throw "MCP opened network sockets: $($connections.Count)." }

    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"my-frame-f111","version":"1"}}}')
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}')
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"unknown_tool","arguments":{}}}')
    Start-Sleep -Milliseconds 1000
    $process.StandardInput.Close()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(15000)) {
        $process.Kill($true)
        throw 'MCP did not terminate after invalid request.'
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw "MCP exited with code $($process.ExitCode)." }
    $responseCount = @($stdout -split "`r?`n" | Where-Object { $_.Trim() }).Count
    if ($stdout -notmatch '"id"\s*:\s*2' -or $stdout -notmatch '"error"') {
        throw "Invalid tool request did not produce a JSON-RPC error. Raw stdout: $stdout"
    }
    if ($stdout -match '(?i)(authorization|bearer|token|MYFRAME_DATA_ROOT|C:\\Users)') {
        throw 'Protocol error exposed a credential or local path.'
    }
    if (@(Get-ChildItem -LiteralPath $root -Force).Count -ne 0) {
        throw 'Invalid request created files in the clean data root.'
    }
    Write-Output 'MCP_FAILURE_READONLY_OK=1'
    Write-Output "MCP_FAILURE_NETWORK_CONNECTIONS=$($connections.Count)"
    Write-Output "MCP_FAILURE_RESPONSES=$responseCount"
    Write-Output 'MCP_FAILURE_ERROR_PRESENT=1'
    Write-Output "MCP_FAILURE_EXIT=$($process.ExitCode)"
}
finally {
    if ($process -and -not $process.HasExited) { $process.Kill($true) }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
