# ==============================================================================
# Native PowerShell High-Concurrency Benchmark Harness
# Usage: .\run-load-test.ps1 -BaseUrl "http://localhost:8080" -Concurrency 20 -Requests 200
# ==============================================================================
[CmdletBinding()]
param (
    [string]$BaseUrl = "http://localhost:8080",
    [int]$Concurrency = 20,
    [int]$Requests = 200
)

Write-Host "🔥 Starting Symbolon Concurrency Benchmark..." -ForegroundColor Cyan
Write-Host "   Target URL:  $BaseUrl"
Write-Host "   Concurrency: $Concurrency threads"
Write-Host "   Total Reqs:  $Requests requests`n"

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$successes = [System.Threading.Interlocked]::Increment([ref]0) - 1
$failures = [System.Threading.Interlocked]::Increment([ref]0) - 1

# Pre-flight health check
try {
    $health = Invoke-RestMethod -Uri "$BaseUrl/health/ready" -TimeoutSec 3
    Write-Host "✅ Pre-flight check passed: $BaseUrl is healthy ($($health.status))`n" -ForegroundColor Green
} catch {
    Write-Warning "⚠️ Could not reach $BaseUrl/health/ready. Proceeding anyway..."
}

$tasks = @()
$latencies = [System.Collections.Concurrent.ConcurrentBag[double]]::new()

$scriptBlock = {
    param($url, $id, $latenciesBag)
    $innerSw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $body = @{
            licenseKey = "BENCH-TEST-KEY-$id"
            machineId = "worker-$id"
            fingerprintComponents = @{ id = "$id" }
            quantity = 1
        } | ConvertTo-Json

        $res = Invoke-WebRequest -Uri "$url/v1/leases" -Method POST -Body $body -ContentType "application/json" -TimeoutSec 5 -SkipHttpErrorCheck
        $innerSw.Stop()
        $latenciesBag.Add($innerSw.Elapsed.TotalMilliseconds)
        return $res.StatusCode
    } catch {
        $innerSw.Stop()
        $latenciesBag.Add($innerSw.Elapsed.TotalMilliseconds)
        return 500
    }
}

# Run concurrent burst
$pool = [runspacefactory]::CreateRunspacePool(1, $Concurrency)
$pool.Open()

$jobs = [System.Collections.Generic.List[PSObject]]::new()

for ($i = 0; $i -lt $Requests; $i++) {
    $powershell = [powershell]::Create().AddScript($scriptBlock).AddArgument($BaseUrl).AddArgument($i).AddArgument($latencies)
    $powershell.RunspacePool = $pool
    $jobs.Add([PSCustomObject]@{
        Pipe = $powershell
        AsyncResult = $powershell.BeginInvoke()
    })
}

$statusCodes = [System.Collections.Generic.List[int]]::new()
foreach ($job in $jobs) {
    $code = $job.Pipe.EndInvoke($job.AsyncResult)
    $statusCodes.Add($code)
    $job.Pipe.Dispose()
}

$pool.Close()
$pool.Dispose()

$sw.Stop()

$okCount = ($statusCodes | Where-Object { $_ -ge 200 -and $_ -lt 300 }).Count
$deniedCount = ($statusCodes | Where-Object { $_ -eq 400 -or $_ -eq 409 }).Count
$errorCount = ($statusCodes | Where-Object { $_ -ge 500 }).Count

$sortedLatencies = $latencies.ToArray() | Sort-Object
$p50 = if ($sortedLatencies.Count -gt 0) { $sortedLatencies[[int]($sortedLatencies.Count * 0.50)] } else { 0 }
$p95 = if ($sortedLatencies.Count -gt 0) { $sortedLatencies[[int]($sortedLatencies.Count * 0.95)] } else { 0 }
$p99 = if ($sortedLatencies.Count -gt 0) { $sortedLatencies[[int]($sortedLatencies.Count * 0.99)] } else { 0 }
$rps = [Math]::Round($Requests / $sw.Elapsed.TotalSeconds, 1)

Write-Host "🏁 Benchmark Complete!" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
Write-Host "   Total Duration:    $([Math]::Round($sw.Elapsed.TotalSeconds, 2)) s"
Write-Host "   Throughput (RPS):  $rps req/sec" -ForegroundColor Yellow
Write-Host "   Success (2xx):     $okCount" -ForegroundColor Green
Write-Host "   Denied (4xx):      $deniedCount (graceful quota/capacity limits)" -ForegroundColor Cyan
Write-Host "   Errors (5xx):      $errorCount" -ForegroundColor $(if ($errorCount -eq 0) { "Green" } else { "Red" })
Write-Host "──────────────────────────────────────────────────"
Write-Host "   Latency P50:       $([Math]::Round($p50, 2)) ms"
Write-Host "   Latency P95:       $([Math]::Round($p95, 2)) ms"
Write-Host "   Latency P99:       $([Math]::Round($p99, 2)) ms"
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━`n"
