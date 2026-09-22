[CmdletBinding()]
param(
    [string]$SkillsRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SkillsRoot)) { $SkillsRoot = Join-Path $PSScriptRoot '..\skills' }
$root = (Resolve-Path -LiteralPath $SkillsRoot).Path
$common = Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw
if ($common -notmatch '\(v5,') {
    throw 'Skills README must advertise the current skill version 5.'
}
if ($common -notmatch 'get_capture_inbox_status' -or
    $common -notmatch 'heartbeatFresh' -or
    $common -notmatch 'validMarkers') {
    throw 'Common skills contract is missing collector readiness requirements.'
}

foreach ($name in @('warframe-builds', 'warframe-farm')) {
    $path = Join-Path $root "$name\SKILL.md"
    $text = Get-Content -LiteralPath $path -Raw
    if ($text -notmatch '(?ms)^metadata:\s*\r?\n\s+version:\s+"5"') {
        throw "$name must declare skill version 5."
    }
    foreach ($required in @('get_capture_inbox_status', 'state=ready', 'heartbeatFresh=true', 'validMarkers>0', 'unverified')) {
        if ($text -notmatch [regex]::Escape($required)) {
            throw "$name is missing readiness requirement: $required"
        }
    }
    $provenanceRequired = @('activeRevisionId', 'parserVersion', 'worldstate-community-1', 'worldstate-1')
    if ($name -eq 'warframe-farm') { $provenanceRequired += 'worldStateParserVersion' }
    foreach ($required in $provenanceRequired) {
        if ($text -notmatch [regex]::Escape($required)) {
            throw "$name is missing World State provenance requirement: $required"
        }
    }
}

Write-Output 'SKILLS_CONTRACT_OK=1'
