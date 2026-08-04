[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot 'SOURCE_MANIFEST.sha256'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Missing source manifest: $manifestPath"
}

$expected = [ordered]@{}
foreach ($line in Get-Content -LiteralPath $manifestPath) {
    if ([String]::IsNullOrWhiteSpace($line)) { continue }
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') {
        throw "Invalid source-manifest line: $line"
    }
    $expected[$Matches[2]] = $Matches[1]
}

$excludedRoots = @('.git', 'artifacts')
$actualPaths = Get-ChildItem -LiteralPath $repoRoot -File -Recurse | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($repoRoot, $_.FullName).Replace('\', '/')
    $top = $relative.Split('/')[0]
    if ($excludedRoots -contains $top) { return }
    if ($relative -eq 'SOURCE_MANIFEST.sha256') { return }
    $relative
} | Sort-Object

$missingFromManifest = @($actualPaths | Where-Object { -not $expected.Contains($_) })
$missingFromTree = @($expected.Keys | Where-Object { -not (Test-Path -LiteralPath (Join-Path $repoRoot $_) -PathType Leaf) })
if ($missingFromManifest.Count -gt 0 -or $missingFromTree.Count -gt 0) {
    if ($missingFromManifest.Count -gt 0) {
        Write-Error ("Files not covered by SOURCE_MANIFEST.sha256:`n" + ($missingFromManifest -join "`n"))
    }
    if ($missingFromTree.Count -gt 0) {
        Write-Error ("Manifest entries missing from the repository:`n" + ($missingFromTree -join "`n"))
    }
    throw 'Source manifest file set mismatch.'
}

$hashMismatch = $false
foreach ($relative in $expected.Keys) {
    $path = Join-Path $repoRoot $relative
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected[$relative]) {
        Write-Host "ACTUAL_HASH $actual  $relative"
        $hashMismatch = $true
    }
}
if ($hashMismatch) { throw 'Source manifest hash mismatch.' }

Write-Host "Verified $($expected.Count) repository files against SOURCE_MANIFEST.sha256."
