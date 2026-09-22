param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.0.1',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repository 'artifacts'
$output = Join-Path $artifactRoot "MyFrame-$Version-$RuntimeIdentifier"

if (Test-Path -LiteralPath $output) {
    $resolved = (Resolve-Path -LiteralPath $output).Path
    $resolvedArtifactRoot = (Resolve-Path -LiteralPath $artifactRoot).Path
    if (-not $resolved.StartsWith($resolvedArtifactRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean output outside artifacts: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
dotnet publish (Join-Path $repository 'MyFrame.App\MyFrame.App.csproj') `
    -c $Configuration -f net10.0-windows10.0.19041.0 -r $RuntimeIdentifier `
    --self-contained true -p:PublishSingleFile=true -p:PublishDir="$output\"
dotnet publish (Join-Path $repository 'MyFrame.Sync\MyFrame.Sync.csproj') `
    -c $Configuration -r $RuntimeIdentifier `
    --self-contained true -p:PublishSingleFile=true -p:PublishDir="$output\"

$required = @('MyFrame.App.exe', 'MyFrame.Mcp.exe', 'MyFrame.Sync.exe')
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $output $name))) {
        throw "Distribution is missing $name"
    }
}

Write-Output $output
