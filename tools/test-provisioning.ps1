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
$migrateScript = Join-Path $provDir 'migrate-instance.ps1'
$decommissionScript = Join-Path $provDir 'decommission-tenant.ps1'

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

    # ── Test-KeycloakImagePinned ──
    Test-Case 'KeycloakPinned : tag patch précis accepté (quay.io officiel)' {
        Assert-True (Test-KeycloakImagePinned -ImageRef 'quay.io/keycloak/keycloak:26.0.8') 'tag major.minor.patch refusé à tort'
    }
    Test-Case 'KeycloakPinned : tag patch précis accepté (registre privé avec port)' {
        Assert-True (Test-KeycloakImagePinned -ImageRef 'registry.local:5000/keycloak:26.0.8') 'tag avec registre+port refusé à tort'
    }
    Test-Case 'KeycloakPinned : digest @sha256 accepté' {
        Assert-True (Test-KeycloakImagePinned -ImageRef ('quay.io/keycloak/keycloak@sha256:' + ('a' * 64))) 'digest sha256 refusé à tort'
    }
    Test-Case 'KeycloakPinned : tag minor seul refusé (26.0)' {
        Assert-True (-not (Test-KeycloakImagePinned -ImageRef 'quay.io/keycloak/keycloak:26.0')) 'tag flottant minor accepté à tort'
    }
    Test-Case 'KeycloakPinned : tag major seul refusé (26)' {
        Assert-True (-not (Test-KeycloakImagePinned -ImageRef 'quay.io/keycloak/keycloak:26')) 'tag flottant major accepté à tort'
    }
    Test-Case 'KeycloakPinned : :latest refusé' {
        Assert-True (-not (Test-KeycloakImagePinned -ImageRef 'quay.io/keycloak/keycloak:latest')) 'tag latest accepté à tort'
    }
    Test-Case 'KeycloakPinned : absence de tag refusée' {
        Assert-True (-not (Test-KeycloakImagePinned -ImageRef 'quay.io/keycloak/keycloak')) 'image sans tag acceptée à tort'
    }
    Test-Case 'KeycloakPinned : chaîne vide refusée (exception ou $false)' {
        # PowerShell refuse un string vide sur un paramètre Mandatory ; on accepte aussi bien une
        # exception (refus du binder) qu'un retour $false (garde interne) — les deux signifient « rejeté ».
        $accepted = $false
        try { $accepted = [bool](Test-KeycloakImagePinned -ImageRef '') } catch { $accepted = $false }
        Assert-True (-not $accepted) 'chaîne vide acceptée à tort'
    }
    Test-Case 'KeycloakPinned : tag minor seul refusé (registre privé avec port)' {
        Assert-True (-not (Test-KeycloakImagePinned -ImageRef 'registry.local:5000/keycloak:26.0')) 'tag flottant minor sur registre privé accepté à tort'
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
    Test-Case 'Copy LF : un fichier CRLF est copié en LF (round-trip + idempotence)' {
        $src = Join-Path $tmpRoot 'crlf-src.conf'
        $dst = Join-Path $tmpRoot 'crlf-dst.conf'
        [System.IO.File]::WriteAllText($src, "reverse_proxy liakont:8080`r`nencode gzip`r`n")
        Copy-DeploymentFileAsLf -Source $src -Destination $dst
        $out = [System.IO.File]::ReadAllText($dst)
        Assert-True (-not $out.Contains("`r")) 'aucun CRLF dans la destination'
        Assert-Equal "reverse_proxy liakont:8080`nencode gzip`n" $out 'contenu utile préservé en LF'
        # Aucun BOM UTF-8 : un BOM en tête d'un Caddyfile casse la 1re directive sous Linux (même
        # classe de bug silencieux que le CRLF) — la lecture via ReadAllText masquerait un BOM régressé.
        $dstBytes = [System.IO.File]::ReadAllBytes($dst)
        Assert-True (-not ($dstBytes.Length -ge 3 -and $dstBytes[0] -eq 0xEF -and $dstBytes[1] -eq 0xBB -and $dstBytes[2] -eq 0xBF)) 'aucun BOM UTF-8 dans la destination'
        # Idempotence : une source déjà en LF reste identique.
        $dst2 = Join-Path $tmpRoot 'crlf-dst2.conf'
        Copy-DeploymentFileAsLf -Source $dst -Destination $dst2
        Assert-Equal $out ([System.IO.File]::ReadAllText($dst2)) 'idempotent sur source LF'
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

    # ── migrate-instance.ps1 (OPS06b) : codes de sortie hors Docker (le round-trip réel est porté par
    # deploy/provisioning/test-migrate-instance.sh, exécuté à la recette GATE_TOOLKIT comme l'appliance). ──
    Test-Case 'migrate-instance EXPORT : instance source inconnue → code 1' {
        $root = Join-Path $tmpRoot 'mig-export-missing'
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        $code = Invoke-Script -Path $migrateScript -Arguments @('-InstanceName', 'nope', '-InstancesRoot', $root, '-DryRun')
        Assert-Equal 1 $code 'instance source inconnue (code 1)'
    }
    Test-Case 'migrate-instance EXPORT : instance sans .env → code 1 (secrets requis)' {
        $root = Join-Path $tmpRoot 'mig-export-noenv'
        New-Item -ItemType Directory -Path (Join-Path $root 'src1') -Force | Out-Null
        $code = Invoke-Script -Path $migrateScript -Arguments @('-InstanceName', 'src1', '-InstancesRoot', $root, '-DryRun')
        Assert-Equal 1 $code 'instance sans .env (code 1)'
    }
    Test-Case 'migrate-instance EXPORT : DryRun sur instance existante (+.env) → code 0' {
        $root = Join-Path $tmpRoot 'mig-export-dry'
        $dir = Join-Path $root 'src1'
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $dir '.env') -Value 'PUBLIC_HOSTNAME=h.test' -NoNewline
        $code = Invoke-Script -Path $migrateScript -Arguments @('-InstanceName', 'src1', '-InstancesRoot', $root, '-DryRun')
        Assert-Equal 0 $code 'DryRun export OK (code 0)'
    }
    Test-Case 'migrate-instance APPLY : bundle absent → code 1' {
        $root = Join-Path $tmpRoot 'mig-apply-missing'
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        $code = Invoke-Script -Path $migrateScript -Arguments @('-ApplyBundle', (Join-Path $root 'absent.zip'), '-InstancesRoot', $root, '-DryRun')
        Assert-Equal 1 $code 'bundle absent (code 1)'
    }
    Test-Case 'migrate-instance APPLY : .zip sans manifeste de migration → code 1' {
        $root = Join-Path $tmpRoot 'mig-apply-badzip'
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        $junk = Join-Path $root 'junk.txt'
        Set-Content -LiteralPath $junk -Value 'x' -NoNewline
        $zip = Join-Path $root 'notbundle.zip'
        Compress-Archive -Path $junk -DestinationPath $zip -Force
        $code = Invoke-Script -Path $migrateScript -Arguments @('-ApplyBundle', $zip, '-TargetInstanceName', 'tgt1', '-InstancesRoot', $root, '-DryRun')
        Assert-Equal 1 $code '.zip non-bundle refusé (code 1)'
    }
    Test-Case 'migrate-instance APPLY : DryRun sur bundle valide → code 0' {
        $root = Join-Path $tmpRoot 'mig-apply-dry'
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        # Bundle minimal valide : migration-manifest.json + backup/SHA256SUMS + config/.env.
        $bsrc = Join-Path $root 'bundlesrc'
        New-Item -ItemType Directory -Path (Join-Path $bsrc 'backup') -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $bsrc 'config') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $bsrc 'migration-manifest.json') -Value '{ "source_instance": "src1", "databases": [] }'
        Set-Content -LiteralPath (Join-Path $bsrc 'backup\SHA256SUMS') -Value '' -NoNewline
        Set-Content -LiteralPath (Join-Path $bsrc 'config\.env') -Value 'PUBLIC_HOSTNAME=h.test' -NoNewline
        $zip = Join-Path $root 'bundle.zip'
        Compress-Archive -Path (Join-Path $bsrc '*') -DestinationPath $zip -Force
        $code = Invoke-Script -Path $migrateScript -Arguments @('-ApplyBundle', $zip, '-TargetInstanceName', 'tgt1', '-InstancesRoot', (Join-Path $root 'targets'), '-DryRun')
        Assert-Equal 0 $code 'DryRun apply OK (code 0)'
    }

    # ── Audit d'instance de fin de vie de tenant (OPS06c) — append-only ──
    Test-Case 'Audit instance : append-only round-trip + préservation de la 1re entrée' {
        $p = Join-Path $tmpRoot 'audit1.jsonl'
        Add-TenantDecommissionAuditEntry -Path $p -Entry ([ordered]@{ event = 'tenant-decommissioned'; tenant_id = 't1'; operator = 'o1' })
        Add-TenantDecommissionAuditEntry -Path $p -Entry ([ordered]@{ event = 'tenant-decommissioned'; tenant_id = 't2'; operator = 'o2' })
        $read = @(Read-TenantDecommissionAuditEntries -Path $p)
        Assert-Equal 2 $read.Count 'deux entrées'
        Assert-Equal 't1' $read[0].tenant_id 'première entrée préservée (append-only)'
        Assert-Equal 't2' $read[1].tenant_id 'seconde entrée ajoutée'
        # Une entrée = une ligne (JSONL).
        $lines = @([System.IO.File]::ReadAllLines($p) | Where-Object { $_.Trim().Length -gt 0 })
        Assert-Equal 2 $lines.Count 'une ligne JSON par entrée'
        # Pas de BOM UTF-8 en tête (casserait le parsing JSON de la 1re ligne).
        $bytes = [System.IO.File]::ReadAllBytes($p)
        Assert-True (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) 'aucun BOM UTF-8'
    }
    Test-Case 'Audit instance : fichier absent → tableau vide' {
        Assert-Equal 0 (@(Read-TenantDecommissionAuditEntries -Path (Join-Path $tmpRoot 'audit-absent.jsonl'))).Count 'vide'
    }

    # ── Fixtures d'export pour les gardes de suppression (vérifiées par le VRAI verifier-integrite-archive.ps1) ──
    # EMPTY : un dossier « archive/ » vide → VERDICT=EMPTY (coffre vide, vert).
    $emptyExport = Join-Path $tmpRoot 'export-empty'
    New-Item -ItemType Directory -Path (Join-Path $emptyExport 'archive') -Force | Out-Null
    # TAMPERED : empreinte de pièce déclarée FAUSSE → VERDICT=TAMPERED.
    $tamperedExport = Join-Path $tmpRoot 'export-tampered'
    $tdir = Join-Path $tamperedExport 'archive\pkg'
    New-Item -ItemType Directory -Path $tdir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $tdir 'piece.txt') -Value 'x' -NoNewline
    $hx = ('de' * 32)  # 64 hex
    $tamperedManifest = '{ "entryKind":"package","packageHash":"' + $hx + '","chainHash":"' + $hx + '","files":[{"name":"piece.txt","sha256":"' + ('0' * 64) + '"}] }'
    Set-Content -LiteralPath (Join-Path $tdir 'manifest.json') -Value $tamperedManifest
    # INCOMPLETE : pièce/paquet COHÉRENTS mais chaîne NON ancrée en genèse → VERDICT=INCOMPLETE.
    $incompleteExport = Join-Path $tmpRoot 'export-incomplete'
    $idir = Join-Path $incompleteExport 'archive\add'
    New-Item -ItemType Directory -Path $idir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $idir 'piece.txt') -Value 'x' -NoNewline
    $fileHash = (Get-FileHash -LiteralPath (Join-Path $idir 'piece.txt') -Algorithm SHA256).Hash.ToLowerInvariant()
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $cb = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("prev$fileHash")) } finally { $sha.Dispose() }
    $chainHash = -join ($cb | ForEach-Object { $_.ToString('x2') })  # sha256("prev"+fileHash) ≠ sha256(""+fileHash)
    $incompleteManifest = '{ "entryKind":"addendum","packageHash":"' + $fileHash + '","chainHash":"' + $chainHash + '","files":[{"name":"piece.txt","sha256":"' + $fileHash + '"}] }'
    Set-Content -LiteralPath (Join-Path $idir 'manifest.json') -Value $incompleteManifest

    # ── decommission-tenant.ps1 (OPS06c) : gardes hors Docker (le round-trip réel est porté par
    # deploy/provisioning/test-decommission-tenant.sh, exécuté à la recette GATE_TOOLKIT). ──
    Test-Case 'decommission : nom d''instance invalide → code 1' {
        $code = Invoke-Script -Path $decommissionScript -Arguments @('-InstanceName', 'mauvais nom', '-Tenant', 'acme', '-DryRun')
        Assert-Equal 1 $code 'nom invalide (code 1)'
    }
    Test-Case 'decommission : DÉSACTIVATION DryRun → code 0 (sans Docker)' {
        $code = Invoke-Script -Path $decommissionScript -Arguments @('-InstanceName', 'fresh', '-Tenant', 'acme', '-DryRun')
        Assert-Equal 0 $code 'désactivation DryRun (code 0)'
    }
    Test-Case 'decommission -Delete : sans export → REFUS (code 1)' {
        $code = Invoke-Script -Path $decommissionScript -Arguments @('-InstanceName', 'fresh', '-Tenant', 'acme', '-Delete', '-DryRun')
        Assert-Equal 1 $code 'suppression sans export refusée (code 1)'
    }
    Test-Case 'decommission -Delete : export introuvable → REFUS (code 1)' {
        $code = Invoke-Script -Path $decommissionScript -Arguments @('-InstanceName', 'fresh', '-Tenant', 'acme', '-Delete',
            '-VerifiedExportPath', (Join-Path $tmpRoot 'export-absent'), '-Operator', 'o@test', '-Yes', '-ConfirmTenantName', 'acme', '-DryRun')
        Assert-Equal 1 $code 'export introuvable refusé (code 1)'
    }
    Test-Case 'decommission -Delete : sans opérateur → REFUS (code 1)' {
        $code = Invoke-Script -Path $decommissionScript -Arguments @('-InstanceName', 'fresh', '-Tenant', 'acme', '-Delete',
            '-VerifiedExportPath', $emptyExport, '-Yes', '-ConfirmTenantName', 'acme', '-DryRun')
        Assert-Equal 1 $code 'opérateur requis (code 1)'
    }
    Test-Case 'decommission -Delete : export ALTÉRÉ (TAMPERED) → REFUS (code 1)' {
        $code = Invoke-Script -Path $decommissionScript -Arguments @('-InstanceName', 'fresh', '-Tenant', 'acme', '-Delete',
            '-VerifiedExportPath', $tamperedExport, '-Operator', 'o@test', '-Yes', '-ConfirmTenantName', 'acme', '-DryRun')
        Assert-Equal 1 $code 'export altéré refusé (code 1)'
    }
    Test-Case 'decommission -Delete : export PARTIEL (INCOMPLETE) → REFUS (code 1)' {
        $code = Invoke-Script -Path $decommissionScript -Arguments @('-InstanceName', 'fresh', '-Tenant', 'acme', '-Delete',
            '-VerifiedExportPath', $incompleteExport, '-Operator', 'o@test', '-Yes', '-ConfirmTenantName', 'acme', '-DryRun')
        Assert-Equal 1 $code 'export partiel refusé (code 1)'
    }
    Test-Case 'decommission -Delete : nom de confirmation ERRONÉ → REFUS (code 1)' {
        $code = Invoke-Script -Path $decommissionScript -Arguments @('-InstanceName', 'fresh', '-Tenant', 'acme', '-Delete',
            '-VerifiedExportPath', $emptyExport, '-Operator', 'o@test', '-Yes', '-ConfirmTenantName', 'mauvais', '-DryRun')
        Assert-Equal 1 $code 'confirmation erronée refusée (code 1)'
    }
    Test-Case 'decommission -Delete : 1re confirmation manquante (sans -Yes, non interactif) → REFUS (code 1)' {
        $code = Invoke-Script -Path $decommissionScript -Arguments @('-InstanceName', 'fresh', '-Tenant', 'acme', '-Delete',
            '-VerifiedExportPath', $emptyExport, '-Operator', 'o@test', '-ConfirmTenantName', 'acme', '-DryRun')
        Assert-Equal 1 $code '1re confirmation manquante refusée (code 1)'
    }
    Test-Case 'decommission -Delete : export VERT (EMPTY) + confirmation correcte → DryRun code 0' {
        $code = Invoke-Script -Path $decommissionScript -Arguments @('-InstanceName', 'fresh', '-Tenant', 'acme', '-Delete',
            '-VerifiedExportPath', $emptyExport, '-Operator', 'o@test', '-Recipient', 'dpo@acme.test', '-Yes', '-ConfirmTenantName', 'acme', '-DryRun')
        Assert-Equal 0 $code 'toutes gardes vertes → DryRun OK (code 0)'
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
