[CmdletBinding()]
param(
    [string]$CasesPath,
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($CasesPath)) { $CasesPath = Join-Path $PSScriptRoot '..\docs\avaliacao\f10-cases.json' }
$parsedCases = Get-Content -LiteralPath $CasesPath -Raw | ConvertFrom-Json
$cases = @($parsedCases)
if ($cases.Count -lt 3) { throw 'F10 requires at least three cases.' }

foreach ($case in $cases) {
    foreach ($field in @('id', 'question', 'required', 'critical_failures')) {
        if ($null -eq $case.$field -or [string]::IsNullOrWhiteSpace([string]$case.$field)) {
            throw "Invalid F10 case: field '$field' is missing from '$($case.id)'."
        }
    }
    if (@($case.required).Count -eq 0 -or @($case.critical_failures).Count -eq 0) {
        throw "Invalid F10 case: requirements or failures are empty in '$($case.id)'."
    }
}

if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    Write-Output "CASES_OK=$($cases.Count)"
    Write-Output 'RESULTS=NOT_PROVIDED'
    exit 0
}

if (-not (Test-Path -LiteralPath $ResultsDirectory -PathType Container)) {
    throw "Results directory was not found: $ResultsDirectory"
}

$caseIds = @{}
foreach ($case in $cases) { $caseIds[[string]$case.id] = $true }
$resultFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.json' -File)
$byId = @{}
foreach ($file in $resultFiles) {
    $result = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$result.caseId)) { throw "Result has no caseId: $($file.Name)" }
    if (-not $caseIds.ContainsKey([string]$result.caseId)) { throw "Result uses an unknown official caseId: $($result.caseId)" }
    if ($byId.ContainsKey([string]$result.caseId)) { throw "Duplicate result: $($result.caseId)" }
    foreach ($field in @('snapshotId', 'sourceRevision', 'toolCalls', 'sources', 'coverage', 'criticalFailures', 'model', 'skillVersion', 'latencyMs')) {
        if ($null -eq $result.$field) { throw "Result $($file.Name) has no '$field' field." }
    }
    if ([string]::IsNullOrWhiteSpace([string]$result.snapshotId) -or
        [string]::IsNullOrWhiteSpace([string]$result.sourceRevision) -or
        [string]::IsNullOrWhiteSpace([string]$result.model) -or
        [string]::IsNullOrWhiteSpace([string]$result.skillVersion)) {
        throw "Result $($file.Name) has incomplete identity, revision, or model information."
    }
    if (@($result.toolCalls).Count -eq 0) { throw "Result $($file.Name) did not record MCP calls." }
    foreach ($call in @($result.toolCalls)) {
        if ([string]::IsNullOrWhiteSpace([string]$call.name)) { throw "Result $($file.Name) has an unnamed MCP call." }
    }
    if (-not ($result.latencyMs -is [int] -or $result.latencyMs -is [long] -or $result.latencyMs -is [double] -or $result.latencyMs -is [decimal]) -or [double]$result.latencyMs -lt 0) {
        throw "Result $($file.Name) has invalid latencyMs."
    }
    if (@($result.criticalFailures).Count -gt 0) { throw "Critical failure recorded in $($file.Name)." }
    $byId[[string]$result.caseId] = $result
}

$missing = @($cases | Where-Object { -not $byId.ContainsKey([string]$_.id) })
if ($missing.Count -gt 0) {
    Write-Output "RESULTS_MISSING=$($missing.id -join ',')"
    throw "Missing results: $($missing.id -join ', ')"
}

Write-Output "CASES_OK=$($cases.Count)"
Write-Output "RESULTS_OK=$($byId.Count)"
