# Building TOR Career Uniques

## Requirements

- .NET 8 SDK
- Internet access for NuGet restore

The projects target .NET Standard 2.0 and compile against Bannerlord 1.3.15 reference assemblies. Game and TOR binaries are not committed.

## Build

```powershell
./scripts/Build.ps1 -Configuration Release
```

The build starts by verifying every tracked source/build file against `SOURCE_MANIFEST.sha256`, restores the pinned NuGet dependencies, and compiles all five runtime assemblies.

## Package

```powershell
./scripts/Pack-Release.ps1 -Configuration Release -Version 1.7.29
```

This creates a clean install archive, a full-source archive, and `SHA256SUMS.txt` under `artifacts/release`. GitHub Actions executes the same scripts for pull requests, `main`, and release tags.

## Pinned compile dependencies

- `Bannerlord.ReferenceAssemblies.Core` 1.3.15.110062
- `Bannerlord.MCM` 5.12.1
- `Lib.Harmony` 2.3.3
