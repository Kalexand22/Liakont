#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fin de vie d'un tenant Liakont — séquence D7 (OPS06c, F12 §6.3). Désactivation logique → export
    complet VÉRIFIÉ (ArchiveVerifier vert) → transfert de responsabilité documenté → suppression
    encadrée, journalisée de façon IRRÉVERSIBLE au niveau de l'INSTANCE (hors base tenant).
.DESCRIPTION
    Réalise la décision D7 (tasks/decisions.md, 2026-06-03) : concilier la résiliation/RGPD avec le
    coffre WORM et la conservation fiscale 10 ans. PAS de suppression sans preuve que le client a
    récupéré une archive INTÈGRE. Deux actions :

      DÉSACTIVATION (par défaut) :
        marque le tenant inactif dans le catalogue système (outbox.tenants.is_active = false) ET, au
        mieux, suspend le service live (tenantsettings.tenant_profiles.statut = 1 « Suspendu », statut
        OPS03). Plus de push, plus de connexion ; les DONNÉES restent INTACTES. État réversible — et
        LÉGITIME EN PERMANENCE : un tenant résilié qui ne demande pas l'effacement reste désactivé
        indéfiniment (étape 1 seule, décision D7 point 6).

      SUPPRESSION (-Delete) : la séquence D7 COMPLÈTE, dans cet ordre strict :
        1. désactivation (ci-dessus) ;
        2. EXPORT complet VÉRIFIÉ : l'export de réversibilité (-VerifiedExportPath, produit par OPS06a)
           est re-contrôlé par tools/verifier-integrite-archive.ps1 (TRK06/TRK05). VERDICT vert exigé ;
        3. transfert de responsabilité documenté : la notice rappelle que les obligations de
           conservation fiscale (10 ans) passent au CLIENT avec l'export ;
        4. double confirmation + saisie EXACTE du nom du tenant ;
        5. audit d'INSTANCE écrit AVANT la suppression (qui, quand, référence de l'export vérifié,
           destinataire) — fichier HÔTE qui SURVIT à la suppression de la base tenant ;
        6. DROP de la base tenant + retrait du catalogue système.

    « Bloquer plutôt qu'envoyer faux » (CLAUDE.md n°3) : la suppression est REFUSÉE si l'export est
    ALTÉRÉ (VERDICT TAMPERED), PARTIEL (VERDICT INCOMPLETE — il faut l'export de réversibilité COMPLET),
    absent / illisible, ou si la confirmation ne correspond pas au nom du tenant. Tant que toutes les
    gardes ne sont pas vertes, AUCUNE action destructrice n'est entreprise (l'export n'est que LU).

    Messages opérateur en français (CLAUDE.md n°12). Aucune donnée client dans le code : instance,
    tenant et export sont des paramètres OPÉRATEUR ; l'audit d'instance est écrit sous instances/
    (gitignoré, données opérateur — CLAUDE.md n°7).
.PARAMETER InstanceName
    Nom de l'instance (projet Docker Compose) hébergeant le tenant (telle que créée par new-instance.ps1).
.PARAMETER Tenant
    Identifiant du tenant à traiter — comparé à outbox.tenants.id, display_name ou database_name
    (résolution UNIQUE exigée). C'est aussi le nom à RETAPER pour confirmer une suppression.
.PARAMETER Delete
    Effectue la SUPPRESSION (séquence D7 complète). Sans ce commutateur, le script se limite à la
    DÉSACTIVATION (sûre, réversible, permanente si le client ne demande pas l'effacement).
.PARAMETER VerifiedExportPath
    (Suppression) Chemin de l'export de réversibilité (dossier décompressé OU .zip) produit par OPS06a,
    re-vérifié avant toute suppression. VERDICT vert (OK ou coffre vide) exigé.
.PARAMETER Operator
    (Suppression) Identité de l'opérateur (e-mail/login) consignée dans l'audit d'instance (le « qui »).
.PARAMETER Recipient
    (Suppression) Destinataire de l'export (le client qui reçoit l'archive et assume la conservation).
    Consigné dans l'audit d'instance (D7 : « l'opérateur conserve la confirmation … destinataire »).
.PARAMETER ConfirmTenantName
    (Suppression, non interactif) Re-saisie EXACTE du nom du tenant (-Tenant) valant 2e confirmation.
.PARAMETER Yes
    (Suppression, non interactif) 1re confirmation de l'action irréversible (équivaut au « OUI » saisi).
.PARAMETER InstancesRoot
    Racine des répertoires d'instance (défaut : deploy/provisioning/instances). Gitignoré.
.PARAMETER AuditPath
    Chemin du journal d'audit d'instance (défaut : <InstancesRoot>/<instance>/tenant-decommission-audit.jsonl).
.PARAMETER SystemDatabase
    Base système (défaut « liakont »).
.PARAMETER PostgresUser
    Rôle PostgreSQL (défaut « liakont »).
.PARAMETER DryRun
    N'écrit/ne supprime rien : affiche la séquence qui SERAIT exécutée (après les gardes), puis sort en 0.
.EXAMPLE
    # Désactivation logique (résiliation sans demande d'effacement) :
    ./decommission-tenant.ps1 -InstanceName acme-prod -Tenant acme
.EXAMPLE
    # Suppression complète (séquence D7), non interactif :
    ./decommission-tenant.ps1 -InstanceName acme-prod -Tenant acme -Delete `
        -VerifiedExportPath ./exports/acme-reversibilite.zip -Operator ops@itinnov.example `
        -Recipient dpo@acme.example -Yes -ConfirmTenantName acme
.EXAMPLE
    ./decommission-tenant.ps1 -InstanceName acme-prod -Tenant acme -Delete -VerifiedExportPath ./e -Operator o -DryRun -Yes -ConfirmTenantName acme
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstanceName,
    [Parameter(Mandatory = $true)][string]$Tenant,
    [switch]$Delete,
    [string]$VerifiedExportPath,
    [string]$Operator,
    [string]$Recipient,
    [string]$ConfirmTenantName,
    [switch]$Yes,
    [string]$InstancesRoot,
    [string]$AuditPath,
    [string]$SystemDatabase = 'liakont',
    [string]$PostgresUser = 'liakont',
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Les appels natifs autonomes (docker, psql, vérifieur …) ne doivent pas lever sur code non-zéro —
# la gestion explicite via $LASTEXITCODE s'en charge (pwsh 7.4+ : défaut $true).
$PSNativeCommandUseErrorActionPreference = $false

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Import-Module (Join-Path $scriptRoot 'Provisioning.psm1') -Force

$repoRootDefault = Split-Path -Parent (Split-Path -Parent $scriptRoot)
$verifierScript = Join-Path $repoRootDefault 'tools\verifier-integrite-archive.ps1'
if (-not $InstancesRoot) { $InstancesRoot = Join-Path $scriptRoot 'instances' }

$script:DbNamePattern = '^[A-Za-z0-9_]+$'

# ── Helpers ─────────────────────────────────────────────────────────────────────
function Get-PsExe { if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell' } }

function ConvertTo-SqlLiteral {
    # Échappe une valeur pour un littéral SQL entre apostrophes (anti-injection sur les chaînes libres).
    param([string]$Value)
    return ([string]$Value).Replace("'", "''")
}

function Get-ExportVerdict {
    <#
    .SYNOPSIS
        Re-vérifie l'intégrité d'un export de réversibilité via tools/verifier-integrite-archive.ps1.
    .OUTPUTS
        PSCustomObject { Verdict ; ExitCode ; Output } — Verdict ∈ { OK, EMPTY, INCOMPLETE, TAMPERED, '' }.
    #>
    param([string]$ExportPath, [string]$VerifierScript)

    if (-not (Test-Path -LiteralPath $VerifierScript)) {
        throw "Vérifieur d'intégrité introuvable ($VerifierScript) — l'outil de réversibilité (OPS06a/TRK06) doit être présent."
    }

    $work = $null
    try {
        $target = $ExportPath
        if ((Test-Path -LiteralPath $ExportPath -PathType Leaf) -and
            ([System.IO.Path]::GetExtension($ExportPath) -ieq '.zip')) {
            $work = Join-Path ([System.IO.Path]::GetTempPath()) ("liakont-export-verify-" + [System.Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $work -Force | Out-Null
            Expand-Archive -LiteralPath $ExportPath -DestinationPath $work -Force
            $target = $work
        }
        $psExe = Get-PsExe
        # 2>&1 + Out-String : on capture la sortie complète du vérifieur (le marqueur « VERDICT=… » est
        # sur stdout) sans la laisser polluer la valeur de retour. Le code de sortie est lu via $LASTEXITCODE.
        $output = & $psExe -NoProfile -ExecutionPolicy Bypass -File $VerifierScript -ExportPath $target 2>&1 | Out-String
        $code = $LASTEXITCODE
        $verdict = if ($output -match 'VERDICT=([A-Z]+)') { $Matches[1] } else { '' }
        return [PSCustomObject]@{ Verdict = $verdict; ExitCode = $code; Output = $output }
    }
    finally {
        if ($work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

function Invoke-PsqlScalar {
    # Exécute une requête lisant une (ou plusieurs) ligne(s) sur le conteneur postgres de l'instance.
    param([string[]]$ComposeArgs, [string]$Database, [string]$Sql, [string]$User, [string]$FieldSep = '|')
    $psqlArgs = $ComposeArgs + @('exec', '-T', 'postgres', 'psql', '-U', $User, '-d', $Database,
        '-v', 'ON_ERROR_STOP=1', '-t', '-A', '-F', $FieldSep, '-c', $Sql)
    $out = & docker @psqlArgs 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Échec d'une requête PostgreSQL sur « $Database » (l'instance est-elle démarrée ?)."
    }
    return @($out | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_.Length -gt 0 })
}

function Invoke-PsqlCommand {
    # Exécute une commande d'écriture sur le conteneur postgres de l'instance. Renvoie le code de sortie.
    param([string[]]$ComposeArgs, [string]$Database, [string]$Sql, [string]$User, [switch]$AllowFailure)
    $psqlArgs = $ComposeArgs + @('exec', '-T', 'postgres', 'psql', '-U', $User, '-d', $Database,
        '-v', 'ON_ERROR_STOP=1', '-c', $Sql)
    # | Out-Host : la sortie de psql (ex. « UPDATE 1 ») est AFFICHÉE mais ne pollue pas la valeur de
    # retour (sinon $code deviendrait un tableau et la garde « -ne 0 » fauterait). Pas de 2>&1 (risqué WinPS 5.1).
    & docker @psqlArgs 2>$null | Out-Host
    $code = $LASTEXITCODE
    if ($code -ne 0 -and -not $AllowFailure) {
        throw "Échec d'une commande PostgreSQL sur « $Database » : $Sql"
    }
    return $code
}

function Write-TransferNotice {
    param([string]$TenantLabel, [string]$Recipient)
    $dest = if ($Recipient) { $Recipient } else { '<destinataire>' }
    Write-Host ''
    Write-Host '  ── Transfert de responsabilité de conservation (décision D7) ──' -ForegroundColor Yellow
    Write-Host "  Avec l'export de réversibilité du tenant « $TenantLabel », les obligations de" -ForegroundColor Yellow
    Write-Host '  CONSERVATION FISCALE (10 ans) passent au CLIENT destinataire. Après la suppression,' -ForegroundColor Yellow
    Write-Host "  IT Innovations ne conserve plus les données fiscales de ce tenant — seule subsiste la" -ForegroundColor Yellow
    Write-Host "  trace d'audit d'instance (qui, quand, référence de l'export vérifié, destinataire)." -ForegroundColor Yellow
    Write-Host "  Destinataire de l'export : $dest" -ForegroundColor Yellow
    Write-Host ''
}

# ════════════════════════════════════════════════════════════════════════════════
function Invoke-Decommission {
    $instance = Resolve-InstanceName -Name $InstanceName
    $instanceDir = Join-Path $InstancesRoot $instance.Name
    $composeFile = Join-Path $instanceDir 'docker-compose.yml'
    if (-not $AuditPath) { $AuditPath = Join-Path $instanceDir 'tenant-decommission-audit.jsonl' }

    $action = if ($Delete) { 'SUPPRESSION (séquence D7)' } else { 'DÉSACTIVATION' }
    Write-Step "Fin de vie d'un tenant — $action : tenant « $Tenant » sur l'instance « $($instance.Name) »"

    if ([string]::IsNullOrWhiteSpace($Tenant)) {
        throw "Identifiant de tenant vide. Indiquez -Tenant (id, nom ou base du tenant)."
    }

    # ── Gardes HORS Docker d'abord (codes de sortie testables sans Docker) ──
    $export = $null
    if ($Delete) {
        if ([string]::IsNullOrWhiteSpace($VerifiedExportPath)) {
            throw "Suppression REFUSÉE : aucun export de réversibilité fourni (-VerifiedExportPath). " +
                  "La décision D7 interdit toute suppression sans preuve que le client a récupéré une archive intègre."
        }
        if (-not (Test-Path -LiteralPath $VerifiedExportPath)) {
            throw "Suppression REFUSÉE : export de réversibilité introuvable ($VerifiedExportPath)."
        }
        if ([string]::IsNullOrWhiteSpace($Operator)) {
            throw "Suppression REFUSÉE : identité de l'opérateur requise (-Operator) — l'audit d'instance doit consigner le « qui »."
        }

        # ── Garde D7 n°2 : l'ArchiveVerifier doit être VERT sur l'export ──
        Write-Step '1/2 Vérification de l''intégrité de l''export de réversibilité (ArchiveVerifier — TRK06/TRK05)'
        $export = Get-ExportVerdict -ExportPath $VerifiedExportPath -VerifierScript $verifierScript
        switch ($export.Verdict) {
            'OK'    { Write-Ok "Export INTÈGRE (VERDICT=OK) — la chaîne d'empreintes est complète et ancrée." }
            'EMPTY' { Write-Ok "Coffre d'archive VIDE (VERDICT=EMPTY) — aucune pièce fiscale à restituer, rien à signaler." }
            'TAMPERED' {
                throw "Suppression REFUSÉE : export ALTÉRÉ (VERDICT=TAMPERED). L'ArchiveVerifier a détecté une " +
                      "incohérence — le client ne dispose pas d'une archive intègre. Reproduisez un export sain avant toute suppression."
            }
            'INCOMPLETE' {
                throw "Suppression REFUSÉE : export PARTIEL (VERDICT=INCOMPLETE — chaîne non ancrée en genèse). " +
                      "Fournissez l'export de RÉVERSIBILITÉ COMPLET du tenant (et non un export de contrôle fiscal partiel)."
            }
            default {
                throw "Suppression REFUSÉE : intégrité de l'export NON vérifiable (VERDICT « $($export.Verdict) », " +
                      "code $($export.ExitCode)). Vérifiez le chemin et le contenu de l'export."
            }
        }

        # ── Garde D7 n°3 : transfert de responsabilité documenté ──
        Write-TransferNotice -TenantLabel $Tenant -Recipient $Recipient

        # ── Garde D7 n°4 : double confirmation + saisie EXACTE du nom du tenant ──
        $interactive = [Environment]::UserInteractive -and -not $Yes -and -not $ConfirmTenantName
        $ack = $false
        if ($Yes) { $ack = $true }
        elseif ($interactive) {
            $r = Read-Host "Suppression DÉFINITIVE et IRRÉVERSIBLE du tenant « $Tenant ». Tapez OUI pour continuer"
            $ack = ($r -ceq 'OUI')
        }
        if (-not $ack) {
            throw "Suppression non confirmée (1re confirmation manquante). Aucune action — le tenant est inchangé."
        }
        $typed = if ($ConfirmTenantName) { $ConfirmTenantName }
                 elseif ($interactive) { Read-Host "Pour confirmer, saisissez EXACTEMENT le nom du tenant à supprimer" }
                 else { '' }
        if ($typed -cne $Tenant) {
            throw "Suppression REFUSÉE : le nom saisi (« $typed ») ne correspond pas au tenant « $Tenant ». Aucune action."
        }
    }

    if ($DryRun) {
        Write-WarnMsg 'DryRun : aucune action. Séquence qui SERAIT exécutée :'
        Write-Host "    1. Désactivation : outbox.tenants.is_active = false + tenant_profiles.statut = 1 (Suspendu, best-effort) ;"
        if ($Delete) {
            Write-Host "    2. (export déjà VÉRIFIÉ : VERDICT=$($export.Verdict)) ;"
            Write-Host "    3. Écriture de l'audit d'instance AVANT suppression ($AuditPath) ;"
            Write-Host "    4. DROP de la base du tenant + retrait de outbox.tenants."
        }
        else {
            Write-Host "    (désactivation seule — légitime et permanente ; relancez avec -Delete pour la suppression D7)."
        }
        Write-Ok 'DryRun terminé.'
        return 0
    }

    # ── Opérations Docker / PostgreSQL ──
    if (-not (Test-Path -LiteralPath $composeFile)) {
        throw "Instance inconnue : docker-compose.yml introuvable ($composeFile). Vérifiez -InstanceName / -InstancesRoot."
    }
    Test-DockerAvailable
    $composeArgs = @('compose', '-p', $instance.ProjectName, '--project-directory', $instanceDir, '-f', $composeFile)

    # ── Résolution du tenant dans le catalogue système (résolution UNIQUE) ──
    $lit = ConvertTo-SqlLiteral $Tenant
    $resolveSql = "SELECT id, database_name, coalesce(company_id::text,''), is_active, coalesce(display_name,'') " +
                  "FROM outbox.tenants WHERE id='$lit' OR display_name='$lit' OR database_name='$lit'"
    $rows = @(Invoke-PsqlScalar -ComposeArgs $composeArgs -Database $SystemDatabase -Sql $resolveSql -User $PostgresUser)
    if ($rows.Count -eq 0) {
        throw "Tenant « $Tenant » introuvable dans outbox.tenants de l'instance « $($instance.Name) ». Aucune action."
    }
    if ($rows.Count -gt 1) {
        throw "Tenant « $Tenant » AMBIGU ($($rows.Count) correspondances dans outbox.tenants). Précisez l'identifiant exact."
    }
    $parts = $rows[0] -split '\|', 5
    $tenantId = $parts[0]
    $tenantDb = $parts[1]
    $companyId = $parts[2]
    $displayName = if ($parts.Count -ge 5) { $parts[4] } else { '' }
    if ([string]::IsNullOrWhiteSpace($tenantDb) -or ($tenantDb -notmatch $script:DbNamePattern)) {
        throw "Nom de base du tenant inattendu (« $tenantDb ») — refus par sécurité (identifiant non conforme à $($script:DbNamePattern))."
    }
    $tenantIdLit = ConvertTo-SqlLiteral $tenantId

    # ── Étape 1 : DÉSACTIVATION logique (catalogue + statut live best-effort) ──
    Write-Step "Désactivation logique du tenant « $tenantId » (base « $tenantDb »)"
    [void](Invoke-PsqlCommand -ComposeArgs $composeArgs -Database $SystemDatabase -User $PostgresUser `
        -Sql "UPDATE outbox.tenants SET is_active = false WHERE id = '$tenantIdLit'")
    # Suspension live (403 console / 503 agents) : statut OPS03 = Suspendu. Best-effort — la table peut
    # être absente d'une base réduite ; l'inactivation du catalogue ci-dessus reste le levier décisif.
    $suspCode = Invoke-PsqlCommand -ComposeArgs $composeArgs -Database $tenantDb -User $PostgresUser `
        -Sql 'UPDATE tenantsettings.tenant_profiles SET statut = 1' -AllowFailure
    if ($suspCode -ne 0) {
        Write-WarnMsg "Statut live « Suspendu » non posé sur la base tenant (table tenant_profiles absente ?) — désactivation du catalogue retenue."
    }
    Write-Ok "Tenant désactivé : plus de service, données intactes (is_active = false)."

    if (-not $Delete) {
        Write-Host ''
        Write-Ok ("Désactivation terminée. Cet état est LÉGITIME et PERMANENT tant que le client ne demande pas " +
                  "l'effacement. Pour la suppression complète (séquence D7), relancez avec -Delete et l'export vérifié.")
        return 0
    }

    # ── Étape 5 : AUDIT d'INSTANCE écrit AVANT la suppression (survit à la base supprimée) ──
    $exportSha = ''
    if (Test-Path -LiteralPath $VerifiedExportPath -PathType Leaf) {
        $exportSha = (Get-FileHash -LiteralPath $VerifiedExportPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $nowIso = [System.DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    $auditEntry = [ordered]@{
        event               = 'tenant-decommissioned'
        occurred_at         = $nowIso
        instance            = $instance.Name
        operator            = $Operator
        recipient           = [string]$Recipient
        tenant_id           = $tenantId
        tenant_display_name = $displayName
        tenant_database     = $tenantDb
        company_id          = $companyId
        export_path         = $VerifiedExportPath
        export_verdict      = $export.Verdict
        export_sha256       = $exportSha
        export_verified_at  = $nowIso
    }
    Add-TenantDecommissionAuditEntry -Path $AuditPath -Entry $auditEntry
    Write-Ok "Audit d'instance écrit (IRRÉVERSIBLE, survit à la suppression) : $AuditPath"

    # ── Étape 6 : DROP de la base tenant + retrait du catalogue ──
    Write-Step "Suppression de la base du tenant « $tenantDb »"
    # Fermer les connexions résiduelles AVANT le DROP (le tenant est désactivé → pas de nouveau trafic légitime).
    [void](Invoke-PsqlCommand -ComposeArgs $composeArgs -Database $SystemDatabase -User $PostgresUser `
        -Sql "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$tenantDb' AND pid <> pg_backend_pid()" `
        -AllowFailure)
    # $tenantDb est validé contre $script:DbNamePattern (alphanumérique + « _ ») → interpolation sûre dans
    # l'identifiant (DROP DATABASE n'accepte ni paramètre lié ni transaction).
    [void](Invoke-PsqlCommand -ComposeArgs $composeArgs -Database $SystemDatabase -User $PostgresUser `
        -Sql "DROP DATABASE IF EXISTS ""$tenantDb"" WITH (FORCE)")
    [void](Invoke-PsqlCommand -ComposeArgs $composeArgs -Database $SystemDatabase -User $PostgresUser `
        -Sql "DELETE FROM outbox.tenants WHERE id = '$tenantIdLit'")
    Write-Ok "Base « $tenantDb » supprimée et tenant retiré du catalogue. La trace d'audit d'instance subsiste."

    Write-Host ''
    Write-Ok ("Fin de vie du tenant « $tenantId » terminée (séquence D7). Conservez l'audit d'instance ($AuditPath) " +
              "comme preuve de fin de conservation.")
    return 0
}

# ── Entrée ──────────────────────────────────────────────────────────────────────
try {
    exit (Invoke-Decommission)
}
catch {
    Write-ErrMsg $_.Exception.Message
    exit 1
}
