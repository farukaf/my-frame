[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$ServerPath = '',
    [switch]$SkipInspector
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

function Invoke-Gate([string]$Name, [scriptblock]$Action) {
    & $Action
    Write-Output "NON_COLLECTOR_GATE_OK=$Name"
}

Invoke-Gate 'english-content' { & (Join-Path $PSScriptRoot 'Test-EnglishContent.ps1') }
Invoke-Gate 'f10-case-contract' { & (Join-Path $PSScriptRoot 'Test-F10Evaluation.ps1') }
Invoke-Gate 'solution-regression' { & (Join-Path $PSScriptRoot 'Test-SolutionRegression.ps1') -Configuration $Configuration }
Invoke-Gate 'mcp-contract' { & (Join-Path $PSScriptRoot 'Test-McpContract.ps1') -Configuration $Configuration -NoBuild }

if ([string]::IsNullOrWhiteSpace($ServerPath)) {
    $ServerPath = Join-Path $repository "MyFrame.Mcp\\bin\\$Configuration\\net10.0\\win-x64\\MyFrame.Mcp.exe"
}
Invoke-Gate 'mcp-read-only' { & (Join-Path $PSScriptRoot 'Test-McpReadOnly.ps1') -ServerPath $ServerPath }
if (-not $SkipInspector) {
    Invoke-Gate 'mcp-clean-install' { & (Join-Path $PSScriptRoot 'Test-McpCleanInstall.ps1') -ServerPath $ServerPath }
}

$remaining = @('community-source-approval', 'f10-results', 'distribution-package', 'upgrade-and-restore')
if ($SkipInspector) { $remaining += 'clean-install' }
Write-Output "NON_COLLECTOR_EXTERNAL_GATES=$($remaining -join ',')"
Write-Output 'NON_COLLECTOR_RELEASE_GATES_OK=1'
