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
