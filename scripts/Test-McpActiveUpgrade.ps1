[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OldDistributionPath,
    [Parameter(Mandatory = $true)][string]$NewDistributionPath,
    [string]$SeedServerPath = ''
)

$ErrorActionPreference = 'Stop'
$old = (Resolve-Path -LiteralPath $OldDistributionPath).Path
$new = (Resolve-Path -LiteralPath $NewDistributionPath).Path
if ([string]::IsNullOrWhiteSpace($SeedServerPath)) { $SeedServerPath = Join-Path $PSScriptRoot '..\MyFrame.Mcp\bin\Debug\net10.0\win-x64\MyFrame.Mcp.exe' }
$seedServer = (Resolve-Path -LiteralPath $SeedServerPath).Path
foreach ($directory in @($old,$new)) {
    if (-not (Test-Path -LiteralPath (Join-Path $directory 'MyFrame.Mcp.exe') -PathType Leaf)) { throw "MyFrame.Mcp.exe ausente: $directory" }
}
$npxCommand = Get-Command npx.cmd -ErrorAction SilentlyContinue
if (-not $npxCommand) { $npxCommand = Get-Command npx -ErrorAction Stop }
$suffix = [guid]::NewGuid().ToString('N')
$root = Join-Path ([IO.Path]::GetTempPath()) "my-frame-active-upgrade-$suffix"
$work = Join-Path ([IO.Path]::GetTempPath()) "my-frame-active-upgrade-work-$suffix"
New-Item -ItemType Directory -Path $root,$work | Out-Null
$processes = @()
$seed = $null
try {
    # Seed the same root through the newer distributed server and the real MCP
    # Inspector, so both versions open an existing SQLite database.
    $seedOut = Join-Path $work 'seed.stdout.json'
    $seedErr = Join-Path $work 'seed.stderr.log'
    $seedArgs = @('--yes','@modelcontextprotocol/inspector','--cli',$seedServer,
        '-e',"MYFRAME_DATA_ROOT=$root",'--method','tools/call','--tool-name','get_overview','--format','json')
    $seed = Start-Process -FilePath $npxCommand.Source -ArgumentList $seedArgs -WorkingDirectory $old -NoNewWindow -Wait -PassThru -RedirectStandardOutput $seedOut -RedirectStandardError $seedErr
    $seedExit = $seed.ExitCode
    if ($null -ne $seedExit -and [int]$seedExit -ne 0) { throw "Seed MCP falhou: $($seedErr | Get-Content -Raw)" }
    $seedPayload = Get-Content -Raw $seedOut | ConvertFrom-Json
    if ($seedPayload.result.isError -or $null -eq $seedPayload.result.structuredContent) { throw 'MCP seed did not return structuredContent.' }
    if (-not (Test-Path -LiteralPath (Join-Path $root 'data.db'))) { throw 'Seed did not create data.db.' }

    foreach ($directory in @($old,$new)) {
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = Join-Path $directory 'MyFrame.Mcp.exe'
        $start.WorkingDirectory = $directory; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
        $start.Environment['MYFRAME_DATA_ROOT'] = $root
        $p = [Diagnostics.Process]::new(); $p.StartInfo = $start
        if (-not $p.Start()) { throw "MCP did not start: $directory" }
        $processes += $p
    }
    Start-Sleep -Milliseconds 750
    if (@($processes | Where-Object HasExited).Count -gt 0) { throw 'A version exited while the active upgrade was open.' }
    foreach ($p in $processes) { $p.StandardInput.Close() }
    foreach ($p in $processes) {
        $out = $p.StandardOutput.ReadToEndAsync().GetAwaiter().GetResult()
        if (-not $p.WaitForExit(15000)) { $p.Kill(); throw 'MCP did not exit after EOF.' }
        if ($p.ExitCode -ne 0) { throw "MCP exited with exit code $($p.ExitCode)." }
        if ($out.Length -ne 0) { throw 'MCP escreveu stdout sem request.' }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $root 'data.db'))) { throw 'data.db disappeared after the active upgrade.' }
    Write-Output 'MCP_ACTIVE_UPGRADE_OK=1'
    Write-Output 'MCP_ACTIVE_UPGRADE_MODE=old-and-new-same-root'
    Write-Output 'MCP_ACTIVE_UPGRADE_SEED_STRUCTURED=1'
    Write-Output 'MCP_ACTIVE_UPGRADE_BOTH_EXITED=1'
    Write-Output 'MCP_ACTIVE_UPGRADE_SQLITE_PRESERVED=1'
}
finally {
    foreach ($p in $processes) { if ($p -and -not $p.HasExited) { $p.Kill() } if ($p) { $p.Dispose() } }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}
