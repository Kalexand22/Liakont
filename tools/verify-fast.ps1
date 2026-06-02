#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fast local verification for Conformat: build + analyzers + unit tests.
.DESCRIPTION
    Runs sequentially, stops on first failure. Writes detailed log to .verify-fast.log,
    prints a compact summary to stdout.
    Exit code 0 = all passed, non-zero = at least one step failed.

    BOOTSTRAP MODE: while src/Gateway.sln does not exist yet (item SOL01 not done),
    only the docs/structure checks run. This lets docs-spec items pass verification
    before the solution exists. Once the solution exists, build+tests are mandatory.
    SOL02 hardens this script against false-greens.
#>
$ErrorActionPreference = 'Continue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$slnPath = Join-Path $repoRoot 'src\Gateway.sln'
$logFile = Join-Path $repoRoot '.verify-fast.log'

$steps = @()
$allPassed = $true
$stepErrors = @{}

"verify-fast started at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Set-Content $logFile

function Run-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    "`n=== $Name ===" | Add-Content $script:logFile
    $output = ''
    try {
        $output = & $Action 2>&1 | Out-String
        $output | Add-Content $script:logFile
        $sw.Stop()
        if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
            throw "Exit code: $LASTEXITCODE"
        }
        $script:steps += @{ Name = $Name; Status = 'PASS'; Duration = [math]::Round($sw.Elapsed.TotalSeconds, 1) }
        return $true
    }
    catch {
        $sw.Stop()
        $_ | Out-String | Add-Content $script:logFile
        $errLines = ($output -split "`n" | Where-Object { $_ -match '(error|fail|Error|FAIL|CS\d{4})' } | Select-Object -First 10)
        if ($errLines) { $script:stepErrors[$Name] = $errLines }
        $script:steps += @{ Name = $Name; Status = 'FAIL'; Duration = [math]::Round($sw.Elapsed.TotalSeconds, 1) }
        $script:allPassed = $false
        return $false
    }
}

# ── Step 1: structure checks (always run) ────────────────────────
$ok = Run-Step 'structure' {
    $required = @('blueprint.md', 'CLAUDE.md', 'orchestration\manifest.yaml', 'orchestration\protocol.md')
    foreach ($f in $required) {
        $p = Join-Path $repoRoot $f
        if (-not (Test-Path $p)) { throw "Missing required file: $f" }
    }
    Write-Output "All required structure files present."
}

# ── Step 2: manifest sanity (always run) ─────────────────────────
if ($ok) {
    $ok = Run-Step 'manifest-sanity' {
        $manifest = Get-Content (Join-Path $repoRoot 'orchestration\manifest.yaml') -Raw
        # Every item id referenced in depends_on must be declared
        $declared = [regex]::Matches($manifest, '\{\s*id:\s*([A-Z_0-9]+)') | ForEach-Object { $_.Groups[1].Value }
        $referenced = [regex]::Matches($manifest, 'depends_on:\s*\[([^\]]*)\]') | ForEach-Object { $_.Groups[1].Value -split ',' } |
            ForEach-Object { $_.Trim() } | Where-Object { $_ }
        $missing = $referenced | Where-Object { $declared -notcontains $_ } | Select-Object -Unique
        if ($missing) { throw "depends_on references undeclared items: $($missing -join ', ')" }
        # Every lot referenced by an item must have a lot file
        $lots = [regex]::Matches($manifest, 'lot:\s*([A-Z_0-9]+)') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
        foreach ($lot in $lots) {
            $lotFile = Join-Path $repoRoot "orchestration\items\$lot.yaml"
            if (-not (Test-Path $lotFile)) { throw "Lot file missing: orchestration/items/$lot.yaml" }
        }
        Write-Output "Manifest sane: $($declared.Count) items, $($lots.Count) lots, all dependencies resolvable."
    }
}

# ── Step 3+: build + tests (only when the solution exists) ───────
if ($ok) {
    if (Test-Path $slnPath) {
        $ok = Run-Step 'restore' {
            dotnet restore $slnPath --verbosity quiet
            if ($LASTEXITCODE -ne 0) { throw "restore failed" }
        }
        if ($ok) {
            $ok = Run-Step 'build+analyzers' {
                dotnet build $slnPath --no-restore --verbosity quiet
                if ($LASTEXITCODE -ne 0) { throw "build failed" }
            }
        }
        if ($ok) {
            $ok = Run-Step 'unit-tests' {
                dotnet test $slnPath --no-build --verbosity quiet --filter "Category!=Integration&Category!=Staging"
                if ($LASTEXITCODE -ne 0) { throw "unit tests failed" }
            }
        }
    }
    else {
        "`n=== build (skipped) ===" | Add-Content $logFile
        "src/Gateway.sln does not exist yet (SOL01 pending) — build/tests skipped (bootstrap mode)." | Add-Content $logFile
        $steps += @{ Name = 'build (bootstrap skip)'; Status = 'SKIP'; Duration = 0 }
    }
}

# ── Summary ──────────────────────────────────────────────────────
Write-Host ""
Write-Host "=== verify-fast summary ===" -ForegroundColor Cyan
foreach ($s in $steps) {
    $color = switch ($s.Status) { 'PASS' { 'Green' } 'SKIP' { 'Yellow' } default { 'Red' } }
    Write-Host ("{0,-30} {1,-6} {2}s" -f $s.Name, $s.Status, $s.Duration) -ForegroundColor $color
}
if (-not $allPassed) {
    Write-Host ""
    Write-Host "FAILED — details in .verify-fast.log" -ForegroundColor Red
    foreach ($k in $stepErrors.Keys) {
        Write-Host "  [$k]" -ForegroundColor Red
        $stepErrors[$k] | ForEach-Object { Write-Host "    $_" }
    }
    exit 1
}
Write-Host ""
Write-Host "PASS" -ForegroundColor Green
exit 0
