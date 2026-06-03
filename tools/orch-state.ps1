#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Thread-safe state.yaml operations for multi-agent orchestration (Conformat).
.DESCRIPTION
    All state.yaml mutations MUST go through this script to prevent concurrent
    corruption when multiple agents run in parallel. Uses a named mutex for locking.
.PARAMETER Command
    The operation: read, claim, update, release.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/orch-state.ps1 read
    powershell -ExecutionPolicy Bypass -File tools/orch-state.ps1 claim -ItemId PIV01 -SlotId 2 -SessionId "orch-..." -ClonePath "C:\Source\Conformat2" -Subbranch "feat/core-foundation-PIV01"
    powershell -ExecutionPolicy Bypass -File tools/orch-state.ps1 update -ItemId PIV01 -Status done
    powershell -ExecutionPolicy Bypass -File tools/orch-state.ps1 release -SlotId 2
#>
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('read', 'claim', 'update', 'release')]
    [string]$Command,

    [string]$ItemId,
    [string]$SlotId,
    [string]$SessionId,
    [string]$ClonePath,
    [string]$Subbranch,
    [string]$Status
)

$ErrorActionPreference = 'Stop'

# ── Resolve ORCH_REPO ────────────────────────────────────────────
$orchRepo = $env:ORCH_REPO
if (-not $orchRepo) {
    $orchRepo = 'C:\Source\conformat-orchestration'
}
$statePath = Join-Path $orchRepo 'state.yaml'

if (-not (Test-Path $statePath)) {
    Write-Error "state.yaml not found at $statePath"
    exit 1
}

# ── Two-layer locking ────────────────────────────────────────────
# Layer 1: named mutex — protects against concurrent agents on the SAME machine.
# Layer 2: exclusive-creation lock file in $ORCH_REPO — protects against agents on
#          OTHER machines (the state repo can be a shared/synced folder; a Windows
#          named mutex is invisible across hosts).
$mutexName = 'Global\ConformatOrchStateYaml'
$lockFilePath = Join-Path $orchRepo '.state.lock'

