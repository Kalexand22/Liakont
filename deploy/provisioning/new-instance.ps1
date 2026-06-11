#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Crée une nouvelle instance éditeur Liakont (OPS02, F12 §6.3) — une commande.
.DESCRIPTION
    Matérialise une instance isolée à partir de la stack appliance (OPS01a) :
      - répertoire/stack DÉDIÉ par instance (projet Docker Compose distinct → volumes, réseaux et
        conteneurs préfixés, aucune interférence entre instances sur une même machine) ;
      - SECRETS générés, forts et UNIQUES par instance (jamais de valeur par défaut ni partagée) ;
      - realm Keycloak + Caddyfile de l'instance ; branding PAR DÉFAUT Liakont tant que BRD01 n'est
        pas appliqué (le branding complet — nom éditeur, logo, couleurs — est enrichi par BRD01) ;
      - .env complet conforme au modèle de l'appliance.

    Deux modes (F12 §6.2) — le script est le MÊME, seule la cible diffère :
      - hosted       : déploie sur l'infra IT Innovations (docker compose up sur cette machine) ;
      - self-hosted  : produit un BUNDLE de configuration à remettre à l'éditeur (aucun déploiement
                       ici ; l'éditeur dépose le bundle dans sa copie de l'appliance et démarre).

    Registre des instances (deploy/provisioning/instances.yaml) : tenu à jour pour les instances
    OPÉRÉES par IT Innovations (hosted). Une instance self-hosted n'y figure que si l'éditeur a
    souscrit la méta-supervision (OPS04) — option -WithSupervision.

    Messages opérateur en français (CLAUDE.md n°12). AUCUNE donnée client n'est versionnée : le .env
    et le registre réel sont gitignorés ; ce script n'embarque aucun SIREN / hôte / secret réel.
.EXAMPLE
    ./new-instance.ps1 -InstanceName acme-prod -Editor "ACME SAS" -PublicHostname liakont.acme.example `
        -KeycloakHostname id.acme.example -AcmeEmail ops@acme.example -Mode hosted
.EXAMPLE
    ./new-instance.ps1 -InstanceName acme-prod -Editor "ACME SAS" -PublicHostname liakont.acme.example `
        -KeycloakHostname id.acme.example -AcmeEmail ops@acme.example -Mode self-hosted -DryRun
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstanceName,
    [Parameter(Mandatory = $true)][string]$Editor,
    [Parameter(Mandatory = $true)][string]$PublicHostname,
    [Parameter(Mandatory = $true)][string]$KeycloakHostname,
    [Parameter(Mandatory = $true)][string]$AcmeEmail,
    [ValidateSet('hosted', 'self-hosted')][string]$Mode = 'hosted',
    [string]$InstancesRoot,
    [string]$RegistryPath,
    [switch]$WithSupervision,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Les appels natifs autonomes (docker …) ne doivent pas lever sur code non-zéro —
# la gestion explicite via $LASTEXITCODE s'en charge (pwsh 7.4+ : défaut $true).
$PSNativeCommandUseErrorActionPreference = $false

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Import-Module (Join-Path $scriptRoot 'Provisioning.psm1') -Force

$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptRoot)
$applianceDir = Join-Path $repoRoot 'deploy\docker\appliance'
if (-not $InstancesRoot) { $InstancesRoot = Join-Path $scriptRoot 'instances' }
if (-not $RegistryPath)  { $RegistryPath  = Join-Path $scriptRoot 'instances.yaml' }

