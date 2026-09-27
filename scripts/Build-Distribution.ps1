[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$OutputRoot = 'artifacts\distribution'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRootPath = if ([IO.Path]::IsPathRooted($OutputRoot)) {
    [IO.Path]::GetFullPath($OutputRoot)
} else {
    [IO.Path]::GetFullPath((Join-Path $repository $OutputRoot))
}
$repositoryPrefix = $repository.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $outputRootPath.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Output must remain inside the repository: $outputRootPath"
}

$publishPath = Join-Path $outputRootPath 'publish'
$packagesPath = Join-Path $outputRootPath 'packages'
foreach ($path in @($publishPath, $packagesPath)) {
    if (Test-Path -LiteralPath $path) {
        $item = Get-Item -LiteralPath $path
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing to clean redirected output: $path"
        }
        Remove-Item -LiteralPath $path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $path | Out-Null
}

$displayVersion = ($Version -split '-', 2)[0]
$applicationProject = Join-Path $repository 'MyFrame.App\MyFrame.App.csproj'
$syncProject = Join-Path $repository 'MyFrame.Sync\MyFrame.Sync.csproj'

& dotnet publish $applicationProject `
    --framework 'net10.0-windows10.0.19041.0' `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishPath `
    -p:RuntimeIdentifierOverride=$RuntimeIdentifier `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:Version=$Version `
    -p:ApplicationDisplayVersion=$displayVersion
if ($LASTEXITCODE -ne 0) { throw "Application publish failed with exit code $LASTEXITCODE." }

& dotnet publish $syncProject `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishDir="$publishPath\" `
    -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "Sync publish failed with exit code $LASTEXITCODE." }

$required = @('MyFrame.App.exe', 'MyFrame.Mcp.exe', 'MyFrame.Sync.exe')
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath $name) -PathType Leaf)) {
        throw "Distribution is missing $name"
    }
}

& dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "Tool restore failed with exit code $LASTEXITCODE." }

& dotnet tool run vpk pack `
    --packId 'MyFrame' `
    --packVersion $Version `
    --packDir $publishPath `
    --mainExe 'MyFrame.App.exe' `
    --packTitle 'My Frame' `
    --packAuthors 'My Frame' `
    --runtime $RuntimeIdentifier `
    --outputDir $packagesPath
if ($LASTEXITCODE -ne 0) { throw "Velopack packaging failed with exit code $LASTEXITCODE." }

foreach ($pattern in @('*-Setup.exe', '*-Portable.zip', '*-full.nupkg')) {
    if (-not (Get-ChildItem -LiteralPath $packagesPath -Filter $pattern -File)) {
        throw "Distribution package is missing $pattern"
    }
}

Write-Output "DISTRIBUTION_PUBLISH=$publishPath"
Write-Output "DISTRIBUTION_PACKAGES=$packagesPath"
Write-Output 'DISTRIBUTION_PACKAGE_OK=1'
