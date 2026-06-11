#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Pilote le Keycloak de DÉVELOPPEMENT local de Liakont (docker compose).
.DESCRIPTION
    Outille le cycle de vie du conteneur Keycloak de dev et, surtout, le RESET propre du realm
    (bug-inbox FIX07a « Import du realm silencieusement sauté si volume Keycloak résiduel »).

    L'import `--import-realm` (stratégie IGNORE_EXISTING) NE réimporte PAS un realm `liakont-dev`
    déjà présent dans le volume : un poste ayant déjà servi conserve l'ANCIEN realm (anciens
    usernames e-mail) et la console répond « Nom d'utilisateur ou mot de passe invalide » sans
    aucun signal. L'action `reset` supprime le volume (`down -v`) puis relance, ce qui FORCE la
    réimportation depuis deploy/docker/keycloak/realm-export.json.

    Actions :
      start   : docker compose up -d           (importe le realm au premier démarrage)
      stop    : docker compose down             (conserve le volume)
      reset   : docker compose down -v && up -d (SUPPRIME le volume -> réimport propre du realm)
      status  : docker compose ps + sonde de joignabilité du realm

    Données 100% dev local (identifiants fictifs admin/admin, secret placeholder). Jamais en prod.

    Usage :
      powershell -ExecutionPolicy Bypass -File tools/keycloak-dev.ps1 reset
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('start', 'stop', 'reset', 'status')]
    [string] $Action = 'start',

    # Délai d'attente max (secondes) de l'import du realm après start/reset.
    [int] $TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'

$repoRoot    = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repoRoot 'deploy\docker\docker-compose.keycloak.yml'

# Constantes de DEV LOCAL — alignées sur docker-compose.keycloak.yml / realm-export.json
# et appsettings.Development.json. Aucune donnée client.
$realmUrl     = 'http://localhost:8080/realms/liakont-dev'
$discoveryUrl = "$realmUrl/.well-known/openid-configuration"

function Test-DockerAvailable {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Host "ERREUR : 'docker' est introuvable dans le PATH. Installez Docker Desktop puis relancez." -ForegroundColor Red
        exit 1
    }
    if (-not (Test-Path $composeFile)) {
        Write-Host "ERREUR : fichier compose introuvable : $composeFile" -ForegroundColor Red
        exit 1
    }
}

# Sonde HTTP du realm : 200 sur le document de découverte OIDC = realm importé et joignable.
function Test-RealmReachable {
    try {
        $resp = Invoke-WebRequest -Uri $discoveryUrl -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
        return [int]$resp.StatusCode -eq 200
    }
    catch {
        return $false
    }
}

# Attend que le realm soit importé et joignable (après start/reset). Échoue si le délai expire :
# un realm jamais importé est un faux départ, pas un succès silencieux.
function Wait-ForRealm {
    Write-Host "Attente de l'import du realm 'liakont-dev' ($discoveryUrl)..." -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-RealmReachable) {
            Write-Host "Realm 'liakont-dev' joignable." -ForegroundColor Green
            return $true
        }
        Start-Sleep -Seconds 3
    }
    Write-Host "ERREUR : le realm 'liakont-dev' n'est pas joignable après $TimeoutSeconds s." -ForegroundColor Red
    Write-Host "  Vérifiez les logs : docker compose -f `"$composeFile`" logs keycloak" -ForegroundColor Yellow
    return $false
}

Test-DockerAvailable

switch ($Action) {
    'start' {
        Write-Host "Démarrage de Keycloak (compose up -d)..." -ForegroundColor Cyan
        docker compose -f $composeFile up -d
        if ($LASTEXITCODE -ne 0) { Write-Host "ERREUR : 'compose up' a échoué (code $LASTEXITCODE)." -ForegroundColor Red; exit $LASTEXITCODE }
        if (-not (Wait-ForRealm)) { exit 1 }
    }
    'stop' {
        Write-Host "Arrêt de Keycloak (compose down, volume conservé)..." -ForegroundColor Cyan
        docker compose -f $composeFile down
        exit $LASTEXITCODE
    }
    'reset' {
        # down -v : SUPPRIME le volume liakont-keycloak-db-data -> l'import du realm n'est plus
        # « skipped » au prochain démarrage. C'est le remède au realm périmé (FIX07a).
        Write-Host "RESET de Keycloak : suppression du volume puis réimport propre du realm..." -ForegroundColor Cyan
        docker compose -f $composeFile down -v
        if ($LASTEXITCODE -ne 0) { Write-Host "ERREUR : 'compose down -v' a échoué (code $LASTEXITCODE)." -ForegroundColor Red; exit $LASTEXITCODE }
        docker compose -f $composeFile up -d
        if ($LASTEXITCODE -ne 0) { Write-Host "ERREUR : 'compose up' a échoué (code $LASTEXITCODE)." -ForegroundColor Red; exit $LASTEXITCODE }
        if (-not (Wait-ForRealm)) { exit 1 }
        Write-Host "Realm réimporté. Comptes de dev disponibles (mot de passe Test@1234)." -ForegroundColor Green
    }
    'status' {
        docker compose -f $composeFile ps
        if (Test-RealmReachable) {
            Write-Host "Realm 'liakont-dev' : JOIGNABLE ($realmUrl)." -ForegroundColor Green
        }
        else {
            Write-Host "Realm 'liakont-dev' : INJOIGNABLE. Lancez 'tools/keycloak-dev.ps1 start' (ou 'reset' si périmé)." -ForegroundColor Yellow
        }
    }
}
