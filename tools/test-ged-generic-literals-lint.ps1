# Self-test du lint anti-littéral de généricité GED (GED11, RL-27).
#
# Prouve que tools/lint-ged-generic-literals.ps1 N'EST PAS pass-by-default : il PASSE sur le code réel
# du module (aucun littéral métier codé) et ÉCHOUE dès qu'un littéral métier est injecté dans du CODE,
# tout en NE se déclenchant PAS sur (a) un littéral situé en COMMENTAIRE, (b) un fichier de projet de
# TEST ou de build (bin/obj), (c) un mot qui n'est qu'un SOUS-CHAINE (« Slot »/« pilot »). Sans ce
# self-test, un lint qui renvoie toujours 0 serait un faux-vert (RL-27). Logique pure (pas de dotnet).
#
# Exit 0 = le lint discrimine correctement. Exit 1 = le lint ne discrimine pas (bug du lint).

$ErrorActionPreference = 'Stop'

$lint     = Join-Path $PSScriptRoot 'lint-ged-generic-literals.ps1'
$realRoot = (Resolve-Path (Join-Path $PSScriptRoot '../src/Modules/Ged')).Path
$psExe    = (Get-Process -Id $PID).Path   # pwsh (Linux/CI) ou powershell.exe (Windows)

if (-not (Test-Path -LiteralPath $lint))     { Write-Host "[TEST-GED-LITERAL] lint introuvable : $lint" -ForegroundColor Red; exit 1 }
if (-not (Test-Path -LiteralPath $realRoot)) { Write-Host "[TEST-GED-LITERAL] module GED introuvable : $realRoot" -ForegroundColor Red; exit 1 }

function Invoke-Lint([string]$root) {
    & $psExe -NoProfile -ExecutionPolicy Bypass -File $lint -Root $root *> $null
    return $LASTEXITCODE
}

$failures = @()
function Check([string]$label, [int]$got, [int]$want) {
    if ($got -ne $want) {
        $script:failures += "$label : exit $got attendu $want"
        Write-Host "[TEST-GED-LITERAL] FAIL  $label (exit $got, attendu $want)" -ForegroundColor Red
    } else {
        Write-Host "[TEST-GED-LITERAL] ok    $label (exit $got)" -ForegroundColor Green
    }
}

$tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ged-literal-lint-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmpRoot -Force | Out-Null

function New-Case([string]$name, [string]$relFile, [string]$content) {
    $dir = Join-Path $tmpRoot $name
    $path = Join-Path $dir $relFile
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    Set-Content -LiteralPath $path -Value $content -Encoding utf8
    return $dir
}

# Écrit un cas en UTF-8 SANS BOM (Set-Content -Encoding utf8 écrit un BOM sous PS 5.1 → aveugle au trou
# CP1252 que RL-27 ferme). Reproduit la norme réelle du repo (.cs sans BOM) : le seul écrivain qui prouve
# que la lecture UTF-8 explicite du lint capte bien un littéral accentué décodé correctement.
function New-CaseNoBom([string]$name, [string]$relFile, [string]$content) {
    $dir = Join-Path $tmpRoot $name
    $path = Join-Path $dir $relFile
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [System.IO.File]::WriteAllText($path, $content, (New-Object System.Text.UTF8Encoding($false)))
    return $dir
}

# Ajoute un fichier de code GÉNÉRIQUE bénin dans le répertoire du cas : garantit que le scan n'est PAS
# vide, de sorte qu'un cas d'exclusion prouve bien « fichier exclu ignoré » et non « scan vide » (qui
# échoue désormais). Sans ce fichier, la garde 0-fichier masquerait ce que le cas veut prouver.
function Add-GenericFile([string]$dir) {
    $p = Join-Path $dir 'Domain/_GenericOk.cs'
    New-Item -ItemType Directory -Path (Split-Path -Parent $p) -Force | Out-Null
    Set-Content -LiteralPath $p -Value 'public sealed class GenericOk { public int Value => 0; }' -Encoding utf8
}

function CheckTrue([string]$label, [bool]$cond) {
    if (-not $cond) {
        $script:failures += "$label : condition fausse"
        Write-Host "[TEST-GED-LITERAL] FAIL  $label" -ForegroundColor Red
    } else {
        Write-Host "[TEST-GED-LITERAL] ok    $label" -ForegroundColor Green
    }
}

# Extrait les jetons entre guillemets d'un bloc d'affectation (multi-ligne) — sert à comparer la liste
# de vocabulaire du lint (@('...')) et celle de GedMigrationScaffoldTests (["..."]).
function Get-QuotedTokens([string]$text, [string]$blockPattern, [string]$tokenPattern) {
    $bm = [regex]::Match($text, $blockPattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $bm.Success) { return @() }
    return @([regex]::Matches($bm.Groups[1].Value, $tokenPattern) | ForEach-Object { $_.Groups[1].Value })
}

