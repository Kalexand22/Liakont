namespace Liakont.Modules.FacturX.Application.Cii;

using System.Globalization;
using System.Text;
using System.Xml;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.FacturX.Domain;
using Liakont.Modules.FacturX.Domain.Cii;

/// <summary>
/// Sérialiseur CII maison (FX03). Mappe un <see cref="PivotDocumentDto"/> vers un
/// <c>CrossIndustryInvoice</c> EN 16931 (COMFORT, UN/CEFACT D22B), en RECOPIANT les valeurs qualitatives
/// du pivot et en DÉRIVANT par arithmétique normative les agrégats non portés (BG-23, BT-106, BT-115),
/// puis en les RÉCONCILIANT avec les totaux portés (BR-CO-14/15/16). Tout écart non réconciliable ou tout
/// BT obligatoire ni porté ni dérivable lève <see cref="FacturXGenerationException"/> (CLAUDE.md n°2/3 ;
/// ADR-0023 INV-FX-2/7). Montants en <see cref="decimal"/>, arrondi <see cref="PivotRounding"/> half-up,
/// formatage XML invariant culture (INV-FX-6). L'ordre des éléments suit le <c>xsd:sequence</c> du XSD CII
/// D22B fourni (DGFiP v3.2) ; aucune dépendance à une PA (ADR-0023 INV-FX-4).
/// </summary>
public sealed class CrossIndustryInvoiceSerializer : ICrossIndustryInvoiceSerializer
{
    private const string RsmPrefix = "rsm";
    private const string RamPrefix = "ram";
    private const string UdtPrefix = "udt";

    /// <inheritdoc />
    public byte[] Serialize(PivotDocumentDto pivot)
    {
        ArgumentNullException.ThrowIfNull(pivot);

        // BG-20/BG-21 (charges/remises de niveau document) non mappés en V1 : les omettre fausserait la
        // base TVA et BR-CO-13 — bloquer plutôt qu'inventer leur ventilation (CLAUDE.md n°3 ; F16 §3,
        // aligné sur SuperPdpPayloadBuilder).
        if (pivot.DocumentCharges.Count > 0)
        {
            var message =
                $"Document n° {pivot.Number} : les charges/remises de niveau document (EN 16931 BG-20/BG-21) " +
                "ne sont pas prises en charge par la génération Factur-X V1. Transmettez ce document par un " +
                "autre canal ou faites évoluer le lot avant émission.";
            throw new FacturXGenerationException(pivot.Number, message);
        }

        if (pivot.Lines.Count == 0)
        {
            var message =
                $"Document n° {pivot.Number} : aucune ligne de facture (EN 16931 BR-16 exige au moins une " +
                "ligne). Vérifiez le document dans le logiciel source.";
            throw new FacturXGenerationException(pivot.Number, message);
        }

        GuardMandatoryParties(pivot);

        var breakdown = DeriveVatBreakdown(pivot);
        ReconcileTotals(pivot, breakdown);

        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        using (var writer = XmlWriter.Create(stream, settings))
        {
            WriteDocument(writer, pivot, breakdown);
        }

        return stream.ToArray();
    }

    private static void WriteDocument(
        XmlWriter writer, PivotDocumentDto pivot, IReadOnlyList<VatBreakdownLine> breakdown)
    {
        writer.WriteStartDocument();

        writer.WriteStartElement(RsmPrefix, "CrossIndustryInvoice", CiiProfile.RsmNamespace);
        writer.WriteAttributeString("xmlns", RamPrefix, null, CiiProfile.RamNamespace);
        writer.WriteAttributeString("xmlns", UdtPrefix, null, CiiProfile.UdtNamespace);

        WriteContext(writer);
        WriteExchangedDocument(writer, pivot);

        writer.WriteStartElement(RsmPrefix, "SupplyChainTradeTransaction", CiiProfile.RsmNamespace);
        for (var index = 0; index < pivot.Lines.Count; index++)
        {
            WriteLine(writer, pivot, pivot.Lines[index], index);
        }

        WriteAgreement(writer, pivot);
        WriteDelivery(writer);
        WriteSettlement(writer, pivot, breakdown);
        writer.WriteEndElement(); // SupplyChainTradeTransaction

        writer.WriteEndElement(); // CrossIndustryInvoice
        writer.WriteEndDocument();
    }

