<#
.SYNOPSIS
    Fonctions partagées du provisioning d'instances Liakont (OPS02, F12 §6.3).
.DESCRIPTION
    Module commun à new-instance.ps1 (création d'une instance éditeur) et update-instance.ps1
    (montée de version multi-bases). Il centralise ce qui DOIT rester cohérent entre la création
    et la mise à jour d'une instance :
      - la validation et la dérivation du NOM d'instance (projet Docker Compose isolé) ;
      - la génération de SECRETS forts et UNIQUES par instance (jamais de valeur partagée) ;
      - le rendu du fichier .env d'instance à partir du modèle de l'appliance (OPS01a) ;
      - la tenue du REGISTRE des instances (deploy/provisioning/instances.yaml) ;
      - les enveloppes Docker Compose (énumération des bases, sauvegarde par base, santé).

    Le modèle de déploiement (F12 §6.2/6.3) est « un répertoire/stack par instance » : chaque
    instance possède son propre répertoire (copie de la stack appliance + .env + Caddyfile), isolé
    des autres par un nom de projet Compose distinct (volumes/réseaux/conteneurs préfixés).

    Messages opérateur en français (CLAUDE.md n°12). Aucune donnée client dans le code : le registre
    réel (deploy/provisioning/instances.yaml) et les .env d'instance sont des DONNÉES OPÉRATEUR, jamais
    versionnés (.gitignore) ; seul instances.example.yaml (fictif) est suivi.
#>

Set-StrictMode -Version Latest

# Nom d'instance = nom de projet Docker Compose. Compose impose [a-z0-9][a-z0-9_-]* ; on borne à
# 2-32 caractères pour des noms de volumes/conteneurs lisibles. Lettre/chiffre en tête.
$script:InstanceNamePattern = '^[a-z0-9][a-z0-9_-]{1,31}$'

# ── Messages opérateur (français) ────────────────────────────────────────────
function Write-Step    { param([string]$Message) Write-Host $Message -ForegroundColor Cyan }
function Write-Ok      { param([string]$Message) Write-Host "  [OK]   $Message" -ForegroundColor Green }
function Write-WarnMsg  { param([string]$Message) Write-Host "  [!]    $Message" -ForegroundColor Yellow }
function Write-ErrMsg   { param([string]$Message) Write-Host "  [ERR]  $Message" -ForegroundColor Red }

function Resolve-InstanceName {
    <#
    .SYNOPSIS
        Valide un nom d'instance et rend ses dérivations (nom de projet Compose).
    .DESCRIPTION
        Le nom est normalisé en minuscules (les noms de projet Compose sont insensibles à la casse,
        mais Compose refuse les majuscules en entrée). Lève une exception française si le nom ne
        respecte pas le motif (lettre/chiffre en tête, puis lettres/chiffres/« - »/« _ », 2-32 car.).
    .OUTPUTS
        PSCustomObject { Name; ProjectName }
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Name)

    $trimmed = ([string]$Name).Trim().ToLowerInvariant()

    if ($trimmed.Length -eq 0) {
        throw "Nom d'instance vide. Indiquez un nom (ex. « acme-prod »)."
    }
    if ($trimmed -notmatch $script:InstanceNamePattern) {
        throw "Nom d'instance invalide : « $Name ». Règle : lettre ou chiffre en première position, " +
              "puis lettres minuscules, chiffres, « - » ou « _ », 2 à 32 caractères."
    }

    return [PSCustomObject]@{
        Name        = $trimmed
        ProjectName = "liakont-$trimmed"
    }
}

