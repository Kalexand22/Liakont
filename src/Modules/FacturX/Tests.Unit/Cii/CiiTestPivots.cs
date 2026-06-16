namespace Liakont.Modules.FacturX.Tests.Unit.Cii;

using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Fixtures pivot de la matrice de sortie V1 du sérialiseur CII (FX03, ADR-0023 critères de sortie) :
/// mono-taux, multi-taux, exonéré (VATEX), autoliquidation, criée mono-Seller. Tous les totaux sont
/// RÉCONCILIABLES (BR-CO-14/15) ; les catégories de TVA appartiennent au sous-ensemble EN 16931
/// (S, E, AE) ; les montants sont en <see cref="decimal"/>. Données fictives (CLAUDE.md n°7).
/// </summary>
internal static class CiiTestPivots
{
    /// <summary>Noms des cas de la matrice (mono-taux, multi-taux, exonéré, autoliquidation, criée).</summary>
    public static readonly IReadOnlyList<string> Names =
        new[] { "mono-taux", "multi-taux", "exonere-vatex", "autoliquidation", "criee-mono-seller" };

    private static readonly Dictionary<string, PivotDocumentDto> Cases =
        new()
        {
            ["mono-taux"] = MonoTaux(),
            ["multi-taux"] = MultiTaux(),
            ["exonere-vatex"] = ExonereVatex(),
            ["autoliquidation"] = Autoliquidation(),
            ["criee-mono-seller"] = CrieeMonoSeller(),
        };

    /// <summary>Récupère le pivot d'un cas de la matrice par son nom.</summary>
    public static PivotDocumentDto Get(string caseName) => Cases[caseName];

    // Cas 1 — mono-taux (TVA standard 20 %), B2C, livraison de biens.
    private static PivotDocumentDto MonoTaux()
    {
        var supplier = new PivotPartyDto(
            "Boucherie Durand SARL",
            siren: "552100554",
            vatNumber: "FR55552100554",
            address: new PivotAddressDto("12 rue des Halles", null, "75001", "Paris", "FR"),
            isCompanyHint: true);
        var customer = new PivotPartyDto(
            "Client particulier",
            address: new PivotAddressDto(null, null, "75004", "Paris", "FR"));
        var lines = new[]
        {
            Line("Colis de viande", net: 100.00m, unitPrice: 100.00m, rate: 20m, taxAmount: 20.00m, category: VatCategory.S),
        };
        return Document("FAC-2026-0001", supplier, customer, lines, OperationCategory.LivraisonBiens, 100.00m, 20.00m, 120.00m);
    }

    // Cas 2 — multi-taux (20 % + 5,5 %, deux ventilations BG-23), B2C.
    private static PivotDocumentDto MultiTaux()
    {
        var supplier = new PivotPartyDto(
            "Épicerie Martin SAS",
            siren: "421930587",
            vatNumber: "FR21421930587",
            address: new PivotAddressDto("5 place du Marché", null, "69002", "Lyon", "FR"),
            isCompanyHint: true);
        var customer = new PivotPartyDto(
            "Client particulier",
            address: new PivotAddressDto(null, null, "69003", "Lyon", "FR"));
        var lines = new[]
        {
            Line("Produits ménagers", net: 200.00m, unitPrice: 200.00m, rate: 20m, taxAmount: 40.00m, category: VatCategory.S),
            Line("Produits alimentaires", net: 100.00m, unitPrice: 100.00m, rate: 5.5m, taxAmount: 5.50m, category: VatCategory.S),
        };
        return Document("FAC-2026-0002", supplier, customer, lines, OperationCategory.LivraisonBiens, 300.00m, 45.50m, 345.50m);
    }