    // rsm:ExchangedDocumentContext — BT-24 (identifiant de spécification EN 16931).
    private static void WriteContext(XmlWriter writer)
    {
        StartRsm(writer, "ExchangedDocumentContext");
        StartRam(writer, "GuidelineSpecifiedDocumentContextParameter");
        Ram(writer, "ID", CiiProfile.SpecificationIdentifier);
        writer.WriteEndElement(); // GuidelineSpecifiedDocumentContextParameter
        writer.WriteEndElement(); // ExchangedDocumentContext
    }

    // rsm:ExchangedDocument — BT-1 (numéro), BT-3 (type=380), BT-2 (date d'émission, format 102).
    private static void WriteExchangedDocument(XmlWriter writer, PivotDocumentDto pivot)
    {
        StartRsm(writer, "ExchangedDocument");
        Ram(writer, "ID", pivot.Number);
        Ram(writer, "TypeCode", CiiProfile.InvoiceTypeCode);

        StartRam(writer, "IssueDateTime");
        writer.WriteStartElement(UdtPrefix, "DateTimeString", CiiProfile.UdtNamespace);
        writer.WriteAttributeString("format", CiiProfile.DateFormatCode);
        writer.WriteString(FormatDate(pivot.IssueDate));
        writer.WriteEndElement(); // DateTimeString
        writer.WriteEndElement(); // IssueDateTime

        writer.WriteEndElement(); // ExchangedDocument
    }

    // ram:IncludedSupplyChainTradeLineItem (BG-25) — recopie BT-126/153/146/129/151/152/131.
    private static void WriteLine(XmlWriter writer, PivotDocumentDto pivot, PivotLineDto line, int index)
    {
        var tax = SingleTax(pivot, line);

        // BT-146 (prix unitaire net) obligatoire (BR-26) : RECOPIÉ du pivot ; absent → blocage, jamais
        // dérivé de BT-131/BT-129 (F16 §3.1).
        if (line.UnitPriceNet is not { } unitPrice)
        {
            var message =
                $"Document n° {pivot.Number}, ligne {index + 1} (« {line.Description} ») : prix unitaire net " +
                "(EN 16931 BT-146) absent. Il est obligatoire et n'est jamais déduit du total de ligne — " +
                "complétez le prix unitaire dans le logiciel source.";
            throw new FacturXGenerationException(pivot.Number, message);
        }

        StartRam(writer, "IncludedSupplyChainTradeLineItem");

        StartRam(writer, "AssociatedDocumentLineDocument");
        Ram(writer, "LineID", (index + 1).ToString(CultureInfo.InvariantCulture));
        writer.WriteEndElement(); // AssociatedDocumentLineDocument

        StartRam(writer, "SpecifiedTradeProduct");
        Ram(writer, "Name", line.Description);
        writer.WriteEndElement(); // SpecifiedTradeProduct

        StartRam(writer, "SpecifiedLineTradeAgreement");
        StartRam(writer, "NetPriceProductTradePrice");
        Ram(writer, "ChargeAmount", FormatPrice(unitPrice));
        writer.WriteEndElement(); // NetPriceProductTradePrice
        writer.WriteEndElement(); // SpecifiedLineTradeAgreement

        StartRam(writer, "SpecifiedLineTradeDelivery");
        StartRam(writer, "BilledQuantity");
        writer.WriteAttributeString("unitCode", CiiProfile.DefaultUnitCode);
        writer.WriteString(FormatQuantity(line.Quantity));
        writer.WriteEndElement(); // BilledQuantity
        writer.WriteEndElement(); // SpecifiedLineTradeDelivery

        StartRam(writer, "SpecifiedLineTradeSettlement");
        StartRam(writer, "ApplicableTradeTax");
        Ram(writer, "TypeCode", CiiProfile.VatTypeCode);
        Ram(writer, "CategoryCode", tax.CategoryCode!.Value.ToString());
        Ram(writer, "RateApplicablePercent", FormatPercent(tax.Rate));
        writer.WriteEndElement(); // ApplicableTradeTax
        StartRam(writer, "SpecifiedTradeSettlementLineMonetarySummation");
        Ram(writer, "LineTotalAmount", FormatAmount(line.NetAmount));
        writer.WriteEndElement(); // SpecifiedTradeSettlementLineMonetarySummation
        writer.WriteEndElement(); // SpecifiedLineTradeSettlement

        writer.WriteEndElement(); // IncludedSupplyChainTradeLineItem
    }

