#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Met à jour une instance Liakont — montée de version MULTI-BASES sûre (OPS02, F12 §6.3).
.DESCRIPTION
    Séquence d'une montée de version d'instance, conçue pour ne JAMAIS laisser une instance tourner
    avec des bases à des versions différentes (database-per-tenant) :

      1. MAINTENANCE — bascule le Caddyfile de l'instance en mode maintenance et le recharge à chaud :
         les push d'agents reçoivent un 503 EXPLICITE (Retry-After) pendant toute l'opération
         (back-off + re-push au heartbeat suivant, F12 §3.3 — aucune perte de document).
      2. SAUVEGARDE PRÉ-MIGRATION OBLIGATOIRE — pg_dump PAR BASE (système + chaque tenant actif),
         dans un dossier horodaté. Granularité par base = restauration sélective possible (rollback).
         Échec de sauvegarde ⇒ migration ANNULÉE (aucune montée de version sans sauvegarde).
      3. NOUVELLE IMAGE — docker compose build --pull (repart d'images de base à jour).
      4. MIGRATION — docker compose up -d : au démarrage, le Host applique les migrations de la base
         SYSTÈME puis de TOUTES les bases tenant (MigrateExistingTenantsAsync, boucle DbUp). Si une
         migration tenant échoue, AppBootstrap relève l'exception et le démarrage du Host est AVORTÉ
         — aucune requête n'est servie sur une base à demi-migrée.
      5. SANTÉ — attend un démarrage STABLE du Host :
           • succès  → sortie de maintenance (503 levé), registre mis à jour, instance à jour ;
           • échec   → le service Host est ARRÊTÉ, l'instance reste HORS LIGNE (jamais d'état mixte
                       en marche), la maintenance reste ACTIVE (agents toujours bloqués), un rapport
                       indique la sauvegarde à restaurer pour revenir à la version précédente.

    Le rollback (restauration des bases depuis la sauvegarde pré-migration + redéploiement de la
    version précédente) est DOCUMENTÉ dans le rapport d'échec et le README. Cette sauvegarde de
    sûreté est distincte de l'outillage de sauvegarde routinière / rotation / PRA porté par OPS01b.

    Messages opérateur en français (CLAUDE.md n°12).
.EXAMPLE
    ./update-instance.ps1 -InstanceName acme-prod
.EXAMPLE
    ./update-instance.ps1 -InstanceName acme-prod -DryRun
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstanceName,
    [string]$InstancesRoot,
    [string]$RegistryPath,
    [string]$TargetVersion,
    [int]$MigrationTimeoutSeconds = 300,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Import-Module (Join-Path $scriptRoot 'Provisioning.psm1') -Force

if (-not $InstancesRoot) { $InstancesRoot = Join-Path $scriptRoot 'instances' }
if (-not $RegistryPath)  { $RegistryPath  = Join-Path $scriptRoot 'instances.yaml' }

# ── Helpers de maintenance (bascule Caddyfile + reload) ──
function Enable-Maintenance {
    param([string[]]$ComposeArgs, [string]$Dir)
    $active = Join-Path $Dir 'Caddyfile'
    $normal = Join-Path $Dir 'Caddyfile.normal'
    $maint  = Join-Path $Dir 'maintenance.Caddyfile'
    if (-not (Test-Path -LiteralPath $maint)) { throw "Caddyfile de maintenance introuvable ($maint)." }
    # Ne sauvegarder le Caddyfile normal QUE s'il ne l'est pas déjà (un run précédent en échec a pu
    # laisser le Caddyfile en mode maintenance — ne pas écraser la sauvegarde du vrai Caddyfile).
    if (-not (Test-Path -LiteralPath $normal)) { Copy-Item -LiteralPath $active -Destination $normal -Force }
    Copy-Item -LiteralPath $maint -Destination $active -Force
    & docker @ComposeArgs exec -T caddy caddy reload --config /etc/caddy/Caddyfile 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-WarnMsg 'Rechargement de Caddy en mode maintenance non confirmé (Caddy injoignable ?) — les push agents peuvent recevoir un 502 au lieu d''un 503 le temps du redémarrage.'
    }
    else { Write-Ok 'Mode maintenance actif : push agents → 503 (Retry-After).' }
}

function Disable-Maintenance {
    param([string[]]$ComposeArgs, [string]$Dir)
    $active = Join-Path $Dir 'Caddyfile'
    $normal = Join-Path $Dir 'Caddyfile.normal'
    if (Test-Path -LiteralPath $normal) {
        Copy-Item -LiteralPath $normal -Destination $active -Force
        Remove-Item -LiteralPath $normal -Force
        & docker @ComposeArgs exec -T caddy caddy reload --config /etc/caddy/Caddyfile 2>$null
        Write-Ok 'Mode maintenance levé : push agents de nouveau acceptés.'
    }
}

