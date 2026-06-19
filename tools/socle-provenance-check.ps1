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

    Scope of the guard: it pins the true vendored Stratum.* files only. Files ADDED by Liakont
    under these roots (legitimate Liakont code placed in the vendored tree, e.g. SOL06 multi-tenant
    job mechanics, the job catalog/executions of FIX210/FIX211, the browser-time display, the
    Keycloak user provisioner) are NOT pinned: editing them is normal Liakont work, not a Stratum
    drift. They are identified — and excluded from BOTH -Generate and check — by a head marker:
    the file's FIRST line contains the literal string "Liakont addition" (the convention of
    provenance section 4.14, e.g. "// Liakont addition (SOL06): ...", "@* Liakont addition ... *@",
    "-- Liakont addition ..."). The guard's job is to catch a silent MODIFICATION or DELETION of an
    existing vendored Stratum file, which is exactly CLAUDE.md rule 11.

    Why a head marker rather than an exclusion list: the marker travels with the file (a new
    Liakont addition is excluded the moment it carries the marker, no second place to update) and
    is self-contained (the check runs on CI with no access to the upstream Stratum repo). A true
    Stratum file never carries the marker, so it stays pinned and a real edit is still caught.

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
# A vendored-root file is a Liakont ADDITION (not a Stratum.* file) when its FIRST line carries the
# head marker. Match is case-SENSITIVE (-cmatch) and anchored to a leading comment delimiter
# (// @* -- #), so a stray substring elsewhere on the line, or a different-case occurrence on a real
# Stratum file, cannot accidentally exclude it from the guard (review RDL09 round 1, P2).
function Test-IsLiakontAddition([string]$absPath) {
    $firstLine = Get-Content -LiteralPath $absPath -TotalCount 1 -ErrorAction SilentlyContinue
    return [bool]($firstLine -and ($firstLine -cmatch '^\s*(//|@\*|--|#)\s*Liakont addition\b'))
}

function Get-VendoredHashes {
    Push-Location $repoRoot
    try {
        $tracked = & git ls-files -- $vendoredRoots
        if ($LASTEXITCODE -ne 0) { throw "git ls-files failed (exit $LASTEXITCODE)" }
        $tracked = @($tracked | Where-Object { $_ })
        # Only hash files that exist on disk (a tracked-but-deleted working-tree file would
        # make git hash-object error; we treat its absence as drift later, against the baseline).
        $existing = @($tracked | Where-Object { Test-Path -LiteralPath (Join-Path $repoRoot $_) })

        # Exclude Liakont additions (head marker — see the scope note above). These are Liakont
        # code placed under the vendored tree, not Stratum.* files: they must not be pinned (else a
        # legitimate edit would fail the guard as a "silent drift"). A file is an addition when its
        # FIRST line contains the literal "Liakont addition". The check is line-1 only and string-
        # anchored, so a true Stratum file (which never carries the marker) is never excluded.
        $existing = @($existing | Where-Object {
            -not (Test-IsLiakontAddition (Join-Path $repoRoot $_))
        })

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
    # Transparency (review RDL09, P2): if a previous baseline exists, list any path that WAS pinned
    # and is now excluded by a head marker (a Stratum.* file leaving the guard). Legitimate during
    # RDL09's cleanup of wrongly-pinned additions; suspicious afterward — surface it so human review
    # catches a bad drop. Does not block (-Generate is itself a deliberate, reviewable act).
    if (Test-Path $baselinePath) {
        $prevPinned = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
        foreach ($line in (Get-Content $baselinePath)) {
            if ($line -match '^[0-9a-f]{40}\s+(.+)$') { [void]$prevPinned.Add($matches[1].Trim()) }
        }
        $leaving = @($prevPinned | Where-Object {
            -not $map.ContainsKey($_) -and
            (Test-Path -LiteralPath (Join-Path $repoRoot $_)) -and
            (Test-IsLiakontAddition (Join-Path $repoRoot $_))
        })
        if ($leaving.Count -gt 0) {
            Write-Host "ATTENTION : $($leaving.Count) fichier(s) precedemment epingle(s) quittent la garde via un marqueur d'ajout (verifier en revue) :" -ForegroundColor Yellow
            $leaving | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
        }
    }
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
$tampered = @()
foreach ($path in $expected.Keys) {
    if (-not $current.ContainsKey($path)) {
        # Missing from $current: either genuinely deleted, OR present on disk but excluded by a head
        # marker. The latter means a PINNED Stratum.* file just gained a "Liakont addition" marker —
        # after RDL09 no pinned file is a Liakont addition, so this is provenance tampering that would
        # silently drop the file from the guard at the next -Generate. It is NOT consignable (review
        # RDL09 round 1, P2 — closes the marker+consign+-Generate bypass at check time).
        $abs = Join-Path $repoRoot $path
        if ((Test-Path -LiteralPath $abs) -and (Test-IsLiakontAddition $abs)) {
            $tampered += $path
        }
        else {
            $drifted += [pscustomobject]@{ Path = $path; Kind = 'supprime' }
        }
    }
    elseif ($current[$path] -ne $expected[$path]) {
        $drifted += [pscustomobject]@{ Path = $path; Kind = 'modifie' }
    }
}

if ($tampered.Count -gt 0) {
    Write-Host "FAIL: un fichier Stratum.* EPINGLE a gagne un marqueur d'ajout Liakont (falsification de provenance) :" -ForegroundColor Red
    $tampered | ForEach-Object { Write-Host "  [falsifie] $_" -ForegroundColor Red }
    Write-Host "Un fichier epingle ne peut pas devenir un << ajout Liakont >>. Retirer le marqueur du fichier ; si la sortie de garde est reellement voulue, la passer par -Generate (acte delibere, journal provenance §4.37)." -ForegroundColor Red
    exit 2
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
