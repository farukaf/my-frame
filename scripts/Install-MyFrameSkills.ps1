[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Claude', 'Codex', 'Both')]
    [string]$Client = 'Both',
    [string]$DestinationRoot,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $repository 'skills'
$skillNames = @('warframe-builds', 'warframe-farm', 'warframe-economy', 'warframe-research')
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $home = [Environment]::GetFolderPath('UserProfile')
    $codexHome = if ([string]::IsNullOrWhiteSpace($env:CODEX_HOME)) { Join-Path $home '.codex' } else { $env:CODEX_HOME }
    $claudeTarget = Join-Path $home '.claude\skills'
    $codexTarget = Join-Path $codexHome 'skills'
    $targets = switch ($Client) {
        'Claude' { @($claudeTarget) }
        'Codex' { @($codexTarget) }
        default { @($claudeTarget, $codexTarget) }
    }
} else { $targets = @([IO.Path]::GetFullPath($DestinationRoot)) }

foreach ($target in $targets) {
    foreach ($name in $skillNames) {
        $from = Join-Path $source $name
        $to = Join-Path $target $name
        if ((Test-Path -LiteralPath $to) -and -not $Force) {
            Write-Output "SKILL_EXISTS=$to"
            continue
        }
        if ($PSCmdlet.ShouldProcess($to, "Install $name skill")) {
            New-Item -ItemType Directory -Path $target -Force | Out-Null
            Copy-Item -LiteralPath $from -Destination $to -Recurse -Force
            Write-Output "SKILL_INSTALLED=$to"
        }
    }
}
