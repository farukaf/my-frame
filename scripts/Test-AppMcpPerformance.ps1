[CmdletBinding()]
param(
    [string]$AppPath = '',
    [string]$ServerPath = '',
    [string]$ProbePath = '',
    [int]$FixtureItems = 20000
)

$ErrorActionPreference = 'Stop'
if ($FixtureItems -lt 1000) { throw 'FixtureItems deve ser >= 1000.' }
if ([string]::IsNullOrWhiteSpace($AppPath)) { $AppPath = Join-Path $PSScriptRoot '..\MyFrame.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\MyFrame.App.exe' }
if ([string]::IsNullOrWhiteSpace($ServerPath)) { $ServerPath = Join-Path $PSScriptRoot '..\MyFrame.Mcp\bin\Debug\net10.0\win-x64\MyFrame.Mcp.exe' }
if ([string]::IsNullOrWhiteSpace($ProbePath)) { $ProbePath = Join-Path $PSScriptRoot '..\MyFrame.Collector.Probe\bin\Debug\net10.0\MyFrame.Collector.Probe.dll' }
$app = (Resolve-Path -LiteralPath $AppPath).Path
$server = (Resolve-Path -LiteralPath $ServerPath).Path
$probe = (Resolve-Path -LiteralPath $ProbePath).Path
$probeCommand = if ($probe.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) { 'dotnet' } else { $probe }
$npxCommand = Get-Command npx.cmd -ErrorAction SilentlyContinue
if (-not $npxCommand) { $npxCommand = Get-Command npx -ErrorAction Stop }

