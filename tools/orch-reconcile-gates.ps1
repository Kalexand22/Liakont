#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Reconcile orchestration gate states with merged PRs (Liakont).
.DESCRIPTION
    Since manifest v11 every still-active SEGMENT gate is a HUMAN-merge gate: the runner runs the
    segment checks and opens a PR to main, a human merges it (the "human merge to main" control
    required by CLAUDE.md). This script closes the loop automatically -- the "flip auto" half of
    the decision:

      for every gate still `blocked` or `gate_pending` whose segment branch has been MERGED into
      main (verified per-branch via `gh`), flip the gate to `done` (through orch-state.ps1) and
      append an event.

    Safe by construction:
      - it NEVER merges anything (only GitHub / the human merges);
      - it only promotes `blocked`/`gate_pending` -> `done`, and ONLY when a merged PR for that
        exact segment branch exists;
      - it is BEST-EFFORT: a transient `gh` failure is a warning, never a session-aborting error
        (protocol.md Step 1.4 runs it before work selection -- it must not block a session);
      - it is CONCURRENCY-SAFE: if another runner reconciles a gate first, the losing `done -> done`
        transition is treated as an idempotent skip, not a failure;
      - state is re-read fresh per gate (multi-agent: status can change mid-run).

    Run it after merging a gate PR, or wire it into the runner's Step 1 (protocol.md Step 1.4).
.PARAMETER DryRun
    Report what WOULD be flipped; change nothing.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/orch-reconcile-gates.ps1
    powershell -ExecutionPolicy Bypass -File tools/orch-reconcile-gates.ps1 -DryRun
#>
param([switch]$DryRun)

$ErrorActionPreference = 'Stop'

# -- Paths --------------------------------------------------------
$repoRoot     = Split-Path -Parent $PSScriptRoot            # tools/ -> repo root
$manifestPath = Join-Path $repoRoot 'orchestration/manifest.yaml'
$orchRepo     = $env:ORCH_REPO
if (-not $orchRepo) { $orchRepo = 'C:\Source\liakont-orchestration' }
$statePath    = Join-Path $orchRepo 'state.yaml'
$eventsPath   = Join-Path $orchRepo 'events.jsonl'
$orchStatePs  = Join-Path $PSScriptRoot 'orch-state.ps1'

if (-not (Test-Path $manifestPath)) { Write-Error "manifest not found: $manifestPath"; exit 1 }
if (-not (Test-Path $statePath))    { Write-Error "state.yaml not found: $statePath"; exit 1 }

# `gh` is a hard dependency of the reconcile query. With ErrorActionPreference='Stop', a missing
# `gh` would throw a TERMINATING CommandNotFoundException before any $LASTEXITCODE guard -- which
# would abort a session (this runs at protocol.md Step 1.4, before work selection). Best-effort:
# if gh is absent, warn and exit 0 (reconcile nothing this tick) rather than crash.
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Warning "gh not found on PATH -- skipping gate reconciliation this tick (best-effort)."
    exit 0
}

# -- 1. Parse manifest `segments:` -> map gate id => segment branch
# Structure: 2-space segment header, 4-space fields (branch:, base:, lots:, gate:).
$gateToBranch = @{}
$inSegments = $false
$curBranch  = $null
foreach ($line in (Get-Content $manifestPath)) {
    if ($line -match '^segments:\s*$') { $inSegments = $true; continue }
    if (-not $inSegments) { continue }
    if ($line -match '^\S') { break }                         # a top-level key ends the block
    if     ($line -match '^\s{4}branch:\s*(\S+)') { $curBranch = $Matches[1] }
    elseif ($line -match '^\s{4}gate:\s*(\S+)') {
        if ($curBranch) { $gateToBranch[$Matches[1]] = $curBranch }
        else { Write-Warning ("manifest: gate {0} has no branch captured (segment block order/format?) -- not reconcilable" -f $Matches[1]) }
    }
    elseif ($line -match '^\s{2}[\w-]+:\s*$')     { $curBranch = $null }   # a real segment header (`  name:`), NOT a comment/field
}
if ($gateToBranch.Count -eq 0) { Write-Host "No segment gate found in the manifest."; exit 0 }

# Fresh read each call: multi-agent, a gate's status can change between iterations.
function Get-GateStatus([string]$gateId) {
    $txt = Get-Content $statePath -Raw
    if ($txt -match "(?m)^\s+$([regex]::Escape($gateId)):\s*\{[^}]*status:\s*(\w+)") { return $Matches[1] }
    return $null   # absent = done (purged)
}

