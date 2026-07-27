param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [string]$Version = "1.7.29",
    [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & "$PSScriptRoot/Build.ps1" -Configuration $Configuration }
$artifactRoot = Join-Path $root "artifacts"
$stage = Join-Path $artifactRoot "stage"
$release = Join-Path $artifactRoot "release"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item "$stage/Modules/TORCareerUniques/bin/Win64_Shipping_Client" -ItemType Directory -Force | Out-Null
New-Item $release -ItemType Directory -Force | Out-Null
Copy-Item "$root/Modules/TORCareerUniques/SubModule.xml" "$stage/Modules/TORCareerUniques/SubModule.xml"
$assemblies = @(
    "TORCareerUniques",
    "TORCareerUniques.HostNavigationSafety",
    "TORCareerUniques.UIIconPassThrough",
    "TORCareerUniques.TavernRumors",
    "TORCareerUniques.CompatibilityFixes"
)
foreach ($assembly in $assemblies) {
    $projectOutput = Join-Path $artifactRoot "bin/$assembly/$Configuration"
    $dll = Join-Path $projectOutput "$assembly.dll"
    if (-not (Test-Path $dll)) { throw "Missing build output: $dll" }
    Copy-Item $dll "$stage/Modules/TORCareerUniques/bin/Win64_Shipping_Client/"
    $pdb = Join-Path $projectOutput "$assembly.pdb"
    if (Test-Path $pdb) { Copy-Item $pdb "$stage/Modules/TORCareerUniques/bin/Win64_Shipping_Client/" }
    $deps = Join-Path $projectOutput "$assembly.deps.json"
    if (Test-Path $deps) { Copy-Item $deps "$stage/Modules/TORCareerUniques/bin/Win64_Shipping_Client/" }
}
$cleanName = "TOR_Career_Uniques_v${Version}_Bannerlord_1.3.15_TOR_1.16_CLEAN.zip"
$fullName = "TOR_Career_Uniques_v${Version}_FULL_SOURCE_Bannerlord_1.3.15_TOR_1.16.zip"
$cleanPath = Join-Path $release $cleanName
$fullPath = Join-Path $release $fullName
Remove-Item $cleanPath,$fullPath -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$stage/Modules" -DestinationPath $cleanPath -CompressionLevel Optimal
$fullStage = Join-Path $artifactRoot "full-source"
Remove-Item $fullStage -Recurse -Force -ErrorAction SilentlyContinue
New-Item $fullStage -ItemType Directory -Force | Out-Null
$exclude = @('.git','artifacts')
Get-ChildItem $root -Force | Where-Object { $exclude -notcontains $_.Name } | Copy-Item -Destination $fullStage -Recurse -Force
New-Item "$fullStage/Modules/TORCareerUniques/bin" -ItemType Directory -Force | Out-Null
Copy-Item "$stage/Modules/TORCareerUniques/bin/*" "$fullStage/Modules/TORCareerUniques/bin/" -Recurse -Force
Compress-Archive -Path "$fullStage/*" -DestinationPath $fullPath -CompressionLevel Optimal
$hashes = @($cleanPath,$fullPath) | ForEach-Object {
    $hash = (Get-FileHash -Algorithm SHA256 $_).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($_))"
}
$hashes | Set-Content (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
Get-ChildItem $release | Format-Table Name,Length
