param(
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PackagePath)) { $PackagePath = Join-Path $PSScriptRoot '..\MyFrame.Collector.Overwolf' }
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$manifestPath = Join-Path $package 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Collector package is missing manifest.json: $package"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.manifest_version -ne 1 -or $manifest.type -ne 'WebApp') {
    throw 'Collector manifest must declare manifest_version=1 and type=WebApp.'
}
foreach ($property in @('name', 'author', 'version', 'minimum-overwolf-version', 'description', 'icon')) {
    if ([string]::IsNullOrWhiteSpace([string]$manifest.meta.$property)) {
        throw "Collector manifest is missing meta.$property."
    }
}
if (-not ($manifest.data.game_events -contains 8954)) {
    throw 'Collector manifest must target Warframe game id 8954.'
}
foreach ($name in @('index.html', 'style.css', 'ui.mjs', 'probe.mjs', 'collector.mjs', [string]$manifest.meta.icon)) {
    $path = Join-Path $package $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Collector package is missing $name."
    }
}

$iconPath = Join-Path $package ([string]$manifest.meta.icon)
$iconBytes = [IO.File]::ReadAllBytes($iconPath)
$pngSignature = '89-50-4E-47-0D-0A-1A-0A'
if ($iconBytes.Length -lt 24 -or [BitConverter]::ToString($iconBytes[0..7]) -ne $pngSignature) {
    throw 'Collector icon must be a PNG.'
}
$width = ([uint32]$iconBytes[16] -shl 24) -bor ([uint32]$iconBytes[17] -shl 16) -bor
    ([uint32]$iconBytes[18] -shl 8) -bor [uint32]$iconBytes[19]
$height = ([uint32]$iconBytes[20] -shl 24) -bor ([uint32]$iconBytes[21] -shl 16) -bor
    ([uint32]$iconBytes[22] -shl 8) -bor [uint32]$iconBytes[23]
if ($width -ne 256 -or $height -ne 256 -or $iconBytes.Length -gt 30720) {
    throw "Collector icon must be 256x256 and <= 30 KiB (actual ${width}x${height}, $($iconBytes.Length) bytes)."
}

Write-Output "Collector package valid: $package"
