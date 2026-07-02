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

try {
    # 1) Code réel du module : générique → le lint DOIT passer (exit 0). C'est la preuve « vert sur le code réel ».
    Check 'code reel (aucun littéral métier)' (Invoke-Lint $realRoot) 0

    # 2) Littéral métier dans une CHAINE C# → le lint DOIT échouer (exit 1).
    $c2 = New-Case 'bad-cs-string' 'Domain/AxisCatalog.cs' 'public static class AxisCatalog { public const string Code = "adjudication"; }'
    Check 'littéral métier en chaîne C# (adjudication)' (Invoke-Lint $c2) 1

    # 3) Code d'axe snake_case dans une migration SQL → le lint DOIT échouer (exit 1). Prouve que la
    #    frontière traite `_` comme séparateur (numero_lot est capté), pas comme un caractère de mot.
    $c3 = New-Case 'bad-sql-snake' 'Migrations/V999__seed.sql' "INSERT INTO ged_catalog.axis_definitions(code) VALUES ('numero_lot');"
    Check 'code d''axe métier snake_case en SQL (numero_lot)' (Invoke-Lint $c3) 1

    # 4) Littéral UNIQUEMENT en commentaire → le lint DOIT passer (exit 0). Prouve le blanchiment des
    #    commentaires (« lot » = « paquet » en français courant y est légitime).
    $c4 = New-Case 'only-comment' 'Application/BatchHandler.cs' "public sealed class BatchHandler {`n    // Ingère un lot de documents (rôle acheteur en exemple, prose).`n    public int Count => 0;`n}"
    Check 'littéral seulement en commentaire' (Invoke-Lint $c4) 0

    # 5) Littéral métier dans un projet de TEST → le lint DOIT passer (exit 0). Prouve l'exclusion Tests.Unit.
    $c5 = New-Case 'test-project' 'Tests.Unit/GenericityTests.cs' 'public class T { const string V = "bordereau"; }'
    Check 'littéral dans Tests.Unit (exclu)' (Invoke-Lint $c5) 0

    # 6) Littéral métier dans un artefact de build → le lint DOIT passer (exit 0). Prouve l'exclusion bin/obj.
    $c6 = New-Case 'build-artifact' 'obj/Debug/Generated.cs' 'class G { const string V = "encheres"; }'
    Check 'littéral dans obj/ (exclu)' (Invoke-Lint $c6) 0

    # 7) Sous-chaînes anodines (« Slot »/« pilot »/« ballot »/« inventer ») → le lint NE DOIT PAS se
    #    déclencher (exit 0). Prouve la frontière lettre (aucun faux positif sur du code générique réel).
    $c7 = New-Case 'no-substring-fp' 'Infrastructure/SlotPilot.cs' 'class SlotPilot { int slot; int Ballot() => 0; string s = "inventer un pilote"; }'
    Check 'sous-chaînes anodines (Slot/pilot/ballot)' (Invoke-Lint $c7) 0
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
