#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Ajoute N factures B2B de démo (acheteur pro adressable « Tricatel », SIREN sandbox 000000001) à la
    source DemoErpA. L'agent les extrait et les envoie automatiquement vers SuperPDP (vendeur = la company
    du compte OAuth, p.ex. Burger Queen). Données 100 % fictives (CLAUDE.md n°7).
.DESCRIPTION
    Re-jouable : facture_id / ligne_id / numéro auto (MAX+1) → toujours uniques d'un appel à l'autre.
    Le client Tricatel (client_id=1) est créé s'il manque. Le vendeur (émetteur) est rempli par la
    plateforme depuis le profil tenant — pas par ce script.
.PARAMETER Count
    Nombre de factures à ajouter (défaut 3).
.PARAMETER SqlInstance
    Instance SQL Server locale (défaut 'localhost').
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File deployments/demo-local/seed-factures-b2b.ps1 -Count 3
#>
param(
    [int]$Count = 3,
    [string]$SqlInstance = 'localhost'
)
$ErrorActionPreference = 'Stop'

$sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
if (-not (Test-Path $sqlcmd)) {
    $cmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
    if (-not $cmd) { throw "sqlcmd introuvable. Installez les outils SQL Server." }
    $sqlcmd = $cmd.Source
}

$sql = @"
USE LiakontDemoErpA;
SET NOCOUNT ON;
-- Acheteur professionnel ADRESSABLE dans l'annuaire sandbox SuperPDP (Tricatel, SIREN 000000001) :
-- créé une seule fois. Sans SIREN acheteur, SuperPDP rejette (facture B2B = destinataire identifié).
IF NOT EXISTS (SELECT 1 FROM dbo.clients WHERE client_id = 1)
    INSERT dbo.clients (client_id, raison_sociale, siren, siret, est_societe, rue, code_postal, ville, pays)
    VALUES (1, N'Tricatel', '000000001', NULL, 1, N'1 rue du Marche', '75001', N'Paris', 'FR');

DECLARE @i int = 0;
WHILE @i < $Count
BEGIN
    DECLARE @b int = ISNULL((SELECT MAX(facture_id) FROM dbo.factures), 0) + 1;
    DECLARE @l int = ISNULL((SELECT MAX(ligne_id)   FROM dbo.lignes_facture), 0) + 1;

    INSERT dbo.factures (facture_id, numero, type_piece, date_emission, client_id,
                         facture_origine_numero, total_ht, total_tva, total_ttc, devise, date_modif)
    VALUES (@b, 'BQ-2026-' + CAST(@b AS varchar(10)), 'FAC', CAST(GETDATE() AS date), 1,
            NULL, 100.00, 20.00, 120.00, 'EUR', SYSUTCDATETIME());

    INSERT dbo.lignes_facture (ligne_id, facture_id, no_ligne, designation, quantite,
                               prix_unitaire_ht, montant_ht, montant_tva, taux_tva, code_regime)
    VALUES (@l, @b, 1, N'Prestation B2B (20%)', 1.0, 100.00, 100.00, 20.00, 20.0, '20');

    SET @i = @i + 1;
END

SELECT numero AS facture_ajoutee
FROM dbo.factures
ORDER BY facture_id DESC
OFFSET 0 ROWS FETCH NEXT $Count ROWS ONLY;
"@

Write-Host "Ajout de $Count facture(s) B2B (acheteur Tricatel) a DemoErpA..." -ForegroundColor Cyan
& $sqlcmd -S $SqlInstance -E -b -h -1 -W -Q $sql
if ($LASTEXITCODE -ne 0) { throw "sqlcmd a echoue (code $LASTEXITCODE)." }
Write-Host "OK. L'agent va les extraire et les envoyer vers SuperPDP au prochain cycle (~1 min)." -ForegroundColor Green
