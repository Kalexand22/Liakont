#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Crée et alimente les 2 bases sources de démonstration (DemoErpA / DemoErpB) sur le SQL Server
    local, avec un login SQL dédié EN LECTURE SEULE pour l'agent.
.DESCRIPTION
    Idempotent. Le mot de passe du login lecture seule est généré au premier run et persistant
    dans .secrets.local.json (GITIGNORÉ — jamais versionné, CLAUDE.md n°10). Les chaînes ODBC
    (avec UID/PWD) sont écrites dans ce même fichier et affichées : elles servent à configurer
    l'agent (chiffrées DPAPI dans agent.json à l'installation).
.PARAMETER Waves
    Nombre de "vagues" de 6 factures à AJOUTER (numéros auto distincts). Défaut 1.
.PARAMETER SqlInstance
    Instance SQL Server cible. Défaut 'localhost' (instance par défaut MSSQLSERVER).
.PARAMETER Reset
    Régénère le mot de passe du login (invalide les agents déjà installés).
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File deployments/demo-local/seed-demo.ps1 -Waves 1
#>
param(
    [int]$Waves = 1,
    [string]$SqlInstance = 'localhost',
    [switch]$Reset
)

$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot

$sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
if (-not (Test-Path $sqlcmd)) {
    $cmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
    if (-not $cmd) { throw "sqlcmd introuvable. Installez les outils SQL Server (sqlcmd)." }
    $sqlcmd = $cmd.Source
}

$secretsFile = Join-Path $dir '.secrets.local.json'

# Mot de passe du login lecture seule : réutilisé s'il existe (sauf -Reset), sinon généré (fort, aléatoire).
$roPwd = $null
if ((Test-Path $secretsFile) -and -not $Reset) {
    try { $roPwd = (Get-Content $secretsFile -Raw | ConvertFrom-Json).roPassword } catch { $roPwd = $null }
}
if (-not $roPwd) {
    $bytes = New-Object byte[] 18
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $roPwd = 'Demo!' + ([Convert]::ToBase64String($bytes) -replace '/', '_' -replace '\+', '-' -replace '=', '')
}

function Invoke-SqlFile {
    param([string]$File, [hashtable]$Vars = @{})
    $sqlArgs = @('-S', $SqlInstance, '-E', '-b', '-l', '15', '-i', $File)
    foreach ($k in $Vars.Keys) { $sqlArgs += @('-v', "$k=$($Vars[$k])") }
    & $sqlcmd @sqlArgs
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd a échoué sur $([System.IO.Path]::GetFileName($File)) (code $LASTEXITCODE)." }
}

Write-Host "== Création des bases + login lecture seule ==" -ForegroundColor Cyan
Invoke-SqlFile (Join-Path $dir '01-create-databases.sql') @{ RoPassword = $roPwd }

Write-Host "== Schémas ==" -ForegroundColor Cyan
Invoke-SqlFile (Join-Path $dir '02-schema-erpA.sql')
Invoke-SqlFile (Join-Path $dir '03-schema-erpB.sql')

Write-Host "== Seed ($Waves vague(s)) ==" -ForegroundColor Cyan
Invoke-SqlFile (Join-Path $dir '04-seed-erpA.sql') @{ WaveCount = $Waves }
Invoke-SqlFile (Join-Path $dir '05-seed-erpB.sql') @{ WaveCount = $Waves }

# Chaînes ODBC lecture seule (login dédié). Le mot de passe N'EST PAS versionné (fichier gitignoré).
$odbcA = "Driver={ODBC Driver 17 for SQL Server};Server=$SqlInstance;Database=LiakontDemoErpA;UID=liakont_demo_ro;PWD=$roPwd;TrustServerCertificate=yes;"
$odbcB = "Driver={ODBC Driver 17 for SQL Server};Server=$SqlInstance;Database=LiakontDemoErpB;UID=liakont_demo_ro;PWD=$roPwd;TrustServerCertificate=yes;"

[ordered]@{ roPassword = $roPwd; odbcErpA = $odbcA; odbcErpB = $odbcB } |
    ConvertTo-Json | Set-Content -Path $secretsFile -Encoding utf8

Write-Host ""
Write-Host "== Vérification ==" -ForegroundColor Cyan
& $sqlcmd -S $SqlInstance -E -h -1 -W -Q @"
SET NOCOUNT ON;
SELECT 'ErpA factures = ' + CAST(COUNT(*) AS varchar(10)) FROM LiakontDemoErpA.dbo.factures;
SELECT 'ErpA lignes   = ' + CAST(COUNT(*) AS varchar(10)) FROM LiakontDemoErpA.dbo.lignes_facture;
SELECT 'ErpB invoices = ' + CAST(COUNT(*) AS varchar(10)) FROM LiakontDemoErpB.dbo.Invoice;
SELECT 'ErpB items    = ' + CAST(COUNT(*) AS varchar(10)) FROM LiakontDemoErpB.dbo.InvoiceItem;
"@

Write-Host ""
Write-Host "Chaînes ODBC (lecture seule) écrites dans $secretsFile (gitignoré)." -ForegroundColor Green
Write-Host "  DemoErpA : $odbcA"
Write-Host "  DemoErpB : $odbcB"
