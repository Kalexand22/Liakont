# Lint anti-jointure cross-schéma GED (GED11, RL-27 ; règle 9 ; F19 §3.4.1/§8).
#
# Garde anti-régression : ÉCHOUE si une migration ou une requête du module src/Modules/Ged/** référence,
# hors commentaire, un schéma d'un AUTRE module (documents. / mandats. / tvamapping.). Le lien entre la
# GED et la chaîne fiscale est un SOFT-LINK LOGIQUE (un uuid nu, sans FK ni jointure), pas une jointure
# SQL cross-schéma : celle-ci codrait en dur une dépendance invisible à NetArchTest (qui ne voit que les
# références .NET, pas le SQL). Une jointure ged_* → documents./mandats./tvamapping. = P1 (règle 9).
#
# Périmètre : .sql (migrations embarquées) + .cs (requêtes SQL en chaîne), hors projets de test. Le lint
# blanchit d'abord les COMMENTAIRES (les soft-links y sont DOCUMENTÉS — « → documents.documents.id, sans
# FK » — c'est légitime) puis ne cherche les références de schéma que dans le code réel. Le caractère qui
# suit le point doit être minuscule/underscore (table PostgreSQL snake_case) : ainsi une référence SQL
# `documents.archive_entries` est captée, mais un accès membre C# `request.Documents.Count` (PascalCase)
# ne l'est pas (zéro faux positif sur le code .NET).
#
# Exit 0 = aucune référence cross-schéma. Exit 1 = jointure/référence cross-schéma (régression) ou racine
# introuvable. Self-test : tools/test-ged-cross-schema-lint.ps1 (prouve qu'il discrimine).

[CmdletBinding()]
param(
    # Racine à scanner. Défaut : le module GED réel. Paramétrable pour le self-test (arbre injecté).
    [string]$Root = ''
)

$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/ged-lint-lib.ps1"

if (-not $Root) { $Root = Get-GedModuleRoot }
$Root = (Resolve-Path -LiteralPath $Root).Path

# Schémas d'autres modules INTERDITS dans le SQL GED (règle 9, F19 §3.4.1). Liste ancrée sur la spec :
# la GED ne parle qu'à ses propres schémas ged_catalog / ged_index / ged_ingestion.
$forbiddenSchemas = @('documents', 'mandats', 'tvamapping')

# Schéma insensible à la casse, mais le caractère suivant le point doit être minuscule/underscore
# (table snake_case) → distingue une référence SQL d'un accès membre C# PascalCase (.Documents.Count).
# Lookbehind : pas précédé d'une lettre/chiffre/_/point (évite un identifiant plus long ou une chaîne
# d'accès membre `a.documents.b`).
$alt = ($forbiddenSchemas | ForEach-Object { [regex]::Escape($_) }) -join '|'
$pattern = "(?<![\p{L}\p{N}_.])(?i:$alt)\.[a-z_]"
$rx = [regex]::new($pattern)

$files = @(Get-GedLintFiles -Root $Root -Extensions @('.cs', '.sql'))

# Anti-faux-vert : un scan à ZÉRO fichier désactiverait la garde en silence (module renommé/déplacé, ou
# code déplacé sous un segment exclu bin/obj/Tests.*). C'est le mode d'échec « pass-by-default » que
# GED11/RL-27 combat → on ÉCHOUE au lieu de rendre un OK vide. (En marche normale : ~130 fichiers.)
if ($files.Count -eq 0) {
    Write-Host "[LINT-GED-XSCHEMA] ECHEC : 0 fichier de code scanné sous « $Root » — module GED introuvable/renommé/déplacé, ou déplacé sous un segment exclu ? La garde se désactiverait en silence (faux-vert)." -ForegroundColor Red
    exit 1
}

$offenders = @()
foreach ($f in $files) {
    $raw = Get-Content -LiteralPath $f.FullName -Raw
    if (-not $raw) { continue }
    $lang = if ($f.Extension -ieq '.sql') { 'sql' } else { 'cs' }
    $code = Convert-CommentsToBlanks -Text $raw -Language $lang
    $lines = $code -split "`n"
    for ($ln = 0; $ln -lt $lines.Count; $ln++) {
        $m = $rx.Matches($lines[$ln])
        foreach ($hit in $m) {
            $rel = $f.FullName.Substring($Root.Length).TrimStart('\', '/')
            $offenders += "  $rel : ligne $($ln + 1) → « $($hit.Value.TrimEnd()) … »"
        }
    }
}

if ($offenders.Count -gt 0) {
    Write-Host "[LINT-GED-XSCHEMA] ECHEC : reference SQL cross-schema depuis src/Modules/Ged/** (regle 9)." -ForegroundColor Red
    $offenders | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host "Le lien GED<->fiscal est un soft-link LOGIQUE (uuid nu, sans FK ni jointure) : retirer la jointure cross-schema (F19 §3.4.1)." -ForegroundColor Red
    exit 1
}

Write-Host "[LINT-GED-XSCHEMA] OK : aucune référence SQL cross-schéma dans src/Modules/Ged/** ($($files.Count) fichiers, règle 9)." -ForegroundColor Green
exit 0