function New-StrongSecret {
    <#
    .SYNOPSIS
        Génère un secret fort alphanumérique (sûr dans un fichier .env, aucun caractère à échapper).
    .DESCRIPTION
        Tirage cryptographique (RandomNumberGenerator) sur un alphabet base62 (a-z A-Z 0-9), avec
        rejection sampling pour que chaque symbole soit équiprobable (256 n'est pas multiple de 62).
        Alphanumérique volontairement : le .env de l'appliance recommande d'éviter les caractères à
        échapper (guillemets, « $ », « # »...) qui cassent l'interpolation Docker Compose.
    #>
    [CmdletBinding()]
    param([int]$Length = 32)

    if ($Length -lt 16) { throw "Longueur de secret trop faible ($Length) : minimum 16 caractères." }

    $alphabet = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'
    $alphabetLen = $alphabet.Length
    $limit = 256 - (256 % $alphabetLen)
    $chars = New-Object char[] $Length
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $produced = 0
        $buffer = New-Object byte[] ($Length * 2)
        while ($produced -lt $Length) {
            $rng.GetBytes($buffer)
            foreach ($b in $buffer) {
                if ($b -lt $limit) {
                    $chars[$produced] = $alphabet[$b % $alphabetLen]
                    $produced++
                    if ($produced -eq $Length) { break }
                }
            }
        }
    }
    finally {
        $rng.Dispose()
    }
    return (-join $chars)
}

function New-InstanceEnvContent {
    <#
    .SYNOPSIS
        Rend le contenu du fichier .env d'une instance (modèle appliance OPS01a + secrets générés).
    .DESCRIPTION
        Reprend EXACTEMENT les variables attendues par deploy/docker/appliance/docker-compose.yml.
        Génère les quatre secrets (jamais de valeur par défaut, jamais partagée entre instances) :
        POSTGRES_PASSWORD, KC_DB_PASSWORD, KC_BOOTSTRAP_ADMIN_PASSWORD, KEYCLOAK_LIAKONT_CLIENT_SECRET.
        Les URLs publiques sont dérivées des hôtes (https://<hôte>). SMTP reste désactivé (optionnel).
    .OUTPUTS
        Chaîne (contenu du .env).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$PublicHostname,
        [Parameter(Mandatory = $true)][string]$KeycloakHostname,
        [Parameter(Mandatory = $true)][string]$AcmeEmail,
        [string]$AdminUsername = 'admin'
    )

    $postgresPassword = New-StrongSecret -Length 32
    $kcDbPassword     = New-StrongSecret -Length 32
    $kcAdminPassword  = New-StrongSecret -Length 32
    $clientSecret     = New-StrongSecret -Length 40

    # Garde anti-faux-vert : aucun secret ne doit sortir vide de la génération.
    foreach ($pair in @(
        @{ n = 'POSTGRES_PASSWORD'; v = $postgresPassword },
        @{ n = 'KC_DB_PASSWORD'; v = $kcDbPassword },
        @{ n = 'KC_BOOTSTRAP_ADMIN_PASSWORD'; v = $kcAdminPassword },
        @{ n = 'KEYCLOAK_LIAKONT_CLIENT_SECRET'; v = $clientSecret })) {
        if ([string]::IsNullOrWhiteSpace($pair.v)) {
            throw "Génération de secret en échec pour $($pair.n) — instance non créée (anti-faux-vert)."
        }
    }

    return @"
# ─────────────────────────────────────────────────────────────────────────────
# Instance Liakont — configuration générée par new-instance.ps1 (OPS02).
# Secrets générés UNIQUES pour cette instance. NE PAS versionner (gitignore : .env).
# ─────────────────────────────────────────────────────────────────────────────

# === Hôtes publics ==========================================================
PUBLIC_HOSTNAME=$PublicHostname
KEYCLOAK_HOSTNAME=$KeycloakHostname
PUBLIC_BASE_URL=https://$PublicHostname
KEYCLOAK_PUBLIC_URL=https://$KeycloakHostname
ACME_EMAIL=$AcmeEmail

# === Secrets — GÉNÉRÉS (uniques par instance) ===============================
POSTGRES_PASSWORD=$postgresPassword
KC_DB_PASSWORD=$kcDbPassword
KC_BOOTSTRAP_ADMIN_USERNAME=$AdminUsername
KC_BOOTSTRAP_ADMIN_PASSWORD=$kcAdminPassword
KEYCLOAK_LIAKONT_CLIENT_SECRET=$clientSecret

# === E-mail sortant (OPTIONNEL — désactivé par défaut) ======================
SMTP_ENABLED=false
SMTP_HOST=
SMTP_PORT=587
SMTP_USERNAME=
SMTP_PASSWORD=
SMTP_FROM_ADDRESS=
OPERATOR_EMAIL=
"@
}

# ── Registre des instances (instances.yaml) ───────────────────────────────────
# Format YAML simple et DÉTERMINISTE, écrit et lu uniquement par ces scripts. Chaque instance est
# une entrée scalaire (aucune structure imbriquée) → un lecteur ligne-à-ligne suffit et reste fiable.
$script:RegistryFields = @('name', 'editor', 'url', 'hosting', 'version', 'project', 'created_at', 'updated_at')

function Read-InstanceRegistry {
    <#
    .SYNOPSIS
        Lit le registre des instances. Fichier absent ou vide → tableau vide (état initial légitime).
    .OUTPUTS
        Tableau de PSCustomObject (un par instance déclarée).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return @() }

    $instances = New-Object System.Collections.Generic.List[object]
    $current = $null
    foreach ($rawLine in (Get-Content -LiteralPath $Path)) {
        $line = $rawLine.TrimEnd()
        if ($line -match '^\s*#' -or $line.Trim().Length -eq 0) { continue }   # commentaire / ligne vide
        if ($line -match '^\s*instances\s*:\s*$') { continue }                  # en-tête de liste

        if ($line -match '^\s*-\s*name\s*:\s*(.*)$') {
            if ($null -ne $current) { $instances.Add([PSCustomObject]$current) }
            $current = [ordered]@{}
            foreach ($f in $script:RegistryFields) { $current[$f] = '' }
            $current['name'] = (Unprotect-YamlScalar $Matches[1])
            continue
        }
        if ($line -match '^\s+([a-z_]+)\s*:\s*(.*)$' -and $null -ne $current) {
            $key = $Matches[1]
            if ($script:RegistryFields -contains $key) {
                $current[$key] = (Unprotect-YamlScalar $Matches[2])
            }
        }
    }
    if ($null -ne $current) { $instances.Add([PSCustomObject]$current) }
    return $instances.ToArray()
}