# UTF-8 WITHOUT BOM, matching orch-state.ps1 (a BOM on a fresh events.jsonl would corrupt line 1).
$utf8NoBom = New-Object System.Text.UTF8Encoding $false

# -- 2. Reconcile -------------------------------------------------
$flipped = @()
foreach ($gate in ($gateToBranch.Keys | Sort-Object)) {
    $branch = $gateToBranch[$gate]
    $status = Get-GateStatus $gate
    if ($null -eq $status) { continue }                       # absent = already done
    if ($status -ne 'blocked' -and $status -ne 'gate_pending') { continue }

    # Targeted query for THIS branch (no --limit cap, no silent false-negative). Use gh's --jq to
    # extract numbers directly: ConvertFrom-Json on a top-level "[]" yields a 1-element array in
    # PS 5.1 (the empty array counted as one object) -> a FALSE positive that would flip a gate
    # whose PR is NOT merged. --jq emits one number per line, or nothing.
    $prNums = gh pr list --head $branch --state merged --base main --json number --jq '.[].number'
    if ($LASTEXITCODE -ne 0) {
        # Best-effort self-healing: gh down/auth/rate-limit must NOT abort the session.
        Write-Warning "gh unavailable (auth/network?) -- skipping gate reconciliation this tick; will retry next session."
        break
    }
    $prNums = @($prNums | Where-Object { "$_".Trim() -ne '' })
    if ($prNums.Count -eq 0) {
        Write-Host ("  - {0} ({1}) : {2} -- no merged PR, leaving as-is" -f $gate, $branch, $status)
        continue
    }
    $prNum = "$($prNums[0])".Trim()
    if ($DryRun) {
        Write-Host ("  -> [DRY-RUN] {0} : {1} -> done (PR #{2} {3} merged)" -f $gate, $status, $prNum, $branch) -ForegroundColor Cyan
        $flipped += $gate
        continue
    }

    & powershell -ExecutionPolicy Bypass -File $orchStatePs update -ItemId $gate -Status done | Out-Host
    if ($LASTEXITCODE -ne 0) {
        # Concurrency: another runner may have flipped it first (done -> done is illegal), and a
        # done item may then be purged from state.yaml (absent = done). Treat both 'done' AND
        # absent ($null) as an idempotent success, not a failure.
        $now = Get-GateStatus $gate
        if ($now -eq 'done' -or $null -eq $now) {
            Write-Host ("  . {0} : already done (reconciled by another runner) -- skip" -f $gate)
            continue
        }
        # Update did not apply and the gate is unchanged -- almost certainly a state-lock TIMEOUT
        # held by another agent (orch-state.ps1 exits 1 on LOCK TIMEOUT), NOT an illegal transition
        # (we only flip from blocked/gate_pending, both -> done are allowed). Transient multi-agent
        # condition. Best-effort: warn and stop this tick (retry next session) -- a reconcile
        # auxiliary must NEVER abort a session (protocol.md Step 1.4 runs before work selection).
        Write-Warning ("could not flip {0} ({1}) now (state lock busy?) -- will retry next session." -f $gate, $status)
        break
    }
    $ts  = (Get-Date).ToUniversalTime().ToString('o')
    $evt = ([ordered]@{ ts = $ts; event = 'gate_reconciled'; item = $gate; from = $status; to = 'done'; pr = $prNum; branch = $branch } | ConvertTo-Json -Compress)
    [System.IO.File]::AppendAllText($eventsPath, $evt + "`r`n", $utf8NoBom)
    Write-Host ("  -> {0} : {1} -> done (PR #{2} merged)" -f $gate, $status, $prNum) -ForegroundColor Green
    $flipped += $gate
}

# -- 3. Persist $ORCH_REPO ----------------------------------------
if ($flipped.Count -eq 0) {
    Write-Host "Nothing to reconcile -- no blocked/gate_pending gate whose PR is merged."
    exit 0
}
if ($DryRun) { Write-Host ("[DRY-RUN] {0} gate(s) would be flipped." -f $flipped.Count); exit 0 }

Push-Location $orchRepo
try {
    git add state.yaml events.jsonl | Out-Null
    git commit -m ("state: reconcile gates -- {0} -> done (merged PR detected)" -f ($flipped -join ', ')) | Out-Host
    if ($LASTEXITCODE -eq 0) {
        git push | Out-Host   # best-effort; the runner pushes again on its own tick
    }
} finally { Pop-Location }

Write-Host ("OK -- {0} gate(s) reconciled: {1}" -f $flipped.Count, ($flipped -join ', ')) -ForegroundColor Green
