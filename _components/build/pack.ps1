#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Packs Fubar.Controls into artifacts/packages, and optionally drops the .nupkg straight into a
    consuming repo's local-packages feed so you can test a real package before publishing.

.DESCRIPTION
    Versions normally come from git tags via MinVer. For local testing that usually yields an ugly
    prerelease like 0.1.1-alpha.0.3, which is fine but awkward to reference; pass -Version to pin one.

    The tighter inner loop for day-to-day work is NOT this script - it is building the consuming app
    with -p:UseLocalComponents=true, which swaps the PackageReference for a ProjectReference. Use
    this script when you specifically want to validate the packaged artifact.

.EXAMPLE
    ./build/pack.ps1
    ./build/pack.ps1 -Version 0.1.0
    ./build/pack.ps1 -Version 0.1.0 -PushTo ../fubar-api-studio, ../fubar-diff
#>
param(
    [string]   $Configuration = 'Release',
    [string]   $Version = '',
    [string[]] $PushTo = @()
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir   = Join-Path $repoRoot 'artifacts/packages'
$project  = Join-Path $repoRoot 'src/Fubar.Controls/Fubar.Controls.csproj'

if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }

$packArgs = @('pack', $project, '-c', $Configuration, '--nologo')
if ($Version) {
    # MinVerVersionOverride bypasses the tag-derived version without touching any file.
    $packArgs += "-p:MinVerVersionOverride=$Version"
}

Write-Host "==> Packing Fubar.Controls ($Configuration)" -ForegroundColor Cyan
& dotnet @packArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed with exit code $LASTEXITCODE" }

$packages = Get-ChildItem -Path $outDir -Filter '*.nupkg'
Write-Host "`n==> Packed:" -ForegroundColor Green
$packages | ForEach-Object { Write-Host "    $($_.Name)" }

foreach ($target in $PushTo) {
    $feed = Join-Path (Resolve-Path $target) 'local-packages'
    if (-not (Test-Path $feed)) {
        Write-Warning "no local-packages folder in $target - skipping"
        continue
    }
    # Clear stale copies first: NuGet resolves the highest version in the folder, so leaving an older
    # build behind is harmless, but leaving a NEWER one silently wins over what you just packed.
    Get-ChildItem -Path $feed -Filter 'Fubar.Controls.*.nupkg' | Remove-Item -Force
    $packages | Copy-Item -Destination $feed
    Write-Host "==> Copied to $feed" -ForegroundColor Green
}
