#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Full test suite for Liakont: unit + integration tests (platform + agent x86/x64).
.DESCRIPTION
    Writes detailed log to .run-tests.log, prints a compact summary to stdout.
    Exit code 0 = all passed, non-zero = failure.

    Two solutions (blueprint.md v2 §4):
      - Platform : src/Liakont.sln          (unit + integration Testcontainers PostgreSQL +
                                               contract tests)
      - Agent    : agent/Liakont.Agent.sln  (unit + integration on fixtures — x86 AND x64)

    Suites excluded from this script:
    - Category=E2E     (Playwright — separate suite run by tools/run-e2e.ps1, decision D3 2026-06-03)
    - Category=Staging (real B2Brouter API — requires a local API key, run manually, never in CI)
    - Category=Sandbox (real Super PDP API)
    See docs/architecture/testing-strategy.md (created by SOL04).

    BOOTSTRAP MODE: a missing solution is only legitimate while its SOL item is pending
    (SOL01 = platform, SOL02 = agent) according to $ORCH_REPO/state.yaml. A missing state
    repo is a FAILURE (never assume bootstrap). Zero suites executed outside bootstrap is
    a FAILURE (a PASS without any test executed is a false green).
#>
$ErrorActionPreference = 'Continue'

# Force the dotnet CLI to emit its test summary in English so the test-count parsing below
# (and the anti-false-green guard that depends on it) is locale-independent. On a French
# Windows the summary reads "Réussi! ... total : N", which none of the English regexes match,
# making the guard wrongly report "0 tests / unrecognized format".
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

$repoRoot = Split-Path -Parent $PSScriptRoot
$platformSln = Join-Path $repoRoot 'src\Liakont.sln'
$agentSln = Join-Path $repoRoot 'agent\Liakont.Agent.sln'
$logFile = Join-Path $repoRoot '.run-tests.log'

"run-tests started at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Set-Content $logFile

# Returns $true while the given SOL item is still pending (absent from state = done).
# THROWS if the orchestration state repo / state.yaml is missing: the protocol makes
# state.yaml mandatory. Treating a missing state as "SOL pending" would let a deleted
# solution (or a misconfigured ORCH_REPO) pass as a bootstrap skip — a false green.
function Test-SolItemPending {
    param([string]$ItemId)
    $orchRepo = $env:ORCH_REPO
    if (-not $orchRepo) { $orchRepo = 'C:\Source\liakont-orchestration' }
    $statePath = Join-Path $orchRepo 'state.yaml'
    if (-not (Test-Path $statePath)) {
        throw "Orchestration state not found ($statePath). state.yaml is mandatory (protocol.md Step 1) - set ORCH_REPO to the state repo. NEVER recreate state.yaml (absent items = done items)."
    }
    $state = Get-Content $statePath -Raw -ErrorAction Stop
    if ($state -notmatch "(?m)^  $([regex]::Escape($ItemId)):") { return $false }                          # absent = done
    if ($state -match "(?m)^  $([regex]::Escape($ItemId)):\s*\{\s*status:\s*done") { return $false }       # explicit done
    return $true
}

$overallExit = 0
$summaries = @()
$suitesExecuted = 0
$testsExecuted = 0
$bootstrapSkips = 0

