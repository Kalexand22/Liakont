#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fast local verification for Conformat: builds + analyzers + unit tests (platform + agent).
.DESCRIPTION
    Runs sequentially, stops on first failure. Writes detailed log to .verify-fast.log,
    prints a compact summary to stdout.
    Exit code 0 = all passed, non-zero = at least one step failed.

    Two solutions (blueprint.md v2 §4):
      - Platform : src/Conformat.sln          (.NET 10 — Host, modules, PA plug-ins, contracts)
      - Agent    : agent/Conformat.Agent.sln  (.NET Framework 4.8, x86 — extraction + transport)

    BOOTSTRAP MODE: while a solution does not exist yet (SOL01 for the platform, SOL02 for
    the agent), its build/test steps are skipped. This lets docs-spec items pass verification
    before the solutions exist. Once a solution exists, its build+tests are mandatory.
    If the corresponding SOL item is done but the solution is missing, that is a FAILURE
    (the solution has been deleted), never a skip.
#>
$ErrorActionPreference = 'Continue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$platformSln = Join-Path $repoRoot 'src\Conformat.sln'
$agentSln = Join-Path $repoRoot 'agent\Conformat.Agent.sln'
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
        # Reset before invoking: a pure-PowerShell step must not inherit the exit code
        # of a native command run by a PREVIOUS step (stale $LASTEXITCODE = false failure).
        $global:LASTEXITCODE = 0
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

# Returns $true while the given SOL item is still pending (absent from state = done).
# THROWS if the orchestration state repo / state.yaml is missing: the protocol makes
# state.yaml mandatory. Treating a missing state as "SOL pending" would let a deleted
# solution (or a misconfigured ORCH_REPO) pass as a bootstrap skip — a false green.
function Test-SolItemPending {
    param([string]$ItemId)
    $orchRepo = $env:ORCH_REPO
    if (-not $orchRepo) { $orchRepo = 'C:\Source\conformat-orchestration' }
    $statePath = Join-Path $orchRepo 'state.yaml'
    if (-not (Test-Path $statePath)) {
        throw "Orchestration state not found ($statePath). state.yaml is mandatory (protocol.md Step 1) - set ORCH_REPO to the state repo. NEVER recreate state.yaml (absent items = done items)."
    }
    $state = Get-Content $statePath -Raw -ErrorAction Stop
    if ($state -notmatch "(?m)^  $([regex]::Escape($ItemId)):") { return $false }                          # absent = done
    if ($state -match "(?m)^  $([regex]::Escape($ItemId)):\s*\{\s*status:\s*done") { return $false }       # explicit done
    return $true
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
        # Every work item must be defined in its lot file (reciprocity manifest → lots)
        $itemLotPairs = [regex]::Matches($manifest, '\{\s*id:\s*([A-Z_0-9]+),\s*lot:\s*([A-Z]+)')
        foreach ($m in $itemLotPairs) {
            $iid = $m.Groups[1].Value; $ilot = $m.Groups[2].Value
            $lotContent = Get-Content (Join-Path $repoRoot "orchestration\items\$ilot.yaml") -Raw
            if ($lotContent -notmatch "(?m)^  $([regex]::Escape($iid)):") {
                throw "Item $iid (manifest) is not defined in orchestration/items/$ilot.yaml"
            }
        }
        # Every gate must be defined in some lot file
        $gates = $declared | Where-Object { $_ -like 'GATE_*' } | Select-Object -Unique
        $allLotContent = ($lots | ForEach-Object { Get-Content (Join-Path $repoRoot "orchestration\items\$_.yaml") -Raw }) -join "`n"
        foreach ($g in $gates) {
            if ($allLotContent -notmatch "(?m)^  $([regex]::Escape($g)):") { throw "Gate $g (manifest) is not defined in any lot file" }
        }
        # Every blueprint referenced must exist
        $blueprints = [regex]::Matches($manifest, 'blueprint:\s*([a-z][a-z0-9-]+)') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
        foreach ($bp in $blueprints) {
            if (-not (Test-Path (Join-Path $repoRoot "orchestration\blueprints\$bp.yaml"))) { throw "Blueprint missing: orchestration/blueprints/$bp.yaml" }
        }
        # State reciprocity: every item in state.yaml must exist in the manifest
        # (manifest items absent from state = done & purged, which is legitimate: absent = done)
        $orchRepo = $env:ORCH_REPO
        if (-not $orchRepo) { $orchRepo = 'C:\Source\conformat-orchestration' }
        $statePath2 = Join-Path $orchRepo 'state.yaml'
        if (Test-Path $statePath2) {
            $state = Get-Content $statePath2 -Raw
            $stateIds = [regex]::Matches($state, '(?m)^  ([A-Z_0-9]+):\s*\{\s*status:') | ForEach-Object { $_.Groups[1].Value }
            $orphans = $stateIds | Where-Object { $declared -notcontains $_ } | Select-Object -Unique
            if ($orphans) { throw "state.yaml contains items absent from the manifest: $($orphans -join ', ')" }
        }
        Write-Output "Manifest sane: $($declared.Count) items, $($lots.Count) lots, items<->lots reciprocal, blueprints present, state consistent."
    }
}

