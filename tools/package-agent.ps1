#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fabrique les packages d'installation de l'agent Liakont (x86 et x64) — OPS05.
.DESCRIPTION
    Construit la solution agent (net48) en Release pour les plateformes demandées, assemble pour
    chacune un package d'installation auto-suffisant (binaires + dépendances natives + scripts
    d'installation), le vérifie (contrôle anti-faux-vert : bitness PE de l'EXE et du SQLite.Interop
    natif, présence des scripts, absence de secret en clair) et le compresse en .zip.

    Format de package et transport de la clé de pré-configuration : voir
    docs/adr/ADR-0019-format-paquet-installeur-agent.md. Documentation intégrateur :
    docs/installation-agent.md.

    RÈGLE x86/x64 (ADR-0019, docs/installation-agent.md) : x86 par DÉFAUT dès qu'un adaptateur ODBC
    32 bits est requis (driver Pervasive d'EncheresV6) ; x64 seulement si TOUS les adaptateurs du
    client sont 64 bits. Le service et ses adaptateurs partagent le même bitness.

    PRÉ-CONFIGURATION (OPS05 pt 3, OPS03 étape 4) : avec -PreConfigApiKey, le package embarque un
    agent.json pré-rempli dont la clé API est chiffrée par un mot de passe à usage unique (jamais en
    clair dans le package). Le mot de passe est communiqué SÉPARÉMENT à l'intégrateur.

    Exit code 0 = tous les packages produits et vérifiés ; non-zéro = au moins un échec.
.PARAMETER Platform
    x86, x64 ou both (défaut both).
.PARAMETER Configuration
    Configuration de build (défaut Release).
.PARAMETER OutputDirectory
    Répertoire de sortie des .zip (défaut <repo>\artifacts\agent-packages).
.PARAMETER Version
    Version du package (défaut : version de l'assembly Liakont.Agent.exe construit).
.PARAMETER SkipBuild
    Assemble depuis la sortie de build existante (itération rapide ; ne pas utiliser pour une release).
.PARAMETER NoZip
    Laisse le package assemblé en dossier (pas de .zip) — inspection / test.
.PARAMETER PreConfigPlatformUrl
    URL HTTPS de la plateforme à pré-remplir dans agent.json.
.PARAMETER PreConfigAdapter
    Adaptateur source à pré-remplir (ex. EncheresV6).
.PARAMETER PreConfigApiKey
    Clé API EN CLAIR à embarquer chiffrée (jamais écrite en clair dans le package).
.PARAMETER PreConfigSchedule
    Heures de planification (HH:mm) à pré-remplir (optionnel).
.PARAMETER PreConfigPassword
    Mot de passe à usage unique chiffrant la clé API. Si omis, un mot de passe fort est généré et
    affiché (à communiquer SÉPARÉMENT à l'intégrateur).
.PARAMETER SigningPublicKeyPath
    Chemin d'une clé publique de signature d'auto-update (XML) à provisionner dans le package.
#>
[CmdletBinding()]
param(
    [ValidateSet('x86', 'x64', 'both')]
    [string]$Platform = 'both',

    [string]$Configuration = 'Release',

    [string]$OutputDirectory,

    [string]$Version,

    [switch]$SkipBuild,

    [switch]$NoZip,

    [string]$PreConfigPlatformUrl,
    [string]$PreConfigAdapter,
    [string]$PreConfigApiKey,
    [string[]]$PreConfigSchedule,
    [string]$PreConfigPassword,
    [string]$SigningPublicKeyPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$agentSln = Join-Path $repoRoot 'agent\Liakont.Agent.sln'
$installerDir = Join-Path $repoRoot 'deploy\agent-installer'
Import-Module (Join-Path $installerDir 'AgentInstall.psm1') -Force

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\agent-packages' }
if ($Platform -eq 'both') { $platforms = @('x86', 'x64') } else { $platforms = @($Platform) }

# Projets exécutables dont la sortie compose le package (le CLI tire transitivement le cœur, les
# adaptateurs, le contrat, Newtonsoft et SQLite + ses natifs ; le service et l'updater ajoutent
# leurs propres exes). L'union des trois sorties = fermeture de déploiement complète.
$exeProjects = @(
    'agent\src\Liakont.Agent\bin\{0}\{1}\net48',
    'agent\src\Liakont.Agent.Cli\bin\{0}\{1}\net48',
    'agent\src\Liakont.Agent.Updater\bin\{0}\{1}\net48'
)
$installerFiles = @('install-agent.ps1', 'uninstall-agent.ps1', 'AgentInstall.psm1')

$failures = @()

function Invoke-Build {
    param([string]$Plat)
    Write-Host "Build agent $Plat ($Configuration)..." -ForegroundColor Cyan
    & dotnet build $agentSln --no-restore -c $Configuration "-p:Platform=$Plat" --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "Échec du build agent $Plat (dotnet build a renvoyé $LASTEXITCODE)." }
}

function Build-Package {
    param([string]$Plat)

    $stageRoot = Join-Path $OutputDirectory '.work'
    $stageDir = Join-Path $stageRoot ("stage-$Plat")
    if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
    $binDir = Join-Path $stageDir 'bin'
    New-Item -ItemType Directory -Path $binDir -Force | Out-Null

    # ── Fusion des sorties de build (last-wins sur les dépendances partagées identiques) ──
    # Copie fichier par fichier en préservant l'arborescence relative : « Copy-Item -Recurse » d'un
    # dossier sur un dossier de même nom déjà présent (sous-dossiers natifs x86/x64 partagés par les
    # trois projets) imbrique ou n'écrase pas correctement (quirk PowerShell). Le merge explicite est
    # déterministe quel que soit le nombre de sources.
    foreach ($tpl in $exeProjects) {
        $src = Join-Path $repoRoot ($tpl -f $Plat, $Configuration)
        if (-not (Test-Path -LiteralPath $src)) {
            throw "Sortie de build absente : « $src ». Lancez sans -SkipBuild, ou vérifiez la plateforme."
        }
        $srcFull = (Resolve-Path -LiteralPath $src).Path
        foreach ($file in Get-ChildItem -LiteralPath $srcFull -Recurse -File) {
            $relative = $file.FullName.Substring($srcFull.Length).TrimStart('\')
            $target = Join-Path $binDir $relative
            $targetDir = Split-Path -Parent $target
            if (-not (Test-Path -LiteralPath $targetDir)) { New-Item -ItemType Directory -Path $targetDir -Force | Out-Null }
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
    }

    # ── Élagage : symboles de debug, docs XML, et le SQLite.Interop natif de l'AUTRE plateforme ──
    # Filtrage par Where-Object (et non -Include) : -Include est IGNORÉ avec -LiteralPath (incompatibilité
    # PowerShell connue) — Get-ChildItem rendrait alors TOUS les fichiers et l'élagage les supprimerait tous.
    Get-ChildItem -LiteralPath $binDir -Recurse -File |
        Where-Object { $_.Extension -eq '.pdb' -or $_.Extension -eq '.xml' } |
        Remove-Item -Force -ErrorAction SilentlyContinue
    $otherPlat = if ($Plat -eq 'x86') { 'x64' } else { 'x86' }
    $otherNativeDir = Join-Path $binDir $otherPlat
    if (Test-Path -LiteralPath $otherNativeDir) { Remove-Item -LiteralPath $otherNativeDir -Recurse -Force }

    # ── Scripts d'installation + module partagé ──
    foreach ($f in $installerFiles) {
        Copy-Item -LiteralPath (Join-Path $installerDir $f) -Destination $stageDir -Force
    }

    # ── Modèle agent.json (FICTIF) — référence pour la configuration manuelle (item OPS05 pt 2) ──
    $exampleConfig = Join-Path $repoRoot 'config\exemples\agent.json'
    if (Test-Path -LiteralPath $exampleConfig) {
        Copy-Item -LiteralPath $exampleConfig -Destination (Join-Path $stageDir 'agent.json.exemple') -Force
    }

    # ── Résolution de la version depuis l'assembly construit ──
    $serviceExe = Join-Path $binDir 'Liakont.Agent.exe'
    $resolvedVersion = $Version
    if (-not $resolvedVersion) {
        $fileVer = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($serviceExe).FileVersion
        $parts = ($fileVer -split '\.')
        $resolvedVersion = ($parts[0..([Math]::Min(2, $parts.Length - 1))] -join '.')
    }
    $pkgName = "Liakont.Agent-$resolvedVersion-$Plat"

    # ── Pré-configuration (clé API jamais en clair dans le package) ──
    $otpToReport = $null
    if ($PreConfigApiKey) {
        if (-not $PreConfigPlatformUrl -or -not $PreConfigAdapter) {
            throw "Pré-configuration incomplète : -PreConfigPlatformUrl et -PreConfigAdapter sont requis avec -PreConfigApiKey."
        }
        $otp = $PreConfigPassword
        if (-not $otp) { $otp = New-AgentOneTimePassword; $otpToReport = $otp }
        $secret = Protect-AgentPreConfigSecret -PlainText $PreConfigApiKey -Password $otp
        $preconfig = [ordered]@{
            platformUrl      = $PreConfigPlatformUrl
            adapter          = $PreConfigAdapter
            heartbeatMinutes = 15
            apiKeySecret     = $secret
        }
        if ($PreConfigSchedule) { $preconfig['schedule'] = @($PreConfigSchedule) }
        ($preconfig | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath (Join-Path $stageDir 'preconfig.json') -Encoding UTF8
    }

    # ── Clé publique de signature d'auto-update (optionnelle) ──
    if ($SigningPublicKeyPath) {
        if (-not (Test-Path -LiteralPath $SigningPublicKeyPath)) {
            throw "Clé publique de signature introuvable : « $SigningPublicKeyPath »."
        }
        Copy-Item -LiteralPath $SigningPublicKeyPath -Destination (Join-Path $stageDir 'update-signing.pubkey.xml') -Force
    }

    # ── Manifeste du package ──
    $manifest = [ordered]@{
        product       = 'Liakont.Agent'
        version       = $resolvedVersion
        platform      = $Plat
        configuration = $Configuration
        targetFramework = 'net48'
        preConfigured = [bool]$PreConfigApiKey
        signingKeyProvisioned = [bool]$SigningPublicKeyPath
    }
    ($manifest | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath (Join-Path $stageDir 'package.json') -Encoding UTF8

    # ── Vérification anti-faux-vert du package assemblé ──
    Assert-Package -StageDir $stageDir -Plat $Plat -ClearApiKey $PreConfigApiKey

    # ── Sortie : dossier (NoZip) ou .zip ──
    if (-not (Test-Path -LiteralPath $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null }
    $result = [ordered]@{ Platform = $Plat; Version = $resolvedVersion; Otp = $otpToReport }
    if ($NoZip) {
        $finalDir = Join-Path $OutputDirectory $pkgName
        if (Test-Path -LiteralPath $finalDir) { Remove-Item -LiteralPath $finalDir -Recurse -Force }
        Move-Item -LiteralPath $stageDir -Destination $finalDir
        $result['Path'] = $finalDir
    }
    else {
        $zipPath = Join-Path $OutputDirectory "$pkgName.zip"
        if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
        # Renommer le dossier de staging au nom final pour que le .zip extraie un dossier nommé.
        $namedStage = Join-Path $stageRoot $pkgName
        if (Test-Path -LiteralPath $namedStage) { Remove-Item -LiteralPath $namedStage -Recurse -Force }
        Move-Item -LiteralPath $stageDir -Destination $namedStage
        Compress-Archive -Path $namedStage -DestinationPath $zipPath -Force
        Remove-Item -LiteralPath $namedStage -Recurse -Force
        $sha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
        $result['Path'] = $zipPath
        $result['Sha256'] = $sha
    }
    return [PSCustomObject]$result
}

function Assert-Package {
    param(
        [string]$StageDir,
        [string]$Plat,
        [string]$ClearApiKey
    )

    $binDir = Join-Path $StageDir 'bin'
    $errors = @()

    # 1. Exécutables présents et du BON bitness. Contrôle par ProcessorArchitecture (managé) : il
    #    distingue x86 (32BITREQUIRED, requis pour un driver ODBC 32 bits) d'AnyCPU/MSIL — le champ
    #    Machine du PE rapporte I386 pour les deux et laisserait passer un EXE AnyCPU.
    $exes = @('Liakont.Agent.exe', 'Liakont.Agent.Cli.exe', 'Liakont.Agent.Updater.exe')
    foreach ($exe in $exes) {
        $path = Join-Path $binDir $exe
        if (-not (Test-Path -LiteralPath $path)) { $errors += "EXE manquant : $exe"; continue }
        $arch = Get-AgentBinaryArchitecture -Path $path
        if ($arch -ne $Plat) { $errors += "$exe est « $arch » mais le package est « $Plat » (AnyCPU/MSIL refusé : le bitness doit être forcé)." }
    }

    # 2. SQLite.Interop.dll de la BONNE plateforme présent ; celui de l'AUTRE plateforme absent.
    $nativeDll = Join-Path (Join-Path $binDir $Plat) 'SQLite.Interop.dll'
    if (-not (Test-Path -LiteralPath $nativeDll)) {
        $errors += "SQLite.Interop.dll ($Plat) manquant dans le package."
    }
    else {
        $nativeMachine = Get-PeMachineType -Path $nativeDll
        if ($nativeMachine -ne $Plat) { $errors += "SQLite.Interop.dll est « $nativeMachine » mais le package est « $Plat »." }
    }
    $otherPlat = if ($Plat -eq 'x86') { 'x64' } else { 'x86' }
    if (Test-Path -LiteralPath (Join-Path $binDir $otherPlat)) {
        $errors += "Le sous-dossier natif « $otherPlat » de l'autre plateforme n'a pas été élagué."
    }

    # 3. Scripts d'installation embarqués.
    foreach ($f in @('install-agent.ps1', 'uninstall-agent.ps1', 'AgentInstall.psm1')) {
        if (-not (Test-Path -LiteralPath (Join-Path $StageDir $f))) { $errors += "Script manquant : $f" }
    }

    # 4. Aucun secret en clair : si pré-configuré, la clé API ne doit JAMAIS apparaître en clair.
    $preconfigPath = Join-Path $StageDir 'preconfig.json'
    if (Test-Path -LiteralPath $preconfigPath) {
        $raw = Get-Content -LiteralPath $preconfigPath -Raw
        if ($ClearApiKey -and $raw.Contains($ClearApiKey)) {
            $errors += "FUITE : la clé API en clair est présente dans preconfig.json."
        }
        if ($raw -notmatch '"apiKeySecret"') { $errors += "preconfig.json ne contient pas le secret chiffré (apiKeySecret)." }
    }

    if ($errors.Count -gt 0) {
        throw "Vérification du package $Plat en échec :`n  - " + ($errors -join "`n  - ")
    }
    Write-Host "  Vérification $Plat : OK (bitness EXE + SQLite natif, scripts présents, aucun secret en clair)." -ForegroundColor Green
}

# ── Exécution ───────────────────────────────────────────────────────────────
try {
    if (-not (Test-Path -LiteralPath $agentSln)) { throw "Solution agent introuvable : « $agentSln »." }

    if (-not $SkipBuild) {
        Write-Host "Restauration de la solution agent..." -ForegroundColor Cyan
        & dotnet restore $agentSln --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw "Échec de la restauration (dotnet restore a renvoyé $LASTEXITCODE)." }
        foreach ($plat in $platforms) { Invoke-Build -Plat $plat }
    }

    $results = @()
    foreach ($plat in $platforms) {
        Write-Host "Assemblage du package $plat..." -ForegroundColor Cyan
        $results += Build-Package -Plat $plat
    }

    # Nettoyage du dossier de travail.
    $stageRoot = Join-Path $OutputDirectory '.work'
    if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue }

    Write-Host ""
    Write-Host "=== Packages produits ===" -ForegroundColor Cyan
    foreach ($r in $results) {
        Write-Host ("  {0,-4} v{1}  ->  {2}" -f $r.Platform, $r.Version, $r.Path) -ForegroundColor Green
        if ($r.PSObject.Properties['Sha256'] -and $r.Sha256) { Write-Host "        SHA-256 : $($r.Sha256)" }
        if ($r.Otp) {
            Write-Host "        Mot de passe à usage unique (pré-config) : $($r.Otp)" -ForegroundColor Yellow
            Write-Host "        -> à communiquer SÉPARÉMENT à l'intégrateur (jamais avec le package)." -ForegroundColor Yellow
        }
    }
    exit 0
}
catch {
    Write-Host ""
    Write-Host "Échec du packaging : $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
