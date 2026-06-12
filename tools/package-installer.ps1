#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fabrique un installateur GUI de l'agent PAR PROFIL intégrateur (OPS08c, F13 §7).
.DESCRIPTION
    « Générique par intégrateur » : à partir du MÊME binaire d'installeur (Liakont.Agent.Installer.exe,
    OPS08a/b) et de N profils intégrateur (branding + visibilité par champ), produit N paquets
    d'installation auto-suffisants, chacun avec son profil EMBARQUÉ comme ressource Win32 RT_RCDATA — sans
    recompiler par intégrateur (« du même binaire produit », F13 §7).

    RÉUTILISE OPS05 (tools/package-agent.ps1) pour la charge utile de l'agent (service + CLI + updater +
    natifs SQLite + scripts d'installation). Chaque paquet = charge OPS05 + Liakont.Agent.Installer.exe
    (profil injecté) placé dans bin\ à côté de Liakont.Agent.exe : le déployeur (OPS08b) y trouve le
    service à installer (paquet auto-suffisant).

    GARDE ANTI-FAUX-VERT (F13 §5.3 ; lessons 2026-06-02) :
      - chaque profil est VALIDÉ (Liakont.Agent.Installer.exe --validate) AVANT tout embarquement ; un
        profil invalide FAIT ÉCHOUER le build — jamais de masquage silencieux d'un profil cassé ;
      - après injection, l'embarquement est VÉRIFIÉ par round-trip : --show-profile DOIT relire le profil ;
      - le bitness de l'installeur embarqué est contrôlé (mêmes contrôles PE qu'OPS05).

    Le profil ne porte QUE du branding + de la visibilité — aucun secret, aucune donnée client
    (F13 §5.5, CLAUDE.md n°7) ; clé API et identifiants ODBC sont saisis au wizard puis chiffrés DPAPI.

    Exit 0 = tous les installateurs produits ET vérifiés ; non-zéro = au moins un échec.
.PARAMETER ProfilePath
    Un ou plusieurs chemins de manifestes de profil (JSON). Cumulable avec -ProfilesDirectory.
.PARAMETER ProfilesDirectory
    Répertoire dont tous les *.json sont des profils intégrateur.
.PARAMETER Platform
    x86, x64 ou both (défaut both) — même règle de bitness ODBC qu'OPS05.
.PARAMETER Configuration
    Configuration de build (défaut Release).
.PARAMETER OutputDirectory
    Répertoire de sortie (défaut <repo>\artifacts\agent-installers).
.PARAMETER Version
    Version (défaut : version résolue par OPS05 depuis l'assembly).
.PARAMETER SkipBuild
    Réutilise la sortie de build existante (itération rapide / self-test ; ne pas utiliser pour une release).
.PARAMETER NoZip
    Laisse chaque paquet en dossier (pas de .zip) — inspection / test.
#>
[CmdletBinding()]
param(
    [string[]]$ProfilePath,

    [string]$ProfilesDirectory,

    [ValidateSet('x86', 'x64', 'both')]
    [string]$Platform = 'both',

    [string]$Configuration = 'Release',

    [string]$OutputDirectory,

    [string]$Version,

    [switch]$SkipBuild,

    [switch]$NoZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$packageAgent = Join-Path $PSScriptRoot 'package-agent.ps1'
$installerProjDir = Join-Path $repoRoot 'agent\src\Liakont.Agent.Installer'
Import-Module (Join-Path $repoRoot 'deploy\agent-installer\AgentInstall.psm1') -Force

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\agent-installers' }
if ($Platform -eq 'both') { $platforms = @('x86', 'x64') } else { $platforms = @($Platform) }

# Nom de la ressource RT_RCDATA — DOIT être identique à EmbeddedProfile.ResourceName (C#). Stockée en
# MAJUSCULES par le chargeur de ressources Windows ; on l'écrit déjà en majuscules pour l'appariement.
$script:ProfileResourceName = 'LIAKONTPROFILE'

function Initialize-ResourceEmbedder {
    # Injecteur de ressource Win32 (BeginUpdateResource/UpdateResource/EndUpdateResource). Compilé une
    # seule fois par session PowerShell. RT_RCDATA = 10, langue neutre = 0 (apparié au lecteur C#).
    if ('Liakont.ResourceEmbedder' -as [type]) { return }

    $code = @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Liakont
{
    public static class ResourceEmbedder
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr BeginUpdateResourceW(string pFileName, bool bDeleteExistingResources);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool UpdateResourceW(IntPtr hUpdate, IntPtr lpType, string lpName, ushort wLanguage, byte[] lpData, uint cb);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool EndUpdateResourceW(IntPtr hUpdate, bool fDiscard);

        public static void Embed(string exePath, string resourceName, byte[] data)
        {
            IntPtr handle = BeginUpdateResourceW(exePath, false);
            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "BeginUpdateResource a echoue pour " + exePath);
            }

            // RT_RCDATA = 10 (entier MAKEINTRESOURCE), langue neutre = 0.
            if (!UpdateResourceW(handle, new IntPtr(10), resourceName, 0, data, (uint)data.Length))
            {
                int err = Marshal.GetLastWin32Error();
                EndUpdateResourceW(handle, true);
                throw new Win32Exception(err, "UpdateResource a echoue pour " + exePath);
            }

            if (!EndUpdateResourceW(handle, false))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "EndUpdateResource a echoue pour " + exePath);
            }
        }
    }
}
'@
    Add-Type -TypeDefinition $code
}