    // Cas 3 — exonéré (franchise en base, catégorie E + code VATEX), prestation de services.
    // EN 16931 BR-E-02 : une facture portant une ligne en catégorie E (exonéré) DOIT porter un
    // identifiant TVA/fiscal vendeur (BT-31/32/63) — un vendeur en franchise garde un n° de TVA
    // intracommunautaire. N° fictif à clé française valide pour le SIREN 839204561 (CLAUDE.md n°7).
    private static PivotDocumentDto ExonereVatex()
    {
        var supplier = new PivotPartyDto(
            "Les Jardins de Léa",
            siren: "839204561",
            vatNumber: "FR35839204561",
            address: new PivotAddressDto("3 chemin Vert", null, "33000", "Bordeaux", "FR"),
            isCompanyHint: true);
        var customer = new PivotPartyDto(
            "Client particulier",
            address: new PivotAddressDto(null, null, "33200", "Bordeaux", "FR"));
        var lines = new[]
        {
            Line(
                "Prestation d'entretien de jardin",
                net: 500.00m,
                unitPrice: 500.00m,
                rate: 0m,
                taxAmount: 0.00m,
                category: VatCategory.E,
                vatexCode: "VATEX-FR-FRANCHISE"),
        };
        return Document("FAC-2026-0003", supplier, customer, lines, OperationCategory.PrestationServices, 500.00m, 0.00m, 500.00m);
    }

    // Cas 4 — autoliquidation (reverse charge, catégorie AE + VATEX-EU-AE), B2B, prestation.
    private static PivotDocumentDto Autoliquidation()
    {
        var supplier = new PivotPartyDto(
            "BTP Services SARL",
            siren: "732829320",
            vatNumber: "FR47732829320",
            address: new PivotAddressDto("18 avenue de la Gare", null, "31000", "Toulouse", "FR"),
            isCompanyHint: true);
        var customer = new PivotPartyDto(
            "Constructions Reynaud SAS",
            siren: "552081317",
            vatNumber: "FR89552081317",
            address: new PivotAddressDto("44 boulevard Lascrosses", null, "31000", "Toulouse", "FR"),
            isCompanyHint: true);
        var lines = new[]
        {
            Line(
                "Sous-traitance gros œuvre",
                net: 1000.00m,
                unitPrice: 1000.00m,
                rate: 0m,
                taxAmount: 0.00m,
                category: VatCategory.AE,
                vatexCode: "VATEX-EU-AE"),
        };
        return Document("FAC-2026-0004", supplier, customer, lines, OperationCategory.PrestationServices, 1000.00m, 0.00m, 1000.00m);
    }

    // Cas 5 — criée mono-Seller (deux lignes agrégées dans une seule ventilation S/20 %) avec acompte
    // (BT-113 → BT-115 = BT-112 − BT-113).
    private static PivotDocumentDto CrieeMonoSeller()
    {
        var supplier = new PivotPartyDto(
            "Hôtel des Ventes de Lyon",
            siren: "642014537",
            vatNumber: "FR32642014537",
            address: new PivotAddressDto("7 quai Saint-Antoine", null, "69002", "Lyon", "FR"),
            isCompanyHint: true);
        var customer = new PivotPartyDto(
            "Acquéreur particulier",
            address: new PivotAddressDto(null, null, "69001", "Lyon", "FR"));
        var lines = new[]
        {
            Line("Adjudication lot n°42", net: 2000.00m, unitPrice: 2000.00m, rate: 20m, taxAmount: 400.00m, category: VatCategory.S),
            Line("Frais de vente (honoraires acheteur)", net: 400.00m, unitPrice: 400.00m, rate: 20m, taxAmount: 80.00m, category: VatCategory.S),
        };
        return Document(
            "FAC-2026-0005", supplier, customer, lines, OperationCategory.LivraisonBiens,
            2400.00m, 480.00m, 2880.00m, prepaidAmount: 880.00m);
    }

    private static PivotLineDto Line(
        string description,
        decimal net,
        decimal unitPrice,
        decimal? rate,
        decimal taxAmount,
        VatCategory category,
        string? vatexCode = null) =>
        new(
            description,
            netAmount: net,
            quantity: 1m,
            unitPriceNet: unitPrice,
            taxes: new[] { new PivotLineTaxDto(taxAmount, rate, category, vatexCode) });

    private static PivotDocumentDto Document(
        string number,
        PivotPartyDto supplier,
        PivotPartyDto customer,
        IReadOnlyList<PivotLineDto> lines,
        OperationCategory operationCategory,
        decimal totalNet,
        decimal totalTax,
        decimal totalGross,
        decimal? prepaidAmount = null) =>
        new(
            sourceDocumentKind: "INVOICE",
            number: number,
            issueDate: new DateTime(2026, 1, 15),
            sourceReference: "SRC-" + number,
            supplier: supplier,
            totals: new PivotTotalsDto(totalNet, totalTax, totalGross),
            operationCategory: operationCategory,
            customer: customer,
            lines: lines,
            prepaidAmount: prepaidAmount);
}
