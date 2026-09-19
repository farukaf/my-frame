[CmdletBinding()]
param(
    [string]$ServerPath = '',
    [switch]$SkipInstall
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ServerPath)) {
    $ServerPath = Join-Path $PSScriptRoot '..\MyFrame.Mcp\bin\Debug\net10.0\win-x64\MyFrame.Mcp.exe'
}
$server = (Resolve-Path -LiteralPath $ServerPath).Path
$npxCommand = Get-Command npx.cmd -ErrorAction SilentlyContinue
if (-not $npxCommand) { $npxCommand = Get-Command npx -ErrorAction Stop }
$npx = $npxCommand.Source
$temp = Join-Path ([IO.Path]::GetTempPath()) ("my-frame-mcp-inspector-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $args = @()
    if ($SkipInstall) { $args += '--offline' }
    $args += @('--yes', '@modelcontextprotocol/inspector', '--cli', $server,
        '--method', 'tools/list', '--strict', '--format', 'json')

    $stdout = Join-Path $temp 'stdout.json'
    $stderr = Join-Path $temp 'stderr.log'
    $process = Start-Process -FilePath $npx -ArgumentList $args -WorkingDirectory (Split-Path $server) -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if ($process.ExitCode -ne 0) {
        throw "MCP Inspector falhou com exit code $($process.ExitCode): $(Get-Content -Raw $stderr)"
    }

    $payload = Get-Content -Raw $stdout | ConvertFrom-Json
    if ($payload.error) { throw "Inspector retornou erro: $($payload.error.message)" }
    $tools = @($payload.result.tools)
    if ($tools.Count -eq 0) { throw 'Inspector não retornou ferramentas.' }
    $warningText = Get-Content -Raw $stderr
    $warningCount = ([regex]::Matches($warningText, '(?m)^Warning: tool ')).Count
    $errorCount = ([regex]::Matches($warningText, '(?m)^Error: ')).Count
    if ($errorCount -gt 0) { throw "Inspector registrou $errorCount erros." }

    Write-Output 'MCP_INSPECTOR_OK=1'
    Write-Output "MCP_INSPECTOR_TOOLS=$($tools.Count)"
    Write-Output "MCP_INSPECTOR_WARNINGS=$warningCount"
    Write-Output "MCP_INSPECTOR_ERRORS=$errorCount"
    Write-Output 'MCP_INSPECTOR_METHOD=tools/list'
}
finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
