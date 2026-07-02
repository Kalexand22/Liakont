#!/usr/bin/env pwsh
# ============================================================================
#  demo.ps1  (ASCII-only : PS 5.1 lit les .ps1 en Win1252 -- pas d'accents ici ;
#             le detail accentue vit dans README.md)
#
#  Orchestrateur de la DEMO observable "Encheres" e-reporting B2C marge :
#    base source SQL Server (EncheresV6_Demo) -> agent ODBC lecture seule
#    -> plateforme -> CHECK -> job B4 (agregation marge) -> emission -> console.
#
#  Ce script automatise UNIQUEMENT la partie infra DETERMINISTE et sans secret :
#    - 'source'        : (re)construit la base source + injecte les SIREN demo +
#                        cree/maj le login SQL LECTURE SEULE de l'agent.
#    - 'agent-config'  : genere les 2 agent.json (gabarits) + imprime la chaine
#                        ODBC en clair a CHIFFRER (DPAPI) sur le poste de l'agent.
#    - 'status'        : etat de la base source (presence des donnees, login RO).
#    - 'help' (defaut) : runbook condense (le detail complet = README.md).
#
#  Le reste du parcours (creation des 2 tenants, saisie des secrets PA, enrolement
#  des agents, declenchement B4, observation) est PILOTE PAR LA CONSOLE : ce sont
#  des etapes que Karl execute lui-meme. Voir README.md "Runbook".
#
#  IMPORTANT : ce script NE declenche AUCUN envoi reel SuperPDP (decision : tout
#  preparer, s'arreter avant l'envoi). La demo observe l'emission via la PA Fake
#  (en memoire) ; le swap vers SuperPDP sandbox est decrit dans le README.
# ============================================================================
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('source', 'agent-config', 'status', 'help')]
    [string] $Action = 'help',

    [string] $SqlInstance = 'localhost',
    [string] $Database    = 'EncheresV6_Demo',
    [string] $PlatformUrl = 'http://localhost:8090',
    [string] $RoLogin     = 'liakont_encheres_ro',

    # Force la re-importation du script SQL de la base source meme si les donnees
    # sont deja presentes (sinon 'source' ne re-importe pas un schema deja peuple).
    [switch] $Force,

    # Racine des fichiers GED (PDF des BA/BV) pour 'agent-config' : la copie locale du dossier
    # GED du serveur (ou le partage reel). Vide = placeholder a remplacer dans agent.json.
    [string] $GedPdfRoot = ''
)

$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot

$sourceSql  = Join-Path $dir 'encheresv6-demo-sqlserver.sql'
$sirenSql   = Join-Path $dir 'inject-demo-siren.sql'
$secretsFile = Join-Path $dir '.secrets.local.json'
$seedDir    = Join-Path $dir 'tenant-seed'
$agentDir   = Join-Path $dir 'agent'

# 1 instance d'agent = 1 dossier comptable = 1 tenant (filtre No_dossier).
$instances = @(
    [ordered]@{ Name = 'volontaire'; Dossier = '2'; Tenant = 'SVV (ventes volontaires, marge B2C)' },
    [ordered]@{ Name = 'judiciaire'; Dossier = '1'; Tenant = 'SCP (ventes judiciaires / criee, B2B)' }
)

function Resolve-Sqlcmd {
    $candidate = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
    if (Test-Path $candidate) { return $candidate }
    $cmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "sqlcmd introuvable. Installez les outils en ligne de commande SQL Server (sqlcmd)."
}

function Invoke-SqlScalar {
    param([string] $Sqlcmd, [string] $Db, [string] $Query)
    $out = & $Sqlcmd -S $SqlInstance -E -d $Db -h -1 -W -b -Q "SET NOCOUNT ON; $Query" 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return ($out | Select-Object -First 1)
}

function Invoke-SqlFile {
    param([string] $Sqlcmd, [string] $Db, [string] $File)
    & $Sqlcmd -S $SqlInstance -E -d $Db -b -l 30 -i $File
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd a echoue sur $([System.IO.Path]::GetFileName($File)) (code $LASTEXITCODE)."
    }
}

function Get-OrCreate-RoPassword {
    $pwd = $null
    if ((Test-Path $secretsFile) -and -not $Force) {
        try { $pwd = (Get-Content $secretsFile -Raw | ConvertFrom-Json).roPassword } catch { $pwd = $null }
    }
    if (-not $pwd) {
        $bytes = New-Object byte[] 18
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
        # Mot de passe fort, sans caracteres genants pour une chaine ODBC.
        $pwd = 'Enc!' + ([Convert]::ToBase64String($bytes) -replace '/', '_' -replace '\+', '-' -replace '=', '')
    }
    return $pwd
}

