$ErrorActionPreference = 'Stop'

$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$roots = @('AGENTS.md', 'CLAUDE.md', 'README.md', 'todo.md', 'docs', 'skills', 'examples', 'scripts', 'MyFrame.App', 'MyFrame.Collector.Overwolf')
$extensions = @('.cs', '.csproj', '.css', '.html', '.json', '.md', '.mjs', '.ps1', '.xaml', '.xml')
$excludedDirectories = @('docs\fixtures', 'MyFrame.Collector.Overwolf\tests')
$violations = [System.Collections.Generic.List[string]]::new()

foreach ($relativeRoot in $roots) {
    $path = Join-Path $repository $relativeRoot
    if (-not (Test-Path -LiteralPath $path)) { continue }

    $files = if (Test-Path -LiteralPath $path -PathType Leaf) { Get-Item -LiteralPath $path } else {
        Get-ChildItem -LiteralPath $path -Recurse -File | Where-Object { $extensions -contains $_.Extension }
    }

    foreach ($file in $files) {
        $relativePath = [IO.Path]::GetRelativePath($repository, $file.FullName)
        if ($excludedDirectories | Where-Object { $relativePath.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }) { continue }
        $lines = @(Get-Content -LiteralPath $file.FullName)
        for ($index = 0; $index -lt $lines.Count; $index++) {
            $hasDiacritic = $false
            foreach ($character in $lines[$index].ToCharArray()) {
                $codePoint = [int][char]$character
                if (($codePoint -ge 0x00C0 -and $codePoint -le 0x00D6) -or
                    ($codePoint -ge 0x00D8 -and $codePoint -le 0x00F6) -or
                    ($codePoint -ge 0x00F8 -and $codePoint -le 0x024F)) {
                    $hasDiacritic = $true
                    break
                }
            }
            if ($hasDiacritic) {
                $violations.Add("${relativePath}:$($index + 1): $($lines[$index].Trim())")
            }
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    throw "English-content validation failed with $($violations.Count) violation(s)."
}

Write-Output 'ENGLISH_CONTENT_OK=1'
