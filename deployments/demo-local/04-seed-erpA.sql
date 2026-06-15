-- 04-seed-erpA.sql
-- Seed RE-JOUABLE de LiakontDemoErpA (données fictives, CLAUDE.md n°7).
-- Chaque "vague" ajoute 6 factures couvrant les cas connus, avec numéros AUTO (A-2026-NNNN)
-- dérivés de l'ID (donc toujours distincts d'une exécution à l'autre — facilite les tests).
-- Variable sqlcmd : WaveCount (nombre de vagues à ajouter à cette exécution).
-- Cas couverts par vague : simple 20% · multi-lignes mixtes 20+10+5,5 · taux 0 · service 10% ·
--                          avoir->facture d'origine · 1 ligne régime 13 NON mappé (-> Blocked).
USE LiakontDemoErpA;
GO
SET NOCOUNT ON;

-- Acheteurs B2C — insérés une seule fois (l'émetteur, lui, vient de la config de l'agent, pas de la source).
IF NOT EXISTS (SELECT 1 FROM dbo.clients)
    INSERT dbo.clients (client_id, raison_sociale, siren, siret, est_societe, rue, code_postal, ville, pays)
    VALUES (1, N'Jean Dupont',  NULL, NULL, 0, N'2 rue des Lilas',  '35000', N'Rennes', 'FR'),
           (2, N'Marie Martin', NULL, NULL, 0, N'8 avenue du Parc', '44000', N'Nantes', 'FR');
GO

DECLARE @count int = $(WaveCount);
DECLARE @w int = 0;
WHILE @w < @count
BEGIN
    DECLARE @b int = ISNULL((SELECT MAX(facture_id) FROM dbo.factures), 0);
    DECLARE @l int = ISNULL((SELECT MAX(ligne_id)   FROM dbo.lignes_facture), 0);

    INSERT dbo.factures (facture_id, numero, type_piece, date_emission, client_id,
                         facture_origine_numero, total_ht, total_tva, total_ttc, devise, date_modif)
    SELECT @b + rn,
           'A-2026-' + RIGHT('0000' + CAST(@b + rn AS varchar(10)), 4),
           type_piece, date_emission, client_id,
           CASE WHEN origine_rn IS NULL THEN NULL
                ELSE 'A-2026-' + RIGHT('0000' + CAST(@b + origine_rn AS varchar(10)), 4) END,
           total_ht, total_tva, total_ttc, 'EUR', SYSUTCDATETIME()
    FROM (VALUES
        (1, 'FAC', '2026-06-01', 1, CAST(NULL AS int), 100.00, 20.00, 120.00),
        (2, 'FAC', '2026-06-02', 2, NULL,              400.00, 45.50, 445.50),
        (3, 'FAC', '2026-06-03', 1, NULL,               50.00,  0.00,  50.00),
        (4, 'FAC', '2026-06-04', 2, NULL,              200.00, 20.00, 220.00),
        (5, 'AVO', '2026-06-05', 1, 1,                 100.00, 20.00, 120.00),
        (6, 'FAC', '2026-06-06', 1, NULL,              100.00, 13.00, 113.00)
    ) AS v(rn, type_piece, date_emission, client_id, origine_rn, total_ht, total_tva, total_ttc);

    INSERT dbo.lignes_facture (ligne_id, facture_id, no_ligne, designation, quantite,
                               prix_unitaire_ht, montant_ht, montant_tva, taux_tva, code_regime)
    SELECT @l + ROW_NUMBER() OVER (ORDER BY (SELECT 1)),
           @b + fac_rn, no_ligne, designation, qte, pu, mht, mtva, taux, reg
    FROM (VALUES
        (1, 1, N'Prestation de démonstration (20%)', CAST(1.0 AS decimal(18,3)), 100.00, 100.00, 20.00, CAST(20.0 AS decimal(5,2)), '20'),
        (2, 1, N'Marchandise A (20%)',               1.0, 100.00, 100.00, 20.00, 20.0, '20'),
        (2, 2, N'Marchandise B (10%)',               1.0, 200.00, 200.00, 20.00, 10.0, '10'),
        (2, 3, N'Marchandise C (5,5%)',              1.0, 100.00, 100.00,  5.50,  5.5, '5.5'),
        (3, 1, N'Produit exonéré (0%)',              1.0,  50.00,  50.00,  0.00,  0.0, '0'),
        (4, 1, N'Service récurrent (10%)',           1.0, 200.00, 200.00, 20.00, 10.0, '10'),
        (5, 1, N'Avoir sur facture (20%)',           1.0, 100.00, 100.00, 20.00, 20.0, '20'),
        (6, 1, N'Ligne régime non mappé (13)',       1.0, 100.00, 100.00, 13.00, 13.0, '13')
    ) AS vl(fac_rn, no_ligne, designation, qte, pu, mht, mtva, taux, reg);

    SET @w = @w + 1;
END
GO