function Get-OdbcString {
    param([string] $Pwd)
    return "Driver={ODBC Driver 17 for SQL Server};Server=$SqlInstance;Database=$Database;UID=$RoLogin;PWD=$Pwd;TrustServerCertificate=yes;"
}

function Do-Source {
    $sqlcmd = Resolve-Sqlcmd
    if (-not (Test-Path $sourceSql)) {
        throw "Base source absente : $sourceSql`n  Generez-la d'abord : build-sqlserver-from-samples.ps1 -SamplesDir <...>\EncheresExtract\samples"
    }

    Write-Host "== Base source ($Database) sur $SqlInstance ==" -ForegroundColor Cyan
    # CREATE DATABASE idempotent (master).
    & $sqlcmd -S $SqlInstance -E -b -Q "IF DB_ID('$Database') IS NULL CREATE DATABASE [$Database];"
    if ($LASTEXITCODE -ne 0) { throw "Creation de la base $Database echouee (code $LASTEXITCODE)." }

    # Re-import seulement si la table maitresse est absente, ou si -Force.
    $hasData = Invoke-SqlScalar -Sqlcmd $sqlcmd -Db $Database -Query "IF OBJECT_ID('enc.ligne_pv','U') IS NULL SELECT 'NO' ELSE SELECT 'YES';"
    if ($Force -or $hasData -ne 'YES') {
        Write-Host "Import du schema + donnees (encheresv6-demo-sqlserver.sql)..." -ForegroundColor Cyan
        Invoke-SqlFile -Sqlcmd $sqlcmd -Db $Database -File $sourceSql
    }
    else {
        Write-Host "Donnees deja presentes (enc.ligne_pv) -- import saute (utilisez -Force pour reconstruire)." -ForegroundColor DarkGray
    }

    Write-Host "Injection des SIREN demo (inject-demo-siren.sql)..." -ForegroundColor Cyan
    Invoke-SqlFile -Sqlcmd $sqlcmd -Db $Database -File $sirenSql

    # Login SQL LECTURE SEULE de l'agent (CLAUDE.md n.5 : aucune ecriture sur la base source).
    Write-Host "Login lecture seule [$RoLogin] (db_datareader, aucun droit d'ecriture)..." -ForegroundColor Cyan
    $roPwd = Get-OrCreate-RoPassword
    $escapedPwd = $roPwd -replace "'", "''"
    $roSql = @"
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$RoLogin')
    CREATE LOGIN [$RoLogin] WITH PASSWORD = N'$escapedPwd', CHECK_POLICY = OFF;
ELSE
    ALTER LOGIN [$RoLogin] WITH PASSWORD = N'$escapedPwd';
"@
    & $sqlcmd -S $SqlInstance -E -b -Q $roSql
    if ($LASTEXITCODE -ne 0) { throw "Creation/maj du login $RoLogin echouee (code $LASTEXITCODE)." }

    $roDbSql = @"
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$RoLogin')
    CREATE USER [$RoLogin] FOR LOGIN [$RoLogin];
ALTER ROLE db_datareader ADD MEMBER [$RoLogin];
"@
    & $sqlcmd -S $SqlInstance -E -d $Database -b -Q $roDbSql
    if ($LASTEXITCODE -ne 0) { throw "Attribution db_datareader a $RoLogin echouee (code $LASTEXITCODE)." }

    # Persiste le mot de passe + les chaines ODBC (fichier GITIGNORE).
    $odbcVol = Get-OdbcString -Pwd $roPwd
    [ordered]@{
        roPassword = $roPwd
        roLogin    = $RoLogin
        database   = $Database
        odbc       = $odbcVol
    } | ConvertTo-Json | Set-Content -Path $secretsFile -Encoding UTF8

    Write-Host ""
    Write-Host "Base source prete." -ForegroundColor Green
    Show-SourceSummary -Sqlcmd $sqlcmd
    Write-Host ""
    Write-Host "Chaine ODBC lecture seule (a chiffrer DPAPI sur le poste agent) ecrite dans :" -ForegroundColor Green
    Write-Host "  $secretsFile (gitignore)"
    Write-Host "  $odbcVol" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "Etape suivante : .\demo.ps1 agent-config" -ForegroundColor Yellow
}

function Show-SourceSummary {
    param([string] $Sqlcmd)
    Write-Host "Repartition des adjudications par dossier x regime (enc.ligne_pv) :" -ForegroundColor Cyan
    & $Sqlcmd -S $SqlInstance -E -d $Database -h -1 -W -Q @"
SET NOCOUNT ON;
SELECT '  D' + ISNULL(CAST(No_dossier AS varchar(4)),'?')
     + ' regime ' + ISNULL(CAST(code_regime_tva AS varchar(4)),'NULL')
     + ' : ' + CAST(COUNT(*) AS varchar(8)) + ' lot(s)'
FROM enc.ligne_pv GROUP BY No_dossier, code_regime_tva ORDER BY No_dossier, code_regime_tva;
"@ 2>$null
    Write-Host "  (regime 6 = marge B2C, regime 5 = taxable ; les autres BLOQUENT au CHECK -- fail-closed)" -ForegroundColor DarkGray
}