function Unprotect-YamlScalar {
    # Retire les guillemets entourants d'un scalaire YAML simple émis par ce module.
    param([string]$Value)
    $v = ([string]$Value).Trim()
    if ($v.Length -ge 2 -and $v.StartsWith('"') -and $v.EndsWith('"')) {
        $v = $v.Substring(1, $v.Length - 2).Replace('\"', '"')
    }
    return $v
}

function Protect-YamlScalar {
    # Émet un scalaire YAML : toujours entre guillemets (robuste pour URLs, dates, espaces).
    param([string]$Value)
    return '"' + ([string]$Value).Replace('"', '\"') + '"'
}

function Write-InstanceRegistry {
    <#
    .SYNOPSIS
        Réécrit intégralement le registre à partir d'un tableau d'instances (round-trip déterministe).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Instances
    )

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('# Registre des instances opérées par IT Innovations — OPS02 (F12 §6.3).')
    [void]$sb.AppendLine('# DONNÉES OPÉRATEUR — NON versionné (.gitignore). Tenu à jour par new-instance.ps1 /')
    [void]$sb.AppendLine('# update-instance.ps1. Les instances self-hosted n''y figurent que si l''éditeur a')
    [void]$sb.AppendLine('# souscrit la méta-supervision (OPS04). Aucun secret ici (clés/mots de passe → .env).')
    [void]$sb.AppendLine('instances:')

    $sorted = @($Instances | Sort-Object -Property name)
    foreach ($inst in $sorted) {
        [void]$sb.AppendLine("  - name: $(Protect-YamlScalar $inst.name)")
        foreach ($f in ($script:RegistryFields | Where-Object { $_ -ne 'name' })) {
            $val = if ($inst.PSObject.Properties.Name -contains $f) { $inst.$f } else { '' }
            [void]$sb.AppendLine("    ${f}: $(Protect-YamlScalar $val)")
        }
    }

    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    # UTF-8 sans BOM : le registre est un fichier de données YAML, pas un .ps1.
    [System.IO.File]::WriteAllText($Path, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
}

