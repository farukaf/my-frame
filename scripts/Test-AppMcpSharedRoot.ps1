[CmdletBinding()]
param(
    [string]$AppPath = '',
    [string]$ServerPath = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($AppPath)) {
    $AppPath = Join-Path $PSScriptRoot '..\MyFrame.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\MyFrame.App.exe'
}
if ([string]::IsNullOrWhiteSpace($ServerPath)) {
    $ServerPath = Join-Path $PSScriptRoot '..\MyFrame.Mcp\bin\Debug\net10.0\win-x64\MyFrame.Mcp.exe'
}
$app = (Resolve-Path -LiteralPath $AppPath).Path
$server = (Resolve-Path -LiteralPath $ServerPath).Path
$npxCommand = Get-Command npx.cmd -ErrorAction SilentlyContinue
if (-not $npxCommand) { $npxCommand = Get-Command npx -ErrorAction Stop }
$suffix = [guid]::NewGuid().ToString('N')
$root = Join-Path ([IO.Path]::GetTempPath()) ("my-frame-shared-root-$suffix")
$logs = Join-Path ([IO.Path]::GetTempPath()) ("my-frame-shared-logs-$suffix")
$work = Join-Path ([IO.Path]::GetTempPath()) ("my-frame-shared-work-$suffix")
New-Item -ItemType Directory -Path $root,$logs,$work | Out-Null
$appProcess = $null
try {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $app
    $start.WorkingDirectory = Split-Path $app
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.Environment['MYFRAME_DATA_ROOT'] = $root
    $start.Environment['MYFRAME_LOG_ROOT'] = $logs
    $appProcess = [Diagnostics.Process]::new()
    $appProcess.StartInfo = $start
    if (-not $appProcess.Start()) { throw 'Unable to start MyFrame.App.' }
    Start-Sleep -Seconds 5
    if ($appProcess.HasExited) { throw "MyFrame.App exited before MCP call (code $($appProcess.ExitCode))." }

    $stdout = Join-Path $work 'stdout.json'
    $stderr = Join-Path $work 'stderr.log'
    $args = @('--yes','@modelcontextprotocol/inspector','--cli',$server,
        '-e',"MYFRAME_DATA_ROOT=$root",'--method','tools/call',
        '--tool-name','get_overview','--format','json')
    $inspector = Start-Process -FilePath $npxCommand.Source -ArgumentList $args -WorkingDirectory (Split-Path $server) -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if ($inspector.ExitCode -ne 0) { throw "Inspector exited with code $($inspector.ExitCode): $(Get-Content -Raw $stderr)" }
    $payload = Get-Content -Raw $stdout | ConvertFrom-Json
    if ($payload.result.isError) { throw 'MCP returned isError=true.' }
    if ($null -eq $payload.result.structuredContent) { throw 'MCP response had no structuredContent.' }
    $aliveDuringMcp = -not $appProcess.HasExited
    if (-not $aliveDuringMcp) { throw "MyFrame.App exited during MCP call (code $($appProcess.ExitCode))." }

    $files = @(Get-ChildItem -LiteralPath $root -Force | Select-Object -ExpandProperty Name)
    if (-not ($files -contains 'data.db')) { throw 'Shared SQLite data.db was not created.' }
    $legacyNames = @('market-quotes.json','market-data.dat','market-items.dat','warframe-market.token')
    $legacy = @($files | Where-Object { $_ -in $legacyNames })
    # SharedDataMigration may copy existing user files into the isolated root
    # for backward-compatible import. Their presence is reported, not treated
    # as a concurrency failure; SQLite remains the read path under test.
    Write-Output 'APP_MCP_SHARED_ROOT_OK=1'
    Write-Output "APP_MCP_APP_ALIVE_DURING_MCP=$([int]$aliveDuringMcp)"
    Write-Output "APP_MCP_EXIT=$($inspector.ExitCode)"
    Write-Output 'APP_MCP_STRUCTURED=1'
    Write-Output 'APP_MCP_SQLITE=1'
    Write-Output "APP_MCP_LEGACY_MARKET_FILES=$($legacy.Count)"
}
finally {
    if ($appProcess -and -not $appProcess.HasExited) { $appProcess.Kill(); $appProcess.WaitForExit() }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    if (Test-Path -LiteralPath $logs) { Remove-Item -LiteralPath $logs -Recurse -Force }
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
