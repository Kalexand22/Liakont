#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Self-test de la logique de provisioning d'instances (OPS02).
.DESCRIPTION
    Vérifie la LOGIQUE PURE de deploy/provisioning/Provisioning.psm1 (validation de nom, génération
    de secrets, rendu du .env, registre des instances) ET le comportement des scripts new-instance.ps1
    / update-instance.ps1 sur les états VIDE / SALE / ÉCHEC (codes de sortie, messages) — sans Docker.

    En PowerShell pur (aucune dépendance Pester) : câblé dans tools/run-tests.ps1 → une régression de
    la logique de provisioning fait échouer une gate permanente (pas seulement un nœud d'orchestration).

    Exit code 0 = tout vert, 1 = au moins un cas en échec. Les contrôles ÉCHOUENT plutôt que de
    « passer » silencieusement (anti-faux-vert).
#>
$ErrorActionPreference = 'Stop'
# Les scripts enfants sortent volontairement en code 1 ; sur pwsh 7.4+ l'appel natif ne doit pas lever, le code de sortie est lu via $LASTEXITCODE.
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = Split-Path -Parent $PSScriptRoot
$provDir = Join-Path $repoRoot 'deploy\provisioning'
Import-Module (Join-Path $provDir 'Provisioning.psm1') -Force

$psExe = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell' }
$newScript = Join-Path $provDir 'new-instance.ps1'
$updateScript = Join-Path $provDir 'update-instance.ps1'

$script:passed = 0
$script:failed = 0

function Test-Case {
    param([string]$Name, [scriptblock]$Body)
    try { & $Body; $script:passed++; Write-Host "  [OK]   $Name" -ForegroundColor Green }
    catch { $script:failed++; Write-Host "  [FAIL] $Name : $($_.Exception.Message)" -ForegroundColor Red }
}
function Assert-True { param([bool]$Cond, [string]$Msg) if (-not $Cond) { throw $Msg } }
function Assert-Equal { param($Expected, $Actual, [string]$Msg) if ($Expected -ne $Actual) { throw "$Msg (attendu « $Expected », obtenu « $Actual »)" } }
function Assert-Throws { param([scriptblock]$Body, [string]$Msg) $threw = $false; try { & $Body } catch { $threw = $true }; if (-not $threw) { throw "$Msg (aucune exception levée)" } }

# Invoque un script enfant et rend son code de sortie (logique testée hors processus, comme en prod).
function Invoke-Script {
    param([string]$Path, [string[]]$Arguments)
    & $psExe -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments *> $null
    return $LASTEXITCODE
}

$tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("liakont-prov-test-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmpRoot -Force | Out-Null

try {
    Write-Host "=== Self-test provisioning (OPS02) ===" -ForegroundColor Cyan

    # ── Resolve-InstanceName ──
    Test-Case 'Resolve : nom valide → minuscules + projet' {
        $r = Resolve-InstanceName -Name 'Acme-Prod'
        Assert-Equal 'acme-prod' $r.Name 'normalisation minuscules'
        Assert-Equal 'liakont-acme-prod' $r.ProjectName 'nom de projet'
    }
    Test-Case 'Resolve : rejette un espace' { Assert-Throws { Resolve-InstanceName -Name 'mauvais nom' } 'espace' }
    Test-Case 'Resolve : rejette un tiret en tête' { Assert-Throws { Resolve-InstanceName -Name '-x' } 'tiret en tête' }
    Test-Case 'Resolve : rejette vide' { Assert-Throws { Resolve-InstanceName -Name '   ' } 'vide' }
    Test-Case 'Resolve : rejette > 32 caractères' { Assert-Throws { Resolve-InstanceName -Name ('a' * 33) } 'trop long' }
    Test-Case 'Resolve : rejette 1 caractère' { Assert-Throws { Resolve-InstanceName -Name 'a' } 'trop court' }

    # ── New-StrongSecret ──
    Test-Case 'Secret : longueur respectée' { Assert-Equal 40 (New-StrongSecret -Length 40).Length 'longueur' }
    Test-Case 'Secret : alphanumérique strict' { Assert-True ((New-StrongSecret -Length 64) -match '^[A-Za-z0-9]+$') 'charset' }
    Test-Case 'Secret : deux tirages distincts' { Assert-True ((New-StrongSecret) -ne (New-StrongSecret)) 'unicité' }
    Test-Case 'Secret : rejette longueur trop faible' { Assert-Throws { New-StrongSecret -Length 8 } 'longueur minimale' }

    # ── New-InstanceEnvContent ──
    Test-Case '.env : toutes les clés requises présentes' {
        $env1 = New-InstanceEnvContent -PublicHostname 'h.test' -KeycloakHostname 'id.test' -AcmeEmail 'a@test'
        foreach ($k in 'PUBLIC_HOSTNAME', 'KEYCLOAK_HOSTNAME', 'PUBLIC_BASE_URL', 'KEYCLOAK_PUBLIC_URL', 'ACME_EMAIL',
                       'POSTGRES_PASSWORD', 'KC_DB_PASSWORD', 'KC_BOOTSTRAP_ADMIN_USERNAME', 'KC_BOOTSTRAP_ADMIN_PASSWORD',
                       'KEYCLOAK_LIAKONT_CLIENT_SECRET') {
            Assert-True ($env1 -match "(?m)^$k=") "clé $k présente"
        }
    }
    Test-Case '.env : aucun secret vide' {
        $env1 = New-InstanceEnvContent -PublicHostname 'h.test' -KeycloakHostname 'id.test' -AcmeEmail 'a@test'
        foreach ($k in 'POSTGRES_PASSWORD', 'KC_DB_PASSWORD', 'KC_BOOTSTRAP_ADMIN_PASSWORD', 'KEYCLOAK_LIAKONT_CLIENT_SECRET') {
            Assert-True ($env1 -match "(?m)^$k=\S+") "secret $k non vide"
        }
    }
    Test-Case '.env : URL publique dérivée de l''hôte' {
        $env1 = New-InstanceEnvContent -PublicHostname 'liakont.acme.test' -KeycloakHostname 'id.acme.test' -AcmeEmail 'a@test'
        # Ancre stricte « $ » VOULUE : passe sur du LF (état normalisé), échoue sur du CRLF — garde
        # ainsi la normalisation LF (un CRLF résiduel laisserait un « \r » en fin de valeur sous Linux).
        Assert-True ($env1 -match '(?m)^PUBLIC_BASE_URL=https://liakont\.acme\.test$') 'PUBLIC_BASE_URL'
    }
    Test-Case '.env : fins de ligne LF (aucun CRLF — consommé sous Linux)' {
        $env1 = New-InstanceEnvContent -PublicHostname 'h.test' -KeycloakHostname 'id.test' -AcmeEmail 'a@test'
        Assert-True (-not ($env1.Contains("`r"))) 'le .env ne doit contenir aucun CRLF (corromprait secrets/URL côté conteneur)'
    }
    Test-Case '.env : secrets uniques entre deux instances' {
        $a = New-InstanceEnvContent -PublicHostname 'h.test' -KeycloakHostname 'id.test' -AcmeEmail 'a@test'
        $b = New-InstanceEnvContent -PublicHostname 'h.test' -KeycloakHostname 'id.test' -AcmeEmail 'a@test'
        $pa = ($a -split "`n" | Where-Object { $_ -match '^POSTGRES_PASSWORD=' })
        $pb = ($b -split "`n" | Where-Object { $_ -match '^POSTGRES_PASSWORD=' })
        Assert-True ($pa -ne $pb) 'secrets distincts par instance'
    }

    # ── Registre des instances ──
    Test-Case 'Registre : fichier absent → tableau vide' {
        $missing = Join-Path $tmpRoot 'absent.yaml'
        Assert-Equal 0 (@(Read-InstanceRegistry -Path $missing)).Count 'vide'
    }
    Test-Case 'Registre : round-trip écriture/lecture' {
        $p = Join-Path $tmpRoot 'reg1.yaml'
        Set-InstanceRegistryEntry -Path $p -Entry @{ name = 'b-inst'; editor = 'B SARL'; url = 'https://b.test'
            hosting = 'hosted'; version = 'v1'; project = 'liakont-b-inst'; created_at = 't0'; updated_at = 't0' }
        $read = @(Read-InstanceRegistry -Path $p)
        Assert-Equal 1 $read.Count 'une entrée'
        Assert-Equal 'b-inst' $read[0].name 'name'
        Assert-Equal 'B SARL' $read[0].editor 'editor (avec espace)'
        Assert-Equal 'https://b.test' $read[0].url 'url'
    }
    Test-Case 'Registre : upsert met à jour sans dupliquer' {
        $p = Join-Path $tmpRoot 'reg2.yaml'
        Set-InstanceRegistryEntry -Path $p -Entry @{ name = 'x'; version = 'v1'; editor = 'X' }
        Set-InstanceRegistryEntry -Path $p -Entry @{ name = 'y'; version = 'v1'; editor = 'Y' }
        Set-InstanceRegistryEntry -Path $p -Entry @{ name = 'x'; version = 'v2'; editor = 'X' }
        $read = @(Read-InstanceRegistry -Path $p)
        Assert-Equal 2 $read.Count 'deux entrées (pas de doublon)'
        Assert-Equal 'v2' (($read | Where-Object { $_.name -eq 'x' }).version) 'version mise à jour'
        Assert-Equal 'v1' (($read | Where-Object { $_.name -eq 'y' }).version) 'autre entrée préservée'
    }
    Test-Case 'Registre : l''exemple fictif versionné est lisible' {
        $read = @(Read-InstanceRegistry -Path (Join-Path $provDir 'instances.example.yaml'))
        Assert-Equal 2 $read.Count 'deux instances dans l''exemple'
        Assert-True (($read | Where-Object { $_.name -eq 'demo-editeur' }).hosting -eq 'hosted') 'champ hosting lu'
    }
    Test-Case 'Registre : mise à jour partielle préserve les champs absents (FIX1 — pas de perte de champs)' {
        $p = Join-Path $tmpRoot 'reg-fix1.yaml'
        # Écriture initiale complète (tous les champs).
        Set-InstanceRegistryEntry -Path $p -Entry @{
            name       = 'fix1-inst'
            editor     = 'Fix1 SARL'
            url        = 'https://fix1.test'
            hosting    = 'self-hosted'
            version    = 'v1'
            project    = 'liakont-fix1-inst'
            created_at = '2024-01-01T00:00:00Z'
            updated_at = '2024-01-01T00:00:00Z'
        }
        # Mise à jour PARTIELLE : seulement name + version + updated_at (comme update-instance.ps1).
        Set-InstanceRegistryEntry -Path $p -Entry @{
            name       = 'fix1-inst'
            version    = 'v2'
            updated_at = '2024-06-01T00:00:00Z'
        }
        $read = @(Read-InstanceRegistry -Path $p)
        $inst = $read | Where-Object { $_.name -eq 'fix1-inst' }
        Assert-Equal 'v2'                   $inst.version    'version mise à jour'
        Assert-Equal '2024-06-01T00:00:00Z' $inst.updated_at 'updated_at mis à jour'
        Assert-Equal 'Fix1 SARL'            $inst.editor     'editor préservé (non écrasé)'
        Assert-Equal '2024-01-01T00:00:00Z' $inst.created_at 'created_at préservé (non écrasé)'
        Assert-Equal 'self-hosted'          $inst.hosting    'hosting préservé (non remplacé par hosted)'
    }

    # ── maintenance.Caddyfile (503 explicite) ──
    Test-Case 'Maintenance : Caddyfile renvoie 503 sur /api/agent/*' {
        $caddy = Get-Content -LiteralPath (Join-Path $provDir 'maintenance.Caddyfile') -Raw
        Assert-True ($caddy -match '/api/agent/\*') 'route agent'
        Assert-True ($caddy -match '503') 'code 503'
        Assert-True ($caddy -match 'Retry-After') 'en-tête Retry-After'
    }

    # ── new-instance.ps1 : états vide / sale / invalide ──
    $commonArgs = @('-Editor', 'Test SARL', '-PublicHostname', 'h.test', '-KeycloakHostname', 'id.test', '-AcmeEmail', 'a@test')
    Test-Case 'new-instance : nom invalide → code 1' {
        $root = Join-Path $tmpRoot 'ni-invalid'
        $code = Invoke-Script -Path $newScript -Arguments (@('-InstanceName', 'mauvais nom', '-InstancesRoot', $root, '-DryRun') + $commonArgs)
        Assert-Equal 1 $code 'code de sortie 1'
    }
    Test-Case 'new-instance : DryRun état vide → code 0, rien créé' {
        $root = Join-Path $tmpRoot 'ni-dry'
        $code = Invoke-Script -Path $newScript -Arguments (@('-InstanceName', 'fresh', '-InstancesRoot', $root, '-DryRun') + $commonArgs)
        Assert-Equal 0 $code 'code de sortie 0'
        Assert-True (-not (Test-Path (Join-Path $root 'fresh'))) 'aucun répertoire créé en DryRun'
    }
    Test-Case 'new-instance : instance déjà existante → code 1' {
        $root = Join-Path $tmpRoot 'ni-dirty'
        New-Item -ItemType Directory -Path (Join-Path $root 'dup') -Force | Out-Null
        $code = Invoke-Script -Path $newScript -Arguments (@('-InstanceName', 'dup', '-InstancesRoot', $root, '-DryRun') + $commonArgs)
        Assert-Equal 1 $code 'refus d''écrasement (code 1)'
    }

    # ── update-instance.ps1 : état vide / DryRun ──
    Test-Case 'update-instance : instance inconnue → code 1' {
        $root = Join-Path $tmpRoot 'up-missing'
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        $code = Invoke-Script -Path $updateScript -Arguments @('-InstanceName', 'nope', '-InstancesRoot', $root, '-DryRun')
        Assert-Equal 1 $code 'instance inconnue (code 1)'
    }
    Test-Case 'update-instance : DryRun sur instance existante → code 0' {
        $root = Join-Path $tmpRoot 'up-dry'
        New-Item -ItemType Directory -Path (Join-Path $root 'known') -Force | Out-Null
        $code = Invoke-Script -Path $updateScript -Arguments @('-InstanceName', 'known', '-InstancesRoot', $root, '-DryRun')
        Assert-Equal 0 $code 'DryRun OK (code 0)'
    }

    Write-Host ""
    Write-Host "=== Self-test provisioning : $($script:passed) OK, $($script:failed) FAIL ===" -ForegroundColor Cyan
    if ($script:failed -gt 0) { Write-Host "ÉCHEC du self-test provisioning." -ForegroundColor Red; exit 1 }
    Write-Host "PASS" -ForegroundColor Green
    exit 0
}
finally {
    Remove-Item -LiteralPath $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
}
