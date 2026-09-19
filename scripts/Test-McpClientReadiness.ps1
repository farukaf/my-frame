[CmdletBinding()]
param([int]$TimeoutSeconds = 15)

$ErrorActionPreference = 'Stop'
if ($TimeoutSeconds -lt 1) { throw 'TimeoutSeconds deve ser positivo.' }
$work = Join-Path ([IO.Path]::GetTempPath()) ("my-frame-client-readiness-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

function Invoke-ReadOnlyCommand([string]$FileName, [string[]]$Arguments, [string]$Name) {
    $stdout = Join-Path $work "$Name.stdout.log"
    $stderr = Join-Path $work "$Name.stderr.log"
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FileName; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.Arguments = (($Arguments | ForEach-Object { '"' + ($_ -replace '"', '\\"') + '"' }) -join ' ')
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
    if (-not $process.Start()) { return [pscustomobject]@{ Name = $Name; State = 'start-failed'; ExitCode = $null; Output = '' } }
    $outTask = $process.StandardOutput.ReadToEndAsync(); $errTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill(); return [pscustomobject]@{ Name = $Name; State = 'timeout'; ExitCode = $null; Output = '' }
    }
    $output = ($outTask.GetAwaiter().GetResult() + $errTask.GetAwaiter().GetResult()).Trim()
    $state = if ($process.ExitCode -eq 0) { 'ok' } else { 'not-configured' }
    return [pscustomobject]@{ Name = $Name; State = $state; ExitCode = $process.ExitCode; Output = $output }
}

try {
    $codex = Get-Command codex -ErrorAction SilentlyContinue
    $claude = Get-Command claude -ErrorAction SilentlyContinue
    $codexResult = if ($codex) { Invoke-ReadOnlyCommand $codex.Source @('mcp','get','my-frame') 'codex' } else { [pscustomobject]@{ Name='codex'; State='not-installed'; ExitCode=$null; Output='' } }
    $claudeResult = if ($claude) { Invoke-ReadOnlyCommand $claude.Source @('mcp','get','my-frame') 'claude' } else { [pscustomobject]@{ Name='claude'; State='not-installed'; ExitCode=$null; Output='' } }
    Write-Output 'MCP_CLIENT_READINESS_PROBE_OK=1'
    Write-Output "MCP_CODEX_STATE=$($codexResult.State)"
    Write-Output "MCP_CLAUDE_STATE=$($claudeResult.State)"
    Write-Output 'MCP_CLIENT_READINESS_MUTATIONS=0'
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}
