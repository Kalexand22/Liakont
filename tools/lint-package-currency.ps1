# Lint CI — currency des paquets NuGet épinglés (RDF07, RL-PKG-1 / avenant ADR-0003).
#
# Garde de currency sur les DEUX catalogues centraux (racine plateforme + agent net48) :
#   - ÉCHEC (exit 1) si une version épinglée passe SOUS son plancher (`floor`) déclaré dans la
#     politique — un downgrade ou une régression de currency d'un paquet gouverné.
#   - ALERTE (warning, exit 0) si une version est >= plancher mais < `target` optionnel, OU pour
#     chaque `advisory` (retard CONNU et accepté, atténuation documentée). Les alertes restent
#     VISIBLES à chaque run (annotation ::warning:: sous GitHub Actions) sans bloquer la CI.
#
# La politique est déclarative (tools/package-currency-policy.json) : ajouter/retirer un paquet
# gouverné ou relever un plancher est un geste d'édition de données, jamais de code. Voir
# docs/adr/ADR-0003-stack-paquets-agent.md (avenant).
#
# Le lint est SANS parseur tiers : JSON natif (ConvertFrom-Json) + regex sur les balises
# <PackageVersion Include="X" Version="Y" /> des catalogues. Comparaison de versions semver-aware
# (release + pré-version). Anti faux-vert : politique absente/vide/invalide, catalogue déclaré
# introuvable, paquet gouverné absent de TOUS les catalogues, ou version illisible => exit 2
# (erreur de configuration, jamais un PASS par défaut).
#
# Exit 0 = tous les planchers respectés (des alertes ont pu être émises).
# Exit 1 = au moins un plancher violé (régression de currency).
# Exit 2 = erreur de lint/configuration (rien n'a été vérifié de façon fiable).

[CmdletBinding()]
param(
    # Politique de currency. Défaut : le fichier réel, résolu relativement à ce script.
    [string]$PolicyPath = '',
    # Racine du dépôt contre laquelle les chemins de catalogues (relatifs) sont résolus.
    # Défaut : le parent de tools/. Paramétrable pour le self-test (catalogues temporaires).
    [string]$RepoRoot = ''
)

$ErrorActionPreference = 'Stop'

if (-not $PolicyPath) { $PolicyPath = Join-Path $PSScriptRoot 'package-currency-policy.json' }
if (-not $RepoRoot)   { $RepoRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }

$IsCi = $env:GITHUB_ACTIONS -eq 'true'

function Fail-Config([string]$msg) {
    Write-Host "[LINT-CURRENCY] ECHEC CONFIG : $msg" -ForegroundColor Red
    exit 2
}

function Write-Alert([string]$msg) {
    if ($IsCi) { Write-Host "::warning title=Currency NuGet::$msg" }
    Write-Host "[LINT-CURRENCY] ALERTE : $msg" -ForegroundColor Yellow
}

# ── Comparaison de versions semver-aware : retourne -1, 0 ou 1 (a<b, a==b, a>b). ──
# Sépare release ('1.0.119') et pré-version ('beta.556'). Compare les segments release numériquement
# (complétés par des zéros). À release égale : une version SANS pré-version est SUPÉRIEURE à une AVEC
# (1.2.0 > 1.2.0-beta) ; deux pré-versions se comparent lexicographiquement (suffisant ici).
function Compare-PkgVersion([string]$a, [string]$b) {
    function Split-Ver([string]$v) {
        $dash = $v.IndexOf('-')
        if ($dash -ge 0) {
            return @{ Core = $v.Substring(0, $dash); Pre = $v.Substring($dash + 1) }
        }
        return @{ Core = $v; Pre = '' }
    }
    function Parse-Core([string]$core) {
        $parts = $core.Split('.')
        $nums = @()
        foreach ($p in $parts) {
            $n = 0
            if (-not [int]::TryParse($p, [ref]$n)) {
                throw "segment de version non numérique : '$p' (dans '$core')"
            }
            $nums += $n
        }
        return ,$nums
    }

    $sa = Split-Ver $a; $sb = Split-Ver $b
    $na = Parse-Core $sa.Core; $nb = Parse-Core $sb.Core
    $len = [Math]::Max($na.Count, $nb.Count)
    for ($i = 0; $i -lt $len; $i++) {
        $va = if ($i -lt $na.Count) { $na[$i] } else { 0 }
        $vb = if ($i -lt $nb.Count) { $nb[$i] } else { 0 }
        if ($va -lt $vb) { return -1 }
        if ($va -gt $vb) { return 1 }
    }
    # Release égale → la pré-version tranche.
    if ($sa.Pre -eq $sb.Pre) { return 0 }
    if ($sa.Pre -eq '') { return 1 }   # release > pré-version
    if ($sb.Pre -eq '') { return -1 }
    return [string]::Compare($sa.Pre, $sb.Pre, [System.StringComparison]::Ordinal)
}

