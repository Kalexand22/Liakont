#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Migre une instance Liakont d'une cible vers une autre (OPS06b, F12 §6.3 — réversibilité de V1).
.DESCRIPTION
    La réversibilité (déplacer une instance « dédiée hébergée » → « self-hosted », ou changer de
    machine d'hébergement) est une exigence de V1, pas une option. Cet outil la réalise en DEUX
    phases, l'une sur la machine SOURCE, l'autre sur la machine CIBLE, autour d'un BUNDLE de
    migration intègre :

      EXPORT (sur la SOURCE, phase par défaut) :
        1. pg_dump de TOUTES les bases (système + chaque tenant, ACTIF ou SUSPENDU) ;
        2. copie du volume applicatif (coffre d'archive WORM + clés Data Protection + staging + PDF) ;
        3. assemblage d'un BUNDLE d'installation complet pour la cible (dumps + volume + config
           + secrets de l'instance + procédure) avec empreintes SHA-256 vérifiables.
      APPLY (sur la CIBLE, avec -ApplyBundle) :
        4. matérialisation de la cible à partir du bundle (config + secrets PRÉSERVÉS de la source) ;
        5. restauration de toutes les bases + du volume dans une cible VIERGE ;
        6. CONTRÔLE DE SANTÉ post-migration (démarrage stable du Host) ;
        7. BASCULE DNS documentée (la cible ne sert qu'une fois la santé verte).

    Réutilise la MÉCANIQUE déjà testée d'OPS01b — `deploy/docker/backup.sh` (dump par base + archive
    du volume + manifeste SHA-256, énumération des bases PAR EXISTENCE sur le cluster donc AUCUN tenant
    suspendu n'est silencieusement omis) et `deploy/docker/restore.sh` (vérification d'intégrité AVANT
    restauration + garde anti-écrasement du coffre WORM). `restore.sh` cite explicitement OPS06b comme
    cible nominale. La migration n'invente aucune mécanique de dump/restore : elle orchestre la sienne.

    NON DESTRUCTIF SUR LA SOURCE : l'export ne fait que LIRE (sauvegarde) ; la source reste en service
    jusqu'à la bascule DNS confirmée. En cas d'échec côté cible, la source est intacte (rollback = ne
    pas basculer le DNS).

    CIBLE NOMINALE : l'appliance tourne sous Linux / pwsh 7 (zip64), bash natif. Le script fonctionne
    aussi sous Windows (Git Bash), mais Compress-Archive / Expand-Archive de Windows PowerShell 5.1
    PLAFONNENT à 2 Go (pas de zip64) : sur un GROS coffre d'archives (rétention 10 ans), utilisez
    pwsh 7 (ou la cible Linux). Voir deploy/provisioning/README.md.

    AUCUNE donnée client dans le code : le bundle (secrets + données fiscales) est un ARTEFACT OPÉRATEUR
    écrit sous `instances/` (gitignoré) ; il n'embarque aucun SIREN/hôte/secret en dur. Messages
    opérateur en français (CLAUDE.md n°12).
.PARAMETER InstanceName
    (EXPORT) Nom de l'instance SOURCE à migrer (telle que créée par new-instance.ps1).
.PARAMETER ApplyBundle
    (APPLY) Chemin du bundle de migration (.zip) produit par la phase EXPORT, à appliquer sur la cible.
.PARAMETER TargetInstanceName
    (APPLY) Nom de l'instance CIBLE (défaut : repris du manifeste de migration du bundle).
.PARAMETER TargetMode
    (EXPORT) Mode d'exécution prévu sur la cible (hosted | self-hosted) — pilote la procédure du bundle.
.PARAMETER RepoRoot
    (APPLY) Racine du dépôt Liakont SUR LA CIBLE (contexte de build de l'image Host). Défaut : le dépôt
    contenant ce script.
.PARAMETER InstancesRoot
    Racine des répertoires d'instance (défaut : deploy/provisioning/instances). Gitignoré.
.PARAMETER BundleDir
    (EXPORT) Dossier où écrire le bundle (.zip) — défaut : InstancesRoot. Gitignoré.
.PARAMETER HealthTimeoutSeconds
    (APPLY) Délai d'attente du démarrage stable du Host cible (défaut 300 s).
.PARAMETER Force
    (APPLY) Autorise l'écrasement d'un répertoire d'instance cible existant ET la restitution du volume
    sur une cible non vierge (DANGEREUX — coffre WORM). À réserver à une reprise contrôlée.
.PARAMETER DryRun
    N'écrit/ne déploie rien : affiche la séquence qui serait exécutée, puis sort en 0.
.EXAMPLE
    # Sur la SOURCE — produire le bundle de migration :
    ./migrate-instance.ps1 -InstanceName acme-prod -TargetMode self-hosted
.EXAMPLE
    # Sur la CIBLE — appliquer le bundle (restauration + santé + bascule DNS) :
    ./migrate-instance.ps1 -ApplyBundle ./instances/acme-prod-migration-20260616T2000Z.zip
.EXAMPLE
    ./migrate-instance.ps1 -InstanceName acme-prod -DryRun
#>
[CmdletBinding(DefaultParameterSetName = 'Export')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Export')][string]$InstanceName,
    [Parameter(ParameterSetName = 'Export')][ValidateSet('hosted', 'self-hosted')][string]$TargetMode = 'self-hosted',
    [Parameter(ParameterSetName = 'Export')][string]$BundleDir,

    [Parameter(Mandatory = $true, ParameterSetName = 'Apply')][string]$ApplyBundle,
    [Parameter(ParameterSetName = 'Apply')][string]$TargetInstanceName,
    [Parameter(ParameterSetName = 'Apply')][string]$RepoRoot,
    [Parameter(ParameterSetName = 'Apply')][string]$RegistryPath,
    [Parameter(ParameterSetName = 'Apply')][int]$HealthTimeoutSeconds = 300,

    [string]$InstancesRoot,
    [switch]$Force,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Les appels natifs autonomes (docker, bash …) ne doivent pas lever sur code non-zéro —
# la gestion explicite via $LASTEXITCODE s'en charge (pwsh 7.4+ : défaut $true).
$PSNativeCommandUseErrorActionPreference = $false

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Import-Module (Join-Path $scriptRoot 'Provisioning.psm1') -Force

$repoRootDefault = Split-Path -Parent (Split-Path -Parent $scriptRoot)
$dockerDir = Join-Path $repoRootDefault 'deploy\docker'
$applianceDir = Join-Path $dockerDir 'appliance'
$backupScript = Join-Path $dockerDir 'backup.sh'
$restoreScript = Join-Path $dockerDir 'restore.sh'
if (-not $InstancesRoot) { $InstancesRoot = Join-Path $scriptRoot 'instances' }

$script:IsWindowsHost = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)

# ── Helpers ────────────────────────────────────────────────────────────────────
function Get-BashExe {
    # La migration réutilise backup.sh / restore.sh (bash, OPS01a/b). L'appliance tourne sous Linux
    # (bash natif) ; en poste Windows, Git Bash fournit bash. Pas de bash → on ABANDONNE clairement
    # plutôt que de réimplémenter une mécanique de dump/restore non testée.
    $bash = Get-Command bash -ErrorAction SilentlyContinue
    if (-not $bash) {
        throw "bash introuvable : la migration réutilise les scripts éprouvés backup.sh / restore.sh " +
              "(OPS01b). Installez bash (Linux : natif ; Windows : Git Bash) puis réessayez."
    }
    return $bash.Source
}

function ConvertTo-BashPath {
    # Linux/macOS : chemin POSIX inchangé. Windows : « C:\x\y » → « C:/x/y » (Git Bash et docker
    # acceptent les chemins à barres obliques avant ; évite la réécriture MSYS des chemins).
    param([string]$Path)
    if ($script:IsWindowsHost) { return ($Path -replace '\\', '/') }
    return $Path
}

function Get-AppVolumeName { param([string]$Project) "${Project}_liakont-app-data" }

function Invoke-BackupScript {
    # Lance backup.sh contre le projet/compose de l'instance SOURCE, vers un dossier de sortie.
    param([string]$Bash, [string]$Project, [string]$ComposeFile, [string]$OutDir)
    $previous = @{
        LIAKONT_PROJECT      = $env:LIAKONT_PROJECT
        LIAKONT_COMPOSE_FILE = $env:LIAKONT_COMPOSE_FILE
        LIAKONT_APP_VOLUME   = $env:LIAKONT_APP_VOLUME
    }
    try {
        $env:LIAKONT_PROJECT = $Project
        $env:LIAKONT_COMPOSE_FILE = (ConvertTo-BashPath $ComposeFile)
        $env:LIAKONT_APP_VOLUME = (Get-AppVolumeName $Project)
        # | Out-Host : la sortie standard du script bash (ex. « sha256sum -c » → « fichier: OK ») est
        # AFFICHÉE mais ne doit PAS polluer la valeur de retour (sinon $code devient un tableau et la
        # garde « -ne 0 » faute) — bug révélé par l'auto-test e2e. (Pas de 2>&1 : risqué en WinPS 5.1.)
        & $Bash (ConvertTo-BashPath $backupScript) -d (ConvertTo-BashPath $OutDir) -k 1 | Out-Host
        return $LASTEXITCODE
    }
    finally {
        $env:LIAKONT_PROJECT = $previous.LIAKONT_PROJECT
        $env:LIAKONT_COMPOSE_FILE = $previous.LIAKONT_COMPOSE_FILE
        $env:LIAKONT_APP_VOLUME = $previous.LIAKONT_APP_VOLUME
    }
}

function Invoke-RestoreScript {
    # Lance restore.sh contre le projet/compose de l'instance CIBLE, depuis un dossier de sauvegarde.
    param([string]$Bash, [string]$Project, [string]$ComposeFile, [string]$BackupSource, [bool]$ForceVolume)
    $previous = @{
        LIAKONT_PROJECT      = $env:LIAKONT_PROJECT
        LIAKONT_COMPOSE_FILE = $env:LIAKONT_COMPOSE_FILE
        LIAKONT_APP_VOLUME   = $env:LIAKONT_APP_VOLUME
    }
    try {
        $env:LIAKONT_PROJECT = $Project
        $env:LIAKONT_COMPOSE_FILE = (ConvertTo-BashPath $ComposeFile)
        $env:LIAKONT_APP_VOLUME = (Get-AppVolumeName $Project)
        $restoreArgs = @((ConvertTo-BashPath $restoreScript), '-s', (ConvertTo-BashPath $BackupSource))
        if ($ForceVolume) { $restoreArgs += '-f' }
        # | Out-Host : voir Invoke-BackupScript — restore.sh écrit la vérification SHA-256 sur stdout ;
        # on l'AFFICHE sans la renvoyer (sinon $code = tableau « OK … 0 » au lieu du seul code de sortie).
        & $Bash @restoreArgs | Out-Host
        return $LASTEXITCODE
    }
    finally {
        $env:LIAKONT_PROJECT = $previous.LIAKONT_PROJECT
        $env:LIAKONT_COMPOSE_FILE = $previous.LIAKONT_COMPOSE_FILE
        $env:LIAKONT_APP_VOLUME = $previous.LIAKONT_APP_VOLUME
    }
}

function Wait-PostgresReady {
    # Attend qu'un service postgres réponde à pg_isready (la restauration crée/charge des bases : le
    # cluster doit accepter les connexions avant restore.sh, sinon faux-rouge prématuré).
    param([string[]]$ComposeArgs, [string]$Service, [string]$User, [int]$TimeoutSeconds = 90)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $probe = $ComposeArgs + @('exec', '-T', $Service, 'pg_isready', '-U', $User)
        & docker @probe *> $null
        if ($LASTEXITCODE -eq 0) { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Read-EnvValue {
    # Lit une valeur d'un fichier .env (clé=valeur), sans interprétation. Renvoie $null si absente.
    param([string]$EnvFile, [string]$Key)
    if (-not (Test-Path -LiteralPath $EnvFile)) { return $null }
    foreach ($line in [System.IO.File]::ReadAllLines($EnvFile)) {
        if ($line -match "^\s*$([regex]::Escape($Key))=(.*)$") { return $Matches[1].Trim() }
    }
    return $null
}

# ════════════════════════════════════════════════════════════════════════════════
# PHASE EXPORT (sur la SOURCE)
# ════════════════════════════════════════════════════════════════════════════════
function Invoke-Export {
    $instance = Resolve-InstanceName -Name $InstanceName
    $instanceDir = Join-Path $InstancesRoot $instance.Name
    $composeFile = Join-Path $instanceDir 'docker-compose.yml'
    $envFile = Join-Path $instanceDir '.env'
    if (-not $BundleDir) { $BundleDir = $InstancesRoot }
    $stamp = [System.DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')

    Write-Step "Migration (EXPORT) de l'instance « $($instance.Name) » (projet « $($instance.ProjectName) »)"

    # Validations HORS Docker d'abord (codes de sortie testables sans Docker).
    if (-not (Test-Path -LiteralPath $instanceDir)) {
        throw "Instance source inconnue : répertoire introuvable ($instanceDir). Créez-la avec " +
              "new-instance.ps1 ou vérifiez -InstancesRoot."
    }
    if (-not (Test-Path -LiteralPath $envFile)) {
        throw "Fichier .env de l'instance introuvable ($envFile) — la migration doit préserver les " +
              "secrets de la source (sinon la base Keycloak restaurée ne correspondrait plus au Host)."
    }
    if (-not (Test-Path -LiteralPath $backupScript)) {
        throw "Script de sauvegarde introuvable ($backupScript) — l'item OPS01b doit être présent."
    }

    if ($DryRun) {
        Write-WarnMsg 'DryRun : aucune action. Séquence qui SERAIT exécutée :'
        Write-Host '    1. backup.sh : pg_dump de TOUTES les bases (système + tenants) + archive du volume (coffre WORM + clés DP) + SHA-256 ;'
        Write-Host "    2. Assemblage du bundle (sauvegarde + config + secrets + MIGRATE-README) pour une cible « $TargetMode » ;"
        Write-Host "    3. Production de $BundleDir\$($instance.Name)-migration-<horodatage>.zip ;"
        Write-Host '    4. Procédure de transfert + APPLY (-ApplyBundle) + bascule DNS affichée.'
        Write-Ok 'DryRun terminé.'
        return 0
    }

    Test-DockerAvailable
    $bash = Get-BashExe

    # ── 1. Dossier de travail du bundle ──
    $work = Join-Path ([System.IO.Path]::GetTempPath()) ("liakont-migrate-" + [System.Guid]::NewGuid().ToString('N'))
    $bundleRoot = Join-Path $work $instance.Name
    $bundleBackup = Join-Path $bundleRoot 'backup'
    $bundleConfig = Join-Path $bundleRoot 'config'
    New-Item -ItemType Directory -Path $bundleBackup -Force | Out-Null
    New-Item -ItemType Directory -Path $bundleConfig -Force | Out-Null
    try {
        # ── 2. Sauvegarde complète via le VRAI backup.sh (mécanique OPS01b déjà testée) ──
        Write-Step '1/4 Sauvegarde complète de la source (backup.sh : toutes bases + volume + SHA-256)'
        $bkStaging = Join-Path $work 'bk'
        $code = Invoke-BackupScript -Bash $bash -Project $instance.ProjectName -ComposeFile $composeFile -OutDir $bkStaging
        if ($code -ne 0) {
            throw "La sauvegarde de la source a échoué (backup.sh, code $code). Migration ANNULÉE — la " +
                  "source est intacte (l'export est en lecture seule)."
        }
        $producedDir = Get-ChildItem -LiteralPath $bkStaging -Directory -Filter '*Z' |
            Sort-Object Name | Select-Object -Last 1
        if (-not $producedDir) {
            throw "Aucune sauvegarde produite par backup.sh (anti-faux-vert). Migration ANNULÉE."
        }
        # -Path (et non -LiteralPath) : le joker « * » doit être EXPANSÉ (LiteralPath le prendrait au pied
        # de la lettre → rien de copié, faux vert que l'auto-test e2e a révélé).
        Copy-Item -Path (Join-Path $producedDir.FullName '*') -Destination $bundleBackup -Recurse -Force
        if (-not (Test-Path -LiteralPath (Join-Path $bundleBackup 'SHA256SUMS'))) {
            throw "Manifeste d'intégrité (SHA256SUMS) absent de la sauvegarde — bundle refusé (anti-faux-vert)."
        }
        # « Bloquer plutôt qu'envoyer faux » : backup.sh saute la base Keycloak avec un simple
        # avertissement si keycloak-db n'est pas « running » à l'instant du dump. Une instance migrée
        # SANS Keycloak = cible sans utilisateurs/realm (connexion impossible). On refuse le bundle.
        if (-not (Test-Path -LiteralPath (Join-Path $bundleBackup 'db-keycloak.dump'))) {
            throw "Dump Keycloak (db-keycloak.dump) absent de la sauvegarde — le service keycloak-db " +
                  "était-il démarré ? Migration ANNULÉE : une cible sans Keycloak est inutilisable " +
                  "(aucun utilisateur/realm). Redémarrez keycloak-db puis relancez l'export."
        }
        $dbDumps = @(Get-ChildItem -LiteralPath $bundleBackup -Filter 'db-*.dump' | ForEach-Object { $_.Name })
        Write-Ok "Sauvegarde : $($dbDumps.Count) base(s) + volume applicatif, intégrité SHA-256 incluse."

        # ── 3. Config + secrets de l'instance (la cible doit démarrer à l'identique) ──
        Write-Step '2/4 Assemblage de la configuration de l''instance (secrets PRÉSERVÉS)'
        Copy-Item -LiteralPath $envFile -Destination (Join-Path $bundleConfig '.env')
        foreach ($f in @('Caddyfile', 'maintenance.Caddyfile')) {
            $src = Join-Path $instanceDir $f
            if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination (Join-Path $bundleConfig $f) }
        }
        $realmSrc = Join-Path $instanceDir 'keycloak\realm-liakont.json'
        if (Test-Path -LiteralPath $realmSrc) {
            New-Item -ItemType Directory -Path (Join-Path $bundleConfig 'keycloak') -Force | Out-Null
            Copy-Item -LiteralPath $realmSrc -Destination (Join-Path $bundleConfig 'keycloak\realm-liakont.json')
        }
        # Le compose de l'instance (s'il existe) est inclus : APPLY réécrira son contexte de build vers
        # le dépôt de la cible. En self-hosted la source n'a pas matérialisé de compose → APPLY repart
        # de l'appliance de la cible.
        if (Test-Path -LiteralPath $composeFile) {
            Copy-Item -LiteralPath $composeFile -Destination (Join-Path $bundleConfig 'docker-compose.yml')
        }

        # ── 4. Manifeste de migration + README (procédure + bascule DNS) ──
        $publicHost = Read-EnvValue -EnvFile $envFile -Key 'PUBLIC_HOSTNAME'
        $kcHost = Read-EnvValue -EnvFile $envFile -Key 'KEYCLOAK_HOSTNAME'
        $manifest = [ordered]@{
            source_instance = $instance.Name
            source_project  = $instance.ProjectName
            target_mode     = $TargetMode
            created_at      = $stamp
            databases       = $dbDumps
            public_hostname = [string]$publicHost
            keycloak_hostname = [string]$kcHost
        }
        $manifestJson = $manifest | ConvertTo-Json -Depth 4
        [System.IO.File]::WriteAllText((Join-Path $bundleRoot 'migration-manifest.json'),
            $manifestJson, (New-Object System.Text.UTF8Encoding($false)))

        $readme = New-MigrateReadme -InstanceName $instance.Name -ProjectName $instance.ProjectName `
            -TargetMode $TargetMode -PublicHost $publicHost -KeycloakHost $kcHost
        [System.IO.File]::WriteAllText((Join-Path $bundleRoot 'MIGRATE-README.md'),
            $readme, (New-Object System.Text.UTF8Encoding($false)))
        Write-Ok 'Configuration + procédure (restauration + bascule DNS) assemblées.'

        # ── 5. Zip du bundle ──
        Write-Step '3/4 Production du bundle de migration (.zip)'
        if (-not (Test-Path -LiteralPath $BundleDir)) { New-Item -ItemType Directory -Path $BundleDir -Force | Out-Null }
        $bundlePath = Join-Path $BundleDir "$($instance.Name)-migration-$stamp.zip"
        if (Test-Path -LiteralPath $bundlePath) { Remove-Item -LiteralPath $bundlePath -Force }
        # NoCompression : le contenu est DÉJÀ compressé (volume-app-data.tar.gz + dumps pg « -Fc ») —
        # le re-déflater gaspille du CPU sans rien gagner. Le .zip n'est qu'un conteneur de transfert.
        # ⚠️ Sur un GROS coffre (rétention 10 ans), Compress-Archive échoue > 2 Go sous Windows
        # PowerShell 5.1 (pas de zip64) : la cible nominale est l'appliance Linux / pwsh 7 (zip64 OK) —
        # voir l'avertissement en tête de fichier et le README.
        Compress-Archive -Path (Join-Path $bundleRoot '*') -DestinationPath $bundlePath -CompressionLevel NoCompression
        Write-Ok "Bundle produit : $bundlePath"

        Write-Step '4/4 Étapes suivantes'
        Write-Host '    Le bundle contient des SECRETS et des DONNÉES FISCALES — transférez-le par un canal sûr,'
        Write-Host '    et supprimez-le après application. La SOURCE reste en service (export non destructif).'
        Write-Host "    Sur la cible :  ./migrate-instance.ps1 -ApplyBundle <bundle>.zip"
        Write-Host '    SANS PERTE : gelez les écritures de la source (maintenance 503) avant l''export, ou prévoyez'
        Write-Host '    un re-export final juste avant la bascule (voir MIGRATE-README.md § Bascule DNS).'
        Write-Host '    La bascule DNS ne se fait qu''APRÈS un contrôle de santé vert sur la cible (voir MIGRATE-README.md).'
        Write-Host ''
        Write-Ok "Export de migration de « $($instance.Name) » terminé."
        return 0
    }
    finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function New-MigrateReadme {
    param([string]$InstanceName, [string]$ProjectName, [string]$TargetMode,
          [string]$PublicHost, [string]$KeycloakHost)
    $ph = if ($PublicHost) { $PublicHost } else { '<hôte-public>' }
    $kh = if ($KeycloakHost) { $KeycloakHost } else { '<hôte-keycloak>' }
    $content = @"
# Bundle de migration d'instance Liakont — $InstanceName

Ce bundle migre l'instance « $InstanceName » (projet « $ProjectName ») vers une nouvelle cible
(mode prévu : **$TargetMode**). Produit par ``migrate-instance.ps1`` (OPS06b, F12 §6.3).

> ⚠️ Ce bundle contient des **secrets** (.env) et des **données fiscales** (dumps + coffre d'archive).
> Transférez-le par un canal sûr et **supprimez-le après application**. Ne le versionnez jamais.

## Contenu

- ``backup/``                — toutes les bases (système + tenants, actifs ET suspendus) + volume
                               applicatif (coffre WORM + clés Data Protection) + ``SHA256SUMS`` + ``manifest.json`` ;
- ``config/``                — ``.env`` (secrets PRÉSERVÉS de la source), ``Caddyfile``, realm Keycloak,
                               ``docker-compose.yml`` (si l'instance source en avait un) ;
- ``migration-manifest.json``— métadonnées (instance, bases, hôtes publics).

## Appliquer sur la cible

Sur la machine CIBLE (dépôt Liakont présent, Docker installé) :

``````
./migrate-instance.ps1 -ApplyBundle ./$InstanceName-migration-<horodatage>.zip
``````

La phase APPLY :
1. matérialise la cible à partir du bundle (secrets préservés → Keycloak/Host cohérents) ;
2. démarre la pile et **vérifie l'intégrité** de la sauvegarde (SHA-256) avant toute restauration ;
3. restaure toutes les bases + le volume (le coffre WORM n'est restitué que sur une cible **vierge**) ;
4. attend un **démarrage stable du Host** (contrôle de santé) ;
5. n'autorise la bascule DNS qu'une fois la santé verte.

## Bascule DNS (cutover)

La SOURCE reste en service tant que le DNS n'est pas basculé — **rollback = ne pas basculer**.

1. **TTL** — AVANT la migration, abaissez le TTL DNS de ``$ph`` et ``$kh`` (ex. 300 s) pour un
   basculement rapide.
2. **GEL des écritures sur la source (sans perte de document)** — l'export est un INSTANTANÉ figé :
   tout document reçu/transmis sur la source APRÈS le dump et AVANT le cutover ne serait PAS dans le
   bundle. Avant d'exporter, **gelez les écritures** de la source (mode maintenance : les push agents
   reçoivent un 503 et **bufferisent** localement, puis re-poussent vers la cible après cutover —
   aucune perte, F12 §3.3). À défaut, planifiez un **re-export final** juste avant la bascule.
   (Le mécanisme 503 est celui d'``update-instance.ps1`` : ``maintenance.Caddyfile`` + ``caddy reload``.)
3. **Export** sur la source (``migrate-instance.ps1 -InstanceName …``), puis **apply** sur la cible
   (``-ApplyBundle …``) et attendez le **contrôle de santé vert**.
4. **Certificats (ACME) avant cutover** — Caddy sur la cible tente d'obtenir les certificats TLS de
   ``$ph`` / ``$kh`` dès son démarrage. Tant que le DNS ne pointe pas encore vers la cible, ces
   tentatives **échouent** et consomment le quota Let's Encrypt « validations échouées ». Pour
   l'éviter : **pré-pointez le DNS vers la cible** avant l'apply (l'ACME réussit, le cutover n'est
   plus qu'une confirmation), ou limitez le nombre d'essais de migration sur un même domaine.
5. **Vérification directe** de la cible (``/etc/hosts`` temporaire ou en interne) : console +
   connexion Keycloak.
6. **Bascule DNS** : pointez ``$ph`` et ``$kh`` vers l'adresse de la **cible**.
7. **Contrôle fiscal** : vérifiez la propagation, puis confirmez que le vérifieur du coffre (TRK06)
   est **VERT** sur la cible (preuve que les archives sont récupérables après migration).
8. **Décommissionnement** : conservez la SOURCE en lecture seule quelques jours, puis mettez-la hors
   service et **retirez son entrée du registre** (OPS06c pour une fin de vie de tenant ; pour une
   instance entière, après confirmation du cutover).
"@
    return ($content -replace "`r`n", "`n")
}

# ════════════════════════════════════════════════════════════════════════════════
# PHASE APPLY (sur la CIBLE)
# ════════════════════════════════════════════════════════════════════════════════
function Invoke-Apply {
    if (-not $RepoRoot) { $RepoRoot = $repoRootDefault }
    if (-not $RegistryPath) { $RegistryPath = Join-Path $scriptRoot 'instances.yaml' }

    Write-Step "Migration (APPLY) depuis le bundle « $ApplyBundle »"

    # Validations HORS Docker d'abord (codes de sortie testables sans Docker).
    if (-not (Test-Path -LiteralPath $ApplyBundle)) {
        throw "Bundle de migration introuvable : $ApplyBundle. Indiquez le .zip produit par la phase EXPORT."
    }
    if (-not (Test-Path -LiteralPath $restoreScript)) {
        throw "Script de restauration introuvable ($restoreScript) — l'item OPS01b doit être présent."
    }

    $work = Join-Path ([System.IO.Path]::GetTempPath()) ("liakont-apply-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    try {
        # ── 1. Extraction + lecture du manifeste ──
        Expand-Archive -LiteralPath $ApplyBundle -DestinationPath $work -Force
        $manifestPath = Join-Path $work 'migration-manifest.json'
        if (-not (Test-Path -LiteralPath $manifestPath)) {
            throw "Bundle invalide : migration-manifest.json absent. Ce .zip n'est pas un bundle de migration OPS06b."
        }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $bundleBackup = Join-Path $work 'backup'
        $bundleConfig = Join-Path $work 'config'
        if (-not (Test-Path -LiteralPath (Join-Path $bundleBackup 'SHA256SUMS'))) {
            throw "Bundle invalide : sauvegarde (backup/SHA256SUMS) absente. Restauration impossible."
        }

        $targetName = if ($TargetInstanceName) { $TargetInstanceName } else { [string]$manifest.source_instance }
        $target = Resolve-InstanceName -Name $targetName
        $instanceDir = Join-Path $InstancesRoot $target.Name
        Write-Host "  Instance cible   : $($target.Name) (projet « $($target.ProjectName) »)"

        if ((Test-Path -LiteralPath $instanceDir) -and -not $Force) {
            $hasContent = @(Get-ChildItem -LiteralPath $instanceDir -Force -ErrorAction SilentlyContinue).Count -gt 0
            if ($hasContent) {
                throw "Le répertoire d'instance cible existe déjà et n'est pas vide ($instanceDir). Choisissez " +
                      "un autre nom (-TargetInstanceName) ou utilisez -Force pour une reprise contrôlée."
            }
        }

        if ($DryRun) {
            Write-WarnMsg 'DryRun : aucune action. Séquence qui SERAIT exécutée :'
            Write-Host "    1. Matérialiser la cible $instanceDir depuis le bundle (config + secrets préservés) ;"
            Write-Host '    2. Démarrer SEULEMENT les bases (postgres + keycloak-db) — PAS le Host (volume vierge) ;'
            Write-Host '    3. restore.sh : vérifier l''intégrité (SHA-256) puis restaurer toutes les bases + le volume ;'
            Write-Host "    4. Démarrer le reste de la pile (Host + Keycloak + Caddy) + contrôle de santé (délai $HealthTimeoutSeconds s) ;"
            Write-Host '    5. Inscrire la cible au registre + procédure de bascule DNS (cutover après santé verte).'
            Write-Ok 'DryRun terminé.'
            return 0
        }

        Test-DockerAvailable
        $bash = Get-BashExe

        # ── 2. Matérialiser la cible (config + secrets PRÉSERVÉS) ──
        Write-Step '1/5 Matérialisation de la cible (config + secrets de la source)'
        New-Item -ItemType Directory -Path $instanceDir -Force | Out-Null
        $bundleEnv = Join-Path $bundleConfig '.env'
        if (-not (Test-Path -LiteralPath $bundleEnv)) {
            throw "Bundle invalide : config/.env absent. Les secrets de la source sont requis pour la cohérence Keycloak/Host."
        }
        Copy-Item -LiteralPath $bundleEnv -Destination (Join-Path $instanceDir '.env') -Force
        foreach ($f in @('Caddyfile', 'maintenance.Caddyfile')) {
            $src = Join-Path $bundleConfig $f
            if (Test-Path -LiteralPath $src) { Copy-DeploymentFileAsLf -Source $src -Destination (Join-Path $instanceDir $f) }
        }
        $realmSrc = Join-Path $bundleConfig 'keycloak\realm-liakont.json'
        if (Test-Path -LiteralPath $realmSrc) {
            New-Item -ItemType Directory -Path (Join-Path $instanceDir 'keycloak') -Force | Out-Null
            Copy-Item -LiteralPath $realmSrc -Destination (Join-Path $instanceDir 'keycloak\realm-liakont.json') -Force
        }
        $composeOut = Join-Path $instanceDir 'docker-compose.yml'
        Set-TargetCompose -BundleConfigDir $bundleConfig -ApplianceDir $applianceDir -RepoRoot $RepoRoot -Destination $composeOut
        Write-Ok 'Cible matérialisée (compose + .env + Caddyfile + realm).'

        $composeArgs = @('compose', '-p', $target.ProjectName, '--project-directory', $instanceDir,
            '-f', $composeOut)

        # ── 3. Démarrer SEULEMENT les bases (PRA §4) — surtout PAS le Host ──
        # Le Host écrit dans le volume DÈS son bootstrap (Directory.CreateDirectory du trousseau
        # Data Protection, AppBootstrap.cs) : le démarrer avant la restauration rendrait le volume
        # NON VIDE → restore.sh refuserait la restitution du coffre WORM (garde anti-écrasement). On
        # démarre donc uniquement postgres + keycloak-db (cibles des pg_restore), volume vierge.
        Write-Step '2/5 Démarrage des bases cible (postgres + keycloak-db ; Host NON démarré)'
        & docker @composeArgs up -d postgres keycloak-db
        if ($LASTEXITCODE -ne 0) { throw "Le démarrage des bases cible a échoué (docker compose up). Voir les logs." }
        # Attendre la disponibilité des DEUX clusters : restore.sh restaure db-*.dump par ordre
        # alphabétique → db-keycloak.dump EN PREMIER. Sur une cible vierge, postgres et keycloak-db
        # lancent initdb en parallèle ; n'attendre que postgres laisserait restore.sh échouer si
        # keycloak-db accepte les connexions un peu plus tard (faux-rouge dépendant du timing).
        if (-not (Wait-PostgresReady -ComposeArgs $composeArgs -Service 'postgres' -User 'liakont')) {
            throw "Le service postgres de la cible n'est pas prêt (pg_isready) — restauration impossible."
        }
        if (-not (Wait-PostgresReady -ComposeArgs $composeArgs -Service 'keycloak-db' -User 'keycloak')) {
            throw "Le service keycloak-db de la cible n'est pas prêt (pg_isready) — restauration impossible."
        }
        Write-Ok 'Bases cible démarrées (Host non démarré → volume vierge pour la restitution du coffre).'

        # ── 4. Restauration (intégrité vérifiée AVANT, via restore.sh — mécanique OPS01b) ──
        Write-Step '3/5 Restauration (restore.sh : vérification SHA-256 + bases + volume)'
        $code = Invoke-RestoreScript -Bash $bash -Project $target.ProjectName -ComposeFile $composeOut `
            -BackupSource $bundleBackup -ForceVolume:([bool]$Force)
        if ($code -ne 0) {
            throw "La restauration sur la cible a échoué (restore.sh, code $code). La SOURCE est intacte — " +
                  "ne basculez PAS le DNS. Diagnostiquez puis relancez l'application du bundle."
        }
        Write-Ok 'Bases + volume restaurés (intégrité SHA-256 vérifiée).'

        # ── 5. Démarrer le RESTE de la pile (Host + Keycloak + Caddy) APRÈS restauration, puis sonder ──
        # Le volume est désormais restauré : le Host bootstrappe sur le trousseau Data Protection et le
        # coffre WORM restitués. Pas de redirection « *> $null » : sous $ErrorActionPreference='Stop' en
        # Windows PowerShell 5.1, rediriger le flux d'erreur d'une commande NATIVE l'emballe en
        # NativeCommandError TERMINANTE (ici la progression « Container … Running » de docker).
        Write-Step "4/5 Démarrage du Host + contrôle de santé (démarrage stable, délai $HealthTimeoutSeconds s)"
        & docker @composeArgs up -d
        # Wait-InstanceHealthy est CONÇU pour RENVOYER un statut (jamais lever) : sa sonde wget renvoie un
        # code non nul sur un 404 ET émet sur stderr, ce qui, sous Windows PowerShell 5.1 +
        # $ErrorActionPreference='Stop' + « 2>&1 », serait emballé en erreur TERMINANTE (sur pwsh 7.4+,
        # $PSNativeCommandUseErrorActionPreference=$false l'évite déjà). « -ErrorAction Continue » pose la
        # préférence À L'INTÉRIEUR de la fonction (une préférence posée dans la fonction APPELANTE ne
        # franchit pas la frontière de module) → comportement identique des deux côtés ; le résultat reste
        # l'objet { Healthy; State; Detail }.
        $health = Wait-InstanceHealthy -ComposeArgs $composeArgs -Service 'liakont' `
            -TimeoutSeconds $HealthTimeoutSeconds -ErrorAction Continue
        if (-not $health.Healthy) {
            & docker @composeArgs stop liakont
            Write-ErrMsg "Contrôle de santé NON confirmé sur la cible : $($health.Detail)"
            Write-Host '  La SOURCE est intacte — NE BASCULEZ PAS le DNS.' -ForegroundColor Yellow
            Write-Host "  Diagnostic : docker compose -p $($target.ProjectName) logs liakont" -ForegroundColor Yellow
            return 1
        }
        Write-Ok "Host cible prêt : $($health.Detail)"

        # ── Registre : inscrire la cible (cohérence avec new-instance/update-instance ; base OPS04) ──
        # created_at OMIS volontairement : Set-InstanceRegistryEntry préserve l'existant (re-migration)
        # et l'origine de l'instance précède cette machine. version/updated_at marquent la migration.
        $targetPublicHost = if ($manifest.PSObject.Properties.Name -contains 'public_hostname' -and $manifest.public_hostname) { [string]$manifest.public_hostname } else { '' }
        $nowIso = [System.DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        Set-InstanceRegistryEntry -Path $RegistryPath -Entry @{
            name       = $target.Name
            project    = $target.ProjectName
            url        = if ($targetPublicHost) { "https://$targetPublicHost" } else { '' }
            hosting    = [string]$manifest.target_mode
            version    = "migrated-$nowIso"
            updated_at = $nowIso
        }
        Write-Ok "Cible inscrite au registre ($RegistryPath). L'entrée SOURCE est à retirer au décommissionnement."

        # ── 6. Bascule DNS documentée ──
        Write-Step '5/5 Bascule DNS (cutover)'
        $ph = if ($manifest.PSObject.Properties.Name -contains 'public_hostname' -and $manifest.public_hostname) { $manifest.public_hostname } else { '<hôte-public>' }
        $kh = if ($manifest.PSObject.Properties.Name -contains 'keycloak_hostname' -and $manifest.keycloak_hostname) { $manifest.keycloak_hostname } else { '<hôte-keycloak>' }
        Write-Host "    1. Vérifiez la cible en direct (hosts/interne) : console + connexion Keycloak."
        Write-Host "    2. Basculez le DNS « $ph » et « $kh » vers l'adresse de CETTE cible."
        Write-Host '    3. Confirmez le contrôle fiscal : vérifieur du coffre (TRK06) VERT sur la cible.'
        Write-Host '    4. Conservez la source en lecture seule, puis mettez-la hors service après cutover confirmé.'
        Write-Host ''
        Write-Ok "Migration de « $($target.Name) » appliquée — santé verte. Procédez à la bascule DNS."
        return 0
    }
    finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Set-TargetCompose {
    # Écrit le docker-compose.yml de la cible. Préfère le compose du bundle ; à défaut, repart de
    # l'appliance de la cible. Si un contexte de build est présent, il est réécrit vers le dépôt de la
    # CIBLE (le chemin absolu de la source n'existe pas ici). Un compose à base d'image (sans build)
    # est repris tel quel. LF forcé (consommé côté Linux).
    param([string]$BundleConfigDir, [string]$ApplianceDir, [string]$RepoRoot, [string]$Destination)
    $bundleCompose = Join-Path $BundleConfigDir 'docker-compose.yml'
    if (Test-Path -LiteralPath $bundleCompose) {
        $content = [System.IO.File]::ReadAllText($bundleCompose)
    }
    else {
        $applianceCompose = Join-Path $ApplianceDir 'docker-compose.yml'
        if (-not (Test-Path -LiteralPath $applianceCompose)) {
            throw "Aucun docker-compose.yml dans le bundle ni dans l'appliance cible ($applianceCompose) — " +
                  "impossible de matérialiser la pile. Vérifiez le dépôt de la cible (-RepoRoot)."
        }
        $content = [System.IO.File]::ReadAllText($applianceCompose)
    }
    # Réécriture du contexte de build vers le dépôt de la cible (uniquement si un contexte existe).
    $absContext = ($RepoRoot -replace '\\', '/')
    $content = [regex]::Replace($content, '(?m)^(\s*context:\s*).+$', "`${1}$absContext")
    $content = $content -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText($Destination, $content, (New-Object System.Text.UTF8Encoding($false)))
}

# ── Entrée ──────────────────────────────────────────────────────────────────────
try {
    if ($PSCmdlet.ParameterSetName -eq 'Apply') {
        exit (Invoke-Apply)
    }
    else {
        exit (Invoke-Export)
    }
}
catch {
    Write-ErrMsg $_.Exception.Message
    exit 1
}