function Invoke-TestSuite {
    param(
        [string]$Label,
        [string]$SlnPath,
        [string]$SolItem,
        [string]$Filter,
        [string]$Platform = ''
    )

    if (-not (Test-Path $SlnPath)) {
        try {
            $isPending = Test-SolItemPending $SolItem
        }
        catch {
            Write-Host "FAIL: $_" -ForegroundColor Red
            "[$Label] $_" | Add-Content $script:logFile
            $script:overallExit = 1
            return
        }
        if (-not $isPending) {
            Write-Host "FAIL: $SlnPath is missing but $SolItem is done — the solution has been deleted." -ForegroundColor Red
            "[$Label] Solution missing while $SolItem is done — failure, not bootstrap." | Add-Content $script:logFile
            $script:overallExit = 1
        }
        else {
            Write-Host "[$Label] solution does not exist yet ($SolItem pending per state.yaml) — nothing to test." -ForegroundColor Yellow
            "[$Label] Solution not found — nothing to test (bootstrap mode)." | Add-Content $script:logFile
            $script:bootstrapSkips++
        }
        return
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    "`n=== [$Label] dotnet test ===" | Add-Content $script:logFile
    $testArgs = @($SlnPath, '--verbosity', 'normal', '--filter', $Filter)
    if ($Platform) { $testArgs += "-p:Platform=$Platform" }
    $testOutput = dotnet test @testArgs 2>&1 | Out-String
    $testOutput | Add-Content $script:logFile
    $exitCode = $LASTEXITCODE
    $sw.Stop()
    $script:suitesExecuted++

    # Count the tests actually executed (not just the suite invocations). Recognized summary
    # formats, tried in order:
    #   1. VSTest classic:  "Total tests: N"
    #   2. VSTest one-line: "Passed!/Failed!  - Failed: x, Passed: y, ..., Total: N, Duration: ..."
    #   3. Microsoft.Testing.Platform (.NET 9/10 default runner): "  total: N" (test run summary block)
    $testsRun = 0
    $totalMatches = [regex]::Matches($testOutput, '(?im)^\s*Total tests:\s*(\d+)')
    if ($totalMatches.Count -eq 0) {
        $totalMatches = [regex]::Matches($testOutput, '(?i)(?:Passed!|Failed!)[^\r\n]*?Total:\s*(\d+)')
    }
    if ($totalMatches.Count -eq 0) {
        $totalMatches = [regex]::Matches($testOutput, '(?im)^\s*total:\s*(\d+)')
    }
    foreach ($m in $totalMatches) { $testsRun += [int]$m.Groups[1].Value }
    $script:testsExecuted += $testsRun
    # If NO known summary format was found at all, the guard below would report a misleading
    # "0 tests". Distinguish "unrecognized output format" from a real zero-test run.
    $summaryRecognized = ($totalMatches.Count -gt 0)

    $summaryLines = ($testOutput -split "`r?`n") | Where-Object { $_ -match '(Passed!|Failed!|Total tests|Passed:|Failed:|Skipped:)' } | Select-Object -Last 5
    $script:summaries += "[$Label] ($([math]::Round($sw.Elapsed.TotalSeconds,1))s, $testsRun test(s))"
    $summaryLines | ForEach-Object { $script:summaries += "  $($_.Trim())" }

    if ($exitCode -ne 0) { $script:overallExit = 1 }

    # Anti false-green: a PRESENT solution whose test run executes ZERO tests is a failure
    # (wrong filter, untagged tests, or no test projects in the sln) — dotnet test exits 0
    # in that case, which would otherwise print PASS without testing anything.
    if ($testsRun -eq 0) {
        if ($summaryRecognized) {
            Write-Host "FAIL: [$Label] executed 0 tests — wrong filter, untagged tests, or no test projects in the solution." -ForegroundColor Red
            "[$Label] ZERO tests executed — false-green guard triggered (exit code was $exitCode)." | Add-Content $script:logFile
        }
        else {
            # No recognized summary format (VSTest or MTP) in the output: do not claim "0 tests",
            # but never PASS on output we cannot interpret either — that would be a false green.
            Write-Host "FAIL: [$Label] test runner output format not recognized — cannot verify how many tests ran. Update the summary parsing in run-tests.ps1 (see .run-tests.log)." -ForegroundColor Red
            "[$Label] Unrecognized test summary format — failing as a precaution (exit code was $exitCode)." | Add-Content $script:logFile
        }
        $script:overallExit = 1
    }
}

# Platform: everything except real-PA suites (Staging/Sandbox) and E2E. Integration
# (Testcontainers) IS included. E2E (Playwright) is a SEPARATE suite run by tools/run-e2e.ps1
# (decision D3 2026-06-03) — it has its own infrastructure requirements (Docker, browsers).
Invoke-TestSuite -Label 'platform' -SlnPath $platformSln -SolItem 'SOL01' -Filter "Category!=Staging&Category!=Sandbox&Category!=E2E"

# Agent: everything except real-ODBC suites (Staging) — on BOTH platforms (SOL02/OPS05
# ship x86 and x64; testing only one of them would be a coverage hole).
Invoke-TestSuite -Label 'agent (x86)' -SlnPath $agentSln -SolItem 'SOL02' -Filter "Category!=Staging" -Platform 'x86'
Invoke-TestSuite -Label 'agent (x64)' -SlnPath $agentSln -SolItem 'SOL02' -Filter "Category!=Staging" -Platform 'x64'

# ── Self-test du packaging de l'agent (OPS05) ────────────────────
# Garde PERMANENTE de la logique de tooling d'installation (module AgentInstall.psm1) + contrôle de
# bitness sur les binaires que les suites agent viennent de construire. Ce n'est pas du dotnet test :
# sans ce câblage, une régression du module ne ferait échouer aucune gate (faux-vert). Exécuté en
# processus séparé pour isoler son code de sortie. Sauté en bootstrap (agent absent = SOL02 pending).
if (Test-Path $agentSln) {
    Write-Host ""
    Write-Host "=== [packaging self-test] ===" -ForegroundColor Cyan
    $psExe = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell' }
    & $psExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'tools\test-agent-packaging.ps1')
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL: [packaging self-test] échec — voir la sortie ci-dessus." -ForegroundColor Red
        "[packaging self-test] FAILED (exit $LASTEXITCODE)." | Add-Content $logFile
        $overallExit = 1
    }
    else {
        "[packaging self-test] PASS." | Add-Content $logFile
    }
}