function Set-InstanceRegistryEntry {
    <#
    .SYNOPSIS
        Insère ou met à jour (par nom) une instance dans le registre, puis le réécrit.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][hashtable]$Entry
    )

    $existing = @(Read-InstanceRegistry -Path $Path)
    $kept = @($existing | Where-Object { $_.name -ne $Entry['name'] })
    $merged = [ordered]@{}
    foreach ($f in $script:RegistryFields) { $merged[$f] = if ($Entry.ContainsKey($f)) { [string]$Entry[$f] } else { '' } }
    $all = @($kept) + @([PSCustomObject]$merged)
    Write-InstanceRegistry -Path $Path -Instances $all
}

# ── Enveloppes Docker Compose ──────────────────────────────────────────────────
function Test-DockerAvailable {
    <#
    .SYNOPSIS
        Vérifie la présence de Docker + plugin Compose v2. Lève une exception française sinon.
    #>
    [CmdletBinding()] param()
    $null = & docker compose version 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker (avec le plugin Compose v2) est requis et introuvable. Installez Docker Engine >= 24 " +
              "puis vérifiez « docker compose version »."
    }
}

function Get-InstanceDatabases {
    <#
    .SYNOPSIS
        Énumère les bases d'une instance : base SYSTÈME (« liakont ») + une par tenant ACTIF.
    .DESCRIPTION
        Interroge outbox.tenants dans la base système via le conteneur postgres de l'instance (psql).
        Source de vérité de la boucle de migration et de la sauvegarde par base (OPS02 pt 4).
    .OUTPUTS
        Tableau de noms de bases (la base système en premier).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string[]]$ComposeArgs,
        [string]$SystemDatabase = 'liakont',
        [string]$PostgresUser = 'liakont'
    )

    $query = 'SELECT database_name FROM outbox.tenants WHERE is_active = true ORDER BY database_name;'
    $psqlArgs = $ComposeArgs + @('exec', '-T', 'postgres', 'psql', '-U', $PostgresUser, '-d', $SystemDatabase,
        '-t', '-A', '-c', $query)
    $output = & docker @psqlArgs 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Impossible d'énumérer les bases tenant (psql sur le conteneur postgres). L'instance est-elle démarrée ?"
    }
    $tenantDbs = @($output | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
    return @($SystemDatabase) + $tenantDbs
}

function Backup-InstanceDatabases {
    <#
    .SYNOPSIS
        Sauvegarde PRÉ-MIGRATION par base (pg_dump) : système + chaque tenant, dans un dossier horodaté.
    .DESCRIPTION
        Sauvegarde obligatoire avant toute montée de version (OPS02 pt 4). Granularité PAR BASE pour
        permettre une restauration sélective (rollback ciblé). Indépendante de l'outillage de
        sauvegarde routinière/rotation/PRA porté par OPS01b : il s'agit ici d'un instantané de sûreté.
    .OUTPUTS
        Chemin du dossier de sauvegarde créé.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string[]]$ComposeArgs,
        [Parameter(Mandatory = $true)][string[]]$Databases,
        [Parameter(Mandatory = $true)][string]$OutputDir,
        [string]$PostgresUser = 'liakont'
    )

    if (-not (Test-Path -LiteralPath $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }

    foreach ($db in $Databases) {
        $target = Join-Path $OutputDir "$db.sql"
        $dumpArgs = $ComposeArgs + @('exec', '-T', 'postgres', 'pg_dump', '-U', $PostgresUser, '-d', $db)
        # pg_dump écrit le dump sur stdout ; on le redirige vers le fichier hôte.
        & docker @dumpArgs > $target 2>$null
        if ($LASTEXITCODE -ne 0) {
            throw "Échec de la sauvegarde pré-migration de la base « $db ». Migration ANNULÉE (aucune montée " +
                  "de version sans sauvegarde — OPS02 pt 4)."
        }
        if (-not (Test-Path -LiteralPath $target) -or (Get-Item -LiteralPath $target).Length -eq 0) {
            throw "Sauvegarde de « $db » vide ou absente ($target). Migration ANNULÉE (anti-faux-vert)."
        }
    }
    return $OutputDir
}