function Resolve-Profiles {
    $list = New-Object System.Collections.Generic.List[string]
    if ($ProfilePath) {
        foreach ($p in $ProfilePath) {
            if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { throw "Profil introuvable : « $p »." }
            $list.Add((Resolve-Path -LiteralPath $p).Path)
        }
    }
    if ($ProfilesDirectory) {
        if (-not (Test-Path -LiteralPath $ProfilesDirectory -PathType Container)) {
            throw "Répertoire de profils introuvable : « $ProfilesDirectory »."
        }
        foreach ($f in Get-ChildItem -LiteralPath $ProfilesDirectory -Filter '*.json' -File) { $list.Add($f.FullName) }
    }
    if ($list.Count -eq 0) {
        throw "Aucun profil fourni (-ProfilePath et/ou -ProfilesDirectory). Un build sans aucun profil est un faux vert."
    }
    $dupes = $list | Group-Object { [System.IO.Path]::GetFileNameWithoutExtension($_).ToLowerInvariant() } | Where-Object { $_.Count -gt 1 }
    if ($dupes) {
        $details = ($dupes | ForEach-Object { "  - « $($_.Name) » : " + ($_.Group -join ', ') }) -join "`n"
        throw "Noms de profil en collision (même nom de fichier → même paquet, un installateur serait écrasé silencieusement) :`n$details"
    }
    return $list
}

function Get-InstallerExe {
    param([string]$Plat)
    $exe = Join-Path $installerProjDir ("bin\$Plat\$Configuration\net48\Liakont.Agent.Installer.exe")
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "Installeur introuvable : « $exe ». Construisez la solution agent (sans -SkipBuild) ou vérifiez Platform/Configuration."
    }
    return (Resolve-Path -LiteralPath $exe).Path
}

function Invoke-AgentPackaging {
    param([string]$AgentOut)
    # Réutilise OPS05 en PROCESSUS SÉPARÉ (son `exit` n'interrompt pas ce script) : il construit la
    # solution agent (l'installeur compris) puis assemble la charge utile de l'agent en dossiers (-NoZip).
    $psExe = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell' }
    $agentArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $packageAgent,
        '-NoZip', '-OutputDirectory', $AgentOut, '-Platform', $Platform, '-Configuration', $Configuration)
    if ($SkipBuild) { $agentArgs += '-SkipBuild' }
    if ($Version) { $agentArgs += @('-Version', $Version) }

    Write-Host "Réutilisation du packaging agent (OPS05)..." -ForegroundColor Cyan
    & $psExe @agentArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Le packaging de l'agent (OPS05) a échoué (code $LASTEXITCODE). Voir la sortie ci-dessus."
    }
}

function Get-AgentPackageDir {
    param([string]$AgentOut, [string]$Plat)
    $dir = Get-ChildItem -LiteralPath $AgentOut -Directory -Filter "Liakont.Agent-*-$Plat" | Select-Object -First 1
    if (-not $dir) {
        throw "Paquet agent (OPS05) introuvable pour « $Plat » dans « $AgentOut »."
    }
    return $dir.FullName
}

function Invoke-Installer {
    # L'installeur est un exécutable GUI (sous-système Windows) : son stdout n'est collecté de façon
    # SYNCHRONE que via un pipeline (| Out-String force PowerShell à attendre la fin du processus et à
    # ramasser la sortie). Un simple « $x = & exe » peut rendre la main avant la fin pour un exe GUI.
    param([string]$ExePath, [string[]]$ExeArgs)
    $text = & $ExePath @ExeArgs 2>&1 | Out-String
    return [PSCustomObject]@{ ExitCode = $LASTEXITCODE; Output = $text }
}

function Test-ProfileValid {
    param([string]$ValidatorExe, [string]$Profile)
    return Invoke-Installer -ExePath $ValidatorExe -ExeArgs @('--validate', $Profile)
}

