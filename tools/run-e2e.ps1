#!/usr/bin/env pwsh
<#
.SYNOPSIS
    End-to-end (Playwright) test suite for the Liakont platform.
.DESCRIPTION
    Runs the SEPARATE E2E suite (decision D3, 2026-06-03): browser journeys against the real
    Liakont.Host backed by PostgreSQL + Keycloak containers (Testcontainers). This suite is
    NEVER run by verify-fast.ps1 or run-tests.ps1 (Category=E2E is excluded there) because it
    has heavier infrastructure requirements: Docker and the Playwright browsers.

    The E2E test project (tests/Liakont.Tests.E2E) starts its own containers via Testcontainers;
    this script only enforces the prerequisites, installs the Playwright browsers, and runs the
    Category=E2E filter.

    PREREQUISITES (this script FAILS explicitly if any is missing — never a silent skip; an E2E
    test that is written but never executed is a false green, blueprint.md §9 / CLAUDE.md):
      - Docker daemon reachable (PostgreSQL + Keycloak containers).
      - Playwright browsers (installed automatically here via the generated playwright.ps1).

    Writes a detailed log to .run-e2e.log and a compact summary to stdout.
    Exit code 0 = all E2E tests passed; non-zero = a prerequisite is missing or a test failed.

.PARAMETER Filter
    dotnet test --filter expression. Defaults to "Category=E2E" (the whole E2E suite). Used by
    blazor-page-item work to target a single scenario, e.g.
    -Filter "Category=E2E&FullyQualifiedName~LoginShell".
#>
param(
    [string]$Filter = 'Category=E2E'
)

$ErrorActionPreference = 'Continue'

# Force English dotnet CLI output so the test-count parsing below (and the anti-false-green
# guard that depends on it) is locale-independent (see run-tests.ps1 for the same reason).
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

$repoRoot = Split-Path -Parent $PSScriptRoot
$e2eProject = Join-Path $repoRoot 'tests\Liakont.Tests.E2E\Liakont.Tests.E2E.csproj'
$logFile = Join-Path $repoRoot '.run-e2e.log'

"run-e2e started at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Set-Content $logFile

function Fail {
    param([string]$Message)
    Write-Host "FAIL: $Message" -ForegroundColor Red
    "FAIL: $Message" | Add-Content $script:logFile
    exit 1
}

# ── Prerequisite 1: the E2E project must exist (delivered by SOL05) ──
if (-not (Test-Path $e2eProject)) {
    Fail "E2E test project not found at $e2eProject (delivered by SOL05). Nothing to run."
}

# ── Prerequisite 2: Docker daemon reachable ──
$dockerAvailable = $false
try {
    & docker info 1>$null 2>$null
    $dockerAvailable = ($LASTEXITCODE -eq 0)
}
catch {
    $dockerAvailable = $false
}
if (-not $dockerAvailable) {
    Fail "Docker daemon is not reachable. The E2E suite needs PostgreSQL + Keycloak containers (Testcontainers). Start Docker Desktop and retry."
}

# ── Build the E2E project (produces the static web assets manifest + the playwright.ps1 helper) ──
Write-Host "Building E2E project..." -ForegroundColor Cyan
$buildOutput = dotnet build $e2eProject -c Debug --verbosity quiet 2>&1 | Out-String
$buildOutput | Add-Content $logFile
if ($LASTEXITCODE -ne 0) {
    Fail "E2E project build failed (see .run-e2e.log)."
}

# ── Prerequisite 3: install the Playwright browsers (Chromium) ──
# Microsoft.Playwright generates playwright.ps1 in the build output once the project is built.
$playwrightScript = Join-Path $repoRoot 'tests\Liakont.Tests.E2E\bin\Debug\net10.0\playwright.ps1'
if (-not (Test-Path $playwrightScript)) {
    Fail "Playwright bootstrap script not found at $playwrightScript after build. The Microsoft.Playwright package may be missing from the E2E project."
}

Write-Host "Installing Playwright browsers (chromium)..." -ForegroundColor Cyan
$installOutput = & $playwrightScript install chromium 2>&1 | Out-String
$installOutput | Add-Content $logFile
if ($LASTEXITCODE -ne 0) {
    Fail "Playwright browser installation failed (see .run-e2e.log). The E2E suite cannot run without browsers."
}

# ── Run the E2E suite ──
Write-Host "Running E2E suite (filter: $Filter)..." -ForegroundColor Cyan
"`n=== dotnet test (filter: $Filter) ===" | Add-Content $logFile
$testOutput = dotnet test $e2eProject --no-build --verbosity normal --filter $Filter 2>&1 | Out-String
$testOutput | Add-Content $logFile
$exitCode = $LASTEXITCODE

# Count the tests actually executed (locale-independent — three recognized summary formats,
# same parsing as run-tests.ps1):
#   1. VSTest classic:  "Total tests: N"
#   2. VSTest one-line: "Passed!/Failed! ... Total: N, Duration: ..."
#   3. Microsoft.Testing.Platform: "  total: N"
$testsRun = 0
$totalMatches = [regex]::Matches($testOutput, '(?im)^\s*Total tests:\s*(\d+)')
if ($totalMatches.Count -eq 0) {
    $totalMatches = [regex]::Matches($testOutput, '(?i)(?:Passed!|Failed!)[^\r\n]*?Total:\s*(\d+)')
}
if ($totalMatches.Count -eq 0) {
    $totalMatches = [regex]::Matches($testOutput, '(?im)^\s*total:\s*(\d+)')
}
foreach ($m in $totalMatches) { $testsRun += [int]$m.Groups[1].Value }
$summaryRecognized = ($totalMatches.Count -gt 0)

$summaryLines = ($testOutput -split "`r?`n") | Where-Object { $_ -match '(Passed!|Failed!|Total tests|Passed:|Failed:|Skipped:)' } | Select-Object -Last 5

Write-Host ""
Write-Host "=== run-e2e summary ===" -ForegroundColor Cyan
Write-Host "  filter: $Filter  ($testsRun test(s))"
$summaryLines | ForEach-Object { Write-Host "  $($_.Trim())" }

if ($exitCode -ne 0) {
    Write-Host ""
    Write-Host "FAILED - details in .run-e2e.log" -ForegroundColor Red
    exit 1
}

# Anti false-green: a clean exit that executed ZERO E2E tests is a failure (wrong filter,
# untagged tests, or the suite did not actually run) — never PASS without running a test.
if ($testsRun -eq 0) {
    if ($summaryRecognized) {
        Fail "executed 0 E2E tests (filter '$Filter' matched nothing, or tests are not tagged [Trait(\"Category\",\"E2E\")])."
    }
    Fail "test runner output format not recognized - cannot verify how many E2E tests ran. Update the summary parsing in run-e2e.ps1 (see .run-e2e.log)."
}

Write-Host ""
Write-Host "PASS ($testsRun E2E test(s) executed)" -ForegroundColor Green
exit 0