try {
    $instance = Resolve-InstanceName -Name $InstanceName
    $instanceDir = Join-Path $InstancesRoot $instance.Name
    $publicBaseUrl = "https://$PublicHostname"
    $stamp = [System.DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')

    Write-Step "Création de l'instance « $($instance.Name) » (projet Compose « $($instance.ProjectName) », mode $Mode)"
    Write-Host "  Éditeur          : $Editor"
    Write-Host "  Console          : $publicBaseUrl"
    Write-Host "  Keycloak         : https://$KeycloakHostname"
    Write-Host "  Répertoire       : $instanceDir"

    if (-not (Test-Path -LiteralPath $applianceDir)) {
        throw "Stack appliance introuvable ($applianceDir) — l'item OPS01a doit être présent."
    }
    if (Test-Path -LiteralPath $instanceDir) {
        throw "Le répertoire d'instance existe déjà ($instanceDir). Choisissez un autre nom ou supprimez " +
              "l'instance existante d'abord (jamais d'écrasement silencieux d'une instance en place)."
    }

    if ($DryRun) {
        Write-WarnMsg "DryRun : aucune écriture. Actions qui SERAIENT exécutées :"
        Write-Host "    1. Créer $instanceDir (+ copie compose/Caddyfile/realm) ;"
        Write-Host "    2. Générer 4 secrets uniques dans $instanceDir\.env ;"
        if ($Mode -eq 'hosted') {
            Write-Host "    3. docker compose -p $($instance.ProjectName) up -d --build ;"
            Write-Host "    4. Enregistrer l'instance dans $RegistryPath."
        }
        else {
            Write-Host "    3. Produire le bundle self-hosted (zip) à remettre à l'éditeur ;"
            $reg = if ($WithSupervision) { "Enregistrer dans $RegistryPath (méta-supervision souscrite)" } else { "PAS d'enregistrement (self-hosted sans méta-supervision)" }
            Write-Host "    4. $reg."
        }
        Write-Ok 'DryRun terminé.'
        exit 0
    }

    if ($Mode -eq 'hosted') { Test-DockerAvailable }

    # ── 1. Matérialiser le répertoire d'instance ──
    Write-Step '1/4 Matérialisation du répertoire d''instance'
    New-Item -ItemType Directory -Path $instanceDir -Force | Out-Null

    if ($Mode -eq 'hosted') {
        # docker-compose.yml : copié avec le contexte de build réécrit en chemin ABSOLU (le chemin
        # relatif « ../../.. » de l'appliance ne vaut plus depuis le répertoire d'instance).
        # En mode self-hosted on NE matérialise PAS le compose — le chemin absolu de l'opérateur
        # n'existe pas sur la machine de l'éditeur et fuiterait son arborescence locale.
        $composeSrc = Get-Content -LiteralPath (Join-Path $applianceDir 'docker-compose.yml') -Raw
        $absContext = ($repoRoot -replace '\\', '/')
        $composeOut = $composeSrc -replace 'context:\s*\.\./\.\./\.\.', "context: $absContext"
        if ($composeOut -eq $composeSrc) {
            throw "Le contexte de build « ../../.. » est introuvable dans le compose appliance — la réécriture " +
                  "du contexte a échoué (le format du compose a changé ?). Instance non créée."
        }
        # LF forcé : artefact de déploiement consommé côté Linux (cohérent avec le .env, évite tout
        # CRLF résiduel issu du checkout Windows de la source).
        $composeOut = $composeOut -replace "`r`n", "`n"
        [System.IO.File]::WriteAllText((Join-Path $instanceDir 'docker-compose.yml'), $composeOut, (New-Object System.Text.UTF8Encoding($false)))
    }

    Copy-Item -LiteralPath (Join-Path $applianceDir 'Caddyfile') -Destination (Join-Path $instanceDir 'Caddyfile')
    Copy-Item -LiteralPath (Join-Path $scriptRoot 'maintenance.Caddyfile') -Destination (Join-Path $instanceDir 'maintenance.Caddyfile')
    New-Item -ItemType Directory -Path (Join-Path $instanceDir 'keycloak') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $applianceDir 'keycloak\realm-liakont.json') -Destination (Join-Path $instanceDir 'keycloak\realm-liakont.json')
    if ($Mode -eq 'hosted') {
        Write-Ok 'Stack copiée (compose, Caddyfile, Caddyfile de maintenance, realm).'
    }
    else {
        Write-Ok 'Configuration copiée (Caddyfile, Caddyfile de maintenance, realm) — compose non matérialisé (self-hosted).'
    }

    # ── 2. Générer le .env (secrets uniques) ──
    Write-Step '2/4 Génération des secrets et du .env'
    $envContent = New-InstanceEnvContent -PublicHostname $PublicHostname -KeycloakHostname $KeycloakHostname -AcmeEmail $AcmeEmail
    [System.IO.File]::WriteAllText((Join-Path $instanceDir '.env'), $envContent, (New-Object System.Text.UTF8Encoding($false)))
    Write-Ok '.env généré (4 secrets uniques ; gitignoré).'

    # ── 3. Déployer (hosted) ou produire le bundle (self-hosted) ──
    if ($Mode -eq 'hosted') {
        Write-Step '3/4 Déploiement (docker compose up -d --build)'
        $composeArgs = @('compose', '-p', $instance.ProjectName, '--project-directory', $instanceDir,
            '-f', (Join-Path $instanceDir 'docker-compose.yml'))
        & docker @composeArgs up -d --build
        if ($LASTEXITCODE -ne 0) { throw "Le démarrage de l'instance a échoué (docker compose up). Voir les logs." }
        Write-Ok "Instance démarrée. Suivi : docker compose -p $($instance.ProjectName) logs -f"
    }
    else {
        Write-Step '3/4 Production du bundle self-hosted'
        $bundleReadme = @"
# Bundle d'instance Liakont — $($instance.Name)

Ce bundle contient la configuration de VOTRE instance (.env, Caddyfile, realm Keycloak).
Il ne contient PAS de docker-compose.yml — utilisez le vôtre (deploy/docker/appliance).

## Procédure de démarrage

1. Copiez « .env », « Caddyfile » et « keycloak/realm-liakont.json » de ce bundle dans votre
   répertoire d'appliance (deploy/docker/appliance), en remplaçant le .env.example par votre .env.
2. Depuis ce répertoire d'appliance, lancez :
   ``docker compose -p $($instance.ProjectName) --env-file .env up -d --build``
3. Vérifiez : la console répond sur $publicBaseUrl, la connexion Keycloak fonctionne.

Le .env contient des SECRETS uniques à votre instance — ne le partagez pas, ne le versionnez pas.
Mise à jour ultérieure : update-instance.ps1 (montée de version multi-bases, sauvegarde + rollback).
"@
        [System.IO.File]::WriteAllText((Join-Path $instanceDir 'BUNDLE-README.md'), $bundleReadme, (New-Object System.Text.UTF8Encoding($false)))
        $bundlePath = Join-Path $InstancesRoot "$($instance.Name)-bundle.zip"
        if (Test-Path -LiteralPath $bundlePath) { Remove-Item -LiteralPath $bundlePath -Force }
        Compress-Archive -Path (Join-Path $instanceDir '*') -DestinationPath $bundlePath
        Write-Ok "Bundle produit : $bundlePath (à remettre à l'éditeur)."
    }

    # ── 4. Registre des instances ──
    Write-Step '4/4 Mise à jour du registre'
    $shouldRegister = ($Mode -eq 'hosted') -or $WithSupervision.IsPresent
    if ($shouldRegister) {
        Set-InstanceRegistryEntry -Path $RegistryPath -Entry @{
            name       = $instance.Name
            editor     = $Editor
            url        = $publicBaseUrl
            hosting    = $Mode
            version    = 'initial'
            project    = $instance.ProjectName
            created_at = $stamp
            updated_at = $stamp
        }
        Write-Ok "Instance enregistrée dans $RegistryPath."
    }
    else {
        Write-WarnMsg 'Instance self-hosted sans méta-supervision : non inscrite au registre (-WithSupervision pour l''inscrire).'
    }

    Write-Host ''
    Write-Ok "Instance « $($instance.Name) » créée."
    exit 0
}
catch {
    Write-ErrMsg $_.Exception.Message
    exit 1
}
