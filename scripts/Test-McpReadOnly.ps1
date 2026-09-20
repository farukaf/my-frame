param(
    [Parameter(Mandatory = $true)]
    [string]$ServerPath
)

$ErrorActionPreference = 'Stop'
$server = (Resolve-Path -LiteralPath $ServerPath).Path
if (-not (Test-Path -LiteralPath $server -PathType Leaf)) { throw "MCP server not found: $ServerPath" }
$root = Join-Path ([IO.Path]::GetTempPath()) ("myframe-mcp-readonly-" + [guid]::NewGuid().ToString('N'))
$stdout = Join-Path ([IO.Path]::GetTempPath()) ("myframe-mcp-stdout-" + [guid]::NewGuid().ToString('N') + '.log')
$stderr = Join-Path ([IO.Path]::GetTempPath()) ("myframe-mcp-stderr-" + [guid]::NewGuid().ToString('N') + '.log')
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
    $process.StandardInput.Close()
    $outTask = $process.StandardOutput.ReadToEndAsync()
    $errTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(15000)) {
        $process.Kill($true)
        throw 'MCP server did not terminate after stdin EOF within 15 seconds.'
    }
    $stdoutText = $outTask.GetAwaiter().GetResult()
    $stderrText = $errTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw "MCP exited with code $($process.ExitCode)." }
    if ($stdoutText.Length -ne 0) { throw 'MCP emitted bytes to stdout before a protocol request.' }
    if (@(Get-ChildItem -LiteralPath $root -Force).Count -ne 0) { throw 'MCP created files in a clean data root.' }
    Write-Output "MCP_READONLY_OK=1"
    Write-Output "MCP_EXIT=$($process.ExitCode)"
}
finally {
    if ($process -and -not $process.HasExited) { $process.Kill($true) }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    foreach ($path in @($stdout, $stderr)) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force } }
}
