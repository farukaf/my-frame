[CmdletBinding()]
param(
    [string]$CasesPath = (Join-Path $PSScriptRoot '..\docs\avaliacao\f10-cases.json'),
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
$cases = @(Get-Content -LiteralPath $CasesPath -Raw | ConvertFrom-Json)
if ($cases.Count -lt 3) { throw 'F10 precisa de pelo menos três casos.' }

foreach ($case in $cases) {
    foreach ($field in @('id', 'question', 'required', 'critical_failures')) {
        if ($null -eq $case.$field -or [string]::IsNullOrWhiteSpace([string]$case.$field)) {
            throw "Caso F10 inválido: campo '$field' ausente em '$($case.id)'."
        }
    }
    if (@($case.required).Count -eq 0 -or @($case.critical_failures).Count -eq 0) {
        throw "Caso F10 inválido: requisitos/falhas vazios em '$($case.id)'."
    }
}

if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    Write-Output "CASES_OK=$($cases.Count)"
    Write-Output 'RESULTS=NOT_PROVIDED'
    exit 0
}

if (-not (Test-Path -LiteralPath $ResultsDirectory -PathType Container)) {
    throw "Diretório de resultados não encontrado: $ResultsDirectory"
}

$resultFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.json' -File)
$byId = @{}
foreach ($file in $resultFiles) {
    $result = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$result.caseId)) { throw "Resultado sem caseId: $($file.Name)" }
    if ($byId.ContainsKey([string]$result.caseId)) { throw "Resultado duplicado: $($result.caseId)" }
    foreach ($field in @('snapshotId', 'toolCalls', 'sources', 'coverage', 'criticalFailures')) {
        if ($null -eq $result.$field) { throw "Resultado $($file.Name) sem campo '$field'." }
    }
    if (@($result.criticalFailures).Count -gt 0) { throw "Falha crítica registrada em $($file.Name)." }
    $byId[[string]$result.caseId] = $result
}

$missing = @($cases | Where-Object { -not $byId.ContainsKey([string]$_.id) })
if ($missing.Count -gt 0) { throw "Resultados ausentes: $($missing.id -join ', ')" }

Write-Output "CASES_OK=$($cases.Count)"
Write-Output "RESULTS_OK=$($byId.Count)"
