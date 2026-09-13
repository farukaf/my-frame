[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ServerPath
)

$ErrorActionPreference = 'Stop'
$expected = (Resolve-Path -LiteralPath $ServerPath).Path

function Get-ClientProbe([string]$name, [string]$command, [string[]]$arguments) {
    $tool = Get-Command $command -ErrorAction SilentlyContinue
    if ($null -eq $tool) {
        return [pscustomobject]@{ Name = $name; State = 'not-installed'; Output = '' }
    }

    $output = (& $tool.Source @arguments 2>&1 | Out-String).Trim()
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        return [pscustomobject]@{ Name = $name; State = 'not-configured'; Output = $output }
    }

    $state = if ($output -match [regex]::Escape($expected)) { 'path-match' } else { 'path-mismatch' }
    return [pscustomobject]@{ Name = $name; State = $state; Output = $output }
}

$codex = Get-ClientProbe 'codex' 'codex' @('mcp', 'get', 'my-frame')
$claude = Get-ClientProbe 'claude' 'claude' @('mcp', 'get', 'my-frame')

Write-Output 'MCP_CLIENT_PATH_PROBE_OK=1'
Write-Output "MCP_CLIENT_EXPECTED_PATH=$expected"
Write-Output "MCP_CODEX_PATH_STATE=$($codex.State)"
Write-Output "MCP_CLAUDE_PATH_STATE=$($claude.State)"
Write-Output 'MCP_CLIENT_PATH_MUTATIONS=0'
