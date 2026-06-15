-- 05-seed-erpB.sql
-- Seed RE-JOUABLE de LiakontDemoErpB (données fictives, CLAUDE.md n°7), montants en FLOAT.
-- Vagues de 6 pièces (numéros AUTO B-2026-NNNN), mêmes cas connus que ErpA mais schéma anglais
-- dénormalisé (acheteur en ligne, régime sur la ligne, Kind 'I'/'C').
-- Variable sqlcmd : WaveCount.
USE LiakontDemoErpB;
GO
SET NOCOUNT ON;

DECLARE @count int = $(WaveCount);
DECLARE @w int = 0;
WHILE @w < @count
BEGIN
    DECLARE @b int = ISNULL((SELECT MAX(InvoiceId) FROM dbo.Invoice), 0);
    DECLARE @l int = ISNULL((SELECT MAX(ItemId)    FROM dbo.InvoiceItem), 0);

    INSERT dbo.Invoice (InvoiceId, InvoiceNo, Kind, IssuedOn, BuyerName, BuyerSiren, BuyerIsCompany,
                        OriginInvoiceNo, NetTotal, VatTotal, GrossTotal, Currency, ModifiedUtc)
    SELECT @b + rn,
           'B-2026-' + RIGHT('0000' + CAST(@b + rn AS varchar(10)), 4),
           kind, issued_on, buyer_name, CAST(NULL AS varchar(9)), 0,
           CASE WHEN origine_rn IS NULL THEN NULL
                ELSE 'B-2026-' + RIGHT('0000' + CAST(@b + origine_rn AS varchar(10)), 4) END,
           net_total, vat_total, gross_total, 'EUR', SYSUTCDATETIME()
    FROM (VALUES
        (1, 'I', '2026-06-01', N'Customer One', CAST(NULL AS int), 100.0, 20.0, 120.0),
        (2, 'I', '2026-06-02', N'Customer Two', NULL,              400.0, 45.5, 445.5),
        (3, 'I', '2026-06-03', N'Customer One', NULL,               50.0,  0.0,  50.0),
        (4, 'I', '2026-06-04', N'Customer Two', NULL,              200.0, 20.0, 220.0),
        (5, 'C', '2026-06-05', N'Customer One', 1,                 100.0, 20.0, 120.0),
        (6, 'I', '2026-06-06', N'Customer One', NULL,              100.0, 13.0, 113.0)
    ) AS v(rn, kind, issued_on, buyer_name, origine_rn, net_total, vat_total, gross_total);

    INSERT dbo.InvoiceItem (ItemId, InvoiceId, LineNumber, Label, Qty, UnitPrice, NetAmount, VatAmount, VatRate, VatRegime)
    SELECT @l + ROW_NUMBER() OVER (ORDER BY (SELECT 1)),
           @b + inv_rn, line_no, label, qty, unit_price, net_amount, vat_amount, vat_rate, regime
    FROM (VALUES
        (1, 1, N'Demo service (20%)',        CAST(1.0 AS float), 100.0, 100.0, 20.0, CAST(20.0 AS float), '20'),
        (2, 1, N'Goods A (20%)',             1.0, 100.0, 100.0, 20.0, 20.0, '20'),
        (2, 2, N'Goods B (10%)',             1.0, 200.0, 200.0, 20.0, 10.0, '10'),
        (2, 3, N'Goods C (5.5%)',            1.0, 100.0, 100.0,  5.5,  5.5, '5.5'),
        (3, 1, N'Exempt product (0%)',       1.0,  50.0,  50.0,  0.0,  0.0, '0'),
        (4, 1, N'Recurring service (10%)',   1.0, 200.0, 200.0, 20.0, 10.0, '10'),
        (5, 1, N'Credit note line (20%)',    1.0, 100.0, 100.0, 20.0, 20.0, '20'),
        (6, 1, N'Unmapped regime line (13)', 1.0, 100.0, 100.0, 13.0, 13.0, '13')
    ) AS vl(inv_rn, line_no, label, qty, unit_price, net_amount, vat_amount, vat_rate, regime);

    SET @w = @w + 1;
END
GO
