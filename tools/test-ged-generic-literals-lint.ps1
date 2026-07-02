# Self-test du lint anti-littéral de généricité GED (GED11, RL-27).
#
# Prouve que tools/lint-ged-generic-literals.ps1 N'EST PAS pass-by-default : il PASSE sur le code réel
# du module (aucun littéral métier codé) et ÉCHOUE dès qu'un littéral métier est injecté dans du CODE,
# tout en NE se déclenchant PAS sur (a) un littéral situé en COMMENTAIRE, (b) un fichier de projet de
# TEST ou de build (bin/obj), (c) un mot qui n'est qu'un SOUS-CHAINE (« Slot »/« pilot »). Sans ce
# self-test, un lint qui renvoie toujours 0 serait un faux-vert (RL-27). Logique pure (pas de dotnet).
#
# La mécanique commune (tmpRoot, écriture des cas, fichier bénin, invocation du lint, assertion d'exit)
# vit dans tools/ged-lint-lib.ps1 (GDF13) — ce self-test ne garde que ses CAS et sa vérif de synchro de
# vocabulaire (spécifique au lint anti-littéral). Un fix de la mécanique s'applique UNE fois.
#
# Exit 0 = le lint discrimine correctement. Exit 1 = le lint ne discrimine pas (bug du lint).

$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/ged-lint-lib.ps1"

$tag      = '[TEST-GED-LITERAL]'
$lint     = Join-Path $PSScriptRoot 'lint-ged-generic-literals.ps1'
$realRoot = Get-GedModuleRoot
$psExe    = (Get-Process -Id $PID).Path   # pwsh (Linux/CI) ou powershell.exe (Windows)

if (-not (Test-Path -LiteralPath $lint))     { Write-Host "$tag lint introuvable : $lint" -ForegroundColor Red; exit 1 }
if (-not (Test-Path -LiteralPath $realRoot)) { Write-Host "$tag module GED introuvable : $realRoot" -ForegroundColor Red; exit 1 }

$failures = [System.Collections.Generic.List[string]]::new()

function CheckTrue([string]$label, [bool]$cond) {
    if (-not $cond) {
        $failures.Add("$label : condition fausse")
        Write-Host "$tag FAIL  $label" -ForegroundColor Red
    } else {
        Write-Host "$tag ok    $label" -ForegroundColor Green
    }
}

# Extrait les jetons entre guillemets d'un bloc d'affectation (multi-ligne) — sert à comparer la liste
# de vocabulaire du lint (@('...')) et celle de GedMigrationScaffoldTests (["..."]).
function Get-QuotedTokens([string]$text, [string]$blockPattern, [string]$tokenPattern) {
    $bm = [regex]::Match($text, $blockPattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $bm.Success) { return @() }
    return @([regex]::Matches($bm.Groups[1].Value, $tokenPattern) | ForEach-Object { $_.Groups[1].Value })
}

$tmpRoot = New-GedLintTmpRoot 'ged-literal-lint'

