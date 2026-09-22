<#
.SYNOPSIS
    Symbolon Local Cross-Platform Packaging & Release Script
.DESCRIPTION
    Builds self-contained single-file executables for specified runtime identifier,
    creates release archives, and computes SHA-256 checksums.
.PARAMETER Rid
    The target Runtime Identifier (e.g. win-x64, win-arm64, linux-x64, linux-arm64, osx-arm64). Default: win-x64
.PARAMETER Version
    The release version string. Default: 1.1.0
.PARAMETER OutputDir
    Destination output directory. Default: artifacts/dist
#>

[CmdletBinding()]
param(
    [string]$Rid = "win-x64",
    [string]$Version = "1.1.0",
    [string]$OutputDir = "artifacts/dist"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

Write-Host "=== Symbolon Packaging: Version $Version for $Rid ===" -ForegroundColor Cyan

$distTarget = Join-Path $OutputDir $Rid
$stagingBin = Join-Path $distTarget "bin"

if (Test-Path $distTarget) {
    Remove-Item -Recurse -Force $distTarget
}
New-Item -ItemType Directory -Force -Path $stagingBin | Out-Null

$projects = @(
    "src/Symbolon.Cli/Symbolon.Cli.csproj",
    "src/Symbolon.ControlPlane/Symbolon.ControlPlane.csproj",
    "src/Symbolon.Relay/Symbolon.Relay.csproj"
)

foreach ($proj in $projects) {
    $projName = [System.IO.Path]::GetFileNameWithoutExtension($proj)
    Write-Host "Compiling & Publishing $projName ($Rid)..." -ForegroundColor Yellow
    dotnet publish $proj `
        -c Release `
        -r $Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $stagingBin
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to publish $projName for $Rid"
    }
}

# Create Archive
$archiveName = "symbolon-$Version-$Rid"
if ($Rid.StartsWith("win-")) {
    $archivePath = Join-Path $distTarget "$archiveName.zip"
    Write-Host "Creating ZIP archive: $archivePath" -ForegroundColor Green
    Compress-Archive -Path "$stagingBin/*" -DestinationPath $archivePath -Force
} else {
    $archivePath = Join-Path $distTarget "$archiveName.tar.gz"
    Write-Host "Creating TAR.GZ archive: $archivePath" -ForegroundColor Green
    tar -czvf $archivePath -C $stagingBin .
}

# Compute SHA-256
$hash = (Get-FileHash -Path $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumFile = Join-Path $distTarget "SHA256SUMS.txt"
"$hash  $(Split-Path -Leaf $archivePath)" | Out-File -FilePath $checksumFile -Encoding utf8

$fileSizeMb = [math]::Round(((Get-Item $archivePath).Length / 1MB), 2)

Write-Host "`n✔ Balíček bol úspešne vytvorený!" -ForegroundColor Green
Write-Host "  Archív:      $archivePath ($fileSizeMb MB)"
Write-Host "  SHA-256:     $hash"
Write-Host "  Kontrolný súčet: $checksumFile`n"
