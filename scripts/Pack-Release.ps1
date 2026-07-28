[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Version,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot 'Modules/TORCareerUniques/SubModule.xml'

[xml]$module = Get-Content -LiteralPath $manifestPath
$manifestVersion = [string]$module.Module.Version.value
if ($manifestVersion -notmatch '^v([0-9]+\.[0-9]+\.[0-9]+)$') {
    throw "Unexpected SubModule.xml version: $manifestVersion"
}
$resolvedVersion = $Matches[1]
if ([String]::IsNullOrWhiteSpace($Version)) {
    $Version = $resolvedVersion
} elseif ($Version -ne $resolvedVersion) {
    throw "Requested version $Version does not match SubModule.xml version $resolvedVersion."
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration
} else {
    & (Join-Path $PSScriptRoot 'Verify-Source.ps1')
}

$artifacts = Join-Path $repoRoot 'artifacts'
$release = Join-Path $artifacts 'release'
$staging = Join-Path $artifacts 'staging'
Remove-Item -LiteralPath $release -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $release | Out-Null

$cleanRoot = Join-Path $staging 'clean'
$cleanModule = Join-Path $cleanRoot 'Modules/TORCareerUniques'
$cleanBin = Join-Path $cleanModule 'bin/Win64_Shipping_Client'
New-Item -ItemType Directory -Path $cleanBin -Force | Out-Null
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $cleanModule 'SubModule.xml')

$assemblies = @(
    'TORCareerUniques',
    'TORCareerUniques.HostNavigationSafety',
    'TORCareerUniques.UIIconPassThrough',
    'TORCareerUniques.TavernRumors',
    'TORCareerUniques.CompatibilityFixes'
)
foreach ($assembly in $assemblies) {
    $projectOutput = Join-Path $repoRoot "artifacts/bin/$assembly/$Configuration"
    Copy-Item -LiteralPath (Join-Path $projectOutput "$assembly.dll") -Destination $cleanBin
    $deps = Join-Path $projectOutput "$assembly.deps.json"
    if (Test-Path -LiteralPath $deps -PathType Leaf) {
        Copy-Item -LiteralPath $deps -Destination $cleanBin
    }
}

$fullRoot = Join-Path $staging 'full'
New-Item -ItemType Directory -Path $fullRoot -Force | Out-Null
$excludedTop = @('.git', 'artifacts')
Get-ChildItem -LiteralPath $repoRoot -Force | Where-Object { $excludedTop -notcontains $_.Name } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $fullRoot -Recurse -Force
}
$fullBin = Join-Path $fullRoot 'Modules/TORCareerUniques/bin/Win64_Shipping_Client'
New-Item -ItemType Directory -Path $fullBin -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $cleanBin '*') -Destination $fullBin -Force

$cleanName = "TOR_Career_Uniques_v${Version}_Bannerlord_1.3.15_TOR_1.16_CLEAN.zip"
$sourceName = "TOR_Career_Uniques_v${Version}_FULL_SOURCE_Bannerlord_1.3.15_TOR_1.16.zip"
$cleanZip = Join-Path $release $cleanName
$sourceZip = Join-Path $release $sourceName
Compress-Archive -Path (Join-Path $cleanRoot '*') -DestinationPath $cleanZip -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $fullRoot '*') -DestinationPath $sourceZip -CompressionLevel Optimal

$sumLines = foreach ($path in @($cleanZip, $sourceZip)) {
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($path))"
}
$sumLines | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii

Write-Host "Created release archives in $release"