    // ram:ApplicableHeaderTradeAgreement — vendeur (BG-4) + acheteur (BG-7, si présent).
    private static void WriteAgreement(XmlWriter writer, PivotDocumentDto pivot)
    {
        StartRam(writer, "ApplicableHeaderTradeAgreement");
        WriteParty(writer, "SellerTradeParty", pivot.Supplier!);
        if (pivot.Customer is not null)
        {
            WriteParty(writer, "BuyerTradeParty", pivot.Customer);
        }

        writer.WriteEndElement(); // ApplicableHeaderTradeAgreement
    }

    private static void WriteParty(XmlWriter writer, string elementName, PivotPartyDto party)
    {
        StartRam(writer, elementName);
        Ram(writer, "Name", party.Name);

        // BT-30 : identifiant légal (SIREN, scheme 0002) recopié ; absent → omis (jamais inventé).
        if (!string.IsNullOrWhiteSpace(party.Siren))
        {
            StartRam(writer, "SpecifiedLegalOrganization");
            RamWithScheme(writer, "ID", CiiProfile.SirenScheme, party.Siren);
            writer.WriteEndElement(); // SpecifiedLegalOrganization
        }

        if (party.Address is { CountryCode: { } country } address && !string.IsNullOrWhiteSpace(country))
        {
            WriteAddress(writer, address, country);
        }

        // BT-31 : numéro de TVA intracommunautaire (scheme VA) recopié ; absent → omis.
        if (!string.IsNullOrWhiteSpace(party.VatNumber))
        {
            StartRam(writer, "SpecifiedTaxRegistration");
            RamWithScheme(writer, "ID", CiiProfile.VatScheme, party.VatNumber);
            writer.WriteEndElement(); // SpecifiedTaxRegistration
        }

        writer.WriteEndElement(); // party
    }

    // ram:PostalTradeAddress (BG-5/BG-8) — CountryID (BT-40) obligatoire dans le XSD ; autres recopiés.
    private static void WriteAddress(XmlWriter writer, PivotAddressDto address, string country)
    {
        StartRam(writer, "PostalTradeAddress");
        RamOptional(writer, "PostcodeCode", address.PostalCode);
        RamOptional(writer, "LineOne", address.Line1);
        RamOptional(writer, "LineTwo", address.Line2);
        RamOptional(writer, "CityName", address.City);
        Ram(writer, "CountryID", country);
        writer.WriteEndElement(); // PostalTradeAddress
    }

    // ram:ApplicableHeaderTradeDelivery — obligatoire dans le xsd:sequence de la transaction (et exigé
    // par EN 16931 / Mustang COMFORT, fût-il vide). Le pivot ne porte pas de date de livraison (BT-72,
    // optionnelle) : on émet l'élément vide — jamais une date fabriquée.
    private static void WriteDelivery(XmlWriter writer)
    {
        writer.WriteStartElement(RamPrefix, "ApplicableHeaderTradeDelivery", CiiProfile.RamNamespace);
        writer.WriteEndElement();
    }

    // ram:ApplicableHeaderTradeSettlement — devise (BT-5), ventilation BG-23, totaux BG-22.
    private static void WriteSettlement(
        XmlWriter writer, PivotDocumentDto pivot, IReadOnlyList<VatBreakdownLine> breakdown)
    {
        StartRam(writer, "ApplicableHeaderTradeSettlement");
        Ram(writer, "InvoiceCurrencyCode", pivot.CurrencyCode);

        foreach (var line in breakdown)
        {
            WriteHeaderTradeTax(writer, line);
        }

        WriteMonetarySummation(writer, pivot);
        writer.WriteEndElement(); // ApplicableHeaderTradeSettlement
    }

