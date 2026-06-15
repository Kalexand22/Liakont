-- 02-schema-erpA.sql
-- Schéma source « DemoErpA » : ERP de gestion commerciale NORMALISÉ (français), montants decimal,
-- table de régimes TVA séparée, types de pièce 'FAC'/'AVO'. Idempotent (création si absente).
USE LiakontDemoErpA;
GO

IF OBJECT_ID('dbo.regimes_tva') IS NULL
CREATE TABLE dbo.regimes_tva (
    code_regime varchar(10)   NOT NULL CONSTRAINT PK_regimes_tva PRIMARY KEY,
    libelle     nvarchar(100) NOT NULL
);
GO

-- Référentiel des régimes observés (codes BRUTS, jamais interprétés par l'agent). Le code 13 est
-- volontairement NON couvert par la table de mapping tenant → démontre l'état Blocked en console.
MERGE dbo.regimes_tva AS t
USING (VALUES
    ('20',  N'Taux normal 20 %'),
    ('10',  N'Taux réduit 10 %'),
    ('5.5', N'Taux réduit 5,5 %'),
    ('0',   N'Taux zéro'),
    ('13',  N'Régime non mappé (démo blocage)')
) AS s(code_regime, libelle)
ON t.code_regime = s.code_regime
WHEN NOT MATCHED THEN INSERT (code_regime, libelle) VALUES (s.code_regime, s.libelle);
GO

IF OBJECT_ID('dbo.clients') IS NULL
CREATE TABLE dbo.clients (
    client_id      int           NOT NULL CONSTRAINT PK_clients PRIMARY KEY,
    raison_sociale nvarchar(200) NOT NULL,
    siren          varchar(9)    NULL,
    siret          varchar(14)   NULL,
    est_societe    bit           NOT NULL CONSTRAINT DF_clients_societe DEFAULT 0,
    rue            nvarchar(200) NULL,
    code_postal    varchar(10)   NULL,
    ville          nvarchar(100) NULL,
    pays           varchar(2)    NULL
);
GO

IF OBJECT_ID('dbo.factures') IS NULL
CREATE TABLE dbo.factures (
    facture_id             int           NOT NULL CONSTRAINT PK_factures PRIMARY KEY,
    -- Numéro de pièce UNIQUE : l'auto-jointure d'origine d'un avoir (o.numero = f.facture_origine_numero)
    -- suppose cette unicité ; sans elle, un numéro en double multiplierait les lignes au regroupement.
    numero                 varchar(30)   NOT NULL CONSTRAINT UQ_factures_numero UNIQUE,
    type_piece             varchar(3)    NOT NULL,                       -- 'FAC' | 'AVO'
    date_emission          date          NOT NULL,
    client_id              int           NULL CONSTRAINT FK_factures_clients REFERENCES dbo.clients(client_id),
    facture_origine_numero varchar(30)   NULL,                          -- renseigné pour un AVO
    total_ht               decimal(18,2) NOT NULL,
    total_tva              decimal(18,2) NOT NULL,
    total_ttc              decimal(18,2) NOT NULL,
    devise                 varchar(3)    NOT NULL CONSTRAINT DF_factures_devise DEFAULT 'EUR',
    date_modif             datetime2     NOT NULL CONSTRAINT DF_factures_modif DEFAULT SYSUTCDATETIME()
);
GO

IF OBJECT_ID('dbo.lignes_facture') IS NULL
CREATE TABLE dbo.lignes_facture (
    ligne_id         int           NOT NULL CONSTRAINT PK_lignes PRIMARY KEY,
    facture_id       int           NOT NULL CONSTRAINT FK_lignes_factures REFERENCES dbo.factures(facture_id),
    no_ligne         int           NOT NULL,
    designation      nvarchar(300) NOT NULL,
    quantite         decimal(18,3) NOT NULL CONSTRAINT DF_lignes_qte DEFAULT 1,
    prix_unitaire_ht decimal(18,2) NULL,
    montant_ht       decimal(18,2) NOT NULL,
    montant_tva      decimal(18,2) NOT NULL,
    taux_tva         decimal(5,2)  NULL,
    code_regime      varchar(10)   NOT NULL CONSTRAINT FK_lignes_regime REFERENCES dbo.regimes_tva(code_regime)
);
GO
