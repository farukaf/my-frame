[CmdletBinding()]
param(
    [string]$SyncPath,
    [string]$ServerPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SyncPath) -or [string]::IsNullOrWhiteSpace($ServerPath)) {
    $artifactRoot = Join-Path $PSScriptRoot '..\artifacts'
    $latest = Get-ChildItem -LiteralPath $artifactRoot -Directory -Filter 'MyFrame-*-win-x64' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $latest) { throw 'MCP_DISTRIBUTION_NOT_FOUND' }
    if ([string]::IsNullOrWhiteSpace($SyncPath)) { $SyncPath = Join-Path $latest.FullName 'MyFrame.Sync.exe' }
    if ([string]::IsNullOrWhiteSpace($ServerPath)) { $ServerPath = Join-Path $latest.FullName 'MyFrame.Mcp.exe' }
}
$sync = (Resolve-Path -LiteralPath $SyncPath).Path
$server = (Resolve-Path -LiteralPath $ServerPath).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ("my-frame-reference-smoke-" + [guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'reference.json'
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $document = '{"kind":"overframe","url":"https://overframe.gg/build/123","title":"Mesa reference","revision":"r1","license":"community","author":"tester","sections":[{"id":"mods","title":"Mods","content":"Use Serration for fire rate."}]}'
    [IO.File]::WriteAllText($source, $document)
    $syncResult = & $sync --reference-file $source --data-root $root | ConvertFrom-Json
    if ($syncResult.state -ne 'imported' -and $syncResult.state -ne 'already-imported') { throw "Reference import failed: $($syncResult | ConvertTo-Json -Compress)" }
    $out = Join-Path $root 'mcp.json'; $err = Join-Path $root 'mcp.err'
    $args = @('--yes','@modelcontextprotocol/inspector','--cli',$server,'-e',"MYFRAME_DATA_ROOT=$root",'--method','tools/call','--tool-name','search_references','--tool-arg','query=Serration','--format','json')
    $process = Start-Process -FilePath 'npx.cmd' -ArgumentList $args -WorkingDirectory (Split-Path $server) -Wait -PassThru -NoNewWindow -RedirectStandardOutput $out -RedirectStandardError $err
    if ($process.ExitCode -ne 0) { throw "Inspector failed: $(Get-Content -Raw $err)" }
    $line = Get-Content $out | Where-Object { $_ -match '^\{"result"' } | Select-Object -Last 1
    if (-not $line) { throw "Inspector returned no JSON result: $(Get-Content -Raw $err)" }
    $structured = ($line | ConvertFrom-Json).result.structuredContent
    [pscustomobject]@{
        importState = $syncResult.state
        referenceState = $structured.state
        documents = $structured.documents
        rejectedDocuments = $structured.rejectedDocuments
        hitKind = $structured.hits[0].kind
        hitLicense = $structured.hits[0].license
        hitAuthor = $structured.hits[0].author
        trustedForFacts = $structured.hits[0].trustedForFacts
    }
}
finally { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } }
