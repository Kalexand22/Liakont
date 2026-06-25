-- ============================================================================
--  inject-demo-siren.sql  (DONNÉE DÉMO, pas de code agent)
--  Ajoute une colonne acheteur_siren à enc.entete_ba et attribue des SIREN
--  FICTIFS Luhn-valides à QUELQUES acheteurs « société » FR, en LAISSANT
--  délibérément d'autres sociétés SANS SIREN.
--
--  But (parcours observable B2B mixte) :
--   - les acheteurs pro AVEC SIREN valide  -> flux B2B (facture électronique) PASSE ;
--   - les acheteurs « société » SANS SIREN  -> la plateforme BLOQUE en
--     BUYER_LOOKS_PROFESSIONAL (IsCompanyHint=true sans SIREN) -> on démontre le
--     verdict opérateur.
--
--  La base source EncheresV6 n'a AUCUNE colonne SIREN (vérifié). L'agent lit
--  e.acheteur_siren en BRUT (PivotPartyDto.Siren) — zéro heuristique côté agent
--  (CLAUDE.md n°6). Les SIREN sont fictifs (base démo, CLAUDE.md n°7) mais
--  rendus VALIDES (Luhn) là où le B2B doit aboutir (cf. README).
--
--  Idempotent : ré-exécutable (réinitialise puis réattribue de façon déterministe,
--  par ordre alphabétique de raison sociale). À exécuter APRÈS encheresv6-demo-sqlserver.sql.
--    sqlcmd -S localhost -E -d EncheresV6_Demo -i inject-demo-siren.sql
-- ============================================================================
SET NOCOUNT ON;

-- 1) Colonne acheteur_siren (ajout idempotent).
IF COL_LENGTH('enc.entete_ba', 'acheteur_siren') IS NULL
    ALTER TABLE [enc].[entete_ba] ADD [acheteur_siren] nvarchar(20) NULL;
GO

-- 2) Réinitialisation (idempotence stricte sur ré-exécution).
UPDATE [enc].[entete_ba] SET [acheteur_siren] = NULL;
GO

-- 3) Attribution : les 4 PREMIÈRES sociétés pro FR (ordre alpha = déterministe)
--    reçoivent un SIREN fictif Luhn-valide ; une même société garde le même SIREN
--    sur tous ses bordereaux. Les sociétés de rang >= 5 restent SANS SIREN (cas
--    bloquant à démontrer : société FR sans SIREN -> BUYER_LOOKS_PROFESSIONAL).
--    « Pro FR » = champ societe non vide ET pays FR/vide.
;WITH pros AS (
    SELECT DISTINCT LTRIM(RTRIM([societe])) AS soc
    FROM [enc].[entete_ba]
    WHERE [societe] IS NOT NULL AND LTRIM(RTRIM([societe])) <> ''
      AND ([code_pays] IS NULL OR LTRIM(RTRIM([code_pays])) IN ('', 'FR'))
),
ranked AS (
    SELECT soc, ROW_NUMBER() OVER (ORDER BY soc) AS rn FROM pros
),
sirens (rn, siren) AS (
    -- SIREN fictifs Luhn-valides ET vérifiés NON ATTRIBUÉS dans l'annuaire des entreprises
    -- (API data.gouv recherche-entreprises, 0 résultat) — évite d'emprunter le SIREN d'une
    -- entreprise réelle (CLAUDE.md n°15). Re-vérifier si réutilisés ailleurs.
    SELECT * FROM (VALUES
        (1, '945678902'),
        (2, '956789010'),
        (3, '967890120'),
        (4, '998877666')
    ) v(rn, siren)
)
UPDATE e
   SET e.[acheteur_siren] = s.siren
FROM [enc].[entete_ba] e
JOIN ranked r ON LTRIM(RTRIM(e.[societe])) = r.soc
JOIN sirens s ON s.rn = r.rn
WHERE (e.[code_pays] IS NULL OR LTRIM(RTRIM(e.[code_pays])) IN ('', 'FR'));
GO

-- 4) Vérification : sociétés AVEC SIREN (B2B passant) vs SANS SIREN (bloquant démo).
PRINT '--- Acheteurs societe AVEC SIREN (flux B2B passant) ---';
SELECT DISTINCT LTRIM(RTRIM([societe])) AS societe, [acheteur_siren]
FROM [enc].[entete_ba]
WHERE [acheteur_siren] IS NOT NULL
ORDER BY societe;

