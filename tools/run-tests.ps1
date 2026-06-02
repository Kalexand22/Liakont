#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Full test suite for Conformat: unit + integration tests (excludes staging).
.DESCRIPTION
    Writes detailed log to .run-tests.log, prints a compact summary to stdout.
    Exit code 0 = all passed, non-zero = failure.
    Staging tests (Category=Staging, real B2Brouter API) are NEVER run by this script —
    they require a local API key and are run manually (see docs/architecture/testing-strategy.md).
#>
$ErrorActionPreference = 'Continue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$slnPath = Join-Path $repoRoot 'src\Gateway.sln'
$logFile = Join-Path $repoRoot '.run-tests.log'

"run-tests started at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Set-Content $logFile

if (-not (Test-Path $slnPath)) {
    Write-Host "src/Gateway.sln does not exist yet (SOL01 pending) — nothing to test." -ForegroundColor Yellow
    "Solution not found — nothing to test (bootstrap mode)." | Add-Content $logFile
    exit 0
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()

dotnet test $slnPath --verbosity normal --filter "Category!=Staging" 2>&1 | Tee-Object -Append -FilePath $logFile | Out-Null
$exitCode = $LASTEXITCODE
$sw.Stop()

# Compact summary from the log
$logTail = Get-Content $logFile -Tail 30
$summaryLines = $logTail | Where-Object { $_ -match '(Passed!|Failed!|Total tests|Passed:|Failed:|Skipped:)' }

Write-Host ""
Write-Host "=== run-tests summary ($([math]::Round($sw.Elapsed.TotalSeconds,1))s) ===" -ForegroundColor Cyan
$summaryLines | ForEach-Object { Write-Host "  $_" }

if ($exitCode -ne 0) {
    Write-Host ""
    Write-Host "FAILED — details in .run-tests.log" -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "PASS" -ForegroundColor Green
exit 0
