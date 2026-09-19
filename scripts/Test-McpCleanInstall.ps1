[CmdletBinding()]
param(
    [string]$ServerPath = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ServerPath)) {
    $ServerPath = Join-Path $PSScriptRoot '..\MyFrame.Mcp\bin\Debug\net10.0\win-x64\MyFrame.Mcp.exe'
}
$server = (Resolve-Path -LiteralPath $ServerPath).Path
$npxCommand = Get-Command npx.cmd -ErrorAction SilentlyContinue
if (-not $npxCommand) { $npxCommand = Get-Command npx -ErrorAction Stop }
$root = Join-Path ([IO.Path]::GetTempPath()) ("my-frame-clean-install-" + [guid]::NewGuid().ToString('N'))
$work = Join-Path ([IO.Path]::GetTempPath()) ("my-frame-clean-inspector-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root,$work | Out-Null
try {
    $stdout = Join-Path $work 'stdout.json'
    $stderr = Join-Path $work 'stderr.log'
    $args = @('--yes', '@modelcontextprotocol/inspector', '--cli', $server,
        '-e', "MYFRAME_DATA_ROOT=$root", '--method', 'tools/call',
        '--tool-name', 'get_overview', '--format', 'json')
    $process = Start-Process -FilePath $npxCommand.Source -ArgumentList $args -WorkingDirectory (Split-Path $server) -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if ($process.ExitCode -ne 0) { throw "Inspector exited with code $($process.ExitCode): $(Get-Content -Raw $stderr)" }
    $payload = Get-Content -Raw $stdout | ConvertFrom-Json
    $overview = $payload.result.structuredContent.overview
    if (-not $overview.setupRequired) { throw 'Clean installation did not report setupRequired=true.' }
    if (-not ($payload.result.structuredContent.meta.warnings.code -contains 'SETUP_REQUIRED')) {
        throw 'Clean installation did not expose SETUP_REQUIRED warning.'
    }
    $legacy = @('market-quotes.json','market-data.dat','market-items.dat','settings.v1.json','warframe-market.token')
    $files = @(Get-ChildItem -LiteralPath $root -Force | Select-Object -ExpandProperty Name)
    $unexpectedLegacy = @($files | Where-Object { $_ -in $legacy })
    if ($unexpectedLegacy.Count -gt 0) { throw "Legacy files appeared: $($unexpectedLegacy -join ', ')" }
    if (-not ($files -contains 'data.db')) { throw 'SQLite data.db was not created.' }
    Write-Output 'MCP_CLEAN_INSTALL_OK=1'
    Write-Output 'MCP_CLEAN_SETUP_REQUIRED=1'
    Write-Output "MCP_CLEAN_LEGACY_FILES=$($unexpectedLegacy.Count)"
    Write-Output 'MCP_CLEAN_SQLITE=1'
    Write-Output "MCP_CLEAN_EXIT=$($process.ExitCode)"
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
