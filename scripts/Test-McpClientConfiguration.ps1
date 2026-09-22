param(
    [Parameter(Mandatory = $true)]
    [string]$ServerPath
)

$ErrorActionPreference = 'Stop'
$server = (Resolve-Path -LiteralPath $ServerPath).Path
if (-not (Test-Path -LiteralPath $server -PathType Leaf)) { throw "MCP server not found: $ServerPath" }

$codex = Get-Command codex -ErrorAction SilentlyContinue
$claude = Get-Command claude -ErrorAction SilentlyContinue
if ($null -eq $codex) { throw 'Codex CLI is not installed.' }
if ($null -eq $claude) { throw 'Claude CLI is not installed.' }

$codexText = (& $codex.Source mcp get my-frame 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) { throw "Codex does not have an enabled my-frame server: $codexText" }
if ($codexText -notmatch [regex]::Escape($server)) {
    throw "Codex my-frame command does not point to the requested executable: $codexText"
}

$claudeText = (& $claude.Source mcp get my-frame 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) { throw "Claude does not have an enabled my-frame server: $claudeText" }
if ($claudeText -notmatch [regex]::Escape($server)) {
    throw "Claude my-frame command does not point to the requested executable: $claudeText"
}

$codexCommand = 'codex mcp add my-frame -- "' + $server + '"'
$claudeCommand = 'claude mcp add --transport stdio --scope user my-frame -- "' + $server + '"'
if ($codexCommand -notmatch '^codex mcp add my-frame -- ".+"$') { throw 'Generated Codex command shape is invalid.' }
if ($claudeCommand -notmatch '^claude mcp add --transport stdio --scope user my-frame -- ".+"$') { throw 'Generated Claude command shape is invalid.' }

Write-Output 'MCP_CLIENT_CONFIG_OK=1'
Write-Output 'MCP_CODEX_CONFIGURED=1'
Write-Output 'MCP_CLAUDE_CONFIGURED=1'
Write-Output "MCP_CODEX_COMMAND=$codexCommand"
Write-Output "MCP_CLAUDE_COMMAND=$claudeCommand"
