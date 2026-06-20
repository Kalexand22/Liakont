# Lint CI — plafond mémoire Keycloak de l'appliance (RDF04, RL-IDP-5 / ADR-0020 §2).
#
# Garde anti-régression : la CI ÉCHOUE si le service `keycloak` du compose de l'appliance perd son
# plafond mémoire (`mem_limit` ou `deploy.resources.limits.memory`) OU le `-XX:MaxRAMPercentage` qui
# borne le tas JVM à ce plafond. Sans plafond la JVM Quarkus happe ≈ 1,83 GiB sur un gros hôte
# (mesure ADR-0020) → OOM/thrash sur une petite VPS self-hosted, non corrigeable à chaud.
#
# Le lint est volontairement SANS parseur YAML tiers (aucune dépendance) : il isole le bloc du
# service `keycloak` par son indentation, puis vérifie la présence des deux gardes dans ce bloc.
# Exit 0 = plafond présent. Exit 1 = plafond manquant (régression) ou compose introuvable/illisible.

[CmdletBinding()]
param(
    # Chemin du compose à vérifier. Défaut : le compose réel de l'appliance, résolu relativement à
    # ce script (fonctionne quel que soit le répertoire courant ; paramétrable pour le self-test).
    [string]$ComposePath = (Join-Path $PSScriptRoot '..\deploy\docker\appliance\docker-compose.yml')
)

$ErrorActionPreference = 'Stop'

function Fail([string]$msg) {
    Write-Host "[LINT-KC-MEMCAP] ECHEC : $msg" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path -LiteralPath $ComposePath)) {
    Fail "compose appliance introuvable : $ComposePath"
}

$lines = Get-Content -LiteralPath $ComposePath
if (-not $lines) { Fail "compose appliance vide : $ComposePath" }

# ── Isoler le bloc du service `keycloak` (sous `services:`, indentation 2 espaces) ──
# Début : la ligne `  keycloak:`. Fin : la prochaine clé de service (2 espaces) ou clé de 1er
# niveau (0 espace) — networks:/volumes:/etc. On ne confond pas avec `keycloak-db:`.
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s{2}keycloak:\s*$') { $start = $i; break }
}
if ($start -lt 0) { Fail "service `keycloak` absent du compose $ComposePath (structure inattendue)" }

$end = $lines.Count
for ($i = $start + 1; $i -lt $lines.Count; $i++) {
    $l = $lines[$i]
    if ($l -match '^\s*$' -or $l -match '^\s*#') { continue }   # ignorer lignes vides / commentaires
    if ($l -match '^\S' -or $l -match '^\s{2}\S') { $end = $i; break }  # nouvelle clé service/top-level
}
$block = $lines[$start..($end - 1)]

# Ne garder que les lignes de CODE : une garde mentionnée en commentaire (`# … MaxRAMPercentage …`)
# ou une clé commentée (`# mem_limit: 1g`) ne compte pas comme présente — sinon le lint passerait
# alors que la garde réelle a disparu (faux-vert).
$code = $block | Where-Object { $_.TrimStart() -notmatch '^#' }

# ── Garde 1 : plafond mémoire (mem_limit OU deploy.resources.limits.memory) avec une valeur non vide ──
$hasMemLimit = $false
foreach ($l in $code) {
    if ($l -match '^\s*mem_limit:\s*\S+') { $hasMemLimit = $true; break }
    if ($l -match '^\s*memory:\s*\S+')    { $hasMemLimit = $true; break }   # sous deploy.resources.limits
}
if (-not $hasMemLimit) {
    Fail ("le service ``keycloak`` n'a aucun plafond mémoire (``mem_limit:`` ou ``deploy.resources.limits.memory:``). " +
          "Restaurer le plafond ~1 GiB (ADR-0020 §2) dans $ComposePath — sinon la JVM Keycloak happe ~1,83 GiB.")
}

# ── Garde 2 : -XX:MaxRAMPercentage (borne le tas au plafond cgroup) ──
# On exige le flag JVM réel (`-XX:MaxRAMPercentage`), pas la simple mention du mot, et uniquement
# sur une ligne de code (les commentaires ont été écartés ci-dessus).
$hasMaxRam = $false
foreach ($l in $code) {
    if ($l -match '-XX:MaxRAMPercentage') { $hasMaxRam = $true; break }
}
if (-not $hasMaxRam) {
    Fail ("le service ``keycloak`` n'a pas de ``-XX:MaxRAMPercentage`` (JAVA_OPTS_APPEND). " +
          "Le restaurer (ADR-0020 §2) dans $ComposePath — sinon le tas JVM ignore le plafond cgroup.")
}

Write-Host "[LINT-KC-MEMCAP] OK : plafond mémoire Keycloak de l'appliance présent (mem_limit + MaxRAMPercentage, ADR-0020 §2)." -ForegroundColor Green
exit 0
