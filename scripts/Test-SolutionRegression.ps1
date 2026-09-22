[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projects = @(
    (Join-Path $root 'MyFrame.Core.Tests\MyFrame.Core.Tests.csproj'),
    (Join-Path $root 'MyFrame.Mcp.Tests\MyFrame.Mcp.Tests.csproj')
)
$common = @('test', '--no-restore', '-c', $Configuration, '-m:1', '-p:TestTfmsInParallel=false', '-v:minimal')
if ($NoBuild) { $common += '--no-build' }
$passed = 0
foreach ($project in $projects) {
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Test project is missing: $project" }
    Write-Output "REGRESSION_PROJECT=$([IO.Path]::GetFileNameWithoutExtension($project))"
    & dotnet @common $project
    if ($LASTEXITCODE -ne 0) { throw "Failure in $project (exit $LASTEXITCODE)." }
    $passed++
}
Write-Output 'SOLUTION_REGRESSION_OK=1'
Write-Output "SOLUTION_REGRESSION_PROJECTS=$passed"
Write-Output 'SOLUTION_REGRESSION_PARALLELISM=sequential'
