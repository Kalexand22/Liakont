# Lint anti-littéral de généricité GED (GED11, RL-27 ; règle 7 / INV-GED-12 ; F19 §7/§8).
#
# Garde anti-régression : ÉCHOUE si un littéral de vocabulaire MÉTIER apparaît, hors commentaire, dans
# le CODE du module src/Modules/Ged/** (hors projets de test). Le méta-modèle GED est GÉNÉRIQUE : les
# axes / types d'entité / rôles sont du PARAMÉTRAGE tenant (seeds fictifs deployments/<demo>/), jamais
# codés en dur. Un axe « lot »/« adjudication »/« acheteur »… dans le code = spécialisation d'un client
# dans le produit = P1 (règle 7).
#
# Généralise GedMigrationScaffoldTests.Ged_migrations_hardcode_no_business_vocabulary (qui ne couvrait
# que les migrations SQL) à TOUT le code du module (.cs + .sql) ET à la CI (ci.yml). Le vocabulaire est
# le MÊME que celui de ce test (source unique de vérité de la règle 7). Le lint blanchit d'abord les
# COMMENTAIRES (« lot » = « paquet » en français courant y est légitime) puis ne cherche le vocabulaire
# que dans le code réel (identifiants + chaînes). Bordé par lettre (Unicode) : « numero_lot » est capté,
# « Slot »/« pilot » ne le sont pas.
#
# Exit 0 = code générique (aucun littéral métier). Exit 1 = littéral métier codé en dur (régression) ou
# racine introuvable. Self-test : tools/test-ged-generic-literals-lint.ps1 (prouve qu'il discrimine).

[CmdletBinding()]
param(
    # Racine à scanner. Défaut : le module GED réel. Paramétrable pour le self-test (arbre injecté).
    [string]$Root = ''
)

$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/ged-lint-lib.ps1"

if (-not $Root) { $Root = Get-GedModuleRoot }
$Root = (Resolve-Path -LiteralPath $Root).Path

# Vocabulaire métier INTERDIT — IDENTIQUE à GedMigrationScaffoldTests.ForbiddenBusinessVocabulary
# (règle 7 / F19 §3.3.2). Toute évolution de la liste doit rester synchrone entre les deux.
$forbidden = @('lot', 'vente', 'pv', 'encheres', 'enchères', 'adjudication', 'acheteur', 'bordereau')

# Bordé par NON-lettre (Unicode) : capte « lot », « numero_lot », « LOT-42 » ; ignore « Slot », « pilot »,
# « inventer ». `_` et chiffres sont des frontières → un code d'axe snake_case métier est bien capté.
$alt = ($forbidden | ForEach-Object { [regex]::Escape($_) }) -join '|'
$pattern = "(?<!\p{L})(?i:$alt)(?!\p{L})"
$rx = [regex]::new($pattern)

$files = @(Get-GedLintFiles -Root $Root -Extensions @('.cs', '.sql'))

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
            $offenders += "  $rel : ligne $($ln + 1) → « $($hit.Value) »"
        }
    }
}

if ($offenders.Count -gt 0) {
    Write-Host "[LINT-GED-LITERAL] ECHEC : vocabulaire metier code en dur dans src/Modules/Ged/** (regle 7)." -ForegroundColor Red
    $offenders | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host "Le meta-modele GED est generique : deplacer ces axes/entites/roles vers le parametrage tenant (seeds fictifs deployments/<demo>/)." -ForegroundColor Red
    exit 1
}

Write-Host "[LINT-GED-LITERAL] OK : aucun littéral métier codé en dur dans src/Modules/Ged/** ($($files.Count) fichiers, règle 7)." -ForegroundColor Green
exit 0
