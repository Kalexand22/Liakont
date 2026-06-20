<#
.SYNOPSIS
    Installe une instance de l'agent Liakont sur le poste (OPS05).
.DESCRIPTION
    Script d'installation embarqué dans le package produit par tools/package-agent.ps1. Il pose
    une INSTANCE de l'agent (multi-instances, OPS05 pt 5) : copie des binaires, création du
    répertoire de données %ProgramData%\Liakont\<instance> avec les ACL requises (le service ET
    le CLI intégrateur y écrivent — fichiers SQLite -wal/-shm), provisionnement optionnel de la
    pré-configuration (agent.json pré-rempli, clé API jamais en clair) et de la clé publique de
    signature d'auto-update, puis enregistrement du service Windows.

    L'instance « Default » conserve les noms et chemins historiques (compat mono-instance déjà
    déployé). Une instance nommée vit dans son propre service « LiakontAgent$<nom> », son propre
    répertoire de données et son propre verrou de run — N instances coexistent sans interférence
    sur une même machine (cas serveur SaaS mutualisé, une base cliente par instance).

    INSTALLATION SILENCIEUSE (ADR-0019) : tous les paramètres sont passables en ligne de commande ;
    avec -Silent, aucune invite. Exemple intégrateur :
        powershell -ExecutionPolicy Bypass -File install-agent.ps1 -InstanceName ClientA -Silent

    Messages opérateur en français (CLAUDE.md n°12).
.PARAMETER InstanceName
    Nom de l'instance (défaut « Default »). Lettres/chiffres/'-'/'_', 32 caractères max.
.PARAMETER InstallRoot
    Racine d'installation des binaires (défaut « %ProgramFiles%\Liakont\Agent »). Les binaires de
    l'instance sont posés sous <InstallRoot>\<instance>.
.PARAMETER IntegratorAccount
    Compte ou groupe DÉDIÉ (nom ou SID) recevant le droit Modify sur le répertoire de données, en plus
    du service (SYSTEM) et des Administrateurs. À fournir quand le CLI de diagnostic est lancé par un
    compte NON administrateur (il doit pouvoir écrire la file SQLite partagée avec le service). Par
    défaut VIDE : moindre privilège — seuls SYSTEM et les Administrateurs accèdent au tampon de
    données fiscales (aucun droit accordé au groupe « Utilisateurs », important sur un hôte mutualisé).
.PARAMETER PreConfigPassword
    Mot de passe à usage unique (communiqué SÉPARÉMENT) déchiffrant la clé API d'un package
    pré-configuré. Requis seulement si le package contient preconfig.json.
.PARAMETER Silent
    Aucune invite interactive (installation automatisée).
.PARAMETER DryRun
    Valide les paramètres, résout les chemins et affiche le plan d'installation SANS rien modifier
    (aucune copie, aucune ACL, aucun service). Ne requiert pas les droits administrateur.
