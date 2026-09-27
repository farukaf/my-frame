[CmdletBinding()]
param(
    [string]$SkillsRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SkillsRoot)) { $SkillsRoot = Join-Path $PSScriptRoot '..\skills' }
$root = (Resolve-Path -LiteralPath $SkillsRoot).Path
$common = Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw
if ($common -notmatch 'build \(v6\)' -or $common -notmatch 'farm \(v5\)' -or
    $common -notmatch 'economy \(v2\)') {
    throw 'Skills README must advertise the current skill versions.'
}
if ($common -notmatch 'get_capture_inbox_status' -or
    $common -notmatch 'heartbeatFresh' -or
    $common -notmatch 'validMarkers') {
    throw 'Common skills contract is missing collector readiness requirements.'
}

$versions = @{ 'warframe-builds' = '6'; 'warframe-farm' = '5' }
foreach ($name in $versions.Keys) {
    $path = Join-Path $root "$name\SKILL.md"
    $text = Get-Content -LiteralPath $path -Raw
    $version = $versions[$name]
    if ($text -notmatch "(?ms)^metadata:\s*\r?\n\s+version:\s+`"$version`"") {
        throw "$name must declare skill version $version."
    }
    foreach ($required in @('get_capture_inbox_status', 'state=ready', 'heartbeatFresh=true', 'validMarkers>0', 'unverified')) {
        if ($text -notmatch [regex]::Escape($required)) {
            throw "$name is missing readiness requirement: $required"
        }
    }
    if ($name -eq 'warframe-farm') {
        foreach ($required in @('activeRevisionId', 'parserVersion', 'worldstate-community-1', 'worldstate-1', 'worldStateParserVersion')) {
            if ($text -notmatch [regex]::Escape($required)) {
                throw "$name is missing World State provenance requirement: $required"
            }
        }
    }
}

Write-Output 'SKILLS_CONTRACT_OK=1'