    // ram:ApplicableTradeTax de document (BG-23) — BT-117/116/118/121/119 dans l'ordre du XSD.
    private static void WriteHeaderTradeTax(XmlWriter writer, VatBreakdownLine line)
    {
        StartRam(writer, "ApplicableTradeTax");
        Ram(writer, "CalculatedAmount", FormatAmount(line.CalculatedAmount));
        Ram(writer, "TypeCode", CiiProfile.VatTypeCode);
        Ram(writer, "BasisAmount", FormatAmount(line.BasisAmount));
        Ram(writer, "CategoryCode", line.Category.ToString());
        RamOptional(writer, "ExemptionReasonCode", line.VatexCode);
        Ram(writer, "RateApplicablePercent", FormatPercent(line.Rate));
        writer.WriteEndElement(); // ApplicableTradeTax
    }

    // ram:SpecifiedTradeSettlementHeaderMonetarySummation (BG-22) — ordre du XSD :
    // LineTotalAmount, TaxBasisTotalAmount, TaxTotalAmount, GrandTotalAmount, TotalPrepaidAmount, DuePayableAmount.
    private static void WriteMonetarySummation(XmlWriter writer, PivotDocumentDto pivot)
    {
        var lineTotal = SumAmount(pivot.Lines.Select(l => l.NetAmount)); // BT-106 = Σ BT-131
        var prepaid = pivot.PrepaidAmount ?? 0m;
        var duePayable = PivotRounding.RoundAmount(pivot.Totals.TotalGross - prepaid); // BT-115 (BR-CO-16)

        StartRam(writer, "SpecifiedTradeSettlementHeaderMonetarySummation");
        Ram(writer, "LineTotalAmount", FormatAmount(lineTotal));
        Ram(writer, "TaxBasisTotalAmount", FormatAmount(pivot.Totals.TotalNet));
        RamWithCurrency(writer, "TaxTotalAmount", pivot.CurrencyCode, FormatAmount(pivot.Totals.TotalTax));
        Ram(writer, "GrandTotalAmount", FormatAmount(pivot.Totals.TotalGross));
        if (prepaid != 0m)
        {
            Ram(writer, "TotalPrepaidAmount", FormatAmount(prepaid));
        }

        Ram(writer, "DuePayableAmount", FormatAmount(duePayable));
        writer.WriteEndElement(); // SpecifiedTradeSettlementHeaderMonetarySummation
    }

    // BG-23 : regroupement des ventilations de ligne par (catégorie, taux, VATEX). BT-116 = Σ nets du
    // groupe (somme exacte) ; BT-117 = arrondi(BT-116 × taux/100) (BR-CO-17). Aucune valeur qualitative
    // inventée : catégorie/taux/VATEX viennent du pivot (F16 §3.3 ; modèle SuperPdpPayloadBuilder).
    private static List<VatBreakdownLine> DeriveVatBreakdown(PivotDocumentDto pivot) =>
        pivot.Lines
            .Select(line => (line, tax: SingleTax(pivot, line)))
            .GroupBy(x => (Category: x.tax.CategoryCode!.Value, x.tax.Rate, x.tax.VatexCode))
            .Select(group =>
            {
                var basis = SumAmount(group.Select(x => x.line.NetAmount));
                var rate = group.Key.Rate ?? 0m;
                var calculated = PivotRounding.RoundAmount(basis * rate / 100m);
                return new VatBreakdownLine(
                    group.Key.Category, group.Key.Rate, group.Key.VatexCode, basis, calculated);
            })
            .ToList();

