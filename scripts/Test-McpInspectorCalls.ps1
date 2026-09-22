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
$temp = Join-Path ([IO.Path]::GetTempPath()) ("my-frame-mcp-inspector-calls-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null

function Invoke-InspectorCall([string]$toolName, [string[]]$toolArgs, [int]$index) {
    $args = @()
    if ($SkipInstall) { $args += '--offline' }
    $args += @('--yes', '@modelcontextprotocol/inspector', '--cli', $server,
        '--method', 'tools/call', '--tool-name', $toolName)
    if ($toolArgs.Count -gt 0) { $args += '--tool-arg'; $args += $toolArgs }
    $args += @('--format', 'json')
    $stdout = Join-Path $temp "$index.stdout.json"
    $stderr = Join-Path $temp "$index.stderr.log"
    $process = Start-Process -FilePath $npx -ArgumentList $args -WorkingDirectory (Split-Path $server) -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if ($process.ExitCode -ne 0) {
        throw "Inspector failed for $toolName with exit code $($process.ExitCode): $(Get-Content -Raw $stderr)"
    }
    $payload = Get-Content -Raw $stdout | ConvertFrom-Json
    if ($payload.error -or -not $payload.result -or -not $payload.result.structuredContent) {
        throw "Inspector did not return structuredContent for $toolName."
    }
    return $payload
}

try {
    $calls = @(
        @{ Name = 'get_capabilities'; Args = @() },
        @{ Name = 'get_sync_status'; Args = @() },
        @{ Name = 'get_market_credential_status'; Args = @() },
        @{ Name = 'get_world_state'; Args = @('limit=1') }
    )
    $index = 0
    foreach ($call in $calls) {
        [void](Invoke-InspectorCall $call.Name $call.Args $index)
        $index++
    }
    Write-Output 'MCP_INSPECTOR_CALLS_OK=1'
    Write-Output "MCP_INSPECTOR_CALLS_COUNT=$($calls.Count)"
    Write-Output 'MCP_INSPECTOR_CALLS_STRUCTURED=4'
}
finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