PRINT '--- Acheteurs societe SANS SIREN (bloques BUYER_LOOKS_PROFESSIONAL) ---';
SELECT DISTINCT TOP 20 LTRIM(RTRIM([societe])) AS societe, ISNULL([code_pays], '(vide)') AS pays
FROM [enc].[entete_ba]
WHERE [societe] IS NOT NULL AND LTRIM(RTRIM([societe])) <> '' AND [acheteur_siren] IS NULL
ORDER BY societe;
GO

-- ============================================================================
--  SECTION VENDEUR (ajout 2026-06-25) — SIREN des vendeurs ASSUJETTIS (regime 5).
--  Un lot « regime du prix total » (code_regime_tva = 5) implique un commettant
--  ASSUJETTI (livraison ouvrant droit a deduction — BOI-TVA-SECT-90-50). Cote
--  reforme, l'honoraire vendeur de ces lots = prestation B2B (facture e-invoicing
--  de l'office vers le vendeur) — par opposition au regime de la marge (regime 6,
--  vendeur particulier) ou l'honoraire vendeur part en e-reporting B2C. SIREN fictifs
--  Luhn-valides, distincts du pool acheteur (base demo, CLAUDE.md n°7/15).
--  NB : donnee preparee POUR LE FUTUR. L'agent ne lit PAS encore vendeur_siren :
--  l'aiguillage B2B de l'honoraire vendeur exige un document HONORAIRE-SEUL distinct
--  (le bordereau vendeur porte aussi le bien, qui releve de la jambe vendeur->office
--  / autofacturation 389) — differe, hors BUG-8 (cf. F03 §2.7).
-- ============================================================================

-- 5) Colonne vendeur_siren (ajout idempotent).
IF COL_LENGTH('enc.entete_bv', 'vendeur_siren') IS NULL
    ALTER TABLE [enc].[entete_bv] ADD [vendeur_siren] nvarchar(20) NULL;
GO

-- 6) Reinitialisation (idempotence stricte sur re-execution).
UPDATE [enc].[entete_bv] SET [vendeur_siren] = NULL;
GO

-- 7) Attribution : chaque vendeur distinct (code_vendeur) ayant au moins un
--    bordereau regime 5 recoit un SIREN fictif Luhn-valide, deterministe
--    (ORDER BY code_vendeur), le meme sur tous ses bordereaux assujettis.
--    Pool cycle si > 6 vendeurs.
;WITH vendeurs AS (
    SELECT DISTINCT [code_vendeur]
    FROM [enc].[entete_bv]
    WHERE [code_regime_tva] = '5'   -- comparaison litterale (la colonne est lue en string par l'agent ; evite une conversion implicite)
),
ranked AS (
    SELECT [code_vendeur], ROW_NUMBER() OVER (ORDER BY [code_vendeur]) AS rn FROM vendeurs
),
sirens (rn, siren) AS (
    SELECT * FROM (VALUES
        (1, '812345676'),
        (2, '823456785'),
        (3, '834567893'),
        (4, '845678903'),
        (5, '856789011'),
        (6, '867890121')
    ) v(rn, siren)
)
UPDATE bv
   SET bv.[vendeur_siren] = s.siren
FROM [enc].[entete_bv] bv
JOIN ranked r ON bv.[code_vendeur] = r.[code_vendeur]
JOIN sirens s ON s.rn = ((r.rn - 1) % 6) + 1
WHERE bv.[code_regime_tva] = '5';
GO

-- 8) Verification vendeur : vendeurs assujettis (regime 5) AVEC SIREN.
PRINT '--- Vendeurs assujettis (regime 5) AVEC SIREN (flux B2B) ---';
SELECT [code_vendeur], MAX([nom_affaire]) AS nom_affaire, [vendeur_siren], COUNT(*) AS nb_bordereaux
FROM [enc].[entete_bv]
WHERE [vendeur_siren] IS NOT NULL
GROUP BY [code_vendeur], [vendeur_siren]
ORDER BY [code_vendeur];
GO