try {
    # 1) Code réel du module : générique → le lint DOIT passer (exit 0). C'est la preuve « vert sur le code réel ».
    Assert-GedLintExit $failures $tag 'code reel (aucun littéral métier)' (Invoke-GedLintExe $lint $realRoot $psExe) 0

    # 2) Littéral métier dans une CHAINE C# → le lint DOIT échouer (exit 1).
    $c2 = New-GedLintCase $tmpRoot 'bad-cs-string' 'Domain/AxisCatalog.cs' 'public static class AxisCatalog { public const string Code = "adjudication"; }'
    Assert-GedLintExit $failures $tag 'littéral métier en chaîne C# (adjudication)' (Invoke-GedLintExe $lint $c2 $psExe) 1

    # 2bis) Littéral métier ACCENTUÉ dans un .cs UTF-8 SANS BOM (norme du repo) → le lint DOIT échouer
    #    (exit 1). C'EST le cas qui prouve RL-27 : sans lecture UTF-8 explicite, Get-Content -Raw sous
    #    Windows PowerShell 5.1 (interpréteur de verify-fast) décoderait « enchères » en CP1252
    #    (« enchÃ¨res »), le littéral échapperait au scan et ce Check obtiendrait 0 (faux-vert LOCAL,
    #    divergence CI pwsh). Le mot « enchères » est dans $forbidden (variante accentuée).
    $c2b = New-GedLintCase $tmpRoot 'bad-cs-accented-nobom' 'Domain/SaleCatalog.cs' 'public static class SaleCatalog { public const string Code = "enchères"; }' -NoBom
    Assert-GedLintExit $failures $tag 'littéral accentué en chaîne C# sans BOM (enchères)' (Invoke-GedLintExe $lint $c2b $psExe) 1

    # 3) Code d'axe snake_case dans une migration SQL → le lint DOIT échouer (exit 1). Prouve que la
    #    frontière traite `_` comme séparateur (numero_lot est capté), pas comme un caractère de mot.
    $c3 = New-GedLintCase $tmpRoot 'bad-sql-snake' 'Migrations/V999__seed.sql' "INSERT INTO ged_catalog.axis_definitions(code) VALUES ('numero_lot');"
    Assert-GedLintExit $failures $tag 'code d''axe métier snake_case en SQL (numero_lot)' (Invoke-GedLintExe $lint $c3 $psExe) 1

    # 4) Littéral UNIQUEMENT en commentaire → le lint DOIT passer (exit 0). Prouve le blanchiment des
    #    commentaires (« lot » = « paquet » en français courant y est légitime).
    $c4 = New-GedLintCase $tmpRoot 'only-comment' 'Application/BatchHandler.cs' "public sealed class BatchHandler {`n    // Ingère un lot de documents (rôle acheteur en exemple, prose).`n    public int Count => 0;`n}"
    Assert-GedLintExit $failures $tag 'littéral seulement en commentaire' (Invoke-GedLintExe $lint $c4 $psExe) 0

    # 5) Littéral métier dans un projet de TEST → le lint DOIT passer (exit 0). Prouve l'exclusion Tests.Unit.
    #    Un fichier générique bénin est ajouté pour que le scan ne soit pas vide (sinon la garde 0-fichier
    #    échouerait et masquerait ce que le cas veut prouver).
    $c5 = New-GedLintCase $tmpRoot 'test-project' 'Tests.Unit/GenericityTests.cs' 'public class T { const string V = "bordereau"; }'
    Add-GedLintGenericFile $c5
    Assert-GedLintExit $failures $tag 'littéral dans Tests.Unit (exclu)' (Invoke-GedLintExe $lint $c5 $psExe) 0

    # 6) Littéral métier dans un artefact de build → le lint DOIT passer (exit 0). Prouve l'exclusion bin/obj.
    $c6 = New-GedLintCase $tmpRoot 'build-artifact' 'obj/Debug/Generated.cs' 'class G { const string V = "encheres"; }'
    Add-GedLintGenericFile $c6
    Assert-GedLintExit $failures $tag 'littéral dans obj/ (exclu)' (Invoke-GedLintExe $lint $c6 $psExe) 0

    # 7) Sous-chaînes anodines (« Slot »/« pilot »/« ballot »/« inventer ») → le lint NE DOIT PAS se
    #    déclencher (exit 0). Prouve la frontière lettre (aucun faux positif sur du code générique réel).
    $c7 = New-GedLintCase $tmpRoot 'no-substring-fp' 'Infrastructure/SlotPilot.cs' 'class SlotPilot { int slot; int Ballot() => 0; string s = "inventer un pilote"; }'
    Assert-GedLintExit $failures $tag 'sous-chaînes anodines (Slot/pilot/ballot)' (Invoke-GedLintExe $lint $c7 $psExe) 0

    # 8) Scan à ZÉRO fichier (racine sans aucun .cs/.sql) → le lint DOIT échouer (exit 1). Prouve la garde
    #    anti-faux-vert « pass-by-default » : un module renommé/déplacé ne doit pas rendre un OK vide.
    $c8 = Join-Path $tmpRoot 'empty-root'
    New-Item -ItemType Directory -Path $c8 -Force | Out-Null
    Assert-GedLintExit $failures $tag 'scan vide (module introuvable/renommé) → échec' (Invoke-GedLintExe $lint $c8 $psExe) 1

    # 9) Synchronisation de la liste de vocabulaire : le lint et GedMigrationScaffoldTests DOIVENT
    #    déclarer le MÊME vocabulaire (sinon l'un des deux sous-applique la règle 7 en silence). On extrait
    #    les deux listes et on assert l'égalité stricte (source unique de vérité GARDÉE, pas juste la prose).
    $testFile = Join-Path $realRoot 'Tests.Unit/GedMigrationScaffoldTests.cs'
    if (-not (Test-Path -LiteralPath $testFile)) {
        CheckTrue "GedMigrationScaffoldTests introuvable ($testFile)" $false
    } else {
        # Lecture explicite en UTF-8 (le .cs n'a pas de BOM → Get-Content de Windows PowerShell 5.1 le
        # décoderait en CP1252 et « enchères » deviendrait « enchÃ¨res », faux écart de synchro).
        $lintVocab = Get-QuotedTokens ([System.IO.File]::ReadAllText($lint, [System.Text.Encoding]::UTF8))     '\$forbidden\s*=\s*@\((.*?)\)'                   "'([^']+)'"
        $testVocab = Get-QuotedTokens ([System.IO.File]::ReadAllText($testFile, [System.Text.Encoding]::UTF8)) 'ForbiddenBusinessVocabulary\s*=\s*\[(.*?)\]'  '"([^"]+)"'
        $a = (($lintVocab | Sort-Object) -join '|')
        $b = (($testVocab | Sort-Object) -join '|')
        $syncOk = ($lintVocab.Count -gt 0) -and ($a -eq $b)
        if (-not $syncOk) {
            Write-Host "$tag   lint = [$a]" -ForegroundColor Yellow
            Write-Host "$tag   test = [$b]" -ForegroundColor Yellow
        }
        CheckTrue 'listes de vocabulaire synchrones (lint == GedMigrationScaffoldTests)' $syncOk
    }
}
finally {
    Remove-Item -LiteralPath $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host "$tag ECHEC : $($failures.Count) cas non conforme(s)." -ForegroundColor Red
    exit 1
}
Write-Host "$tag OK : le lint discrimine (code réel/commentaire/test/sous-chaîne => 0 ; littéral codé => 1)." -ForegroundColor Green
exit 0