try {
    $instance = Resolve-InstanceName -Name $InstanceName
    $instanceDir = Join-Path $InstancesRoot $instance.Name
    if (-not $TargetVersion) { $TargetVersion = [System.DateTime]::UtcNow.ToString('yyyy.MM.dd-HHmm') }
    $stamp = [System.DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')

    Write-Step "Mise à jour de l'instance « $($instance.Name) » (projet « $($instance.ProjectName) »)"

    if (-not (Test-Path -LiteralPath $instanceDir)) {
        throw "Instance inconnue : répertoire introuvable ($instanceDir). Listez les instances dans " +
              "$InstancesRoot ou créez-la avec new-instance.ps1."
    }

    $composeArgs = @('compose', '-p', $instance.ProjectName, '--project-directory', $instanceDir,
        '-f', (Join-Path $instanceDir 'docker-compose.yml'))

    if ($DryRun) {
        Write-WarnMsg 'DryRun : aucune action. Séquence qui SERAIT exécutée :'
        Write-Host '    1. Maintenance ON  : Caddyfile → maintenance, reload → push agents en 503 ;'
        Write-Host '    2. Sauvegarde      : pg_dump par base (système + chaque tenant) dans backups\<horodatage>\ ;'
        Write-Host '    3. Nouvelle image  : docker compose build --pull ;'
        Write-Host '    4. Migration       : docker compose up -d (le Host migre toutes les bases au démarrage) ;'
        Write-Host '    5. Santé           : attente d''un démarrage stable du Host —'
        Write-Host '         • succès → maintenance OFF + registre mis à jour ;'
        Write-Host '         • échec  → service Host ARRÊTÉ, instance hors ligne, maintenance maintenue, rapport de rollback.'
        Write-Ok 'DryRun terminé.'
        exit 0
    }

    Test-DockerAvailable

    # ── 1. Maintenance (503) ──
    Write-Step '1/5 Activation du mode maintenance (503 pour les push agents)'
    Enable-Maintenance -ComposeArgs $composeArgs -Dir $instanceDir

    # ── 2. Sauvegarde pré-migration par base ──
    Write-Step '2/5 Sauvegarde pré-migration (pg_dump par base)'
    try {
        $databases = Get-InstanceDatabases -ComposeArgs $composeArgs
        $backupDir = Join-Path $instanceDir ("backups\" + [System.DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
        Backup-InstanceDatabases -ComposeArgs $composeArgs -Databases $databases -OutputDir $backupDir | Out-Null
        Write-Ok "Sauvegarde de $($databases.Count) base(s) → $backupDir"
    }
    catch {
        # Aucune migration n'a eu lieu : l'instance tourne toujours l'ancienne version → on lève la
        # maintenance et on s'arrête proprement (l'opérateur garde une instance fonctionnelle).
        Write-ErrMsg "Sauvegarde pré-migration en échec — montée de version ANNULÉE : $($_.Exception.Message)"
        Disable-Maintenance -ComposeArgs $composeArgs -Dir $instanceDir
        exit 1
    }

    # ── 3. Nouvelle image ──
    Write-Step '3/5 Construction de la nouvelle image (build --pull)'
    & docker @composeArgs build --pull
    if ($LASTEXITCODE -ne 0) {
        Write-ErrMsg 'Construction de l''image en échec — montée de version ANNULÉE. Instance inchangée (ancienne version toujours en marche).'
        Disable-Maintenance -ComposeArgs $composeArgs -Dir $instanceDir
        exit 1
    }
    Write-Ok 'Nouvelle image construite.'

    # ── 4. Migration (au démarrage du Host) ──
    Write-Step '4/5 Application de la migration multi-bases (docker compose up -d)'
    & docker @composeArgs up -d
    if ($LASTEXITCODE -ne 0) {
        Write-WarnMsg 'docker compose up a renvoyé une erreur — vérification de l''état réel du Host ci-dessous.'
    }

    # ── 5. Santé ──
    Write-Step "5/5 Vérification de santé (démarrage stable du Host, délai $MigrationTimeoutSeconds s)"
    $health = Wait-InstanceHealthy -ComposeArgs $composeArgs -Service 'liakont' -TimeoutSeconds $MigrationTimeoutSeconds
    if ($health.Healthy) {
        Disable-Maintenance -ComposeArgs $composeArgs -Dir $instanceDir
        Set-InstanceRegistryEntry -Path $RegistryPath -Entry @{
            name = $instance.Name; project = $instance.ProjectName
            url = "https://$( (Get-Content -LiteralPath (Join-Path $instanceDir '.env') | Where-Object { $_ -match '^PUBLIC_HOSTNAME=' } | ForEach-Object { ($_ -split '=', 2)[1] } | Select-Object -First 1) )"
            version = $TargetVersion; hosting = 'hosted'; updated_at = $stamp
        }
        Write-Host ''
        Write-Ok "Instance « $($instance.Name) » à jour (version « $TargetVersion »). Toutes les bases migrées."
        exit 0
    }

    # Échec : ne JAMAIS laisser une instance en marche sur des bases à demi-migrées.
    Write-ErrMsg "Démarrage du Host non confirmé : $($health.Detail)"
    & docker @composeArgs stop liakont 2>$null
    Write-Host ''
    Write-ErrMsg 'MONTÉE DE VERSION EN ÉCHEC — instance ARRÊTÉE (aucun état mixte en marche).'
    Write-Host  "  Cause probable : échec de migration d'une base tenant (le Host a avorté son démarrage)." -ForegroundColor Yellow
    Write-Host  "  Diagnostic     : docker compose -p $($instance.ProjectName) logs liakont" -ForegroundColor Yellow
    Write-Host  "  ROLLBACK       : 1) restaurer les bases depuis $backupDir (psql < <base>.sql par base) ;" -ForegroundColor Yellow
    Write-Host  "                   2) redéployer la version précédente du code ;" -ForegroundColor Yellow
    Write-Host  "                   3) update-instance.ps1 lève la maintenance une fois l'instance saine." -ForegroundColor Yellow
    Write-Host  "  La maintenance (503) reste ACTIVE : les agents restent bloqués jusqu'à résolution." -ForegroundColor Yellow
    exit 1
}
catch {
    Write-ErrMsg $_.Exception.Message
    exit 1
}
