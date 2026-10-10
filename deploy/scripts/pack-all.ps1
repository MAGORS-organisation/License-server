# ==============================================================================
# Symbolon Multi-Platform SDK Packaging Script (Windows PowerShell)
# Builds and packages .NET NuGet, Python PyPI, and WASM npm packages.
# ==============================================================================

[CmdletBinding()]
param(
    [string]$OutputDir = "dist",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Symbolon Multi-Platform SDK Packaging Pipeline" -ForegroundColor Yellow
Write-Host "============================================================" -ForegroundColor Cyan

if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir | Out-Null
$absOut = (Resolve-Path $OutputDir).Path

# 1. Package .NET NuGet Packages
Write-Host "`n[1/3] Packaging .NET Client & Protocol SDKs (NuGet)..." -ForegroundColor Green
$dotnetProjects = @(
    "src/Symbolon.Crypto/Symbolon.Crypto.csproj",
    "src/Symbolon.Format/Symbolon.Format.csproj",
    "src/Symbolon.Protocol/Symbolon.Protocol.csproj",
    "src/Symbolon.Client/Symbolon.Client.csproj"
)

foreach ($proj in $dotnetProjects) {
    Write-Host "  -> Packing $proj" -ForegroundColor Gray
    dotnet pack $proj -c $Configuration -o $absOut --include-symbols -p:SymbolPackageFormat=snupkg
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed on $proj" }
}

# 2. Package WASM npm Package
Write-Host "`n[2/3] Packaging WASM / Browser Offline Validator (npm)..." -ForegroundColor Green
if (Test-Path "sdk/wasm/package.json") {
    npm pack ./sdk/wasm --pack-destination $absOut
    if ($LASTEXITCODE -ne 0) { throw "npm pack failed on sdk/wasm" }
}

# 3. Package Python SDK
Write-Host "`n[3/3] Packaging Python SDK (sdist & wheel)..." -ForegroundColor Green
if (Test-Path "sdk/python/symbolon/pyproject.toml") {
    $hasBuild = python -c "import build; print('ok')" 2>$null
    if ($hasBuild -eq "ok") {
        python -m build sdk/python/symbolon --outdir $absOut
    } else {
        Write-Host "  [i] Python 'build' package not installed; skipping local python wheel generation." -ForegroundColor Yellow
    }
}

# 4. Generate Cryptographic Checksums (SHA-256)
Write-Host "`n[4/4] Computing Cryptographic SHA-256 Checksums..." -ForegroundColor Green
$checksumFile = Join-Path $absOut "SHA256SUMS.txt"
$files = Get-ChildItem -Path $absOut -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" }

$checksumLines = @()
foreach ($f in $files) {
    $hash = (Get-FileHash -Path $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $line = "$hash  $($f.Name)"
    $checksumLines += $line
    Write-Host "  $line" -ForegroundColor Gray
}

$checksumLines | Out-File -FilePath $checksumFile -Encoding utf8
Write-Host "`n✔ All SDK packages successfully built and hashed in: $OutputDir" -ForegroundColor Cyan
