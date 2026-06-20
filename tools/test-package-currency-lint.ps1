# Self-test du lint de currency NuGet (RDF07).
#
# Prouve que tools/lint-package-currency.ps1 N'EST PAS pass-by-default : il PASSE quand toutes les
# versions épinglées sont >= leur plancher, et ÉCHOUE (exit 1) dès qu'une version passe SOUS son
# plancher. Couvre aussi les erreurs de configuration (exit 2 : politique absente/invalide, catalogue
# manquant, paquet gouverné absent, version illisible) et le canal d'alerte (target/advisory : exit 0
# avec warning). Et un dernier cas branché sur la POLITIQUE + les CATALOGUES RÉELS du dépôt
# (l'état courant doit passer — sinon RDF07 introduirait une régression de build).
#
# Logique pure (pas de dotnet, pas de réseau) → garde permanente en CI et en verify-fast. Sans ce
# self-test, un lint qui renverrait toujours 0 serait un faux-vert.
#
# Exit 0 = lint conforme. Exit 1 = le lint ne discrimine pas correctement (bug du lint).

$ErrorActionPreference = 'Stop'

$lint     = Join-Path $PSScriptRoot 'lint-package-currency.ps1'
$realPol  = Join-Path $PSScriptRoot 'package-currency-policy.json'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$psExe    = (Get-Process -Id $PID).Path   # pwsh (Linux/CI) ou powershell.exe (Windows)

if (-not (Test-Path -LiteralPath $lint))    { Write-Host "[TEST-CURRENCY] lint introuvable : $lint" -ForegroundColor Red; exit 1 }
if (-not (Test-Path -LiteralPath $realPol)) { Write-Host "[TEST-CURRENCY] politique réelle introuvable : $realPol" -ForegroundColor Red; exit 1 }

# Invoque le lint dans un process enfant (le `exit` du lint ne doit pas tuer ce self-test).
function Invoke-Lint([string]$policyPath, [string]$root) {
    & $psExe -NoProfile -ExecutionPolicy Bypass -File $lint -PolicyPath $policyPath -RepoRoot $root *> $null
    return $LASTEXITCODE
}

$tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) ("currency-lint-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null

$failures = @()
function Check([string]$label, [int]$got, [int]$want) {
    if ($got -ne $want) {
        $script:failures += "$label : exit $got attendu $want"
        Write-Host "[TEST-CURRENCY] FAIL  $label (exit $got, attendu $want)" -ForegroundColor Red
    } else {
        Write-Host "[TEST-CURRENCY] ok    $label (exit $got)" -ForegroundColor Green
    }
}

# Écrit un catalogue Directory.Packages.props synthétique (id -> version) dans $dir/$rel.
function Write-Catalog([string]$dir, [string]$rel, [hashtable]$pkgs) {
    $full = Join-Path $dir $rel
    New-Item -ItemType Directory -Path (Split-Path -Parent $full) -Force | Out-Null
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine('<Project>')
    [void]$sb.AppendLine('  <ItemGroup>')
    foreach ($k in $pkgs.Keys) {
        [void]$sb.AppendLine("    <PackageVersion Include=`"$k`" Version=`"$($pkgs[$k])`" />")
    }
    [void]$sb.AppendLine('  </ItemGroup>')
    [void]$sb.AppendLine('</Project>')
    Set-Content -LiteralPath $full -Value $sb.ToString() -Encoding utf8
}

try {
    # Racine de catalogues synthétique partagée.
    $synRoot = Join-Path $tmpDir 'syn'
    Write-Catalog $synRoot 'Directory.Packages.props'       @{ 'Npgsql' = '9.0.3'; 'MailKit' = '4.17.0' }
    Write-Catalog $synRoot 'agent/Directory.Packages.props' @{ 'System.Data.SQLite.Core' = '1.0.119'; 'Newtonsoft.Json' = '13.0.3' }

    $catalogs = @('Directory.Packages.props', 'agent/Directory.Packages.props')

    function Write-Policy([string]$name, $obj) {
        $p = Join-Path $tmpDir $name
        Set-Content -LiteralPath $p -Value ($obj | ConvertTo-Json -Depth 6) -Encoding utf8
        return $p
    }

    # 1) État sain (toutes les versions == plancher) → PASS (exit 0).
    $polOk = Write-Policy 'ok.json' @{
        catalogs = $catalogs
        governed = @(
            @{ id = 'Npgsql'; floor = '9.0.3' },
            @{ id = 'System.Data.SQLite.Core'; floor = '1.0.119' }
        )
    }
    Check 'etat sain (== plancher)' (Invoke-Lint $polOk $synRoot) 0

    # 2) Version SOUS le plancher → ECHEC (exit 1) — la direction discriminante.
    $polBelow = Write-Policy 'below.json' @{
        catalogs = $catalogs
        governed = @( @{ id = 'Npgsql'; floor = '9.0.4' } )   # épinglé 9.0.3 < 9.0.4
    }
    Check 'version sous le plancher' (Invoke-Lint $polBelow $synRoot) 1

    # 3) Version AU-DESSUS du plancher → PASS (exit 0).
    $polAbove = Write-Policy 'above.json' @{
        catalogs = $catalogs
        governed = @( @{ id = 'MailKit'; floor = '4.0.0' } )  # épinglé 4.17.0 > 4.0.0
    }
    Check 'version au-dessus du plancher' (Invoke-Lint $polAbove $synRoot) 0

    # 4) target non atteint (floor <= pin < target) → ALERTE mais PASS (exit 0).
    $polTarget = Write-Policy 'target.json' @{
        catalogs = $catalogs
        governed = @( @{ id = 'System.Data.SQLite.Core'; floor = '1.0.119'; target = '1.0.200' } )
    }
    Check 'cible non atteinte (alerte, pas echec)' (Invoke-Lint $polTarget $synRoot) 0

    # 5) Plancher en pré-version : release > pré-version → PASS (1.0.119 > 1.0.119-beta).
    $polPre = Write-Policy 'pre.json' @{
        catalogs = $catalogs
        governed = @( @{ id = 'System.Data.SQLite.Core'; floor = '1.0.119-beta' } )
    }
    Check 'plancher pre-version (release > pre)' (Invoke-Lint $polPre $synRoot) 0

    # 6) Politique introuvable → ECHEC CONFIG (exit 2), jamais un PASS par defaut.
    Check 'politique introuvable' (Invoke-Lint (Join-Path $tmpDir 'nope.json') $synRoot) 2

    # 7) Catalogue declare introuvable → ECHEC CONFIG (exit 2).
    $polBadCat = Write-Policy 'badcat.json' @{
        catalogs = @('Directory.Packages.props', 'missing/Directory.Packages.props')
        governed = @( @{ id = 'Npgsql'; floor = '9.0.3' } )
    }
    Check 'catalogue manquant' (Invoke-Lint $polBadCat $synRoot) 2

    # 8) Paquet gouverne absent de TOUS les catalogues → ECHEC CONFIG (exit 2) : politique perimee.
    $polGhost = Write-Policy 'ghost.json' @{
        catalogs = $catalogs
        governed = @( @{ id = 'Package.Qui.N.Existe.Pas'; floor = '1.0.0' } )
    }
    Check 'paquet gouverne absent (politique perimee)' (Invoke-Lint $polGhost $synRoot) 2

    # 9) Version illisible dans un catalogue → ECHEC CONFIG (exit 2).
    Write-Catalog $synRoot 'agent/Directory.Packages.props' @{ 'System.Data.SQLite.Core' = '1.0.119'; 'Newtonsoft.Json' = 'abc' }
    $polBadVer = Write-Policy 'badver.json' @{
        catalogs = $catalogs
        governed = @( @{ id = 'Newtonsoft.Json'; floor = '13.0.3' } )
    }
    Check 'version illisible' (Invoke-Lint $polBadVer $synRoot) 2
    # Restaurer le catalogue agent sain pour l'isolation des cas suivants (aucun pour l'instant).
    Write-Catalog $synRoot 'agent/Directory.Packages.props' @{ 'System.Data.SQLite.Core' = '1.0.119'; 'Newtonsoft.Json' = '13.0.3' }

    # 10) Balise NON auto-fermante : le lint doit parser les deux formes de balise MSBuild.
    #     Cas A (>= plancher, forme non auto-fermante) → PASS (exit 0).
    #     Cas B (sous plancher, forme non auto-fermante) → ECHEC (exit 1) : prouve que la forme est lue.
    $nonScRoot = Join-Path $tmpDir 'nonsc'
    $nonScCat  = Join-Path $nonScRoot 'Directory.Packages.props'
    New-Item -ItemType Directory -Path $nonScRoot -Force | Out-Null
    $nonScContent = @"
<Project>
  <ItemGroup>
    <PackageVersion Include="Npgsql" Version="9.0.3"></PackageVersion>
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath $nonScCat -Value $nonScContent -Encoding utf8

    $polNonScOk = Write-Policy 'nonsc-ok.json' @{
        catalogs = @('Directory.Packages.props')
        governed = @( @{ id = 'Npgsql'; floor = '9.0.3' } )
    }
    Check 'balise non auto-fermante (>= plancher)' (Invoke-Lint $polNonScOk $nonScRoot) 0

    # Remplacer la version sous le plancher dans le catalogue non auto-fermant.
    $nonScContentBelow = @"
<Project>
  <ItemGroup>
    <PackageVersion Include="Npgsql" Version="9.0.2"></PackageVersion>
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath $nonScCat -Value $nonScContentBelow -Encoding utf8

    $polNonScBelow = Write-Policy 'nonsc-below.json' @{
        catalogs = @('Directory.Packages.props')
        governed = @( @{ id = 'Npgsql'; floor = '9.0.3' } )
    }
    Check 'balise non auto-fermante (sous plancher)' (Invoke-Lint $polNonScBelow $nonScRoot) 1

    # 11) POLITIQUE + CATALOGUES REELS du depot : l'etat courant DOIT passer (exit 0). Sinon RDF07
    #     introduirait une regression (l'avenant differe le bump SQLite : advisory, pas echec).
    Check 'politique + catalogues reels (etat courant)' (Invoke-Lint $realPol $repoRoot) 0
}
finally {
    Remove-Item -LiteralPath $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host "[TEST-CURRENCY] ECHEC : $($failures.Count) cas non conforme(s)." -ForegroundColor Red
    exit 1
}
Write-Host "[TEST-CURRENCY] OK : le lint discrimine correctement (sain/au-dessus => 0, sous plancher => 1, config cassee => 2, alerte => 0)." -ForegroundColor Green
exit 0