function Do-AgentConfig {
    if (-not (Test-Path $secretsFile)) {
        throw "Secrets absents ($secretsFile). Lancez d'abord : .\demo.ps1 source"
    }
    $roPwd = (Get-Content $secretsFile -Raw | ConvertFrom-Json).roPassword
    $odbc  = Get-OdbcString -Pwd $roPwd

    # PDF BA/BV : gedPdf n'est emis QUE si les tables GED existent reellement dans la base source
    # (build-sqlserver-from-samples.ps1 les SKIP tant que les samples GED ne sont pas revenus --
    # un gabarit gedPdf='tables' sur une base sans GED rendrait CheckHealth Unhealthy et ferait
    # avorter chaque cycle d'extraction en boucle).
    $sqlcmd = Resolve-Sqlcmd
    $gedTablesPresent = (Invoke-SqlScalar -Sqlcmd $sqlcmd -Db $Database -Query "IF OBJECT_ID('enc.GED_Relation','U') IS NULL SELECT 'NO' ELSE SELECT 'YES';") -eq 'YES'

    if (-not (Test-Path $agentDir)) { New-Item -ItemType Directory -Path $agentDir | Out-Null }

    foreach ($inst in $instances) {
        $instDir = Join-Path $agentDir $inst.Name
        if (-not (Test-Path $instDir)) { New-Item -ItemType Directory -Path $instDir | Out-Null }
        $cfgPath = Join-Path $instDir 'agent.json'

        $cfg = [ordered]@{
            '_commentaire' = "GABARIT agent.json instance '$($inst.Name)' (dossier $($inst.Dossier) = $($inst.Tenant)). " +
                "Remplacez apiKey et odbcConnectionString par leurs valeurs CHIFFREES (Liakont.Agent.Cli.exe encrypt, " +
                "execute sur CE poste -- DPAPI est lie a la machine). apiKey = cle de l'agent creee dans la console du tenant. " +
                "odbcConnectionString = chiffrement de la chaine en clair imprimee par demo.ps1 agent-config."
            'platformUrl' = $PlatformUrl
            'apiKey'      = '<<CHIFFRER: cle API de l agent (console du tenant) via Liakont.Agent.Cli.exe encrypt>>'
            'heartbeatMinutes' = 15
            'extraction' = [ordered]@{
                'adapter' = 'EncheresV6'
                'odbcConnectionString' = '<<CHIFFRER: la chaine ODBC en clair (voir sortie demo.ps1) via Liakont.Agent.Cli.exe encrypt>>'
                'schedule' = @('03:00', '13:00')
                'catchUpOnStart' = $false
            }
            'adapterConfig' = [ordered]@{
                'EncheresV6' = [ordered]@{
                    'dossier' = $inst.Dossier
                    'schema'  = 'enc'
                }
            }
        }
        if ($gedTablesPresent) {
            # PDF des bordereaux : lus via les tables GED de la base source (lecture seule).
            # gedPdfRoot remplace GED_Param_Document.Chemin_stockage (le partage du serveur
            # client) quand les fichiers sont ailleurs (copie de demo, replication).
            $cfg['adapterConfig']['EncheresV6']['gedPdf'] = 'tables'
            $cfg['adapterConfig']['EncheresV6']['gedPdfRoot'] = $(if ($GedPdfRoot) { $GedPdfRoot } else { '<<CHEMIN du dossier GED (copie locale ou partage reel) -- ou retirez cette cle pour utiliser le chemin de GED_Param_Document>>' })
        }

        $cfg | ConvertTo-Json -Depth 6 | Set-Content -Path $cfgPath -Encoding UTF8
        Write-Host ("Gabarit ecrit : {0}  (dossier {1})" -f $cfgPath, $inst.Dossier) -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "Chaine ODBC EN CLAIR a chiffrer (identique pour les 2 instances) :" -ForegroundColor Yellow
    Write-Host "  $odbc"
    Write-Host ""
    Write-Host "Pour chiffrer (sur le poste ou tournera l'agent) :" -ForegroundColor Cyan
    Write-Host '  "<chaine ODBC>" | Liakont.Agent.Cli.exe encrypt    (ou collez la chaine quand l outil la demande)'
    Write-Host "Puis collez le resultat dans extraction.odbcConnectionString des 2 agent.json."
    Write-Host ""
    if (-not $gedTablesPresent) {
        Write-Host "PDF des BA/BV : tables GED ABSENTES de la base source -- gedPdf non emis dans les gabarits." -ForegroundColor Yellow
        Write-Host "Importez les tables GED (build-sqlserver-from-samples.ps1 avec les samples GED, puis re-import)" -ForegroundColor Yellow
        Write-Host "et relancez : .\demo.ps1 agent-config" -ForegroundColor Yellow
    }
    elseif (-not $GedPdfRoot) {
        Write-Host "PDF des BA/BV (GED) : remplacez le placeholder gedPdfRoot des agent.json par le chemin du" -ForegroundColor Yellow
        Write-Host "dossier GED sur CE poste (ou relancez : .\demo.ps1 agent-config -GedPdfRoot '<chemin>')." -ForegroundColor Yellow
    }
}

function Do-Status {
    $sqlcmd = Resolve-Sqlcmd
    $dbExists = Invoke-SqlScalar -Sqlcmd $sqlcmd -Db 'master' -Query "IF DB_ID('$Database') IS NULL SELECT 'NO' ELSE SELECT 'YES';"
    if ($dbExists -eq 'YES') {
        Write-Host "Base source $Database : PRESENTE" -ForegroundColor Green
        Show-SourceSummary -Sqlcmd $sqlcmd
    }
    else {
        Write-Host "Base source $Database : ABSENTE -- lancez .\demo.ps1 source" -ForegroundColor Yellow
    }
    $roExists = Invoke-SqlScalar -Sqlcmd $sqlcmd -Db 'master' -Query "IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$RoLogin') SELECT 'YES' ELSE SELECT 'NO';"
    if ($roExists -eq 'YES') { Write-Host "Login lecture seule $RoLogin : PRESENT" -ForegroundColor Green }
    else { Write-Host "Login lecture seule $RoLogin : ABSENT -- lancez .\demo.ps1 source" -ForegroundColor Yellow }
}

function Show-Help {
    Write-Host ""
    Write-Host "===================================================================" -ForegroundColor Yellow
    Write-Host "  DEMO 'Encheres' observable -- e-reporting B2C marge (SuperPDP)" -ForegroundColor Yellow
    Write-Host "===================================================================" -ForegroundColor Yellow
    Write-Host "Actions de ce script (infra deterministe, sans secret) :"
    Write-Host "  .\demo.ps1 source         # (re)construit la base source + SIREN + login lecture seule"
    Write-Host "  .\demo.ps1 agent-config   # genere les 2 agent.json (gabarits) + chaine ODBC a chiffrer"
    Write-Host "  .\demo.ps1 status         # etat de la base source"
    Write-Host ""
    Write-Host "Parcours complet (detail + commandes exactes) : voir README.md 'Runbook'." -ForegroundColor Cyan
    Write-Host "Resume des etapes PILOTEES PAR LA CONSOLE (Karl) :" -ForegroundColor Cyan
    Write-Host "  1. Plateforme propre : deployments\bucodi\demo.ps1 reset   (Host + PG + Keycloak)"
    Write-Host "  2. Console (sysadmin) -> Clients -> creer 2 tenants : volontaire (SVV) et judiciaire (SCP)"
    Write-Host "     IDENTITE LEGALE (SIREN + raison sociale) saisie A LA MAIN -- jamais seedee (BUG-14)."
    Write-Host "     Valeurs exactes par tenant : voir README.md (section 'tenant-seed')."
    Write-Host "  3. Par tenant : Parametrage fiscal -> saisir le mapping (cf. tenant-seed\<inst>\mapping-tva.json)"
    Write-Host "                  + Plateforme Agreee -> ajouter le compte 'Fake' (Staging)"
    Write-Host "                  + enroler un agent -> recuperer sa cle API"
    Write-Host "  4. .\demo.ps1 source ; .\demo.ps1 agent-config ; chiffrer ODBC + cle API ; installer les agents"
    Write-Host "     (deployments\demo-local\install-services.ps1 sert de modele d'installation de service)"
    Write-Host "  5. Lancer un run agent -> ingestion -> CHECK -> declencher le job B4 -> observer :"
    Write-Host "       /traitements (run B4), /documents (cas B2B / B2C / bloques)"
    Write-Host "  6. ENVOI REEL SuperPDP : NON declenche par cette demo (PA Fake en memoire). Pour le run"
    Write-Host "     reel, swap du compte PA vers SuperPDP sandbox (secrets OAuth2 en console) -- voir README."
    Write-Host "===================================================================" -ForegroundColor Yellow
}

switch ($Action) {
    'source'       { Do-Source }
    'agent-config' { Do-AgentConfig }
    'status'       { Do-Status }
    default        { Show-Help }
}
