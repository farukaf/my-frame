param(
    [Parameter(Mandatory = $true)]
    [string]$IconPath
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$source = Join-Path $repository 'MyFrame.Collector.Overwolf'
$output = Join-Path $repository 'artifacts\collector-overwolf'
$icon = (Resolve-Path -LiteralPath $IconPath).Path
if (-not $icon.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use an existing generated My Frame icon inside this repository.'
}
$bytes = [IO.File]::ReadAllBytes($icon)
if ($bytes.Length -gt 30720 -or $bytes.Length -lt 24 -or
    [BitConverter]::ToString($bytes[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A' -or
    [BitConverter]::ToString($bytes[16..23]) -ne '00-00-01-00-00-00-01-00') {
    throw 'Expected an existing 256x256 PNG no larger than 30 KiB.'
}
if ((Test-Path -LiteralPath $output) -and
    ((Get-Item -LiteralPath $output).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'Refusing a redirected output directory.'
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
foreach ($name in @('manifest.json', 'index.html', 'style.css', 'ui.mjs', 'probe.mjs', 'collector.mjs')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $output $name) -Force
}
Copy-Item -LiteralPath $icon -Destination (Join-Path $output 'icon.png') -Force
Write-Output $output