    // Réconciliation des agrégats dérivés avec les totaux portés par le pivot (decimal, zéro tolérance
    // après arrondi half-up — modèle ArithmeticRule). Tout écart → blocage tracé (ADR-0023 INV-FX-7).
    private static void ReconcileTotals(PivotDocumentDto pivot, IReadOnlyList<VatBreakdownLine> breakdown)
    {
        var lineTotal = SumAmount(pivot.Lines.Select(l => l.NetAmount));
        var taxBasis = PivotRounding.RoundAmount(pivot.Totals.TotalNet);
        var taxTotal = PivotRounding.RoundAmount(pivot.Totals.TotalTax);
        var grandTotal = PivotRounding.RoundAmount(pivot.Totals.TotalGross);

        // BR-CO-10 / BR-CO-13 (sans BG-20/21) : BT-106 (Σ BT-131) = BT-109.
        if (lineTotal != taxBasis)
        {
            var message =
                $"Document n° {pivot.Number} : la somme des totaux de ligne ({FormatAmount(lineTotal)}) ne " +
                $"correspond pas au total hors taxes du document ({FormatAmount(taxBasis)}) " +
                "(EN 16931 BR-CO-10/13). Vérifiez les montants dans le logiciel source.";
            throw new FacturXGenerationException(pivot.Number, message);
        }

        // BR-CO-14 : Σ BT-117 (ventilation TVA dérivée) = BT-110.
        var breakdownTax = SumAmount(breakdown.Select(b => b.CalculatedAmount));
        if (breakdownTax != taxTotal)
        {
            var message =
                $"Document n° {pivot.Number} : la TVA recalculée par ventilation EN 16931 " +
                $"({FormatAmount(breakdownTax)}) ne correspond pas au total de TVA du document " +
                $"({FormatAmount(taxTotal)}) (BR-CO-14). Écart non réconciliable — vérifiez les taux et " +
                "montants de TVA dans le logiciel source ; le document n'est pas émis.";
            throw new FacturXGenerationException(pivot.Number, message);
        }

        // BR-CO-15 : BT-109 + BT-110 = BT-112.
        if (PivotRounding.RoundAmount(taxBasis + taxTotal) != grandTotal)
        {
            var message =
                $"Document n° {pivot.Number} : le total TTC ({FormatAmount(grandTotal)}) ne correspond pas au " +
                $"total hors taxes plus la TVA ({FormatAmount(PivotRounding.RoundAmount(taxBasis + taxTotal))}) " +
                "(EN 16931 BR-CO-15). Vérifiez le document dans le logiciel source.";
            throw new FacturXGenerationException(pivot.Number, message);
        }
    }

    // BT obligatoires des parties EN 16931 que le sérialiseur RECOPIE : bloquer plutôt qu'émettre un
    // Factur-X tronqué (CLAUDE.md n°3 ; même fail-closed que BT-146/BT-151). BR-07 = nom de l'acheteur
    // (BT-44, donc un acheteur identifié) ; BR-09 = pays du vendeur (BT-40) ; BR-11 = pays de l'acheteur
    // (BT-55). Le nom du vendeur (BT-27) et le nom de l'acheteur (BT-44) sont non-null par construction du
    // pivot dès que la partie est présente.
    private static void GuardMandatoryParties(PivotDocumentDto pivot)
    {
        if (pivot.Supplier is null)
        {
            // Défense en profondeur : la plateforme remplit l'émetteur à l'ingestion (ADR-0031 amendé) et le CHECK
            // bloque un profil tenant incomplet (SUPPLIER_SIREN_MISSING). Un émetteur nul ici = invariant violé :
            // bloquer plutôt qu'émettre un Factur-X sans vendeur (CLAUDE.md n°3).
            var supplierMissing =
                $"Document n° {pivot.Number} : émetteur (vendeur) absent. L'identité de l'émetteur doit être " +
                "remplie par la plateforme depuis le profil tenant — complétez le profil de l'entreprise (SIREN, " +
                "raison sociale) dans Liakont.";
            throw new FacturXGenerationException(pivot.Number, supplierMissing);
        }

        if (pivot.Customer is null)
        {
            var message =
                $"Document n° {pivot.Number} : destinataire (acheteur) absent. EN 16931 (BR-07) exige le nom " +
                "de l'acheteur (BT-44) pour un Factur-X conforme — renseignez le client dans le logiciel " +
                "source ou transmettez ce document par un autre canal.";
            throw new FacturXGenerationException(pivot.Number, message);
        }

        if (string.IsNullOrWhiteSpace(pivot.Supplier.Address?.CountryCode))
        {
            var message =
                $"Document n° {pivot.Number} : pays du vendeur absent (EN 16931 BT-40, BR-09). Complétez le " +
                "code pays de l'adresse du vendeur dans le paramétrage du tenant ou le logiciel source.";
            throw new FacturXGenerationException(pivot.Number, message);
        }

        if (string.IsNullOrWhiteSpace(pivot.Customer.Address?.CountryCode))
        {
            var message =
                $"Document n° {pivot.Number} : pays de l'acheteur absent (EN 16931 BT-55, BR-11). Complétez " +
                "le code pays de l'adresse du client dans le logiciel source.";
            throw new FacturXGenerationException(pivot.Number, message);
        }
    }

