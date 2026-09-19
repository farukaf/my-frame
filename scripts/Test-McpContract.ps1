param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repository 'MyFrame.Mcp.Tests\MyFrame.Mcp.Tests.csproj'

if (-not $NoBuild) {
    dotnet build $project --no-restore -c $Configuration -v:minimal -m:1 -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw "MCP contract build failed with exit code $LASTEXITCODE." }
}

$arguments = @('test', $project, '--no-build', '-c', $Configuration, '-v:minimal')
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "MCP contract tests failed with exit code $LASTEXITCODE." }

Write-Output 'MCP_CONTRACT_OK=1'
Write-Output 'MCP_CONTRACT_SCOPE=stdio,schemas,pagination,cursors,errors,context-switch,readonly'
