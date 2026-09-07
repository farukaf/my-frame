[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('win-x64')]
    [string] $RuntimeIdentifier = 'win-x64',

    [string] $OutputRoot = 'artifacts\distribution'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRootPath = if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    [System.IO.Path]::GetFullPath($OutputRoot)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputRoot))
}

$repositoryPrefix = $repositoryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $outputRootPath.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "O diretório de saída deve estar dentro do repositório: $outputRootPath"
}

$projectPath = Join-Path $repositoryRoot 'MyFrame.App\MyFrame.App.csproj'
$publishPath = Join-Path $outputRootPath 'publish'
$packagesPath = Join-Path $outputRootPath 'packages'

foreach ($path in @($publishPath, $packagesPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $path | Out-Null
}

$displayVersion = ($Version -split '-', 2)[0]

Write-Host "Publicando My Frame $Version para $RuntimeIdentifier..."
& dotnet publish $projectPath `
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

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish falhou com o código $LASTEXITCODE."
}

$applicationPath = Join-Path $publishPath 'MyFrame.App.exe'
if (-not (Test-Path -LiteralPath $applicationPath)) {
    throw "O executável esperado não foi gerado: $applicationPath"
}

Write-Host 'Restaurando a ferramenta de empacotamento...'
& dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet tool restore falhou com o código $LASTEXITCODE."
}

Write-Host 'Gerando o instalador one-click, a versão portátil e os pacotes de atualização...'
& dotnet tool run vpk pack `
    --packId 'MyFrame' `
    --packVersion $Version `
    --packDir $publishPath `
    --mainExe 'MyFrame.App.exe' `
    --packTitle 'My Frame' `
    --packAuthors 'My Frame' `
    --runtime $RuntimeIdentifier `
    --outputDir $packagesPath

if ($LASTEXITCODE -ne 0) {
    throw "O empacotamento com Velopack falhou com o código $LASTEXITCODE."
}

if (-not (Get-ChildItem -LiteralPath $packagesPath -Filter '*-Setup.exe')) {
    throw 'O Setup.exe esperado não foi gerado pelo Velopack.'
}

if (-not (Get-ChildItem -LiteralPath $packagesPath -Filter '*-Portable.zip')) {
    throw 'O pacote portátil esperado não foi gerado pelo Velopack.'
}

Write-Host "Pacotes gerados em $packagesPath"
Get-ChildItem -LiteralPath $packagesPath | Select-Object Name, Length
