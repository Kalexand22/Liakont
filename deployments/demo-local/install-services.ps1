#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Installe (et démarre) les 2 vrais services Windows agent de démonstration : LiakontAgent$DemoErpA
    et LiakontAgent$DemoErpB. À lancer dans une console ÉLEVÉE (administrateur).
.DESCRIPTION
    Les agent.json des 2 instances doivent déjà exister sous C:\ProgramData\Liakont\<instance>\
    (générés par la mise en place de la démo). Chaque service lit son agent.json, se connecte à sa
    base SQL Server via le login lecture seule (UID/PWD chiffré DPAPI — fonctionne même en LocalSystem),
    extrait toutes les minutes et pousse vers la plateforme. Idempotent : ré-extraire ne re-pousse pas.
.PARAMETER Uninstall
    Désinstalle les 2 services au lieu de les installer.
.EXAMPLE
    # Console PowerShell en tant qu'administrateur :
    powershell -ExecutionPolicy Bypass -File deployments/demo-local/install-services.ps1
#>
param(
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $admin) {
    Write-Error "Ce script doit être lancé dans une console ÉLEVÉE (administrateur)."
    exit 1
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$agentExe = Join-Path $repoRoot 'agent\src\Liakont.Agent\bin\x64\Release\net48\Liakont.Agent.exe'
if (-not (Test-Path $agentExe)) {
    Write-Error "Binaire du service introuvable : $agentExe. Construisez d'abord l'agent (Release x64)."
    exit 1
}

$instances = @('DemoErpA', 'DemoErpB')
foreach ($name in $instances) {
    $svc = "LiakontAgent`$$name"
    if ($Uninstall) {
        Write-Host "== Désinstallation $svc ==" -ForegroundColor Cyan
        & sc.exe stop $svc 2>$null | Out-Null
        & $agentExe uninstall --instance $name
    }
    else {
        $cfg = "C:\ProgramData\Liakont\$name\agent.json"
        if (-not (Test-Path $cfg)) {
            Write-Warning "agent.json absent pour $name ($cfg) — installez la config de démo d'abord. Ignoré."
            continue
        }
        Write-Host "== Installation $svc ==" -ForegroundColor Cyan
        & $agentExe install --instance $name
        # NB : Write-WARNING (et non Write-Error) car $ErrorActionPreference='Stop' rendrait Write-Error
        # terminant → il avorterait tout le script ; on veut ignorer CETTE instance et continuer.
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Installation de $svc échouée (code $LASTEXITCODE). Instance ignorée."
            continue
        }
        Write-Host "Démarrage $svc…"
        & sc.exe start $svc | Out-Null
        # 1056 = service déjà en cours (bénin). Tout autre code non nul : ignorer l'instance, continuer.
        if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 1056) {
            Write-Warning "Démarrage de $svc échoué (code $LASTEXITCODE) — vérifiez agent.json et le journal de l'agent."
            continue
        }
        # Anti faux-vert : confirmer que le service atteint réellement l'état Running.
        $svcObj = Get-Service -Name $svc -ErrorAction SilentlyContinue
        if ($svcObj) {
            try { $svcObj.WaitForStatus('Running', '00:00:15') }
            catch { Write-Warning "$svc installé mais n'a pas atteint l'état Running : $($_.Exception.Message)" }
        }
    }
}

Write-Host ""
Write-Host "== État des services ==" -ForegroundColor Cyan
Get-Service -Name 'LiakontAgent*' -ErrorAction SilentlyContinue | Select-Object Name, Status, StartType | Format-Table -AutoSize
