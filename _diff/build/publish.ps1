#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publishes Fubar Diff as self-contained binaries for one or more runtimes and packages each
    (zip for Windows, tar.gz for Linux, a .app bundle zipped for macOS).

.DESCRIPTION
    Works from any OS (PowerShell 7+). Cross-RID publishing produces the binaries + Avalonia native
    assets for the target platform without needing that platform. Packaging notes:
      - macOS .app: assembled here. The executable bit and symlinks are only preserved when the zip is
        made ON macOS (this script uses `ditto`/`chmod` there); zipping a macOS build from Windows/Linux
        loses the +x bit, so users would need `chmod +x` - run the macOS leg on a macOS/CI runner for a
        ready-to-launch .app. This is why the CI workflow builds each OS on its native runner.

.EXAMPLE
    ./build/publish.ps1                         # all default runtimes
    ./build/publish.ps1 -Runtimes osx-arm64     # just one
    ./build/publish.ps1 -Version 1.2.3
#>
param(
    [string[]] $Runtimes = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64'),
    [string]   $Configuration = 'Release',
    [string]   $Version = '',
    [string]   $ArtifactDir = 'artifacts'
)

$ErrorActionPreference = 'Stop'

$repoRoot    = Split-Path -Parent $PSScriptRoot
$project     = Join-Path $repoRoot 'src/Fubar.Diff.UI/Fubar.Diff.UI.csproj'
$exeName     = 'FubarDiff'    # matches <AssemblyName>
$displayName = 'Fubar Diff'
$bundleId    = 'dev.fubar.diff'

if ($Version) { $Version = $Version.TrimStart('v') }
$plistVersion = if ($Version) { $Version } else { '1.0.0' }

$artifacts = Join-Path $repoRoot $ArtifactDir
$publishRoot = Join-Path $artifacts 'publish'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

foreach ($rid in $Runtimes) {
    Write-Host "==> Publishing $rid ($Configuration, self-contained)" -ForegroundColor Cyan
    $publishDir = Join-Path $publishRoot $rid
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

    $dotnetArgs = @(
        'publish', $project,
        '-c', $Configuration,
        '-r', $rid,
        '--self-contained', 'true',
        '-o', $publishDir,
        '--nologo',
        '-p:DebugType=none',
        '-p:DebugSymbols=false',
        # Single-file deploy: one self-contained executable per platform. Avalonia's native libraries
        # (Skia/HarfBuzz/etc.) are embedded and self-extracted at first launch; compression shrinks the
        # download. Not trimmed - Avalonia relies on reflection/XAML, so trimming is unsafe here.
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true'
    )
    if ($Version) { $dotnetArgs += "-p:Version=$Version" }

    dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }

    # SkiaSharp/HarfBuzzSharp ship native .pdb files that land next to the single exe; drop them so the
    # deploy really is one file (they're debug symbols we don't distribute).
    Get-ChildItem -LiteralPath $publishDir -Filter *.pdb -File -ErrorAction SilentlyContinue | Remove-Item -Force

    if ($rid -like 'osx-*') {
        Write-Host "    packaging .app bundle" -ForegroundColor DarkGray
        $appDir   = Join-Path $publishDir "$displayName.app"
        $macosDir = Join-Path $appDir 'Contents/MacOS'
        $resDir   = Join-Path $appDir 'Contents/Resources'
        New-Item -ItemType Directory -Force -Path $macosDir, $resDir | Out-Null

        # Move the published payload into Contents/MacOS (everything except the .app we just created).
        Get-ChildItem -LiteralPath $publishDir -Force |
            Where-Object { $_.Name -ne "$displayName.app" } |
            ForEach-Object { Move-Item -LiteralPath $_.FullName -Destination $macosDir -Force }

        # App icon (Assets are embedded as avares, not copied to the publish output, so copy it in here).
        $icns = Join-Path $repoRoot 'src/Fubar.Diff.UI/Assets/fubar.icns'
        if (Test-Path $icns) { Copy-Item -LiteralPath $icns -Destination (Join-Path $resDir 'fubar.icns') -Force }

        Set-Content -LiteralPath (Join-Path $appDir 'Contents/Info.plist') -Encoding utf8 -Value @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>$displayName</string>
    <key>CFBundleDisplayName</key><string>$displayName</string>
    <key>CFBundleIdentifier</key><string>$bundleId</string>
    <key>CFBundleExecutable</key><string>$exeName</string>
    <key>CFBundleIconFile</key><string>fubar</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$plistVersion</string>
    <key>CFBundleVersion</key><string>$plistVersion</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>LSApplicationCategoryType</key><string>public.app-category.developer-tools</string>
</dict>
</plist>
"@

        $zipPath = Join-Path $artifacts "$exeName-$rid.zip"
        if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
        if ($IsMacOS) {
            & chmod '+x' (Join-Path $macosDir $exeName)
            & ditto '-c' '-k' '--sequesterRsrc' '--keepParent' $appDir $zipPath
        }
        else {
            Write-Warning "Zipping a macOS .app off macOS loses the executable bit - users must 'chmod +x $displayName.app/Contents/MacOS/$exeName'. Build the osx leg on macOS/CI for a ready-to-run bundle."
            Compress-Archive -Path $appDir -DestinationPath $zipPath -Force
        }
        Write-Host "    -> $zipPath" -ForegroundColor Green
    }
    elseif ($rid -like 'win-*') {
        $zipPath = Join-Path $artifacts "$exeName-$rid.zip"
        if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
        Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force
        Write-Host "    -> $zipPath" -ForegroundColor Green
    }
    else {
        # Linux: tar.gz preserves the executable bit (tar is available on Windows 10+, Linux, macOS).
        $tarPath = Join-Path $artifacts "$exeName-$rid.tar.gz"
        if (Test-Path $tarPath) { Remove-Item -Force $tarPath }
        if ($IsWindows -eq $false) { & chmod '+x' (Join-Path $publishDir $exeName) }
        & tar '-czf' $tarPath '-C' $publishDir '.'
        Write-Host "    -> $tarPath" -ForegroundColor Green
    }
}

Write-Host "`nArtifacts in $artifacts :" -ForegroundColor Cyan
Get-ChildItem -LiteralPath $artifacts -File | ForEach-Object { "  {0,10:N0} KB  {1}" -f ($_.Length / 1KB), $_.Name }
