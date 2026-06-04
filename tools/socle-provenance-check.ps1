#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Provenance guard for the vendored Stratum socle (CLAUDE.md rule 11 / SOL03).
.DESCRIPTION
    The vendored Stratum.* tree must not drift silently. Every vendored source file is
    pinned to a checked-in baseline of git blob hashes (tools/socle-baseline.sha1). On each
    run this script recomputes the working-tree hash of every pinned file; any file whose
    content drifted from the baseline (modified or deleted) MUST be referenced in
    docs/architecture/provenance-socle-stratum.md, otherwise the run fails.

    Why git blob hashes (git hash-object) and not raw SHA-256 of the bytes: git hash-object
    applies the same clean filters as a commit would (line-ending normalization), so the
    hash is identical on a Windows dev box and a Linux CI runner for an unchanged file, while
    still differing for any real working-tree edit. A raw byte hash would report false drift
    on a platform with a different autocrlf setting.

    Vendored roots (provenance section 2): src/Common and the vendored modules
    (Identity, Party/Contracts, Job, Notification, Audit). The adapted Liakont.Host is NOT
    vendored Stratum.* (it is reviewed as Liakont code) and is excluded.

    Scope of the guard: it pins the files present at baseline generation (the consigned
    vendored set = SOL01 vendoring + the modifications documented in provenance section 4).
    Files ADDED later under these roots are NOT pinned (they may be legitimate Liakont code,
    e.g. SOL06 multi-tenant job mechanics) and are ignored. The guard's job is to catch a
    silent MODIFICATION or DELETION of an existing vendored Stratum file, which is exactly
    CLAUDE.md rule 11.

    Modes:
      (default)  check    : recompute hashes, fail on drift not consigned in provenance.
      -Generate           : (re)generate the baseline from the current tree (git ls-files).
                            Run this ONLY after a new vendored modification is consigned in
                            provenance section 4 (a deliberate, reviewable act).

    Exit codes:
      0 = clean (no drift, or all drift consigned in provenance)
      2 = drift NOT consigned in provenance (a silent Stratum.* modification)
      3 = baseline missing / tool error
#>
param([switch]$Generate)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
# [IO.Path]::Combine uses the platform separator, so the script runs unchanged on Windows
# (local verify-fast) and Linux (the CI platform job invokes it via pwsh).
$baselinePath = [System.IO.Path]::Combine($repoRoot, 'tools', 'socle-baseline.sha1')
$provenancePath = [System.IO.Path]::Combine($repoRoot, 'docs', 'architecture', 'provenance-socle-stratum.md')

# Vendored roots (provenance section 2). Paths are forward-slash, repo-root relative,
# matching git ls-files output.
$vendoredRoots = @(
    'src/Common',
    'src/Modules/Identity',
    'src/Modules/Party',
    'src/Modules/Job',
    'src/Modules/Notification',
    'src/Modules/Audit'
)

# Returns a hashtable { relpath -> git blob hash } for every currently-tracked vendored file
# that exists on disk. Calls `git hash-object` with the paths as ARGUMENTS, in batches (one
# process per batch, not per file) so 1200+ files hash in well under a second. Arguments are
# used rather than `--stdin-paths` because PowerShell 5.1 prepends a UTF-8 BOM to a native
# command's piped stdin, which corrupts the first path.
function Get-VendoredHashes {
    Push-Location $repoRoot
    try {
        $tracked = & git ls-files -- $vendoredRoots
        if ($LASTEXITCODE -ne 0) { throw "git ls-files failed (exit $LASTEXITCODE)" }
        $tracked = @($tracked | Where-Object { $_ })
        # Only hash files that exist on disk (a tracked-but-deleted working-tree file would
        # make git hash-object error; we treat its absence as drift later, against the baseline).
        $existing = @($tracked | Where-Object { Test-Path -LiteralPath (Join-Path $repoRoot $_) })

        $map = @{}
        $batchSize = 200
        for ($start = 0; $start -lt $existing.Count; $start += $batchSize) {
            $end = [math]::Min($start + $batchSize, $existing.Count) - 1
            # @(...) forces an array: a 1-element slice is a scalar string, and indexing a
            # string ($batch[0]) would return its first CHARACTER, not the path.
            $batch = @($existing[$start..$end])
            $hashes = @(& git hash-object -- $batch)
            if ($LASTEXITCODE -ne 0) { throw "git hash-object failed (exit $LASTEXITCODE)" }
            if ($hashes.Count -ne $batch.Count) {
                throw "git hash-object returned $($hashes.Count) hashes for $($batch.Count) files"
            }
            for ($i = 0; $i -lt $batch.Count; $i++) {
                $map[$batch[$i]] = $hashes[$i].Trim()
            }
        }
        return $map
    }
    finally { Pop-Location }
}