function Invoke-WithLock {
    param([scriptblock]$Action)

    $mutex = [System.Threading.Mutex]::new($false, $mutexName)
    $acquired = $false
    $lockStream = $null
    try {
        $acquired = $mutex.WaitOne(10000)  # 10 second timeout
        if (-not $acquired) {
            Write-Error "LOCK TIMEOUT: another agent holds the state.yaml lock (mutex)"
            exit 1
        }
        # Cross-host lock: exclusive file creation (CreateNew fails if the file exists).
        $deadline = (Get-Date).AddSeconds(10)
        while ($true) {
            try {
                $lockStream = [System.IO.File]::Open($lockFilePath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
                $info = [System.Text.Encoding]::UTF8.GetBytes("host=$env:COMPUTERNAME pid=$PID utc=$((Get-Date).ToUniversalTime().ToString('o'))")
                $lockStream.Write($info, 0, $info.Length)
                $lockStream.Flush()
                break
            }
            catch {
                if ((Get-Date) -gt $deadline) {
                    Write-Error "LOCK TIMEOUT: cross-host lock file exists ($lockFilePath). Another host's agent holds it. If no agent is running anywhere (stale lock after a crash), delete the file manually."
                    exit 1
                }
                Start-Sleep -Milliseconds 500
            }
        }
        & $Action
    }
    finally {
        if ($lockStream) {
            $lockStream.Dispose()
            Remove-Item $lockFilePath -Force -ErrorAction SilentlyContinue
        }
        if ($acquired) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

# ── YAML helpers (simple line-based, no external dependency) ─────
# state.yaml uses simple key: { ... } format on single lines.

function Read-State {
    # Explicit UTF-8: PowerShell 5.1 Get-Content reads BOM-less UTF-8 as ANSI and would
    # corrupt accented characters on a read/write round trip.
    [System.IO.File]::ReadAllText($statePath, [System.Text.Encoding]::UTF8)
}

function Write-State {
    param([string]$Content)
    [System.IO.File]::WriteAllText($statePath, $Content, (New-Object System.Text.UTF8Encoding $false))
}

# ── Commands ─────────────────────────────────────────────────────

switch ($Command) {
    'read' {
        # No lock needed for read-only
        $content = Read-State
        Write-Output $content
    }

    'claim' {
        if (-not $ItemId) { Write-Error "-ItemId required"; exit 1 }
        if (-not $SlotId) { Write-Error "-SlotId required"; exit 1 }
        if (-not $SessionId) { Write-Error "-SessionId required"; exit 1 }
        # Subbranch is mandatory and must follow the convention <segment-branch>-<item_id>
        # (dash separator — see protocol.md Step 3). "unknown" placeholders are forbidden.
        if (-not $Subbranch) { Write-Error "-Subbranch required (convention: <segment-branch>-$ItemId)"; exit 1 }
        if ($Subbranch -notmatch "-$([regex]::Escape($ItemId))$") {
            Write-Error "Subbranch '$Subbranch' does not follow the convention <segment-branch>-$ItemId"
            exit 1
        }
        if ($Subbranch -match "/$([regex]::Escape($ItemId))$") {
            Write-Error "Subbranch '$Subbranch' uses a slash before the item id — use a dash (git ref namespace collision)"
            exit 1
        }

        Invoke-WithLock {
            $content = Read-State

            # Check item is still pending
            if ($content -match "(?m)^\s+${ItemId}:\s*\{[^}]*status:\s*(\w+)") {
                $currentStatus = $Matches[1]
                if ($currentStatus -ne 'pending') {
                    Write-Host "[CLAIM FAILED] $ItemId is '$currentStatus', not 'pending'" -ForegroundColor Yellow
                    exit 1
                }
            }
            else {
                Write-Error "Item $ItemId not found in state.yaml"
                exit 1
            }

            # Update item status to claimed
            $content = $content -replace "(?m)(^\s+${ItemId}:\s*\{[^}]*status:\s*)pending", "`${1}claimed"

            # Build active_sessions entry
            $now = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
            $clonePathEsc = if ($ClonePath) { $ClonePath } else { (Get-Location).Path }

            $slotEntry = "  slot-${SlotId}: { session_id: `"$SessionId`", item_id: `"$ItemId`", clone_path: `"$clonePathEsc`", subbranch: `"$Subbranch`", started_at: `"$now`" }"

            # Replace the slot line
            $content = $content -replace "(?m)^\s+slot-${SlotId}:\s*.*$", $slotEntry

            Write-State $content
            Write-Host "[CLAIMED] $ItemId by slot-$SlotId ($SessionId)" -ForegroundColor Green
        }
    }

    'update' {
        if (-not $ItemId) { Write-Error "-ItemId required"; exit 1 }
        if (-not $Status) { Write-Error "-Status required"; exit 1 }

        # Status must be one of the statuses documented in state.yaml's header.
        $validStatuses = @('pending', 'claimed', 'in_progress', 'done', 'blocked', 'stale', 'failed', 'gate_pending')
        if ($validStatuses -notcontains $Status) {
            Write-Error "Invalid status '$Status'. Allowed: $($validStatuses -join ', ')"
            exit 1
        }

        Invoke-WithLock {
            $content = Read-State

            # Update item status (with transition guard)
            if ($content -match "(?m)^\s+${ItemId}:\s*\{[^}]*status:\s*(\w+)") {
                $currentStatus = $Matches[1]

                # State machine: prevents accidental promotions (e.g. pending → done without claim)
                # and any demotion of a done item. 'done' is terminal: done items get purged
                # from state.yaml (absent = done), they are never demoted.
                $allowedTransitions = @{
                    'pending'      = @('claimed', 'blocked', 'gate_pending')
                    'claimed'      = @('in_progress', 'done', 'blocked', 'failed', 'stale', 'pending')
                    'in_progress'  = @('done', 'blocked', 'failed', 'stale')
                    'blocked'      = @('pending', 'claimed', 'in_progress', 'done')
                    'stale'        = @('done', 'pending', 'claimed', 'failed')
                    'failed'       = @('pending', 'claimed')
                    'gate_pending' = @('done', 'pending')
                    'done'         = @()
                }
                $allowed = $allowedTransitions[$currentStatus]
                if ($null -eq $allowed -or $allowed -notcontains $Status) {
                    $allowedList = if ($allowed) { $allowed -join ', ' } else { '(none — terminal status)' }
                    Write-Error "Illegal transition for ${ItemId}: '$currentStatus' → '$Status'. Allowed from '$currentStatus': $allowedList"
                    exit 1
                }

                $content = $content -replace "(?m)(^\s+${ItemId}:\s*\{[^}]*status:\s*)\w+", "`${1}$Status"
                Write-State $content
                Write-Host "[UPDATED] $ItemId : $currentStatus → $Status" -ForegroundColor Green
            }
            else {
                Write-Error "Item $ItemId not found in state.yaml"
                exit 1
            }
        }
    }

    'release' {
        if (-not $SlotId) { Write-Error "-SlotId required"; exit 1 }

        Invoke-WithLock {
            $content = Read-State

            # Clear the slot entry
            $content = $content -replace "(?m)^\s+slot-${SlotId}:\s*\{[^}]*\}", "  slot-${SlotId}: null"

            Write-State $content
            Write-Host "[RELEASED] slot-$SlotId" -ForegroundColor Green
        }
    }
}