function Assert-EmbeddedProfile {
    param([string]$InstallerExe, [string]$ExpectedProfileName)
    $result = Invoke-Installer -ExePath $InstallerExe -ExeArgs @('--show-profile')
    if ($result.ExitCode -ne 0) {
        throw "Round-trip d'embarquement : --show-profile a échoué (code $($result.ExitCode)) : $($result.Output)"
    }
    if ($result.Output -notmatch [regex]::Escape($ExpectedProfileName)) {
        throw "Round-trip d'embarquement : le profil « $ExpectedProfileName » n'est pas relu par --show-profile.`n$($result.Output)"
    }
}

function Build-InstallerPackage {
    param([string]$Plat, [string]$Profile, [string]$AgentPackageDir, [string]$ResolvedVersion)

    # Lecture des OCTETS du profil (UTF-8 versionné), JAMAIS Get-Content : Windows PowerShell 5.1 lit un
    # fichier UTF-8 SANS BOM en ANSI, ce qui corromprait les accents (« masqué », « verrouillé ») à
    # l'injection — le profil embarqué deviendrait illisible. On injecte les octets VERBATIM (BOM retiré),
    # et on décode en UTF-8 pour en extraire le nom.
    $profileBytes = [System.IO.File]::ReadAllBytes($Profile)
    if ($profileBytes.Length -ge 3 -and $profileBytes[0] -eq 0xEF -and $profileBytes[1] -eq 0xBB -and $profileBytes[2] -eq 0xBF) {
        $profileBytes = [byte[]]($profileBytes[3..($profileBytes.Length - 1)])
    }
    $profileObj = [System.Text.UTF8Encoding]::new($false).GetString($profileBytes) | ConvertFrom-Json
    $profileName = [string]$profileObj.profil
    if ([string]::IsNullOrWhiteSpace($profileName)) { throw "Profil « $Profile » : champ « profil » manquant." }
    $profileSlug = [System.IO.Path]::GetFileNameWithoutExtension($Profile)

    $stageRoot = Join-Path $OutputDirectory '.work'
    $pkgName = "Liakont.Agent.Installer-$ResolvedVersion-$Plat-$profileSlug"
    $stageDir = Join-Path $stageRoot $pkgName
    if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
    New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

    # Charge utile de l'agent (OPS05) : copie intégrale du contenu du paquet agent (bin\ + scripts +
    # modèles). On énumère les enfants (et non « <dir>\* » en -LiteralPath, où « * » serait littéral).
    Get-ChildItem -LiteralPath $AgentPackageDir -Force | Copy-Item -Destination $stageDir -Recurse -Force

    # Installeur GUI dans bin\ (à côté de Liakont.Agent.exe : le déployeur OPS08b l'y attend). Ses
    # dépendances (Core/Cli/Adapters/Newtonsoft) sont déjà dans bin\ via la charge utile de l'agent.
    $binDir = Join-Path $stageDir 'bin'
    $installerExe = Join-Path $binDir 'Liakont.Agent.Installer.exe'
    Copy-Item -LiteralPath (Get-InstallerExe -Plat $Plat) -Destination $installerExe -Force

    # Injection du profil (octets UTF-8 sans BOM, verbatim) comme ressource RT_RCDATA dans la COPIE du paquet.
    [Liakont.ResourceEmbedder]::Embed($installerExe, $script:ProfileResourceName, $profileBytes)

    # Manifeste du paquet installeur.
    $manifest = [ordered]@{
        product       = 'Liakont.Agent.Installer'
        version       = $ResolvedVersion
        platform      = $Plat
        profile       = $profileName
        profileSource = [System.IO.Path]::GetFileName($Profile)
    }
    ($manifest | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath (Join-Path $stageDir 'installer.json') -Encoding UTF8

    Assert-InstallerPackage -StageDir $stageDir -Plat $Plat -ProfileName $profileName

    if (-not (Test-Path -LiteralPath $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null }
    $result = [ordered]@{ Platform = $Plat; Profile = $profileName; Version = $ResolvedVersion }
    if ($NoZip) {
        $finalDir = Join-Path $OutputDirectory $pkgName
        if (Test-Path -LiteralPath $finalDir) { Remove-Item -LiteralPath $finalDir -Recurse -Force }
        Move-Item -LiteralPath $stageDir -Destination $finalDir
        $result['Path'] = $finalDir
    }
    else {
        $zipPath = Join-Path $OutputDirectory "$pkgName.zip"
        if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
        Compress-Archive -Path $stageDir -DestinationPath $zipPath -Force
        Remove-Item -LiteralPath $stageDir -Recurse -Force
        $result['Path'] = $zipPath
        $result['Sha256'] = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    }
    return [PSCustomObject]$result
}

function Assert-InstallerPackage {
    param([string]$StageDir, [string]$Plat, [string]$ProfileName)

    $binDir = Join-Path $StageDir 'bin'
    $errors = @()

    # 1. Installeur présent et du BON bitness (mêmes contrôles PE qu'OPS05 ; AnyCPU refusé).
    $installerExe = Join-Path $binDir 'Liakont.Agent.Installer.exe'
    if (-not (Test-Path -LiteralPath $installerExe)) {
        $errors += 'Liakont.Agent.Installer.exe manquant dans bin\.'
    }
    else {
        $arch = Get-AgentBinaryArchitecture -Path $installerExe
        if ($arch -ne $Plat) { $errors += "Liakont.Agent.Installer.exe est « $arch » mais le paquet est « $Plat »." }
    }

    # 2. Charge utile OPS05 présente : le service que le déployeur installe (preuve du paquet auto-suffisant).
    if (-not (Test-Path -LiteralPath (Join-Path $binDir 'Liakont.Agent.exe'))) {
        $errors += 'Liakont.Agent.exe (charge utile OPS05) manquant dans bin\ — le déployeur ne trouverait pas le service.'
    }

    if ($errors.Count -gt 0) {
        throw "Vérification du paquet installeur ($Plat / $ProfileName) en échec :`n  - " + ($errors -join "`n  - ")
    }

    # 3. Round-trip : le profil embarqué est relu par l'exécutable produit (anti-faux-vert de l'embarquement).
    Assert-EmbeddedProfile -InstallerExe $installerExe -ExpectedProfileName $ProfileName
    Write-Host "  Vérification $Plat / $ProfileName : OK (bitness, charge OPS05, profil embarqué relu)." -ForegroundColor Green
}

# ── Exécution ───────────────────────────────────────────────────────────────
try {
    Initialize-ResourceEmbedder
    $profiles = Resolve-Profiles

    $agentOut = Join-Path $OutputDirectory '.agent-payload'
    if (Test-Path -LiteralPath $agentOut) { Remove-Item -LiteralPath $agentOut -Recurse -Force }
    Invoke-AgentPackaging -AgentOut $agentOut

    # ── Validation de TOUS les profils AVANT tout embarquement (échec si l'un est invalide) ──
    $validatorExe = Get-InstallerExe -Plat $platforms[0]
    $invalid = @()
    foreach ($profile in $profiles) {
        $check = Test-ProfileValid -ValidatorExe $validatorExe -Profile $profile
        if ($check.ExitCode -ne 0) {
            $invalid += "Profil « $profile » invalide :`n$($check.Output)"
        }
        else {
            Write-Host "  Profil validé : $([System.IO.Path]::GetFileName($profile))" -ForegroundColor Green
        }
    }
    if ($invalid.Count -gt 0) {
        throw "Packaging interrompu — profil(s) invalide(s) (aucun installateur produit) :`n" + ($invalid -join "`n`n")
    }

    # ── Résolution de la version depuis le paquet agent (cohérence installeur ↔ agent) ──
    $firstAgentPkg = Get-AgentPackageDir -AgentOut $agentOut -Plat $platforms[0]
    $resolvedVersion = (Get-Content -LiteralPath (Join-Path $firstAgentPkg 'package.json') -Raw | ConvertFrom-Json).version

    # ── Assemblage : un installateur par (plateforme × profil) ──
    $results = @()
    foreach ($plat in $platforms) {
        $agentPkg = Get-AgentPackageDir -AgentOut $agentOut -Plat $plat
        foreach ($profile in $profiles) {
            Write-Host "Assemblage installeur $plat / $([System.IO.Path]::GetFileName($profile))..." -ForegroundColor Cyan
            $results += Build-InstallerPackage -Plat $plat -Profile $profile -AgentPackageDir $agentPkg -ResolvedVersion $resolvedVersion
        }
    }

    # Nettoyage des dossiers de travail.
    foreach ($tmp in @((Join-Path $OutputDirectory '.work'), $agentOut)) {
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    Write-Host ""
    Write-Host "=== Installateurs produits ===" -ForegroundColor Cyan
    foreach ($r in $results) {
        Write-Host ("  {0,-4} {1,-28} v{2}  ->  {3}" -f $r.Platform, $r.Profile, $r.Version, $r.Path) -ForegroundColor Green
        if ($r.PSObject.Properties['Sha256'] -and $r.Sha256) { Write-Host "        SHA-256 : $($r.Sha256)" }
    }
    exit 0
}
catch {
    Write-Host ""
    Write-Host "Échec du packaging installeur : $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
