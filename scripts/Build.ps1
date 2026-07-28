[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot 'Verify-Source.ps1')

$solution = Join-Path $repoRoot 'TORCareerUniques.sln'
& dotnet restore $solution --configfile (Join-Path $repoRoot 'nuget.config')
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

& dotnet build $solution --configuration $Configuration --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

$expectedAssemblies = @(
    'TORCareerUniques',
    'TORCareerUniques.HostNavigationSafety',
    'TORCareerUniques.UIIconPassThrough',
    'TORCareerUniques.TavernRumors',
    'TORCareerUniques.CompatibilityFixes'
)
foreach ($assembly in $expectedAssemblies) {
    $dll = Join-Path $repoRoot "artifacts/bin/$assembly/$Configuration/$assembly.dll"
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
        throw "Expected build output is missing: $dll"
    }
}

Write-Host "Built all five runtime assemblies in $Configuration configuration."