$suffix = [guid]::NewGuid().ToString('N')
$root1 = Join-Path ([IO.Path]::GetTempPath()) "my-frame-perf-root1-$suffix"
$root2 = Join-Path ([IO.Path]::GetTempPath()) "my-frame-perf-root2-$suffix"
$capture = Join-Path ([IO.Path]::GetTempPath()) "my-frame-perf-capture-$suffix"
$logs = Join-Path ([IO.Path]::GetTempPath()) "my-frame-perf-logs-$suffix"
$work = Join-Path ([IO.Path]::GetTempPath()) "my-frame-perf-work-$suffix"
New-Item -ItemType Directory -Path $root1,$root2,$capture,$logs,$work | Out-Null
$appProcess = $null
$inspectors = @()
try {
    # Build one valid Overwolf envelope with a large synthetic inventory and
    # import it into both roots through the production probe/importer path.
    $equipment = [Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt $FixtureItems; $i++) {
        $equipment.Add([ordered]@{ instanceId = "perf-$i"; typeId = "/Lotus/Weapon/Perf$i"; rank = ($i % 31) })
    }
    $payload = [ordered]@{ equipment = $equipment } | ConvertTo-Json -Depth 4 -Compress
    $eventId = [guid]::NewGuid()
    $captureName = "$eventId.capture.json"
    $capturePath = Join-Path $capture $captureName
    $envelope = [ordered]@{
        schemaVersion = 1; gameId = 8954; source = 'overwolf-native'; kind = 'inventory'
        sessionId = [guid]::NewGuid(); eventId = $eventId; sequence = 1
        receivedAt = ([DateTimeOffset]::UtcNow).ToString('O'); providerVersion = 'performance-fixture'
        completeness = 'unverified'; encoding = 'json-object'; payload = $payload
    } | ConvertTo-Json -Depth 5 -Compress
    [IO.File]::WriteAllText($capturePath, $envelope, [Text.UTF8Encoding]::new($false))
    $body = [IO.File]::ReadAllBytes($capturePath)
    $markerPath = Join-Path $capture "$eventId.ready.json"
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $digest = $sha.ComputeHash($body) } finally { $sha.Dispose() }
    $marker = [ordered]@{ schemaVersion = 1; fileName = $captureName; bytes = $body.Length; sha256 = ([BitConverter]::ToString($digest) -replace '-', '') } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText($markerPath, $marker, [Text.UTF8Encoding]::new($false))
    foreach ($root in @($root1,$root2)) {
        $probeCheck = @(& dotnet $probe '--marker' $markerPath 2>&1)
        if ($LASTEXITCODE -ne 0) { throw "Fixture probe failed: $($probeCheck -join ' ')" }
        if ($probeCommand -eq 'dotnet') {
            $probeOutput = @(& dotnet $probe '--import' '--marker' $markerPath '--database' (Join-Path $root 'data.db') '--allow-raw' 2>&1)
        } else {
            $probeOutput = @(& $probe '--import' '--marker' $markerPath '--database' (Join-Path $root 'data.db') '--allow-raw' 2>&1)
        }
        if ($LASTEXITCODE -ne 0) { throw "Fixture import failed: $($probeOutput -join ' ')" }
    }

    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $app; $start.WorkingDirectory = Split-Path $app
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.Environment['MYFRAME_DATA_ROOT'] = $root1
    $start.Environment['MYFRAME_LOG_ROOT'] = $logs
    $appProcess = [Diagnostics.Process]::new(); $appProcess.StartInfo = $start
    if (-not $appProcess.Start()) { throw 'Unable to start MyFrame.App.' }
    Start-Sleep -Seconds 5
    if ($appProcess.HasExited) { throw "MyFrame.App exited (code $($appProcess.ExitCode))." }

    $jobs = @(
        @{ Root = $root1; Name = 'root1' },
        @{ Root = $root2; Name = 'root2' }
    )
    $timer = [Diagnostics.Stopwatch]::StartNew()
    foreach ($job in $jobs) {
        $stdout = Join-Path $work "$($job.Name).stdout.json"
        $stderr = Join-Path $work "$($job.Name).stderr.log"
        $args = @('--yes','@modelcontextprotocol/inspector','--cli',$server,'-e',"MYFRAME_DATA_ROOT=$($job.Root)",
            '--method','tools/call','--tool-name','get_overview','--format','json')
        $p = Start-Process -FilePath $npxCommand.Source -ArgumentList $args -WorkingDirectory (Split-Path $server) -NoNewWindow -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        $inspectors += [ordered]@{ Process = $p; Name = $job.Name; Stdout = $stdout; Stderr = $stderr }
    }
    foreach ($item in $inspectors) {
        if (-not $item.Process.WaitForExit(120000)) { throw "Inspector $($item.Name) timeout." }
    }
    $timer.Stop()
    $totalBytes = 0L
    foreach ($item in $inspectors) {
        $exitCode = $item.Process.ExitCode
        if ($null -ne $exitCode -and [int]$exitCode -ne 0) { throw "Inspector $($item.Name) exit ${exitCode}: $(Get-Content -Raw $item.Stderr)" }
        $payloadResult = Get-Content -Raw $item.Stdout | ConvertFrom-Json
        if ($payloadResult.result.isError -or $null -eq $payloadResult.result.structuredContent) { throw "Inspector $($item.Name) returned invalid structuredContent." }
        $totalBytes += (Get-Item -LiteralPath $item.Stdout).Length
    }
    if ($appProcess.HasExited) { throw "MyFrame.App exited during benchmark (code $($appProcess.ExitCode))." }
    $workingSet = [long]0
    foreach ($item in $inspectors) { $workingSet += [long]$item.Process.PeakWorkingSet64 }
    $workingSet += $appProcess.WorkingSet64
    Write-Output 'APP_MCP_PERF_OK=1'
    Write-Output "APP_MCP_PERF_FIXTURE_ITEMS=$FixtureItems"
    Write-Output 'APP_MCP_PERF_SERVERS=2'
    Write-Output 'APP_MCP_PERF_APP_ALIVE=1'
    Write-Output "APP_MCP_PERF_ELAPSED_MS=$([long]$timer.Elapsed.TotalMilliseconds)"
    Write-Output "APP_MCP_PERF_RESPONSE_BYTES=$totalBytes"
    Write-Output "APP_MCP_PERF_WORKING_SET_BYTES=$workingSet"
}
finally {
    foreach ($item in $inspectors) { if ($item.Process -and -not $item.Process.HasExited) { $item.Process.Kill() } }
    if ($appProcess -and -not $appProcess.HasExited) { $appProcess.Kill(); $appProcess.WaitForExit() }
    if ($env:MYFRAME_KEEP_PERF_TEMP -eq '1') {
        Write-Output "APP_MCP_PERF_DEBUG_ROOT1=$root1"
        Write-Output "APP_MCP_PERF_DEBUG_CAPTURE=$capture"
    } else {
        foreach ($path in @($root1,$root2,$capture,$logs,$work)) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue } }
    }
}