try {
    # 1) Code réel du module : générique → le lint DOIT passer (exit 0). C'est la preuve « vert sur le code réel ».
    Check 'code reel (aucun littéral métier)' (Invoke-Lint $realRoot) 0

    # 2) Littéral métier dans une CHAINE C# → le lint DOIT échouer (exit 1).
    $c2 = New-Case 'bad-cs-string' 'Domain/AxisCatalog.cs' 'public static class AxisCatalog { public const string Code = "adjudication"; }'
    Check 'littéral métier en chaîne C# (adjudication)' (Invoke-Lint $c2) 1

    # 2bis) Littéral métier ACCENTUÉ dans un .cs UTF-8 SANS BOM (norme du repo) → le lint DOIT échouer
    #    (exit 1). C'EST le cas qui prouve RL-27 : sans lecture UTF-8 explicite, Get-Content -Raw sous
    #    Windows PowerShell 5.1 (interpréteur de verify-fast) décoderait « enchères » en CP1252
    #    (« enchÃ¨res »), le littéral échapperait au scan et ce Check obtiendrait 0 (faux-vert LOCAL,
    #    divergence CI pwsh). Le mot « enchères » est dans $forbidden (variante accentuée).
    $c2b = New-CaseNoBom 'bad-cs-accented-nobom' 'Domain/SaleCatalog.cs' 'public static class SaleCatalog { public const string Code = "enchères"; }'
    Check 'littéral accentué en chaîne C# sans BOM (enchères)' (Invoke-Lint $c2b) 1

    # 3) Code d'axe snake_case dans une migration SQL → le lint DOIT échouer (exit 1). Prouve que la
    #    frontière traite `_` comme séparateur (numero_lot est capté), pas comme un caractère de mot.
    $c3 = New-Case 'bad-sql-snake' 'Migrations/V999__seed.sql' "INSERT INTO ged_catalog.axis_definitions(code) VALUES ('numero_lot');"
    Check 'code d''axe métier snake_case en SQL (numero_lot)' (Invoke-Lint $c3) 1

    # 4) Littéral UNIQUEMENT en commentaire → le lint DOIT passer (exit 0). Prouve le blanchiment des
    #    commentaires (« lot » = « paquet » en français courant y est légitime).
    $c4 = New-Case 'only-comment' 'Application/BatchHandler.cs' "public sealed class BatchHandler {`n    // Ingère un lot de documents (rôle acheteur en exemple, prose).`n    public int Count => 0;`n}"
    Check 'littéral seulement en commentaire' (Invoke-Lint $c4) 0

    # 5) Littéral métier dans un projet de TEST → le lint DOIT passer (exit 0). Prouve l'exclusion Tests.Unit.
    #    Un fichier générique bénin est ajouté pour que le scan ne soit pas vide (sinon la garde 0-fichier
    #    échouerait et masquerait ce que le cas veut prouver).
    $c5 = New-Case 'test-project' 'Tests.Unit/GenericityTests.cs' 'public class T { const string V = "bordereau"; }'
    Add-GenericFile $c5
    Check 'littéral dans Tests.Unit (exclu)' (Invoke-Lint $c5) 0

    # 6) Littéral métier dans un artefact de build → le lint DOIT passer (exit 0). Prouve l'exclusion bin/obj.
    $c6 = New-Case 'build-artifact' 'obj/Debug/Generated.cs' 'class G { const string V = "encheres"; }'
    Add-GenericFile $c6
    Check 'littéral dans obj/ (exclu)' (Invoke-Lint $c6) 0

    # 7) Sous-chaînes anodines (« Slot »/« pilot »/« ballot »/« inventer ») → le lint NE DOIT PAS se
    #    déclencher (exit 0). Prouve la frontière lettre (aucun faux positif sur du code générique réel).
    $c7 = New-Case 'no-substring-fp' 'Infrastructure/SlotPilot.cs' 'class SlotPilot { int slot; int Ballot() => 0; string s = "inventer un pilote"; }'
    Check 'sous-chaînes anodines (Slot/pilot/ballot)' (Invoke-Lint $c7) 0

    # 8) Scan à ZÉRO fichier (racine sans aucun .cs/.sql) → le lint DOIT échouer (exit 1). Prouve la garde
    #    anti-faux-vert « pass-by-default » : un module renommé/déplacé ne doit pas rendre un OK vide.
    $c8 = Join-Path $tmpRoot 'empty-root'
    New-Item -ItemType Directory -Path $c8 -Force | Out-Null
    Check 'scan vide (module introuvable/renommé) → échec' (Invoke-Lint $c8) 1

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
            Write-Host "[TEST-GED-LITERAL]   lint = [$a]" -ForegroundColor Yellow
            Write-Host "[TEST-GED-LITERAL]   test = [$b]" -ForegroundColor Yellow
        }
        CheckTrue 'listes de vocabulaire synchrones (lint == GedMigrationScaffoldTests)' $syncOk
    }
}
finally {
    Remove-Item -LiteralPath $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host "[TEST-GED-LITERAL] ECHEC : $($failures.Count) cas non conforme(s)." -ForegroundColor Red
    exit 1
}
Write-Host "[TEST-GED-LITERAL] OK : le lint discrimine (code réel/commentaire/test/sous-chaîne => 0 ; littéral codé => 1)." -ForegroundColor Green
exit 0
