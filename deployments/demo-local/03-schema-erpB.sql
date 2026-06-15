-- 03-schema-erpB.sql
-- Schéma source « DemoErpB » : facturation DÉNORMALISÉE (anglais, legacy), montants en FLOAT
-- (exerce la conversion float->decimal half-up obligatoire côté adaptateur — ADR-0004 D3-7),
-- acheteur en ligne (pas de table clients), régime porté sur la ligne, types 'I'/'C'. Idempotent.
USE LiakontDemoErpB;
GO

IF OBJECT_ID('dbo.Invoice') IS NULL
CREATE TABLE dbo.Invoice (
    InvoiceId       int           NOT NULL CONSTRAINT PK_Invoice PRIMARY KEY,
    -- Numéro UNIQUE : l'auto-jointure d'origine (o.InvoiceNo = i.OriginInvoiceNo) suppose cette unicité ;
    -- sans elle, un numéro en double multiplierait les lignes au regroupement.
    InvoiceNo       varchar(30)   NOT NULL CONSTRAINT UQ_Invoice_InvoiceNo UNIQUE,
    Kind            char(1)       NOT NULL,                              -- 'I' invoice | 'C' credit note
    IssuedOn        date          NOT NULL,
    BuyerName       nvarchar(200) NULL,
    BuyerSiren      varchar(9)    NULL,
    BuyerIsCompany  bit           NOT NULL CONSTRAINT DF_Invoice_IsCompany DEFAULT 0,
    OriginInvoiceNo varchar(30)   NULL,                                  -- renseigné pour un 'C'
    NetTotal        float         NOT NULL,
    VatTotal        float         NOT NULL,
    GrossTotal      float         NOT NULL,
    Currency        varchar(3)    NOT NULL CONSTRAINT DF_Invoice_Currency DEFAULT 'EUR',
    ModifiedUtc     datetime2     NOT NULL CONSTRAINT DF_Invoice_Modified DEFAULT SYSUTCDATETIME()
);
GO

IF OBJECT_ID('dbo.InvoiceItem') IS NULL
CREATE TABLE dbo.InvoiceItem (
    ItemId    int           NOT NULL CONSTRAINT PK_InvoiceItem PRIMARY KEY,
    InvoiceId  int          NOT NULL CONSTRAINT FK_InvoiceItem_Invoice REFERENCES dbo.Invoice(InvoiceId),
    LineNumber int          NOT NULL,   -- 'LineNo' est un mot-clé réservé T-SQL (LINENO)
    Label     nvarchar(300) NOT NULL,
    Qty       float         NOT NULL CONSTRAINT DF_Item_Qty DEFAULT 1,
    UnitPrice float         NULL,
    NetAmount float         NOT NULL,
    VatAmount float         NOT NULL,
    VatRate   float         NULL,
    VatRegime varchar(10)   NOT NULL                                     -- code régime BRUT sur la ligne
);
GO