    // EN 16931 BG-30 : une ventilation de TVA par ligne, catégorie posée. 0 ou plusieurs ventilations, ou
    // catégorie nulle = contrat de mapping plateforme (F03) violé → blocage (jamais inventer/droper une
    // taxe : sous-déclaration de TVA, CLAUDE.md n°2/3 ; modèle SuperPdpPayloadBuilder.SingleTax).
    private static PivotLineTaxDto SingleTax(PivotDocumentDto pivot, PivotLineDto line)
    {
        if (line.Taxes.Count != 1)
        {
            var detail = line.Taxes.Count == 0
                ? "aucune ventilation de TVA (EN 16931 BG-30 exige une catégorie de TVA par ligne). "
                : "plusieurs ventilations de TVA (EN 16931 BG-30 : une catégorie par ligne). ";
            var message =
                $"Document n° {pivot.Number}, ligne « {line.Description} » : " + detail +
                "Le mapping TVA de la plateforme doit ventiler la ligne avant la génération du Factur-X.";
            throw new FacturXGenerationException(pivot.Number, message);
        }

        var tax = line.Taxes[0];
        if (tax.CategoryCode is null)
        {
            var message =
                $"Document n° {pivot.Number}, ligne « {line.Description} » : catégorie de TVA (UNCL5305, " +
                "EN 16931 BT-151) absente. Le mapping TVA de la plateforme doit poser la catégorie avant la " +
                "génération du Factur-X — aucune catégorie n'est inventée (CLAUDE.md n°2).";
            throw new FacturXGenerationException(pivot.Number, message);
        }

        return tax;
    }

    private static void StartRsm(XmlWriter writer, string name) =>
        writer.WriteStartElement(RsmPrefix, name, CiiProfile.RsmNamespace);

    private static void StartRam(XmlWriter writer, string name) =>
        writer.WriteStartElement(RamPrefix, name, CiiProfile.RamNamespace);

    private static void Ram(XmlWriter writer, string name, string value)
    {
        writer.WriteStartElement(RamPrefix, name, CiiProfile.RamNamespace);
        writer.WriteString(value);
        writer.WriteEndElement();
    }

    private static void RamOptional(XmlWriter writer, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Ram(writer, name, value);
        }
    }

    private static void RamWithScheme(XmlWriter writer, string name, string schemeId, string value)
    {
        writer.WriteStartElement(RamPrefix, name, CiiProfile.RamNamespace);
        writer.WriteAttributeString("schemeID", schemeId);
        writer.WriteString(value);
        writer.WriteEndElement();
    }

    private static void RamWithCurrency(XmlWriter writer, string name, string currencyId, string value)
    {
        writer.WriteStartElement(RamPrefix, name, CiiProfile.RamNamespace);
        writer.WriteAttributeString("currencyID", currencyId);
        writer.WriteString(value);
        writer.WriteEndElement();
    }

    private static decimal SumAmount(IEnumerable<decimal> values) =>
        PivotRounding.RoundAmount(values.Sum());

    private static string FormatAmount(decimal value) =>
        PivotRounding.RoundAmount(value).ToString("0.00", CultureInfo.InvariantCulture);

    // BT-146 (prix unitaire) RECOPIÉ fidèlement, sans arrondi à 2 décimales (un prix unitaire peut porter
    // plus de décimales) ; au moins deux décimales pour un xsd:decimal monétaire lisible.
    private static string FormatPrice(decimal value) =>
        value.ToString("0.00##########", CultureInfo.InvariantCulture);

    private static string FormatQuantity(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    // Taux (BT-119/BT-152) : recopié ; absent (exonéré/autoliquidation) → 0.
    private static string FormatPercent(decimal? value) =>
        (value ?? 0m).ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTime date) =>
        date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    // Ligne de ventilation TVA document (BG-23) dérivée : agrégats en decimal, catégorie/taux/VATEX
    // recopiés du pivot.
    private sealed record VatBreakdownLine(
        VatCategory Category, decimal? Rate, string? VatexCode, decimal BasisAmount, decimal CalculatedAmount);
}
