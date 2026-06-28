namespace Liakont.Modules.FacturX.Tests.Unit.Cii;

using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;
using Liakont.Modules.FacturX.Domain.Cii;

/// <summary>
/// Validation STRUCTURELLE EN 16931 du CII produit (tier rapide / « assertions structurelles » de
/// F16 §8) : le XML est bien formé, l'élément racine et les espaces de noms sont corrects, et les
/// Business Terms / Groups OBLIGATOIRES du profil COMFORT sont présents (BT-24, BT-1/2/3, ≥1 ligne avec
/// BT-153/146/129/151, vendeur BT-27, ventilation BG-23, totaux BG-22 BT-106/109/110/112/115).
/// <para>
/// ⚠️ Périmètre : ce n'est NI la validation XSD CII EN 16931 NI le Schematron CEN/TC 434. Les XSD CII
/// présents au dépôt (<c>docs/references/dgfip-v3.2/.../F1_BASE|F1_FULL_CII_D22B</c>) sont des profils
/// DGFiP RESTREINTS (la plupart des BT EN 16931 — dont BT-106/112/115 — y sont commentés) : valider
/// contre eux rejetterait à tort un CII EN 16931 conforme (faux négatif). Le XSD CII EN 16931 complet et
/// le Schematron CEN/TC 434 sont des artefacts EXTERNES non vendorés (F16 §10 action A4, NON TRANCHÉ) ;
/// le Schematron exige en plus un processeur XSLT 2.0 (Saxon → ADR, aucun package sans ADR). La
/// conformité EN 16931 RÉELLE est portée par <b>Mustangproject</b> au tier intégration de FX04 (F16 §8 /
/// ADR-0023 §4) + la recette GATE_FACTURX.
/// </para>
/// </summary>
internal static class CiiStructuralValidator
{
    private static readonly XNamespace Rsm = CiiProfile.RsmNamespace;
    private static readonly XNamespace Ram = CiiProfile.RamNamespace;
    private static readonly XNamespace Udt = CiiProfile.UdtNamespace;

