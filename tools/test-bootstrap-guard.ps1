#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Self-test of the bootstrap false-green guard (tools/sol-state-lib.ps1 : Test-SolItemPending).
.DESCRIPTION
    The build/test of each solution (platform, agent, on-site signature client) is skipped while
    its item is still pending, but is a FAILURE once the item is done and the solution is missing
    (the solution was deleted). A skip that hid a deleted solution would be a false green
    (CLAUDE.md : a guard that passes when the thing it guards is broken is a false green).
    This self-test proves the predicate decides BOTH directions correctly, on synthetic state
    files, WITHOUT touching the real $ORCH_REPO or any solution on disk:
      - item done            -> $false (guard FAILS on a missing .sln : deleted-solution caught)
      - item pending         -> $true  (guard SKIPS : legitimate bootstrap)
      - item absent          -> $false (absent = done : guard FAILS on a missing .sln)
      - item claimed         -> $true  (present but not done : still bootstrap, locks the contract)
      - exact-id match       -> SIG08 not matched by a SIG080 line (no accidental prefix skip)
      - state.yaml missing   -> throws (never a silent skip on a misconfigured ORCH_REPO)
    Cabled in verify-fast.ps1 as an always-run step. ASCII-only (PS 5.1 reads .ps1 without BOM
    in ANSI). Compatible pwsh 7 and Windows PowerShell 5.1.
#>
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/sol-state-lib.ps1"

$script:failures = 0
function Assert-That([bool]$Condition, [string]$Name) {
    if ($Condition) {
        Write-Host "PASS  $Name" -ForegroundColor Green
    }
    else {
        Write-Host "FAIL  $Name" -ForegroundColor Red
        $script:failures = $script:failures + 1
    }
}

# Runs Test-SolItemPending against a SYNTHETIC state.yaml in a throwaway ORCH_REPO, restoring the
# real ORCH_REPO afterwards. $StateBody = the 'items:' block content (or $null to write NO
# state.yaml, simulating a missing/misconfigured state repo).
function Invoke-Predicate {
    param([string]$ItemId, [string]$StateBody, [switch]$NoStateFile)
    $saved = $env:ORCH_REPO
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("liakont-bootstrap-selftest-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null
    try {
        $env:ORCH_REPO = $tmp
        if (-not $NoStateFile) {
            Set-Content -Path (Join-Path $tmp 'state.yaml') -Value $StateBody -Encoding UTF8
        }
        return (Test-SolItemPending $ItemId)
    }
    finally {
        $env:ORCH_REPO = $saved
        Remove-Item -Path $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Helper: returns $true if Test-SolItemPending threw for the given (no) state.
function Predicate-Throws {
    param([string]$ItemId, [switch]$NoStateFile, [string]$StateBody)
    try {
        [void](Invoke-Predicate -ItemId $ItemId -StateBody $StateBody -NoStateFile:$NoStateFile)
        return $false
    }
    catch {
        return $true
    }
}

# Canonical state bodies (two-space indent + "{ status: ... }" shape, exactly like state.yaml).
$stateDone    = "items:`n  SIG08: { status: done, retry_count: 0 }`n"
$statePending = "items:`n  SIG08: { status: pending, retry_count: 0 }`n"
$stateClaimed = "items:`n  SIG08: { status: claimed, retry_count: 0 }`n"
$stateOther   = "items:`n  SIG080: { status: done, retry_count: 0 }`n"   # SIG08 absent; only a prefix-y id present

# --- Both directions of the guard ----------------------------------------------------------------
Assert-That (-not (Invoke-Predicate -ItemId 'SIG08' -StateBody $stateDone))    'done    -> not pending (guard FAILS on missing .sln : deleted solution caught)'
Assert-That ((Invoke-Predicate -ItemId 'SIG08' -StateBody $statePending))      'pending -> pending     (guard SKIPS : legitimate bootstrap)'
Assert-That (-not (Invoke-Predicate -ItemId 'SIG08' -StateBody $stateOther))   'absent  -> not pending (absent = done : guard FAILS on missing .sln)'
Assert-That ((Invoke-Predicate -ItemId 'SIG08' -StateBody $stateClaimed))      'claimed -> pending     (present-not-done is still bootstrap)'

# Exact id match : SIG08 must NOT be satisfied by a SIG080 line (no accidental prefix skip).
Assert-That (-not (Invoke-Predicate -ItemId 'SIG08' -StateBody $stateOther))   'exact id: SIG08 not matched by a SIG080 line'

# Missing state.yaml -> throws (never a silent skip on a misconfigured ORCH_REPO).
Assert-That (Predicate-Throws -ItemId 'SIG08' -NoStateFile)                    'missing state.yaml -> throws (no silent skip)'

if ($script:failures -gt 0) {
    Write-Host "ECHEC: $($script:failures) assertion(s) du self-test bootstrap-guard." -ForegroundColor Red
    exit 1
}
Write-Host "OK: self-test du faux-vert bootstrap-guard vert (6 scenarios, deux sens)." -ForegroundColor Green
exit 0
