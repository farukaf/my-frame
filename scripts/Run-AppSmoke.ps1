[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [int] $StartupTimeoutSeconds = 8
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'MyFrame.App\MyFrame.App.csproj'
$app = Join-Path $repoRoot "MyFrame.App\bin\$Configuration\net10.0-windows10.0.19041.0\win-x64\MyFrame.App.exe"
$startupLog = Join-Path $env:LOCALAPPDATA 'MyFrame\startup-error.log'

dotnet build $project --configuration $Configuration --no-restore -v:minimal
if ($LASTEXITCODE -ne 0) { throw "The MAUI app build failed." }
if (-not (Test-Path -LiteralPath $app)) { throw "The MAUI app executable was not produced: $app" }

if (Test-Path -LiteralPath $startupLog) { Remove-Item -LiteralPath $startupLog -Force }
$process = Start-Process -FilePath $app -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds $StartupTimeoutSeconds
    if ($process.HasExited) {
        $details = if (Test-Path -LiteralPath $startupLog) { Get-Content -LiteralPath $startupLog -Raw } else { 'No startup log was produced.' }
        throw "MyFrame.App exited with code $($process.ExitCode). $details"
    }
    if (Test-Path -LiteralPath $startupLog) {
        $details = Get-Content -LiteralPath $startupLog -Raw
        if ($details.Trim().Length -gt 0) { throw "MyFrame.App reported a startup error: $details" }
    }
    Write-Output "MAUI Windows smoke passed: process stayed alive for $StartupTimeoutSeconds seconds."
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    $process.Dispose()
}
