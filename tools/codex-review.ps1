#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs a code review on current changes (Claude Code primary, Codex fallback).
.DESCRIPTION
    Uses Claude Code CLI (claude --print) as the primary reviewer.
    Falls back to Codex if Claude is unavailable.
    Modes:
    - Default (no args): reviews uncommitted working tree changes
    - -Base <ref>: reviews dirty tree (if any) + committed changes vs a ref
    - -Commit <sha>: reviews a specific commit
    Round parameter controls review depth:
    - Round 1 (default): full review
    - Round 2+: re-review (verify fixes only)
.PARAMETER Base
    Base ref to diff against. Also reviews uncommitted changes if tree is dirty.
.PARAMETER Commit
    Specific commit SHA to review.
.PARAMETER Round
    Review round number (default: 1). Round 1 = full review. Round 2+ = verify fixes only.
#>
param(
    [string]$Base = '',
    [string]$Commit = '',
    [int]$Round = 1,
    [ValidateSet('auto', 'claude', 'codex')]
    [string]$Engine = 'auto'
)

$ErrorActionPreference = 'Stop'

# --- Review instructions (Conformat-specific) ---
$reviewInstructions = @'
You are a strict code reviewer for Conformat, a French e-invoicing compliance gateway
(.NET Framework 4.8, WPF, SQLite, B2Brouter PA client). Read blueprint.md and
docs/architecture/ for project conventions before reviewing.

**Report only:**
1. Bugs probables
2. Regressions possibles
3. Problemes de robustesse
4. Problemes de securite (secrets en clair, donnees fiscales exposees)
5. Dette technique importante
6. Ecarts aux conventions du repo (blueprint.md, docs/architecture/)
7. Trous de test
8. Faux positifs / faux verts dans scripts, CI, verify, checks, or tooling

**Conformat-specific P1 rules (always blocking):**
- float/double used for monetary amounts (must be decimal, half-up, 2 decimals)
- Invented fiscal rule: VAT category, VATEX code, or threshold with no source in docs/conception/
- Blocking validation weakened to Warning, or validation bypass path
- Update/delete path on DocumentEvent, or automatic purge of audit tables
- Write operation (INSERT/UPDATE/DELETE) or lock on the legacy source database in an adapter
- Gateway.Core referencing an adapter project
- Secret in clear text (PA API key, SMTP password) in code, config, or logs
- WPF item without ViewModel unit tests, or business logic in code-behind

**Format per finding:**
[P1] or [P2] | file:line | concrete description | suggested fix

**Rules:**
- P1 = blocking (bug, security, regression, fiscal correctness). P2 = important but non-blocking.
- No compliments, no summaries, no cosmetic suggestions.
- If everything is clean: "No findings."
- Be strict and concrete.
- Review the **diff and the behavior**, not only file presence or naming.
- Check the real failure mode of automation: `continue-on-error`, silent skips, pass-by-default logic.
- For scripts, verify what happens on empty state, dirty state, failure state, and partial state.
- For CI, verify that a failing validation step really fails the pipeline.
- If a finding is based on a local verification limit (sandbox, restore, tooling issue), say so explicitly.
'@

$reReviewSuffix = @'

**Re-review scope (round {0}):**
- ONLY verify that the fixes for previous findings are correct and complete.
- ONLY check for regressions introduced by the fixes themselves.
- Do NOT expand scope to find new unrelated issues in unchanged code.
- Do NOT re-review code that was not modified since the last round.
- If the fixes are correct and introduce no regressions: "No findings."
'@

# --- Round display ---
if ($Round -gt 1) {
    Write-Host "=== Re-review round $Round (verify fixes only) ===" -ForegroundColor Cyan
}
else {
    Write-Host "=== Initial review (round 1) ===" -ForegroundColor Cyan
}

# --- Diff helpers ---
# Note: $ErrorActionPreference = 'Continue' is required around native git calls.
# In PowerShell 5.1, stderr output from a native command (even a benign warning
# like "LF will be replaced by CRLF") becomes a NativeCommandError record; with
# 'Stop' it terminates the script and the review never runs (false failure).
function Get-UncommittedDiff {
    $ErrorActionPreference = 'Continue'
    $staged = git diff --cached 2>$null | Out-String
    $unstaged = git diff 2>$null | Out-String
    $untracked = git ls-files --others --exclude-standard 2>$null
    $untrackedContent = ''
    if ($untracked) {
        foreach ($f in ($untracked -split "`n" | Where-Object { $_.Trim() })) {
            $untrackedContent += "`n--- new file: $f ---`n"
            $fileContent = Get-Content $f -Raw -ErrorAction SilentlyContinue
            if ($fileContent) { $untrackedContent += "$fileContent`n" }
        }
    }
    return ($staged + $unstaged + $untrackedContent)
}

function Get-BaseDiff {
    param([string]$Ref)
    $ErrorActionPreference = 'Continue'
    return (git diff "$Ref...HEAD" 2>$null | Out-String)
}