# ── Step 3: PLATFORM build + unit tests (when src/Conformat.sln exists) ──
if ($ok) {
    if (Test-Path $platformSln) {
        $ok = Run-Step 'platform: restore' {
            dotnet restore $platformSln --verbosity quiet
            if ($LASTEXITCODE -ne 0) { throw "restore failed" }
        }
        if ($ok) {
            $ok = Run-Step 'platform: build+analyzers' {
                dotnet build $platformSln --no-restore --verbosity quiet
                if ($LASTEXITCODE -ne 0) { throw "build failed" }
            }
        }
        if ($ok) {
            # Unit + architecture tests only — integration (Testcontainers), Staging, Sandbox,
            # E2E (Playwright) run via run-tests.ps1.
            $ok = Run-Step 'platform: unit-tests' {
                dotnet test $platformSln --no-build --verbosity quiet --filter "Category!=Integration&Category!=Staging&Category!=Sandbox&Category!=E2E"
                if ($LASTEXITCODE -ne 0) { throw "unit tests failed" }
            }
        }
    }
    else {
        # Solution missing: decide bootstrap-skip vs failure from the orchestration state.
        # This step FAILS if the state repo is missing (Test-SolItemPending throws) or if
        # SOL01 is done (the solution has been deleted) — never a silent skip.
        $ok = Run-Step 'platform: bootstrap-check' {
            if (-not (Test-SolItemPending 'SOL01')) {
                throw "src/Conformat.sln is missing but SOL01 is done — the solution has been deleted or the checkout is broken."
            }
            Write-Output "src/Conformat.sln does not exist yet (SOL01 pending per state.yaml) — platform build/tests skipped (bootstrap mode)."
        }
    }
}

# ── Step 4: AGENT build + unit tests (when agent/Conformat.Agent.sln exists) ──
if ($ok) {
    if (Test-Path $agentSln) {
        $ok = Run-Step 'agent: restore' {
            dotnet restore $agentSln --verbosity quiet
            if ($LASTEXITCODE -ne 0) { throw "restore failed" }
        }
        if ($ok) {
            # x86 is the constraining platform (32-bit Pervasive ODBC drivers).
            # The x64 build is covered by run-tests / CI (SOL03) to keep verify-fast fast.
            $ok = Run-Step 'agent: build+analyzers (x86)' {
                dotnet build $agentSln --no-restore --verbosity quiet /p:Platform=x86
                if ($LASTEXITCODE -ne 0) { throw "build failed" }
            }
        }
        if ($ok) {
            $ok = Run-Step 'agent: unit-tests' {
                dotnet test $agentSln --no-build --verbosity quiet --filter "Category!=Integration&Category!=Staging"
                if ($LASTEXITCODE -ne 0) { throw "unit tests failed" }
            }
        }
    }
    else {
        # Solution missing: decide bootstrap-skip vs failure from the orchestration state.
        $ok = Run-Step 'agent: bootstrap-check' {
            if (-not (Test-SolItemPending 'SOL02')) {
                throw "agent/Conformat.Agent.sln is missing but SOL02 is done — the solution has been deleted or the checkout is broken."
            }
            Write-Output "agent/Conformat.Agent.sln does not exist yet (SOL02 pending per state.yaml) — agent build/tests skipped (bootstrap mode)."
        }
    }
}

# ── Summary ──────────────────────────────────────────────────────
Write-Host ""
Write-Host "=== verify-fast summary ===" -ForegroundColor Cyan
foreach ($s in $steps) {
    $color = switch ($s.Status) { 'PASS' { 'Green' } 'SKIP' { 'Yellow' } default { 'Red' } }
    Write-Host ("{0,-35} {1,-6} {2}s" -f $s.Name, $s.Status, $s.Duration) -ForegroundColor $color
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
