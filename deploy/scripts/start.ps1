# ==============================================================================
# Symbolon Production Stack Startup Script (PowerShell)
# ==============================================================================
[CmdletBinding()]
param (
    [switch]$Build = $true,
    [switch]$Detach = $true
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent (Split-Path -Parent $ScriptDir)

Write-Host "🚀 Starting Symbolon Licensing Server Stack..." -ForegroundColor Cyan

$EnvFile = Join-Path $RootDir ".env"
$EnvExample = Join-Path $RootDir ".env.example"

if (-not (Test-Path $EnvFile) -and (Test-Path $EnvExample)) {
    Write-Host "⚠️  No .env file found. Creating .env from .env.example..." -ForegroundColor Yellow
    Copy-Item $EnvExample $EnvFile
}

Set-Location $RootDir

$composeArgs = @("compose", "up")
if ($Detach) { $composeArgs += "-d" }
if ($Build) { $composeArgs += "--build" }

& docker $composeArgs

Write-Host "`n⏳ Waiting for services to initialize..." -ForegroundColor DarkGray
Start-Sleep -Seconds 5

try {
    $response = Invoke-RestMethod -Uri "http://localhost:8080/health/ready" -TimeoutSec 3 -ErrorAction SilentlyContinue
    Write-Host "✅ Control Plane is ready!" -ForegroundColor Green
} catch {
    Write-Host "ℹ️ Control plane initializing..." -ForegroundColor Yellow
}

Write-Host "`n✅ Symbolon Stack Status:" -ForegroundColor Green
Write-Host "   - Web Control Plane (Retro TUI): http://localhost:8080"
Write-Host "   - Relay Proxy Node:              http://localhost:8081"
Write-Host "   - Prometheus Metrics:            http://localhost:9090"
Write-Host "   - Grafana Dashboards:            http://localhost:3000 (admin / admin)`n"