#>
[CmdletBinding()]
param(
    [string]$InstanceName = 'Default',
    [string]$InstallRoot = (Join-Path $env:ProgramFiles 'Liakont\Agent'),
    [string]$IntegratorAccount = '',
    [string]$PreConfigPassword,
    [switch]$Silent,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Import-Module (Join-Path $scriptRoot 'AgentInstall.psm1') -Force

function Write-Step([string]$Message) { Write-Host "  $Message" }

try {
    # ── 1. Résolution de l'instance (validation fail-fast, dérivations canoniques) ──
    $instance = Resolve-AgentInstance -Name $InstanceName
    Write-Host "Installation de l'agent Liakont — instance « $($instance.Name) »" -ForegroundColor Cyan
    Write-Step "Service Windows  : $($instance.ServiceName)"
    Write-Step "Données          : $($instance.DataDirectory)"

    $installDir = Join-Path $InstallRoot $instance.Name
    Write-Step "Binaires         : $installDir"

    # ── 2. Localisation des binaires dans le package ──
    $binSource = Join-Path $scriptRoot 'bin'
    if (-not (Test-Path -LiteralPath $binSource)) {
        throw "Binaires introuvables : « $binSource ». Lancez ce script depuis le package extrait " +
              "(le dossier doit contenir bin\, install-agent.ps1 et AgentInstall.psm1)."
    }
    $serviceExe = Join-Path $binSource 'Liakont.Agent.exe'
    $cliExe = Join-Path $binSource 'Liakont.Agent.Cli.exe'
    foreach ($exe in @($serviceExe, $cliExe)) {
        if (-not (Test-Path -LiteralPath $exe)) {
            throw "Binaire manquant dans le package : « $exe ». Package incomplet ou corrompu."
        }
    }

    # ── 3. Pré-configuration éventuelle (lecture seule à ce stade) ──
    $preconfigPath = Join-Path $scriptRoot 'preconfig.json'
    $hasPreconfig = Test-Path -LiteralPath $preconfigPath
    if ($hasPreconfig) {
        # Le mot de passe à usage unique n'est demandé qu'au moment du déchiffrement (étape 7),
        # JAMAIS en -DryRun (qui ne déchiffre ni n'écrit rien) : un -DryRun -Silent reste possible.
        Write-Step "Pré-configuration: preconfig.json présent (agent.json sera généré)"
    }

    $pubkeyPath = Join-Path $scriptRoot 'update-signing.pubkey.xml'
    $hasPubkey = Test-Path -LiteralPath $pubkeyPath

    if ($DryRun) {
        Write-Host ""
        Write-Host "[DryRun] Plan validé — aucune modification effectuée." -ForegroundColor Yellow
        Write-Step "Copierait les binaires vers : $installDir"
        $aclWho = if ($IntegratorAccount) { "SYSTEM + Administrateurs + « $IntegratorAccount »" } else { 'SYSTEM + Administrateurs' }
        Write-Step "Créerait/ACL le répertoire  : $($instance.DataDirectory) ($aclWho)"
        if ($hasPreconfig) { Write-Step "Génèrerait agent.json depuis preconfig.json (clé API DPAPI)" }
        if ($hasPubkey) {
            # Validation read-only (RDF14) : surface une clé trop courte/invalide dès le -DryRun, sans
            # rien écrire. Une clé < 2048 bits échoue le plan plutôt que d'être posée inutilement.
            $pubkeyBits = Test-UpdateSigningPublicKey -Path $pubkeyPath
            Write-Step "Provisionnerait update-signing.pubkey.xml ($pubkeyBits bits, >= 2048 requis)"
        }
        Write-Step "Enregistrerait le service   : $($instance.ServiceName)"
        exit 0
    }

    # ── 4. Contrôle d'élévation (création de service + ACL = administrateur) ──
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Droits administrateur requis (création du service Windows et ACL). Relancez cette " +
              "console « en tant qu'administrateur »."
    }

    # ── 5. Copie des binaires ──
    Write-Host "Copie des binaires..." -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $installDir)) {
        New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    }
    # Énumération explicite des enfants : « Copy-Item src\* -Recurse » vers un dossier existant
    # n'y dépose que les sous-dossiers (quirk PowerShell) — on copie fichiers ET dossiers.
    Get-ChildItem -LiteralPath $binSource | Copy-Item -Destination $installDir -Recurse -Force

    $installedService = Join-Path $installDir 'Liakont.Agent.exe'
    $installedCli = Join-Path $installDir 'Liakont.Agent.Cli.exe'

    # ── 6. Répertoire de données + ACL (service ET intégrateur écrivent la file SQLite) ──
    Write-Host "Préparation du répertoire de données et des ACL..." -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $instance.DataDirectory)) {
        New-Item -ItemType Directory -Path $instance.DataDirectory -Force | Out-Null
    }
    # Moindre privilège sur le tampon de données fiscales : on RETIRE l'héritage (la racine
    # %ProgramData% accorde un droit d'écriture au groupe « Utilisateurs ») et on n'accorde
    # explicitement qu'à SYSTEM (le service, LocalSystem) et aux Administrateurs (gestion + CLI lancé
    # en administrateur). Sur un hôte mutualisé, aucun utilisateur local non privilégié n'accède au
    # tampon d'un autre tenant. SID littéraux (préfixe *) = indépendants de la langue de Windows.
    & icacls "$($instance.DataDirectory)" /inheritance:r /grant "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Échec de la pose des ACL sur « $($instance.DataDirectory) » (icacls a renvoyé $LASTEXITCODE)."
    }
    # Compte intégrateur DÉDIÉ (CLI lancé par un compte non administrateur) : droit Modify ciblé.
    if ($IntegratorAccount) {
        & icacls "$($instance.DataDirectory)" /grant "${IntegratorAccount}:(OI)(CI)M" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Échec de la pose des ACL pour « $IntegratorAccount » sur « $($instance.DataDirectory) » (icacls a renvoyé $LASTEXITCODE)."
        }
    }

    # ── 7. Pré-configuration : génération d'agent.json (clé API jamais en clair) ──
    if ($hasPreconfig) {
        Write-Host "Génération de la configuration pré-remplie..." -ForegroundColor Cyan
        if (-not $PreConfigPassword) {
            if ($Silent) {
                throw "Le package est pré-configuré mais aucun -PreConfigPassword n'a été fourni en " +
                      "mode silencieux. Indiquez le mot de passe à usage unique communiqué séparément."
            }
            $secure = Read-Host "Mot de passe à usage unique de pré-configuration" -AsSecureString
            $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
            try { $PreConfigPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
            finally { [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
        }
        $preconfig = Get-Content -LiteralPath $preconfigPath -Raw | ConvertFrom-Json
        $plainApiKey = Unprotect-AgentPreConfigSecret -Secret $preconfig.apiKeySecret -Password $PreConfigPassword

        # Re-chiffrement DPAPI via le CLI (source de vérité du schéma DPAPI machine de l'agent) :
        # le secret transite par l'entrée standard, jamais par les arguments (process list / historique).
        $encryptOutput = $plainApiKey | & $installedCli encrypt
        $plainApiKey = $null
        $apiKeyProtected = ($encryptOutput | Where-Object { $_ -match '^[A-Za-z0-9+/=]{20,}$' } | Select-Object -Last 1)
        if (-not $apiKeyProtected) {
            throw "Le chiffrement DPAPI de la clé API a échoué (sortie inattendue de « encrypt »). " +
                  "Configuration non écrite."
        }

        $agentConfig = [ordered]@{
            platformUrl      = [string]$preconfig.platformUrl
            apiKey           = [string]$apiKeyProtected
            heartbeatMinutes = if ($preconfig.PSObject.Properties['heartbeatMinutes']) { [int]$preconfig.heartbeatMinutes } else { 15 }
            extraction       = [ordered]@{
                adapter = [string]$preconfig.adapter
            }
        }
        if ($preconfig.PSObject.Properties['schedule'] -and $preconfig.schedule) {
            $agentConfig.extraction['schedule'] = @($preconfig.schedule)
        }

        $configPath = Join-Path $instance.DataDirectory 'agent.json'
        ($agentConfig | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $configPath -Encoding UTF8
        Write-Step "agent.json écrit : $configPath (clé API chiffrée DPAPI)"
    }

    # ── 8. Clé publique de signature d'auto-update (ADR-0013 : fail-closed sans clé) ──
    if ($hasPubkey) {
        # Plancher de taille de clé (RDF14, RL-UPD-1) : refuser EXPLICITEMENT une clé < 2048 bits au
        # provisionnement — sinon l'agent la chargerait puis refuserait silencieusement toute mise à
        # jour (le vérificateur la traite comme « pas de clé »). Échec fail-fast AVANT la copie.
        $pubkeyBits = Test-UpdateSigningPublicKey -Path $pubkeyPath
        Copy-Item -LiteralPath $pubkeyPath -Destination (Join-Path $instance.DataDirectory 'update-signing.pubkey.xml') -Force
        Write-Step "Clé publique de signature provisionnée (auto-update activé, $pubkeyBits bits)"
    }

    # ── 9. Enregistrement du service Windows pour CETTE instance ──
    Write-Host "Enregistrement du service Windows..." -ForegroundColor Cyan
    if ($instance.IsDefault) {
        & $installedService install
    }
    else {
        & $installedService install --instance $instance.Name
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Échec de l'enregistrement du service « $($instance.ServiceName) » (code $LASTEXITCODE)."
    }

    Write-Host ""
    Write-Host "Installation terminée — instance « $($instance.Name) »." -ForegroundColor Green
    Write-Step "Démarrez le service : sc start `"$($instance.ServiceName)`""
    Write-Step "Vérifiez la config  : `"$installedCli`" check-config"
    if (-not $instance.IsDefault) {
        Write-Step "(Le CLI cible cette instance avec : --instance $($instance.Name))"
    }
    exit 0
}
catch {
    Write-Host ""
    Write-Host "Échec de l'installation : $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
