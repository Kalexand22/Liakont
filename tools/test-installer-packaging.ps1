#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Self-test du packaging multi-profils de l'installeur agent (OPS08c).
.DESCRIPTION
    Exerce tools/package-installer.ps1 DE BOUT EN BOUT sur les binaires de l'installeur déjà construits
    (réutilise OPS05 -SkipBuild) : vérifie que

      1. un profil intégrateur d'exemple VALIDE produit un paquet auto-suffisant (bin\Installer.exe avec
         profil EMBARQUÉ + bin\Liakont.Agent.exe = charge OPS05), et que le profil embarqué est RELU par
         l'exécutable produit (--show-profile) — preuve que l'embarquement en ressource fonctionne ;
      2. un profil INVALIDE FAIT ÉCHOUER le packaging et ne produit AUCUN installateur (anti-faux-vert,
         F13 §5.3 — jamais de masquage silencieux d'un profil cassé).

    PowerShell pur (aucune dépendance Pester) : câblé dans tools/run-tests.ps1 et .github/workflows/ci.yml
    → une régression du packaging fait échouer une gate permanente. La configuration construite (Debug en
    local via run-tests, Release en CI) est DÉTECTÉE — le test échoue (jamais ne « passe ») si l'installeur
    n'a pas été construit (anti-faux-vert).

    Exit 0 = tout vert, 1 = au moins un cas en échec.
#>
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$packageInstaller = Join-Path $PSScriptRoot 'package-installer.ps1'
$installerProjDir = Join-Path $repoRoot 'agent\src\Liakont.Agent.Installer'
$psExe = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell' }

$script:passed = 0
$script:failed = 0

function Test-Case {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        $script:passed++
        Write-Host "  [OK]   $Name" -ForegroundColor Green
    }
    catch {
        $script:failed++
        Write-Host "  [FAIL] $Name : $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Assert-True { param([bool]$Cond, [string]$Msg) if (-not $Cond) { throw $Msg } }

function Invoke-PackageInstaller {
    param([string[]]$ScriptArgs)
    $allArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $packageInstaller) + $ScriptArgs
    $output = & $psExe @allArgs 2>&1 | Out-String
    return [PSCustomObject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

Write-Host "=== Self-test packaging installeur multi-profils (OPS08c) ===" -ForegroundColor Cyan

# ── Détection de la configuration construite (anti-faux-vert : échec si l'installeur n'existe pas) ──
$plat = 'x86'
$config = $null
foreach ($c in @('Debug', 'Release')) {
    $candidate = Join-Path $installerProjDir "bin\$plat\$c\net48\Liakont.Agent.Installer.exe"
    if (Test-Path -LiteralPath $candidate) { $config = $c; break }
}

Test-Case "Installeur construit ($plat) présent" {
    Assert-True ($null -ne $config) "Liakont.Agent.Installer.exe ($plat) introuvable (Debug ou Release) — construire la solution agent d'abord."
}

if ($null -eq $config) {
    Write-Host "=== Self-test : $($script:passed) OK, $($script:failed) FAIL ===" -ForegroundColor Cyan
    exit 1
}

$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) "liakont-ops08c-$PID"
if (Test-Path -LiteralPath $workRoot) { Remove-Item -LiteralPath $workRoot -Recurse -Force }
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null

try {
    $exampleMonoSite = Join-Path $repoRoot 'config\exemples\profil-integrateur-exemple.json'
    $exampleHebergeur = Join-Path $repoRoot 'config\exemples\profil-integrateur-hebergeur-exemple.json'

    # Répertoire de profils dédié (config/exemples contient aussi des JSON qui ne sont PAS des profils
    # intégrateur) : on n'y dépose que les deux profils d'exemple, puis on exerce -ProfilesDirectory.
    $profilesDir = Join-Path $workRoot 'profils'
    New-Item -ItemType Directory -Path $profilesDir -Force | Out-Null
    Copy-Item -LiteralPath $exampleMonoSite -Destination $profilesDir -Force
    Copy-Item -LiteralPath $exampleHebergeur -Destination $profilesDir -Force

    # ── Cas nominal : 2 profils valides → 2 installateurs, profils relus ──
    $validOut = Join-Path $workRoot 'valid'
    $validRun = Invoke-PackageInstaller -ScriptArgs @(
        '-ProfilesDirectory', $profilesDir,
        '-Platform', $plat, '-Configuration', $config, '-SkipBuild', '-NoZip',
        '-OutputDirectory', $validOut)

    Test-Case 'Profils valides : packaging réussit (exit 0)' {
        Assert-True ($validRun.ExitCode -eq 0) "exit $($validRun.ExitCode). Sortie :`n$($validRun.Output)"
    }

    Test-Case 'Profils valides : 2 paquets installeur produits' {
        $packages = @(Get-ChildItem -LiteralPath $validOut -Directory -Filter 'Liakont.Agent.Installer-*' -ErrorAction SilentlyContinue)
        Assert-True ($packages.Count -eq 2) "attendu 2 paquets, obtenu $($packages.Count)."
    }

    Test-Case 'Profils valides : chaque paquet est auto-suffisant (installeur + service OPS05)' {
        foreach ($pkg in Get-ChildItem -LiteralPath $validOut -Directory -Filter 'Liakont.Agent.Installer-*') {
            $installer = Join-Path $pkg.FullName 'bin\Liakont.Agent.Installer.exe'
            $service = Join-Path $pkg.FullName 'bin\Liakont.Agent.exe'
            Assert-True (Test-Path -LiteralPath $installer) "installeur manquant dans $($pkg.Name)"
            Assert-True (Test-Path -LiteralPath $service) "service (charge OPS05) manquant dans $($pkg.Name)"
        }
    }

    Test-Case 'Profils valides : le profil embarqué est relu par l''exécutable produit (--show-profile)' {
        # Vérification INDÉPENDANTE du script : on relit l'embarquement sur le binaire livré.
        $monoPkg = Get-ChildItem -LiteralPath $validOut -Directory -Filter '*profil-integrateur-exemple*' | Select-Object -First 1
        Assert-True ($null -ne $monoPkg) 'paquet du profil mono-site introuvable.'
        $installer = Join-Path $monoPkg.FullName 'bin\Liakont.Agent.Installer.exe'
        $shown = (& $installer --show-profile 2>&1 | Out-String)
        Assert-True ($LASTEXITCODE -eq 0) "--show-profile a échoué (code $LASTEXITCODE) : $shown"
        Assert-True ($shown -match 'exemple-integrateur') "profil « exemple-integrateur » non relu. Sortie : $shown"
    }

    # ── Cas anti-faux-vert : profil INVALIDE → échec, aucun installateur ──
    $invalidProfile = Join-Path $workRoot 'profil-invalide.json'
    '{ "profil": "cassé", "champs": { "champInconnu": { "etat": "affiché" } } }' |
        Set-Content -LiteralPath $invalidProfile -Encoding UTF8
    $invalidOut = Join-Path $workRoot 'invalid'
    $invalidRun = Invoke-PackageInstaller -ScriptArgs @(
        '-ProfilePath', $invalidProfile,
        '-Platform', $plat, '-Configuration', $config, '-SkipBuild', '-NoZip',
        '-OutputDirectory', $invalidOut)

    Test-Case 'Profil invalide : packaging échoue (exit non nul)' {
        Assert-True ($invalidRun.ExitCode -ne 0) "le packaging aurait dû échouer sur un profil invalide (exit $($invalidRun.ExitCode))."
    }

    Test-Case 'Profil invalide : aucun installateur produit' {
        $packages = @(Get-ChildItem -LiteralPath $invalidOut -Directory -Filter 'Liakont.Agent.Installer-*' -ErrorAction SilentlyContinue)
        Assert-True ($packages.Count -eq 0) "aucun paquet attendu, obtenu $($packages.Count)."
    }
}
finally {
    if (Test-Path -LiteralPath $workRoot) { Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host ""
Write-Host "=== Self-test : $($script:passed) OK, $($script:failed) FAIL ===" -ForegroundColor Cyan
if ($script:failed -gt 0) {
    Write-Host "ÉCHEC du self-test packaging installeur." -ForegroundColor Red
    exit 1
}
Write-Host "PASS" -ForegroundColor Green
exit 0