# ── Self-test du packaging multi-profils de l'installeur (OPS08c) ────────────────
# Garde PERMANENTE du packaging par profil intégrateur (tools/package-installer.ps1) : embarquement du
# profil en ressource + round-trip --show-profile + échec sur profil invalide, sur les binaires que les
# suites agent viennent de construire. Réutilise OPS05 (-SkipBuild). Sauté en bootstrap (agent absent).
if (Test-Path $agentSln) {
    Write-Host ""
    Write-Host "=== [installer packaging self-test] ===" -ForegroundColor Cyan
    $psExe = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell' }
    & $psExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'tools\test-installer-packaging.ps1')
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL: [installer packaging self-test] échec — voir la sortie ci-dessus." -ForegroundColor Red
        "[installer packaging self-test] FAILED (exit $LASTEXITCODE)." | Add-Content $logFile
        $overallExit = 1
    }
    else {
        "[installer packaging self-test] PASS." | Add-Content $logFile
    }
}

# ── Self-test du provisioning d'instances (OPS02) ────────────────
# Garde PERMANENTE de la logique de provisioning (deploy/provisioning/Provisioning.psm1 + scripts
# new-instance/update-instance sur les états vide/sale/échec). PowerShell pur, sans Docker (les
# chemins Docker sont revalidés à la recette GATE_TOOLKIT). Ne dépend d'aucune solution → toujours
# exécuté (même en bootstrap). Processus séparé pour isoler son code de sortie.
Write-Host ""
Write-Host "=== [provisioning self-test] ===" -ForegroundColor Cyan
$psExe = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell' }
& $psExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'tools\test-provisioning.ps1')
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAIL: [provisioning self-test] échec — voir la sortie ci-dessus." -ForegroundColor Red
    "[provisioning self-test] FAILED (exit $LASTEXITCODE)." | Add-Content $logFile
    $overallExit = 1
}
else {
    "[provisioning self-test] PASS." | Add-Content $logFile
}

# ── Summary ──────────────────────────────────────────────────────
Write-Host ""
Write-Host "=== run-tests summary ===" -ForegroundColor Cyan
$summaries | ForEach-Object { Write-Host "  $_" }

if ($overallExit -ne 0) {
    Write-Host ""
    Write-Host "FAILED — details in .run-tests.log" -ForegroundColor Red
    exit 1
}

# Guard against the "PASS with zero tests" false green: a clean exit with no suite executed
# is only legitimate in bootstrap mode (solutions pending per state.yaml).
if ($suitesExecuted -eq 0) {
    if ($bootstrapSkips -gt 0) {
        Write-Host ""
        Write-Host "BOOTSTRAP — no test suite executed (solutions pending per state.yaml)." -ForegroundColor Yellow
        exit 0
    }
    Write-Host ""
    Write-Host "FAILED — zero test suites were executed and bootstrap mode does not apply." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "PASS ($suitesExecuted suite(s), $testsExecuted test(s) executed)" -ForegroundColor Green
exit 0
