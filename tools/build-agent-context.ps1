#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Assembles scoped context file list for an orchestration work item (Conformat).
.DESCRIPTION
    Given an item ID from the manifest, resolves the item's lot, then collects relevant
    documentation files the agent should read before starting work.
    Outputs one file path per line to stdout.
.PARAMETER ItemId
    The manifest item ID (e.g., PIV01, TVA02, WPF03).
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/build-agent-context.ps1 -ItemId PIV01
#>
param(
    [Parameter(Mandatory)]
    [string]$ItemId,

    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $ManifestPath) {
    $ManifestPath = Join-Path $repoRoot 'orchestration\manifest.yaml'
}

if (-not (Test-Path $ManifestPath)) {
    Write-Error "Manifest not found: $ManifestPath"
    exit 1
}

# ── Resolve the item's lot ───────────────────────────────────────
$manifest = Get-Content $ManifestPath -Raw
$itemMatch = [regex]::Match($manifest, "\{\s*id:\s*$ItemId\s*,\s*lot:\s*([A-Z_0-9]+)")
if (-not $itemMatch.Success) {
    # Gates have no lot — they get global docs only
    $lot = $null
}
else {
    $lot = $itemMatch.Groups[1].Value
}

# ── Lot-to-context mapping ───────────────────────────────────────
# Maps lots to the feature specs and docs the agent must read.
# Conventions docs (always included) + lot-specific conception specs.

$lotToSpecs = @{
    'SOL' = @('docs/market/Conception-Produit-Passerelle.md')
    'PIV' = @('docs/conception/F01-F02-Modele-Pivot-Contrat-Extraction.md')
    'TVA' = @('docs/conception/F03-Mapping-TVA.md', 'docs/conception/F01-F02-Modele-Pivot-Contrat-Extraction.md')
    'VAL' = @('docs/conception/F04-Controles-Qualite-Validation.md', 'docs/conception/F07-F08-Avoirs-Frontiere-B2B-B2C.md')
    'TRK' = @('docs/conception/F06-Tracking-Piste-Audit.md')
    'PAC' = @('docs/conception/F05-Client-API-B2Brouter.md')
    'PIP' = @('docs/conception/F11-CLI-Mode-Automatique.md', 'docs/conception/F07-F08-Avoirs-Frontiere-B2B-B2C.md', 'docs/conception/F09-E-Reporting-Paiement.md')
    'CLI' = @('docs/conception/F11-CLI-Mode-Automatique.md')
    'SVC' = @('docs/conception/F11-CLI-Mode-Automatique.md')
    'ADP' = @('docs/conception/F01-F02-Modele-Pivot-Contrat-Extraction.md', 'docs/conception/F03-Mapping-TVA.md', 'docs/conception/F09-E-Reporting-Paiement.md')
    'WPF' = @('docs/conception/F10-Console-Admin-WPF.md')
    'CFG' = @('docs/conception/F11-CLI-Mode-Automatique.md', 'docs/conception/F05-Client-API-B2Brouter.md', 'docs/market/Offre-Editeur-Passerelle.md')
    'DOC' = @('docs/conception/F10-Console-Admin-WPF.md', 'docs/conception/README-Index-Conception.md')
}

# ── Always-included context ──────────────────────────────────────
$context = [System.Collections.Generic.List[string]]::new()

$always = @(
    'blueprint.md',
    'CLAUDE.md',
    'tasks/lessons.md',
    'docs/conception/README-Index-Conception.md'
)
foreach ($f in $always) {
    $p = Join-Path $repoRoot $f
    if (Test-Path $p) { $context.Add($p) }
}

# Architecture docs (exist after SOL03)
$archDir = Join-Path $repoRoot 'docs\architecture'
if (Test-Path $archDir) {
    Get-ChildItem $archDir -Filter '*.md' -File | ForEach-Object { $context.Add($_.FullName) }
}

# Lot file (item details)
if ($lot) {
    $lotFile = Join-Path $repoRoot "orchestration\items\$lot.yaml"
    if (Test-Path $lotFile) { $context.Add($lotFile) }

    # Lot-specific specs
    if ($lotToSpecs.ContainsKey($lot)) {
        foreach ($f in $lotToSpecs[$lot]) {
            $p = Join-Path $repoRoot ($f -replace '/', '\')
            if (Test-Path $p) { $context.Add($p) }
            else { Write-Warning "Referenced spec not found: $f" }
        }
    }
}

# ── Output ───────────────────────────────────────────────────────
$context | Select-Object -Unique
