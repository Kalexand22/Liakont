#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Shared bootstrap-state predicate for Liakont verification tooling.
.DESCRIPTION
    Single source of truth for "is this solution-bearing item still pending?" (SOL01 = platform,
    SOL02 = agent, SIG08 = on-site signature client). Consumed by tools/verify-fast.ps1 AND
    tools/run-tests.ps1: both decide bootstrap-skip vs FAILURE for a missing solution from it.
    Keeping ONE definition (dot-sourced) means the false-green guard has ONE self-tested source
    (tools/test-bootstrap-guard.ps1) instead of two copies that could silently diverge.
    Pure: dot-source it, no side effects on load.
    ASCII-only (PS 5.1 reads .ps1 without BOM in ANSI). Compatible pwsh 7 and Windows PowerShell 5.1.
#>

# Returns $true while the given item is still pending (absent from state = done).
# THROWS if the orchestration state repo / state.yaml is missing: the protocol makes
# state.yaml mandatory. Treating a missing state as "item pending" would let a deleted
# solution (or a misconfigured ORCH_REPO) pass as a bootstrap skip - a false green.
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
