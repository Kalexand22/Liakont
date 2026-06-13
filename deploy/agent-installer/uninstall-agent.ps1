<#
.SYNOPSIS
    Désinstalle une instance de l'agent Liakont (OPS05).
.DESCRIPTION
    Cible UNE instance précise : arrêt et suppression de son service Windows, retrait de ses
    binaires, et — sur demande explicite — suppression de son répertoire de données. Les AUTRES
    instances installées sur la même machine ne sont jamais touchées.

    GARDE DONNÉES : par défaut, le répertoire de données (file locale, journaux, configuration)
    est CONSERVÉ (une réinstallation reprend la file en attente). Avec -RemoveData, seules les
    données de CETTE instance sont supprimées ; pour l'instance « Default » — dont le répertoire de
    données EST la racine partagée %ProgramData%\Liakont qui héberge aussi les instances nommées —
    seuls les fichiers propres à Default sont retirés, jamais les sous-dossiers des instances nommées.

    Messages opérateur en français (CLAUDE.md n°12).
.PARAMETER InstanceName
    Nom de l'instance à désinstaller (défaut « Default »).
.PARAMETER InstallRoot
    Racine d'installation des binaires (défaut « %ProgramFiles%\Liakont\Agent »).
.PARAMETER RemoveData
    Supprime aussi le répertoire de données de l'instance (file locale, journaux, configuration).
    IRRÉVERSIBLE. Sans ce commutateur, les données sont conservées.
.PARAMETER DryRun
    Affiche le plan de désinstallation SANS rien modifier (ne requiert pas les droits administrateur).
#>
[CmdletBinding()]
param(
    [string]$InstanceName = 'Default',
    [string]$InstallRoot = (Join-Path $env:ProgramFiles 'Liakont\Agent'),
    [switch]$RemoveData,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Import-Module (Join-Path $scriptRoot 'AgentInstall.psm1') -Force

function Write-Step([string]$Message) { Write-Host "  $Message" }

# Artefacts propres à l'instance Default vivant à la racine partagée %ProgramData%\Liakont :
# avec -RemoveData sur Default, on ne retire QUE ceux-ci (jamais les sous-dossiers d'instances nommées).
$script:DefaultOwnedArtifacts = @(
    'agent.json', 'agent-queue.db', 'agent-queue.db-wal', 'agent-queue.db-shm',
    'update-signing.pubkey.xml', 'update-status.json', 'heartbeat.marker',
    'logs', 'update-work'
)

try {
    $instance = Resolve-AgentInstance -Name $InstanceName
    $installDir = Join-Path $InstallRoot $instance.Name

    Write-Host "Désinstallation de l'agent Liakont — instance « $($instance.Name) »" -ForegroundColor Cyan
    Write-Step "Service Windows  : $($instance.ServiceName)"
    Write-Step "Binaires         : $installDir"
    Write-Step "Données          : $($instance.DataDirectory)$(if ($RemoveData) { ' (SUPPRESSION demandée)' } else { ' (conservées)' })"

    if ($DryRun) {
        Write-Host ""
        Write-Host "[DryRun] Plan validé — aucune modification effectuée." -ForegroundColor Yellow
        Write-Step "Arrêterait et supprimerait le service : $($instance.ServiceName)"
        Write-Step "Supprimerait les binaires             : $installDir"
        if ($RemoveData) {
            if ($instance.IsDefault) {
                Write-Step "Supprimerait UNIQUEMENT les artefacts Default (pas les instances nommées)"
            }
            else {
                Write-Step "Supprimerait le répertoire de données : $($instance.DataDirectory)"
            }
        }
        exit 0
    }

    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Droits administrateur requis (suppression du service). Relancez « en tant qu'administrateur »."
    }

    # ── 1. Arrêt + suppression du service ──
    $svc = Get-Service -Name $instance.ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.Status -ne 'Stopped') {
            Write-Host "Arrêt du service..." -ForegroundColor Cyan
            Stop-Service -Name $instance.ServiceName -Force -ErrorAction SilentlyContinue
        }
        $installedService = Join-Path $installDir 'Liakont.Agent.exe'
        if (Test-Path -LiteralPath $installedService) {
            Write-Host "Suppression du service..." -ForegroundColor Cyan
            if ($instance.IsDefault) { & $installedService uninstall } else { & $installedService uninstall --instance $instance.Name }
            if ($LASTEXITCODE -ne 0) {
                throw "Échec de la suppression du service « $($instance.ServiceName) » (code $LASTEXITCODE)."
            }
        }
        else {
            # Binaire absent : retrait direct via sc.exe (le service existe sans son exe d'installation).
            & sc.exe delete "$($instance.ServiceName)" | Out-Null
        }
    }
    else {
        Write-Step "Service déjà absent — rien à supprimer côté SCM."
    }

    # ── 2. Suppression des binaires de l'instance ──
    if (Test-Path -LiteralPath $installDir) {
        Remove-Item -LiteralPath $installDir -Recurse -Force
        Write-Step "Binaires supprimés."
    }

    # ── 3. Données (sur demande explicite uniquement) ──
    if ($RemoveData -and (Test-Path -LiteralPath $instance.DataDirectory)) {
        Write-Host "Suppression des données..." -ForegroundColor Cyan
        if ($instance.IsDefault) {
            # Default partage sa racine avec les instances nommées : ne retirer que ses artefacts.
            foreach ($artifact in $script:DefaultOwnedArtifacts) {
                $target = Join-Path $instance.DataDirectory $artifact
                if (Test-Path -LiteralPath $target) {
                    Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
                }
            }
            Write-Step "Artefacts Default supprimés (instances nommées préservées)."
        }
        else {
            Remove-Item -LiteralPath $instance.DataDirectory -Recurse -Force
            Write-Step "Répertoire de données supprimé."
        }
    }

    Write-Host ""
    Write-Host "Désinstallation terminée — instance « $($instance.Name) »." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ""
    Write-Host "Échec de la désinstallation : $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
