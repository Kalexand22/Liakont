#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Pilote l'environnement de DÉMO Isatech (stack Docker Liakont isolée, locale) — couleurs Isatech.
.DESCRIPTION
    Déclinaison de marque ISATECH (partenaire « Enchères SVV » pour commissaires-priseurs) du stack de
    démo Liakont (cf. deployments/bucodi/). Projet Compose `liakont-isatech` : Host (.NET 10, Development)
    + PostgreSQL plateforme + Keycloak (realm `bucodi` RÉUTILISÉ pour l'auth) + sa base. Tout en http sur
    localhost. Seule la MARQUE (couleurs/logo/nom) diffère de Bucodi — l'auth et les services sont identiques.

    NB :MÊMES PORTS que Bucodi (8090/8081/5442) → UN SEUL env de démo à la fois : arrêtez Bucodi
       (deployments/bucodi/demo.ps1 down) avant de lancer Isatech.

    Actions :
      up      : build + démarre la stack, attend la santé, affiche les accès (par défaut)
      down    : arrête la stack (conserve les volumes → données préservées)
      reset   : down -v + up  (SUPPRIME les volumes → base + realm repartent de zéro)
      status  : état des conteneurs + sondes de joignabilité (realm + console)
      logs    : suit les logs du Host, de Keycloak et de la base

    Usage :
      powershell -ExecutionPolicy Bypass -File demo.ps1 up
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('up', 'down', 'reset', 'status', 'logs')]
    [string] $Action = 'up',

    [int] $TimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
$composeDir  = $PSScriptRoot
$project     = 'liakont-isatech'
$composeFile = Join-Path $composeDir 'docker-compose.yml'

$consoleUrl   = 'http://localhost:8090'
$keycloakUrl  = 'http://localhost:8081'
$discoveryUrl = "$keycloakUrl/realms/bucodi/.well-known/openid-configuration"

function Test-DockerAvailable {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Host "ERREUR : 'docker' introuvable dans le PATH. Démarrez Docker Desktop puis relancez." -ForegroundColor Red
        exit 1
    }
    if (-not (Test-Path $composeFile)) {
        Write-Host "ERREUR : compose introuvable : $composeFile" -ForegroundColor Red
        exit 1
    }
}

function Invoke-Compose {
    param([string[]] $ComposeArgs)
    $ErrorActionPreference = 'Continue'
    & docker compose -p $project -f $composeFile @ComposeArgs 2>&1 | ForEach-Object { Write-Host "$_" }
    return $LASTEXITCODE
}

function Test-UrlOk {
    param([string] $Url, [int[]] $OkCodes = @(200))
    try {
        $resp = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 4 -ErrorAction Stop
        return $OkCodes -contains [int]$resp.StatusCode
    }
    catch {
        $code = $_.Exception.Response.StatusCode.value__
        return ($null -ne $code -and ($OkCodes -contains [int]$code))
    }
}

function Wait-For {
    param([string] $Label, [string] $Url, [int[]] $OkCodes = @(200))
    Write-Host "Attente : $Label ..." -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-UrlOk $Url $OkCodes) { Write-Host "  OK : $Label" -ForegroundColor Green; return $true }
        Start-Sleep -Seconds 4
    }
    Write-Host "  ÉCHEC : $Label non prêt après $TimeoutSeconds s." -ForegroundColor Red
    return $false
}

function Show-Access {
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor DarkYellow
    Write-Host "  Environnement de démo ISATECH — prêt" -ForegroundColor DarkYellow
    Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor DarkYellow
    Write-Host "  Console Liakont (Isatech) : $consoleUrl"
    Write-Host "  Keycloak (admin)          : $keycloakUrl/admin   (admin / admin)"
    Write-Host "  PostgreSQL                : localhost:5442   (liakont / liakont_demo_pwd)"
    Write-Host ""
    Write-Host "  Connexion (realm bucodi réutilisé pour l'auth) :" -ForegroundColor Cyan
    Write-Host "    sysadmin / Test@1234   → super-admin d'instance (stratum-admin)"
    Write-Host "    1er login : changement de mot de passe FORCÉ + enrôlement 2FA TOTP (appli d'authentification)" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  NB :Mêmes ports que Bucodi → arrêtez Bucodi avant (deployments/bucodi/demo.ps1 down)." -ForegroundColor DarkGray
    Write-Host "  Arrêt        : demo.ps1 down     |  Remise à zéro : demo.ps1 reset" -ForegroundColor DarkGray
    Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor DarkYellow
}

Test-DockerAvailable

switch ($Action) {
    'up' {
        Write-Host "Build + démarrage de la stack de démo Isatech (projet $project)..." -ForegroundColor Cyan
        if ((Invoke-Compose @('up', '-d', '--build')) -ne 0) {
            Write-Host "ERREUR : 'compose up --build' a échoué." -ForegroundColor Red; exit 1
        }
        $ok = (Wait-For 'realm Keycloak importé' $discoveryUrl @(200)) `
              -and (Wait-For 'console Liakont joignable' $consoleUrl @(200, 302, 401))
        if (-not $ok) {
            Write-Host "La stack a démarré mais une sonde a échoué. Logs : demo.ps1 logs" -ForegroundColor Yellow
            exit 1
        }
        Show-Access
    }
    'down' {
        Write-Host "Arrêt de la stack (volumes conservés)..." -ForegroundColor Cyan
        Invoke-Compose @('down') | Out-Null
        exit $LASTEXITCODE
    }
    'reset' {
        Write-Host "RESET : suppression des volumes (base + realm) puis redémarrage..." -ForegroundColor Cyan
        Invoke-Compose @('down', '-v') | Out-Null
        if ((Invoke-Compose @('up', '-d', '--build')) -ne 0) {
            Write-Host "ERREUR : 'compose up --build' a échoué." -ForegroundColor Red; exit 1
        }
        $ok = (Wait-For 'realm Keycloak importé' $discoveryUrl @(200)) `
              -and (Wait-For 'console Liakont joignable' $consoleUrl @(200, 302, 401))
        if ($ok) { Show-Access } else { Write-Host "Sonde échouée. Logs : demo.ps1 logs" -ForegroundColor Yellow; exit 1 }
    }
    'status' {
        $null = Invoke-Compose @('ps')
        Write-Host ""
        if (Test-UrlOk $discoveryUrl)              { Write-Host "Realm        : JOIGNABLE ($keycloakUrl)" -ForegroundColor Green }
        else                                       { Write-Host "Realm        : INJOIGNABLE" -ForegroundColor Yellow }
        if (Test-UrlOk $consoleUrl @(200,302,401)) { Write-Host "Console      : JOIGNABLE ($consoleUrl)" -ForegroundColor Green }
        else                                       { Write-Host "Console      : INJOIGNABLE" -ForegroundColor Yellow }
    }
    'logs' {
        $null = Invoke-Compose @('logs', '-f', 'liakont', 'keycloak', 'postgres')
    }
}
