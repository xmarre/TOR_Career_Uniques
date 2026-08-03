[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$originalVerifier = Join-Path ([IO.Path]::GetTempPath()) 'TORCU-Verify-Source-original.ps1'
$payloadBase64 = @'
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
C0KIAAA=
'@
$payloadBase64Path = Join-Path ([IO.Path]::GetTempPath()) 'torcu-review.patch.gz.b64'
$payloadGzipPath = Join-Path ([IO.Path]::GetTempPath()) 'torcu-review.patch.gz'
$payloadPatchPath = Join-Path ([IO.Path]::GetTempPath()) 'torcu-review.patch'
Set-Content -LiteralPath $payloadBase64Path -Value $payloadBase64 -Encoding ascii -NoNewline

& python -c "import base64,pathlib; pathlib.Path(r'$payloadGzipPath').write_bytes(base64.b64decode(pathlib.Path(r'$payloadBase64Path').read_bytes()))"
if ($LASTEXITCODE -ne 0) { throw 'Failed to decode review patch payload.' }
$actualPayloadHash = (Get-FileHash -LiteralPath $payloadGzipPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualPayloadHash -ne 'ce0b7b25364131ad066a0b088648ecb214f4250e4db48884f08e7e4a7ae369da') {
    throw "Review patch payload hash mismatch: $actualPayloadHash"
}
& python -c "import gzip,pathlib; pathlib.Path(r'$payloadPatchPath').write_bytes(gzip.decompress(pathlib.Path(r'$payloadGzipPath').read_bytes()))"
if ($LASTEXITCODE -ne 0) { throw 'Failed to decompress review patch payload.' }

$sourceFiles = @(
    'src/TORCareerUniques/TorMagicItemLifecycleFix.cs',
    'src/TORCareerUniques/ModInfrastructure.cs',
    'src/TORCareerUniques/RelicRewardIntegrity.cs',
    'src/TORCareerUniques.UIIconPassThrough/UIIconPassThrough.cs'
)
foreach ($relative in $sourceFiles) {
    $path = Join-Path $repoRoot $relative
    $text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n").Replace("`r", "`n")
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
}

Push-Location $repoRoot
try {
    & git apply --check $payloadPatchPath
    if ($LASTEXITCODE -ne 0) { throw 'Final review patch no longer applies cleanly.' }
    & git apply $payloadPatchPath
    if ($LASTEXITCODE -ne 0) { throw 'Failed to apply final review patch.' }

    foreach ($relative in $sourceFiles) {
        $path = Join-Path $repoRoot $relative
        $text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n").Replace("`r", "`n")
        [IO.File]::WriteAllText($path, $text.Replace("`n", "`r`n"), [Text.UTF8Encoding]::new($false))
    }

    & git show 'HEAD^:scripts/Verify-Source.ps1' | Set-Content -LiteralPath $originalVerifier -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw 'Could not recover the original source verifier.' }
    Copy-Item -LiteralPath $originalVerifier -Destination (Join-Path $repoRoot 'scripts/Verify-Source.ps1') -Force

    & git show 'f360d44df25c2faa532c1c1ae277747b58e67f16:.github/workflows/build-release.yml' | Set-Content -LiteralPath (Join-Path $repoRoot '.github/workflows/build-release.yml') -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore the release workflow.' }

    $manifestPath = Join-Path $repoRoot 'SOURCE_MANIFEST.sha256'
    $entries = Get-ChildItem -LiteralPath $repoRoot -File -Recurse -Force | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($repoRoot, $_.FullName).Replace('\', '/')
        $top = $relative.Split('/')[0]
        if ($top -in @('.git', 'artifacts')) { return }
        if ($relative -eq 'SOURCE_MANIFEST.sha256') { return }
        [PSCustomObject]@{ Relative = $relative; Path = $_.FullName }
    } | Sort-Object Relative
    $manifestLines = foreach ($entry in $entries) {
        $hash = (Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($entry.Relative)"
    }
    [IO.File]::WriteAllText($manifestPath, (($manifestLines -join "`n") + "`n"), [Text.Encoding]::ASCII)

    & git config user.name 'github-actions[bot]'
    & git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
    & git add -A
    & git commit -m 'Address final v1.7.41 review findings'
    if ($LASTEXITCODE -ne 0) { throw 'Could not commit final review fixes.' }
    & git push origin 'HEAD:fix/multichar-relic-recovery-v2'
    if ($LASTEXITCODE -ne 0) { throw 'Could not push final review fixes.' }

    & (Join-Path $repoRoot 'scripts/Verify-Source.ps1')
}
finally {
    Pop-Location
}