function Get-CommitDiff {
    param([string]$Sha)
    $ErrorActionPreference = 'Continue'
    return (git show $Sha 2>$null | Out-String)
}

# --- Review runner ---
function Invoke-Review {
    param(
        [string]$Diff,
        [string]$Label,
        [switch]$Uncommitted
    )

    if ([string]::IsNullOrWhiteSpace($Diff)) {
        Write-Host "[SKIP] No diff content for: $Label" -ForegroundColor Yellow
        return
    }

    $prompt = $reviewInstructions
    if ($Round -gt 1) {
        $prompt += ($reReviewSuffix -f $Round)
    }
    $prompt += "`n`n--- DIFF TO REVIEW ---`n$Diff"

    # --- Engine selection ---
    $useClaude = $Engine -ne 'codex'
    $useCodex  = $Engine -ne 'claude'

    $claudeExit = 1  # assume failed unless we try and succeed
    if ($useClaude) {
        Write-Host "=== Claude Code review: $Label ===" -ForegroundColor Cyan
        $tmpFile = Join-Path ([System.IO.Path]::GetTempPath()) "claude-review-$([guid]::NewGuid().ToString('N')).txt"
        try {
            [System.IO.File]::WriteAllText($tmpFile, $prompt, (New-Object System.Text.UTF8Encoding $false))
            $ErrorActionPreference = 'Continue'
            # Force l'auth abonnement (OAuth) pour le sous-processus claude :
            # ANTHROPIC_API_KEY (héritée de l'env Windows) pointe vers un compte API
            # sans crédits et ferait échouer la review ("Credit balance is too low").
            $env:ANTHROPIC_API_KEY = $null
            Get-Content $tmpFile -Raw | & claude --print --model opus
            $claudeExit = $LASTEXITCODE
            $ErrorActionPreference = 'Stop'
        }
        finally {
            Remove-Item $tmpFile -ErrorAction SilentlyContinue
        }

        if ($claudeExit -eq 0) {
            return
        }
    }

    if (-not $useCodex) {
        if ($claudeExit -ne 0) {
            Write-Host "[FAIL] Claude Code review failed (exit $claudeExit) and no fallback engine allowed." -ForegroundColor Red
            exit $claudeExit
        }
        return
    }

    # Codex (fallback in auto mode, primary if -Engine codex)
    if ($Engine -eq 'auto') {
        Write-Host "[FALLBACK] Claude Code failed (exit $claudeExit) - trying Codex for: $Label" -ForegroundColor Yellow
    } else {
        Write-Host "=== Codex review: $Label ===" -ForegroundColor Cyan
    }
    try {
        $null = Get-Command codex -ErrorAction Stop
    }
    catch {
        Write-Host "[FAIL] Neither Claude Code nor Codex available." -ForegroundColor Red
        exit 1
    }

    $codexEffort = 'high'
    if ($Round -gt 1) { $codexEffort = 'medium' }
    $codexArgs = @('-c', "model_reasoning_effort=$codexEffort")
    if ($Uncommitted) {
        $codexArgs += '--uncommitted'
    }
    elseif ($script:Commit) {
        $codexArgs += '--commit', $script:Commit
    }
    elseif ($script:Base) {
        $codexArgs += '--base', $script:Base
    }
    else {
        $codexArgs += '--uncommitted'
    }
    $ErrorActionPreference = 'Continue'
    codex review @codexArgs 2>&1
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[FAIL] Codex review also failed (exit $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

# --- Main ---
if ($Commit) {
    $diff = Get-CommitDiff -Sha $Commit
    Invoke-Review -Diff $diff -Label "commit $Commit"
}
elseif ($Base) {
    # Review uncommitted changes first if tree is dirty
    $ErrorActionPreference = 'Continue'
    $dirtyCheck = git status --porcelain 2>$null | Out-String
    $ErrorActionPreference = 'Stop'

    if ($dirtyCheck.Trim().Length -gt 0) {
        $diff = Get-UncommittedDiff
        Invoke-Review -Diff $diff -Label 'uncommitted changes' -Uncommitted
    }

    $diff = Get-BaseDiff -Ref $Base
    Invoke-Review -Diff $diff -Label "vs $Base"
}
else {
    # Default: review uncommitted working tree changes only
    $ErrorActionPreference = 'Continue'
    $statusOutput = git status --porcelain 2>$null | Out-String
    $ErrorActionPreference = 'Stop'

    if ($statusOutput.Trim().Length -gt 0) {
        $diff = Get-UncommittedDiff
        Invoke-Review -Diff $diff -Label 'uncommitted changes' -Uncommitted
    }
    else {
        Write-Host "[SKIP] No uncommitted changes to review." -ForegroundColor Yellow
        Write-Host 'Use -Base <ref> to review committed branch changes.' -ForegroundColor Yellow
    }
}