# ── Generate mode ────────────────────────────────────────────────
if ($Generate) {
    $map = Get-VendoredHashes
    $lines = $map.Keys | Sort-Object | ForEach-Object { "{0}  {1}" -f $map[$_], $_ }
    $header = @(
        "# Baseline de provenance du socle Stratum vendored (genere par tools/socle-provenance-check.ps1 -Generate)",
        "# Format : <git-blob-sha1>  <chemin relatif>   (hash = git hash-object, independant de la plateforme)",
        "# Toute derive d'un de ces fichiers doit etre consignee dans docs/architecture/provenance-socle-stratum.md.",
        "# Voir SOL03 et CLAUDE.md regle 11. Ne pas editer a la main : regenerer apres consignation."
    )
    $content = ($header + $lines) -join "`n"
    # UTF-8 sans BOM est acceptable ici (contenu ASCII pur) ; on reste neutre.
    [System.IO.File]::WriteAllText($baselinePath, $content + "`n", [System.Text.UTF8Encoding]::new($false))
    Write-Host "Baseline ecrite : tools/socle-baseline.sha1 ($($lines.Count) fichiers vendored)" -ForegroundColor Green
    exit 0
}

# ── Check mode ───────────────────────────────────────────────────
if (-not (Test-Path $baselinePath)) {
    Write-Host "FAIL: baseline absente ($baselinePath). Lancer: tools/socle-provenance-check.ps1 -Generate" -ForegroundColor Red
    exit 3
}
if (-not (Test-Path $provenancePath)) {
    Write-Host "FAIL: provenance absente ($provenancePath)." -ForegroundColor Red
    exit 3
}

# Parse the baseline into { relpath -> expected hash }.
$expected = @{}
foreach ($line in (Get-Content $baselinePath)) {
    $t = $line.Trim()
    if (-not $t -or $t.StartsWith('#')) { continue }
    # "<hash>  <path>" — split on the first run of whitespace.
    if ($t -match '^([0-9a-f]{40})\s+(.+)$') {
        $expected[$matches[2].Trim()] = $matches[1]
    }
    else {
        Write-Host "FAIL: ligne de baseline non reconnue: $t" -ForegroundColor Red
        exit 3
    }
}
if ($expected.Count -eq 0) {
    Write-Host "FAIL: baseline vide ($baselinePath)." -ForegroundColor Red
    exit 3
}

$current = Get-VendoredHashes

# A pinned file is "drifted" if it is missing from the working tree (deletion) or its hash
# differs from the baseline (modification). Added files (present now, absent from baseline)
# are intentionally NOT flagged — see the scope note in the header.
$drifted = @()
foreach ($path in $expected.Keys) {
    if (-not $current.ContainsKey($path)) {
        $drifted += [pscustomobject]@{ Path = $path; Kind = 'supprime' }
    }
    elseif ($current[$path] -ne $expected[$path]) {
        $drifted += [pscustomobject]@{ Path = $path; Kind = 'modifie' }
    }
}

if ($drifted.Count -eq 0) {
    Write-Host "PROVENANCE OK : $($expected.Count) fichiers vendored conformes au baseline." -ForegroundColor Green
    exit 0
}

# A drifted file is consigné ONLY if its EXACT repo-relative path is listed in the
# machine-readable consigned-drift block of the provenance doc (between the START/END markers).
# NO leaf-filename match and NO loose substring: vendored filenames collide heavily
# (ServiceCollectionExtensions.cs x14, _Imports.razor x6, MODULE.md/INVARIANTS.md/SCENARIOS.md x4,
# NullPartyQueries.cs, AssemblyInfo.cs, ...). A leaf or substring match would let a silent edit
# to file B pass merely because a homonym A is consigned — the exact false-green found in SOL03
# review round 1 (P1). Only an anchored, full-path, line-exact match is safe.
$startMarker = 'SOCLE-CONSIGNED-DRIFT:START'
$endMarker = 'SOCLE-CONSIGNED-DRIFT:END'
$inBlock = $false
$sawStart = $false
$sawEnd = $false
$consignedPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($line in (Get-Content $provenancePath)) {
    if ($line -match $startMarker) { $inBlock = $true; $sawStart = $true; continue }
    if ($line -match $endMarker) { $inBlock = $false; $sawEnd = $true; continue }
    if ($inBlock) {
        $p = $line.Trim()
        # Skip blanks and comment/markup lines; everything else is an exact repo-relative path.
        if (-not $p -or $p.StartsWith('#') -or $p.StartsWith('<!--') -or $p.StartsWith('//')) { continue }
        [void]$consignedPaths.Add($p)
    }
}
if (-not ($sawStart -and $sawEnd)) {
    Write-Host "FAIL: bloc de consignation introuvable dans provenance-socle-stratum.md (marqueurs $startMarker / $endMarker)." -ForegroundColor Red
    exit 3
}
$unconsigned = @($drifted | Where-Object { -not $consignedPaths.Contains($_.Path) })

if ($unconsigned.Count -eq 0) {
    Write-Host "PROVENANCE OK : $($drifted.Count) fichier(s) vendored modifie(s), tous consignes dans la provenance." -ForegroundColor Yellow
    $drifted | ForEach-Object { Write-Host "  [$($_.Kind)] $($_.Path)" -ForegroundColor Yellow }
    exit 0
}

Write-Host "FAIL: modification(s) du socle Stratum vendored NON consignee(s) dans la provenance :" -ForegroundColor Red
$unconsigned | ForEach-Object { Write-Host "  [$($_.Kind)] $($_.Path)" -ForegroundColor Red }
Write-Host ""
Write-Host "Action corrective : consigner chaque fichier dans docs/architecture/provenance-socle-stratum.md (section 4)," -ForegroundColor Red
Write-Host "puis regenerer le baseline (tools/socle-provenance-check.ps1 -Generate). Ne jamais modifier Stratum.* en silence." -ForegroundColor Red
exit 2
