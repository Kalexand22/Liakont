#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Full test suite for Conformat: unit + integration + E2E tests (platform + agent).
.DESCRIPTION
    Writes detailed log to .run-tests.log, prints a compact summary to stdout.
    Exit code 0 = all passed, non-zero = failure.

    Two solutions (blueprint.md v2 §4):
      - Platform : src/Conformat.sln          (unit + integration Testcontainers PostgreSQL +
                                               contract tests + E2E Playwright)
      - Agent    : agent/Conformat.Agent.sln  (unit + integration on fixtures)

    Suites excluded from this script (run manually, never in CI):
    - Category=Staging (real B2Brouter API — requires a local API key)
    - Category=Sandbox (real Super PDP API)
    See docs/architecture/testing-strategy.md.

    BOOTSTRAP MODE: a missing solution is only legitimate while its SOL item is pending
    (SOL01 = platform, SOL02 = agent). Otherwise it is a failure.
#>
$ErrorActionPreference = 'Continue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$platformSln = Join-Path $repoRoot 'src\Conformat.sln'
$agentSln = Join-Path $repoRoot 'agent\Conformat.Agent.sln'
$logFile = Join-Path $repoRoot '.run-tests.log'

"run-tests started at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Set-Content $logFile

function Test-SolItemPending {
    param([string]$ItemId)
    $orchRepo = $env:ORCH_REPO
    if (-not $orchRepo) { $orchRepo = 'C:\Source\conformat-orchestration' }
    $statePath = Join-Path $orchRepo 'state.yaml'
    if (-not (Test-Path $statePath)) { return $true }
    $state = Get-Content $statePath -Raw
    if ($state -notmatch "(?m)^  $([regex]::Escape($ItemId)):") { return $false }                          # absent = done
    if ($state -match "(?m)^  $([regex]::Escape($ItemId)):\s*\{\s*status:\s*done") { return $false }       # explicit done
    return $true
}

$overallExit = 0
$summaries = @()

function Invoke-TestSuite {
    param(
        [string]$Label,
        [string]$SlnPath,
        [string]$SolItem,
        [string]$Filter
    )

    if (-not (Test-Path $SlnPath)) {
        if (-not (Test-SolItemPending $SolItem)) {
            Write-Host "FAIL: $SlnPath is missing but $SolItem is done — the solution has been deleted." -ForegroundColor Red
            "[$Label] Solution missing while $SolItem is done — failure, not bootstrap." | Add-Content $script:logFile
            $script:overallExit = 1
        }
        else {
            Write-Host "[$Label] solution does not exist yet ($SolItem pending) — nothing to test." -ForegroundColor Yellow
            "[$Label] Solution not found — nothing to test (bootstrap mode)." | Add-Content $script:logFile
        }
        return
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    "`n=== [$Label] dotnet test ===" | Add-Content $script:logFile
    dotnet test $SlnPath --verbosity normal --filter $Filter 2>&1 | Tee-Object -Append -FilePath $script:logFile | Out-Null
    $exitCode = $LASTEXITCODE
    $sw.Stop()

    $logTail = Get-Content $script:logFile -Tail 30
    $summaryLines = $logTail | Where-Object { $_ -match '(Passed!|Failed!|Total tests|Passed:|Failed:|Skipped:)' }
    $script:summaries += "[$Label] ($([math]::Round($sw.Elapsed.TotalSeconds,1))s)"
    $summaryLines | ForEach-Object { $script:summaries += "  $_" }

    if ($exitCode -ne 0) { $script:overallExit = 1 }
}

# Platform: everything except real-PA suites (Staging/Sandbox). Integration (Testcontainers)
# and E2E (Playwright) ARE included — writing them without executing them is a false-green.
Invoke-TestSuite -Label 'platform' -SlnPath $platformSln -SolItem 'SOL01' -Filter "Category!=Staging&Category!=Sandbox"

# Agent: everything except real-ODBC suites (Staging).
Invoke-TestSuite -Label 'agent' -SlnPath $agentSln -SolItem 'SOL02' -Filter "Category!=Staging"

# ── Summary ──────────────────────────────────────────────────────
Write-Host ""
Write-Host "=== run-tests summary ===" -ForegroundColor Cyan
$summaries | ForEach-Object { Write-Host "  $_" }

if ($overallExit -ne 0) {
    Write-Host ""
    Write-Host "FAILED — details in .run-tests.log" -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "PASS" -ForegroundColor Green
exit 0