    /// <summary>Retourne les manques structurels EN 16931 (liste vide = structure complète et bien formée).</summary>
    public static IReadOnlyList<string> Check(byte[] xml)
    {
        var issues = new List<string>();

        XDocument document;
        try
        {
            document = XDocument.Parse(Encoding.UTF8.GetString(xml));
        }
        catch (System.Xml.XmlException ex)
        {
            return new[] { "XML non bien formé : " + ex.Message };
        }

        var root = document.Root;
        if (root is null || root.Name != Rsm + "CrossIndustryInvoice")
        {
            return new[] { "Racine attendue rsm:CrossIndustryInvoice absente." };
        }

        // BT-24 (identifiant de spécification).
        Require(issues, root.Descendants(Ram + "GuidelineSpecifiedDocumentContextParameter")
            .Elements(Ram + "ID").Any(), "BT-24 (GuidelineSpecifiedDocumentContextParameter/ID)");

        var exchanged = root.Element(Rsm + "ExchangedDocument");
        Require(issues, Has(exchanged, "ID"), "BT-1 (ExchangedDocument/ID)");
        Require(issues, Has(exchanged, "TypeCode"), "BT-3 (ExchangedDocument/TypeCode)");
        var issueDate = exchanged?.Element(Ram + "IssueDateTime")?.Element(Udt + "DateTimeString");
        Require(issues, issueDate is not null, "BT-2 (ExchangedDocument/IssueDateTime/DateTimeString)");

        var lines = root.Descendants(Ram + "IncludedSupplyChainTradeLineItem").ToList();
        Require(issues, lines.Count > 0, "BG-25 (au moins une ligne)");
        foreach (var line in lines)
        {
            var id = line.Element(Ram + "AssociatedDocumentLineDocument")?.Element(Ram + "LineID")?.Value ?? "?";
            Require(issues, line.Element(Ram + "SpecifiedTradeProduct")?.Element(Ram + "Name") is not null,
                $"BT-153 (ligne {id} : Name)");
            Require(issues, line.Descendants(Ram + "NetPriceProductTradePrice")
                .Elements(Ram + "ChargeAmount").Any(), $"BT-146 (ligne {id} : prix unitaire net)");
            Require(issues, line.Descendants(Ram + "BilledQuantity").Any(), $"BT-129 (ligne {id} : quantité)");
            var lineTax = line.Descendants(Ram + "SpecifiedLineTradeSettlement")
                .Elements(Ram + "ApplicableTradeTax").FirstOrDefault();
            Require(issues, lineTax?.Element(Ram + "CategoryCode") is not null, $"BT-151 (ligne {id} : catégorie TVA)");
            Require(issues, line.Descendants(Ram + "SpecifiedTradeSettlementLineMonetarySummation")
                .Elements(Ram + "LineTotalAmount").Any(), $"BT-131 (ligne {id} : total ligne)");
        }

        // ApplicableHeaderTradeDelivery est obligatoire dans le xsd:sequence de la transaction ET ne doit
        // JAMAIS être vide (PEPPOL-EN16931-R008 interdit un élément vide — BUG-26 / F16 §3.5) : on exige la
        // date de livraison effective (BT-72) via ActualDeliverySupplyChainEvent/OccurrenceDateTime.
        var delivery = root.Descendants(Ram + "ApplicableHeaderTradeDelivery").FirstOrDefault();
        Require(issues, delivery is not null, "BG-13 (ApplicableHeaderTradeDelivery)");
        var deliveryDate = delivery?.Element(Ram + "ActualDeliverySupplyChainEvent")?
            .Element(Ram + "OccurrenceDateTime")?.Element(Udt + "DateTimeString");
        Require(issues, deliveryDate is not null, "BT-72 (ApplicableHeaderTradeDelivery non vide : date de livraison, R008)");

        var seller = root.Descendants(Ram + "SellerTradeParty").FirstOrDefault();
        Require(issues, seller?.Element(Ram + "Name") is not null, "BT-27 (SellerTradeParty/Name)");
        Require(issues, PartyCountry(seller) is not null, "BT-40 (SellerTradeParty pays)");

        var buyer = root.Descendants(Ram + "BuyerTradeParty").FirstOrDefault();
        Require(issues, buyer?.Element(Ram + "Name") is not null, "BT-44 (BuyerTradeParty/Name)");
        Require(issues, PartyCountry(buyer) is not null, "BT-55 (BuyerTradeParty pays)");

        var settlement = root.Descendants(Ram + "ApplicableHeaderTradeSettlement").FirstOrDefault();
        Require(issues, settlement?.Element(Ram + "InvoiceCurrencyCode") is not null, "BT-5 (InvoiceCurrencyCode)");

        var breakdown = settlement?.Elements(Ram + "ApplicableTradeTax").ToList() ?? new List<XElement>();
        Require(issues, breakdown.Count > 0, "BG-23 (au moins une ventilation TVA)");
        foreach (var tax in breakdown)
        {
            Require(issues, Has(tax, "CalculatedAmount"), "BT-117 (BG-23/CalculatedAmount)");
            Require(issues, Has(tax, "BasisAmount"), "BT-116 (BG-23/BasisAmount)");
            Require(issues, Has(tax, "CategoryCode"), "BT-118 (BG-23/CategoryCode)");
            Require(issues, Has(tax, "RateApplicablePercent"), "BT-119 (BG-23/RateApplicablePercent)");
        }

        var summation = settlement?.Element(Ram + "SpecifiedTradeSettlementHeaderMonetarySummation");
        Require(issues, Has(summation, "LineTotalAmount"), "BT-106 (LineTotalAmount)");
        Require(issues, Has(summation, "TaxBasisTotalAmount"), "BT-109 (TaxBasisTotalAmount)");
        Require(issues, Has(summation, "TaxTotalAmount"), "BT-110 (TaxTotalAmount)");
        Require(issues, Has(summation, "GrandTotalAmount"), "BT-112 (GrandTotalAmount)");
        Require(issues, Has(summation, "DuePayableAmount"), "BT-115 (DuePayableAmount)");

        return issues;
    }

    private static bool Has(XElement? parent, string name) =>
        parent?.Element(Ram + name) is not null;

    private static XElement? PartyCountry(XElement? party) =>
        party?.Element(Ram + "PostalTradeAddress")?.Element(Ram + "CountryID");

    private static void Require(List<string> issues, bool present, string label)
    {
        if (!present)
        {
            issues.Add("Manque : " + label);
        }
    }
}
