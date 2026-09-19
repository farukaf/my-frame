[CmdletBinding()]
param(
    [string]$Url = 'https://content.warframe.com/dynamic/worldState.php',
    [string]$InputPath,
    [ValidateRange(1, 300)] [int]$TimeoutSec = 30
)

$ErrorActionPreference = 'Stop'
$handler = [System.Net.Http.HttpClientHandler]::new()
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)

try {
    if ([string]::IsNullOrWhiteSpace($InputPath)) {
        $response = $client.GetAsync($Url).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            throw "HTTP_$([int]$response.StatusCode)"
        }
        $transport = 'http'
        $httpOk = '1'
    }
    else {
        if (-not (Test-Path -LiteralPath $InputPath -PathType Leaf)) { throw "INPUT_NOT_FOUND=$InputPath" }
        $body = Get-Content -LiteralPath $InputPath -Raw
        $transport = 'fixture'
        $httpOk = 'SKIPPED'
    }
    $root = $body | ConvertFrom-Json
    if ($null -eq $root -or $root -is [array]) { throw 'WORLDSTATE_ROOT_INVALID' }

    $rootPropertyNames = @($root.PSObject.Properties | ForEach-Object Name)
    if ($rootPropertyNames -ccontains 'SyndicateMissions') {
        $schema = 'official'
        $missions = @($root.PSObject.Properties['SyndicateMissions'].Value)
    }
    elseif ($rootPropertyNames -ccontains 'syndicateMissions') {
        $schema = 'community'
        $missions = @($root.PSObject.Properties['syndicateMissions'].Value)
    }
    else { throw 'WORLDSTATE_SYNDICATE_MISSIONS_MISSING' }
    $rewardCount = 0
    $motherTokenCount = 0
    foreach ($mission in $missions) {
        $jobs = if ($null -ne $mission.Jobs) { @($mission.Jobs) } else { @($mission.jobs) }
        foreach ($job in $jobs) {
            $dropValue = if ($null -ne $job.rewardPoolDrops) { $job.rewardPoolDrops } else { $job.RewardPoolDrops }
            $drops = if ($null -eq $dropValue) { @() } else { @($dropValue) }
            $rewardCount += @($drops).Length
            $motherTokenCount += @($drops | Where-Object { [string]$_.item -match '(?i)mother token' -or [string]$_.Item -match '(?i)mother token' }).Length
        }
    }

    Write-Output "WORLDSTATE_HTTP_OK=$httpOk"
    Write-Output "WORLDSTATE_TRANSPORT=$transport"
    Write-Output "WORLDSTATE_SCHEMA=$schema"
    Write-Output "WORLDSTATE_BYTES=$([Text.Encoding]::UTF8.GetByteCount($body))"
    Write-Output "WORLDSTATE_MISSIONS=$($missions.Length)"
    Write-Output "WORLDSTATE_EXPLICIT_REWARDS=$rewardCount"
    Write-Output "WORLDSTATE_EXPLICIT_MOTHER_TOKENS=$motherTokenCount"
    exit 0
}
catch {
    Write-Output "WORLDSTATE_HTTP_OK=0"
    Write-Output "WORLDSTATE_ERROR=$($_.Exception.Message)"
    exit 2
}
finally {
    $client.Dispose()
    $handler.Dispose()
}