# ── Charger la politique ──
if (-not (Test-Path -LiteralPath $PolicyPath)) { Fail-Config "politique introuvable : $PolicyPath" }
$rawPolicy = Get-Content -LiteralPath $PolicyPath -Raw -Encoding UTF8
if ([string]::IsNullOrWhiteSpace($rawPolicy)) { Fail-Config "politique vide : $PolicyPath" }
try { $policy = $rawPolicy | ConvertFrom-Json } catch { Fail-Config "politique JSON invalide : $($_.Exception.Message)" }

if (-not $policy.catalogs -or $policy.catalogs.Count -eq 0) { Fail-Config "aucun catalogue déclaré ('catalogs' vide) dans $PolicyPath" }
if (-not $policy.governed -or $policy.governed.Count -eq 0) { Fail-Config "aucun paquet gouverné ('governed' vide) dans $PolicyPath" }

# ── Lire les catalogues : map catalogRelPath -> (pkgId -> version) ──
$catalogPkgs = @{}
foreach ($rel in $policy.catalogs) {
    $full = Join-Path $RepoRoot $rel
    if (-not (Test-Path -LiteralPath $full)) { Fail-Config "catalogue déclaré introuvable : $rel (résolu : $full)" }
    $content = Get-Content -LiteralPath $full -Raw -Encoding UTF8
    $map = @{}
    foreach ($m in [regex]::Matches($content, '<PackageVersion\s+Include="(?<id>[^"]+)"\s+Version="(?<ver>[^"]+)"\s*/>')) {
        $map[$m.Groups['id'].Value] = $m.Groups['ver'].Value
    }
    $catalogPkgs[$rel] = $map
}

# ── Vérifier chaque paquet gouverné ──
$violations = @()
foreach ($g in $policy.governed) {
    if (-not $g.id)    { Fail-Config "entrée 'governed' sans 'id' dans $PolicyPath" }
    if (-not $g.floor) { Fail-Config "paquet gouverné '$($g.id)' sans 'floor' dans $PolicyPath" }

    $seen = $false
    foreach ($rel in $policy.catalogs) {
        $map = $catalogPkgs[$rel]
        if (-not $map.ContainsKey($g.id)) { continue }
        $seen = $true
        $pinned = $map[$g.id]

        try {
            $cmpFloor = Compare-PkgVersion $pinned $g.floor
        } catch {
            Fail-Config "version illisible pour '$($g.id)' dans $rel : $($_.Exception.Message)"
        }

        if ($cmpFloor -lt 0) {
            $violations += "[$rel] $($g.id) épinglé à $pinned < plancher $($g.floor) (régression de currency). $($g.note)"
            continue
        }

        if ($g.target) {
            try { $cmpTarget = Compare-PkgVersion $pinned $g.target } catch { Fail-Config "target illisible pour '$($g.id)' : $($_.Exception.Message)" }
            if ($cmpTarget -lt 0) {
                Write-Alert "[$rel] $($g.id) épinglé à $pinned < cible $($g.target) (prend du retard). $($g.note)"
            }
        }
    }

    # Gouverner un paquet absent de TOUS les catalogues = politique périmée (typo / paquet retiré)
    # → le plancher ne garderait plus rien silencieusement : faux-vert.
    if (-not $seen) {
        Fail-Config "paquet gouverné '$($g.id)' absent de TOUS les catalogues déclarés — politique périmée (retirer l'entrée ou corriger l'id)."
    }
}

# ── Advisories : retards connus/acceptés, toujours visibles ──
if ($policy.advisories) {
    foreach ($a in $policy.advisories) {
        $id = if ($a.id) { $a.id } else { '(global)' }
        Write-Alert "$id — $($a.reason) ACTION: $($a.action)"
    }
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host "[LINT-CURRENCY] ECHEC : $v" -ForegroundColor Red }
    Write-Host "[LINT-CURRENCY] ECHEC : $($violations.Count) régression(s) de currency (version sous le plancher)." -ForegroundColor Red
    exit 1
}

Write-Host "[LINT-CURRENCY] OK : tous les paquets gouvernés respectent leur plancher sur les $($policy.catalogs.Count) catalogues." -ForegroundColor Green
exit 0
