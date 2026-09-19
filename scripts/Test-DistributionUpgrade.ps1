param(
    [Parameter(Mandatory = $true)]
    [string]$OldDistributionPath,
    [Parameter(Mandatory = $true)]
    [string]$NewDistributionPath
)

$ErrorActionPreference = 'Stop'
$old = (Resolve-Path -LiteralPath $OldDistributionPath).Path
$new = (Resolve-Path -LiteralPath $NewDistributionPath).Path
if ([string]::Equals($old, $new, [StringComparison]::OrdinalIgnoreCase)) { throw 'Old and new distributions must be different directories.' }
$required = @('MyFrame.App.exe', 'MyFrame.Mcp.exe')
foreach ($directory in @($old, $new)) {
    foreach ($name in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $directory $name) -PathType Leaf)) {
            throw "Distribution is missing ${name}: $directory"
        }
    }
}

$roots = @(
    (Join-Path ([IO.Path]::GetTempPath()) ("myframe-upgrade-old-" + [guid]::NewGuid().ToString('N'))),
    (Join-Path ([IO.Path]::GetTempPath()) ("myframe-upgrade-new-" + [guid]::NewGuid().ToString('N')))
)
$processes = @()
try {
    foreach ($pair in @(@($old, 0), @($new, 1))) {
        $directory = $pair[0]
        $index = [int]$pair[1]
        New-Item -ItemType Directory -Path $roots[$index] -Force | Out-Null
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = Join-Path $directory 'MyFrame.Mcp.exe'
        $start.WorkingDirectory = $directory
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.Environment['MYFRAME_DATA_ROOT'] = $roots[$index]
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $start
        if (-not $process.Start()) { throw "Unable to start distribution $directory." }
        $processes += $process
    }
    Start-Sleep -Milliseconds 500
    if (@($processes | Where-Object HasExited).Count -gt 0) { throw 'A distribution exited while the upgrade pair was active.' }

    foreach ($process in $processes) { $process.StandardInput.Close() }
    foreach ($process in $processes) {
        $stdout = $process.StandardOutput.ReadToEndAsync().GetAwaiter().GetResult()
        if (-not $process.WaitForExit(15000)) { $process.Kill($true); throw 'Distribution MCP did not stop after EOF.' }
        if ($process.ExitCode -ne 0) { throw "Distribution MCP exited with code $($process.ExitCode)." }
        if ($stdout.Length -ne 0) { throw 'Distribution MCP emitted stdout without a protocol request.' }
    }
    foreach ($root in $roots) {
        if (@(Get-ChildItem -LiteralPath $root -Force).Count -ne 0) { throw "MCP wrote to clean upgrade root: $root" }
    }

    Write-Output 'DISTRIBUTION_UPGRADE_OK=1'
    Write-Output 'DISTRIBUTION_UPGRADE_MODE=side-by-side'
    Write-Output "DISTRIBUTION_OLD=$old"
    Write-Output "DISTRIBUTION_NEW=$new"
}
finally {
    foreach ($process in $processes) {
        if ($process -and -not $process.HasExited) { $process.Kill($true) }
        if ($process) { $process.Dispose() }
    }
    foreach ($root in $roots) {
        if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