function Wait-InstanceHealthy {
    <#
    .SYNOPSIS
        Attend que le service Host soit STABLEMENT démarré (ou conclut à l'échec de démarrage).
    .DESCRIPTION
        Sans endpoint /health applicatif, la santé se déduit de l'état du conteneur Host : « running »
        de façon STABLE pendant StableSeconds = démarrage réussi (migrations appliquées). « restarting »
        ou « exited » = le Host a avorté son démarrage — typiquement une migration tenant en échec
        (AppBootstrap.InitializeDataAsync relève l'AggregateException de MigrateExistingTenantsAsync,
        ce qui interrompt le démarrage : aucune requête servie sur une base à demi-migrée).
    .OUTPUTS
        PSCustomObject { Healthy [bool]; State [string]; Detail [string] }
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string[]]$ComposeArgs,
        [string]$Service = 'liakont',
        [int]$TimeoutSeconds = 180,
        [int]$StableSeconds = 15
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $stableSince = $null
    $lastState = 'unknown'
    while ((Get-Date) -lt $deadline) {
        $idArgs = $ComposeArgs + @('ps', '-q', $Service)
        $containerId = (& docker @idArgs 2>$null | Select-Object -First 1)
        if ($containerId) {
            $state = (& docker inspect -f '{{.State.Status}}' $containerId 2>$null | Select-Object -First 1)
            $restarting = (& docker inspect -f '{{.State.Restarting}}' $containerId 2>$null | Select-Object -First 1)
            $lastState = "$state"
            if ($state -eq 'exited' -or $state -eq 'dead') {
                return [PSCustomObject]@{ Healthy = $false; State = $state
                    Detail = "Le conteneur « $Service » s'est arrêté (état « $state ») — démarrage du Host avorté." }
            }
            if ($state -eq 'running' -and "$restarting" -ne 'true') {
                if ($null -eq $stableSince) { $stableSince = Get-Date }
                elseif (((Get-Date) - $stableSince).TotalSeconds -ge $StableSeconds) {
                    return [PSCustomObject]@{ Healthy = $true; State = 'running'
                        Detail = "Conteneur « $Service » stable depuis $StableSeconds s." }
                }
            }
            else {
                $stableSince = $null   # « restarting » remet le compteur de stabilité à zéro
            }
        }
        Start-Sleep -Seconds 3
    }
    return [PSCustomObject]@{ Healthy = $false; State = $lastState
        Detail = "Délai dépassé ($TimeoutSeconds s) sans démarrage stable du Host (dernier état « $lastState »)." }
}

Export-ModuleMember -Function `
    Write-Step, Write-Ok, Write-WarnMsg, Write-ErrMsg, `
    Resolve-InstanceName, New-StrongSecret, New-InstanceEnvContent, `
    Read-InstanceRegistry, Write-InstanceRegistry, Set-InstanceRegistryEntry, `
    Protect-YamlScalar, Unprotect-YamlScalar, `
    Test-DockerAvailable, Get-InstanceDatabases, Backup-InstanceDatabases, Wait-InstanceHealthy
