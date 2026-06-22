namespace Liakont.Modules.Pipeline.Infrastructure.Serialization;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Désérialise le JSON canonique (ADR-0007) d'un <see cref="PivotDocumentDto"/> relu depuis le magasin
/// de staging (PIP00). MIROIR EXACT de <c>CanonicalJson.WriteDocument</c> : le round-trip
/// <c>Serialize(Read(json)) == json</c> est garanti octet par octet (échelle décimale préservée,
/// énumérations par nom, dates <c>yyyy-MM-dd</c>, optionnels omis, collections toujours présentes).
/// Lecteur EXPLICITE (parcours de <see cref="JsonDocument"/> + constructeurs immuables, AUCUNE réflexion).
/// Il vit DANS le module Pipeline — pas dans <c>Liakont.Agent.Contracts</c> (BCL-only / zéro package).
/// Il NE RE-SÉRIALISE PAS pour ré-hacher : la re-vérification du <c>payload_hash</c> est faite par
/// <c>IPayloadStagingStore.ReadAsync</c> sur la string brute (PIP00), jamais par re-sérialisation.
/// <para>PÉRIMÈTRE — chemin de STAGING uniquement. Ce lecteur sert la RELECTURE du pivot depuis le
/// magasin de staging (CHECK/SEND, PIP00). L'INGESTION HTTP (PIV04,
/// <c>IngestDocumentBatchHandler</c>) ne l'utilise PAS : elle reçoit le DTO déjà désérialisé par
/// System.Text.Json (liaison minimal-API, <c>AgentApiJson</c>) puis re-dérive l'empreinte via
/// <c>CanonicalJson.Serialize(dto)</c>. Le round-trip d'intégrité de CETTE jambe STJ
/// (<c>STJ.Deserialize(canonique) → CanonicalJson == canonique</c>, dont dépendent l'anti-doublon et
/// l'archive WORM) est gardé par <c>AgentContractRehashIntegrityTests</c> côté Host, pas ici.</para>
/// </summary>
public static class PivotCanonicalJsonReader
{
    /// <summary>Reconstruit un document pivot depuis son JSON canonique.</summary>
    /// <param name="canonicalJson">Le JSON canonique (tel que produit par le writer du contrat).</param>
    /// <returns>Le document pivot reconstruit.</returns>
    public static PivotDocumentDto Read(string canonicalJson)
    {
        ArgumentNullException.ThrowIfNull(canonicalJson);

        using JsonDocument document = JsonDocument.Parse(canonicalJson);
        return ReadDocument(document.RootElement);
    }

    private static PivotDocumentDto ReadDocument(JsonElement element) => new(
        sourceDocumentKind: Str(element, "SourceDocumentKind"),
        number: Str(element, "Number"),
        issueDate: Date(element, "IssueDate"),
        sourceReference: Str(element, "SourceReference"),
        supplier: TryObject(element, "Supplier", out JsonElement supplier) ? ReadParty(supplier) : null,
        totals: ReadTotals(element.GetProperty("Totals")),
        operationCategory: ReadOperationCategory(element),
        currencyCode: Str(element, "CurrencyCode"),
        customer: TryObject(element, "Customer", out JsonElement customer) ? ReadParty(customer) : null,
        lines: ReadList(element, "Lines", ReadLine),
        creditNoteRefs: ReadList(element, "CreditNoteRefs", ReadReference),
        payments: ReadList(element, "Payments", ReadPayment),
        documentCharges: ReadList(element, "DocumentCharges", ReadCharge),
        invoicer: TryObject(element, "Invoicer", out JsonElement invoicer) ? ReadParty(invoicer) : null,
        payee: TryObject(element, "Payee", out JsonElement payee) ? ReadParty(payee) : null,
        isSelfBilled: Bool(element, "IsSelfBilled"),
        prepaidAmount: DecimalOrNull(element, "PrepaidAmount"),
        sourceData: StrOrNull(element, "SourceData"),
        paymentDueDate: DateOrNull(element, "PaymentDueDate"),
        isB2cReportingDeclaration: BoolOrFalse(element, "IsB2cReportingDeclaration"),
        sellerFees: ReadListOrNull(element, "SellerFees", ReadSellerFee),
        buyerFees: ReadListOrNull(element, "BuyerFees", ReadBuyerFee),
        invoicePeriod: TryObject(element, "InvoicePeriod", out JsonElement invoicePeriod) ? ReadInvoicePeriod(invoicePeriod) : null);

    // EN 16931 BG-14 (RD406, slot abonnement) : objet additif optionnel, miroir exact de
    // CanonicalJson.WriteInvoicePeriod (StartDate=BT-73 puis EndDate=BT-74, dates yyyy-MM-dd). Absent du
    // JSON → null (le writer omet un optionnel null) ; un document sans période traverse le staging inchangé.
    private static PivotInvoicePeriodDto ReadInvoicePeriod(JsonElement element) => new(
        startDate: Date(element, "StartDate"),
        endDate: Date(element, "EndDate"));

    private static PivotPartyDto ReadParty(JsonElement element) => new(
        name: Str(element, "Name"),
        siren: StrOrNull(element, "Siren"),
        siret: StrOrNull(element, "Siret"),
        vatNumber: StrOrNull(element, "VatNumber"),
        address: TryObject(element, "Address", out JsonElement address) ? ReadAddress(address) : null,
        email: StrOrNull(element, "Email"),
        isCompanyHint: Bool(element, "IsCompanyHint"));

    private static PivotAddressDto ReadAddress(JsonElement element) => new(
        line1: StrOrNull(element, "Line1"),
        line2: StrOrNull(element, "Line2"),
        postalCode: StrOrNull(element, "PostalCode"),
        city: StrOrNull(element, "City"),
        countryCode: StrOrNull(element, "CountryCode"));

    private static PivotTotalsDto ReadTotals(JsonElement element) => new(
        totalNet: Dec(element, "TotalNet"),
        totalTax: Dec(element, "TotalTax"),
        totalGross: Dec(element, "TotalGross"),
        sourceTotalGross: DecimalOrNull(element, "SourceTotalGross"));

    private static PivotLineDto ReadLine(JsonElement element) => new(
        description: Str(element, "Description"),
        netAmount: Dec(element, "NetAmount"),
        quantity: Dec(element, "Quantity"),
        unitPriceNet: DecimalOrNull(element, "UnitPriceNet"),
        sourceRegimeCodes: StrList(element, "SourceRegimeCodes"),
        taxes: ReadList(element, "Taxes", ReadLineTax),
        sourceLineRef: StrOrNull(element, "SourceLineRef"),
        sourceData: StrOrNull(element, "SourceData"),
        unitCode: StrOrNull(element, "UnitCode"));

    private static PivotLineTaxDto ReadLineTax(JsonElement element) => new(
        taxAmount: Dec(element, "TaxAmount"),
        rate: DecimalOrNull(element, "Rate"),
        categoryCode: TryString(element, "CategoryCode", out string category) ? EnumByName<VatCategory>(category) : null,
        vatexCode: StrOrNull(element, "VatexCode"));

    private static PivotDocumentRefDto ReadReference(JsonElement element) => new(
        number: Str(element, "Number"),
        issueDate: Date(element, "IssueDate"),
        sourceReference: StrOrNull(element, "SourceReference"));

    private static PivotPaymentDto ReadPayment(JsonElement element) => new(
        paymentDate: Date(element, "PaymentDate"),
        amount: Dec(element, "Amount"),
        method: StrOrNull(element, "Method"),
        relatedDocumentNumber: StrOrNull(element, "RelatedDocumentNumber"),
        sourceReference: StrOrNull(element, "SourceReference"));

    private static PivotDocumentChargeDto ReadCharge(JsonElement element) => new(
        isCharge: Bool(element, "IsCharge"),
        amount: Dec(element, "Amount"),
        reason: StrOrNull(element, "Reason"),
        reasonCode: StrOrNull(element, "ReasonCode"),
        sourceRegimeCodes: StrList(element, "SourceRegimeCodes"));

    private static PivotSellerFeeDto ReadSellerFee(JsonElement element) => new(
        lotReference: Str(element, "LotReference"),
        netAmount: Dec(element, "NetAmount"),
        sourceRegimeCode: StrOrNull(element, "SourceRegimeCode"),
        sourceLineRef: StrOrNull(element, "SourceLineRef"),
        description: StrOrNull(element, "Description"));

    // Frais acheteur (B2C-08c) : miroir exact de ReadSellerFee — collection additive optionnelle lue par
    // ReadListOrNull (absente du JSON → null, comme l'omet le writer).
    private static PivotBuyerFeeDto ReadBuyerFee(JsonElement element) => new(
        lotReference: Str(element, "LotReference"),
        netAmount: Dec(element, "NetAmount"),
        sourceRegimeCode: StrOrNull(element, "SourceRegimeCode"),
        sourceLineRef: StrOrNull(element, "SourceLineRef"),
        description: StrOrNull(element, "Description"));

    // ── Primitives de lecture (par NOM de membre : l'ordre est sans incidence sur la lecture) ──
    private static string Str(JsonElement element, string name) => element.GetProperty(name).GetString()!;

    private static string? StrOrNull(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;

    private static bool TryString(JsonElement element, string name, out string value)
    {
        if (element.TryGetProperty(name, out JsonElement raw))
        {
            value = raw.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool Bool(JsonElement element, string name) => element.GetProperty(name).GetBoolean();

    // Booléen optionnel ABSENT du JSON → false : miroir EXACT du writer, qui OMET le marqueur 10.3 (B2C01)
    // quand il est faux (champ additif hash-neutre). Un document qui ne porte pas la clé n'est jamais une
    // déclaration B2C 10.3 ; le round-trip d'un tel document reste octet par octet identique.
    private static bool BoolOrFalse(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.GetBoolean();

    private static DateTime Date(JsonElement element, string name) =>
        DateTime.ParseExact(Str(element, name), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Date optionnelle (EN 16931 BT-9) : absente du JSON → null (le writer omet un optionnel null) ;
    // miroir exact du writer, l'échéance ne fait jamais partie du round-trip d'un document qui ne la porte pas.
    private static DateTime? DateOrNull(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
            ? DateTime.ParseExact(value.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

    // Échelle décimale PRÉSERVÉE : on parse le TEXTE BRUT du jeton (« 10.00 » → 10.00m), jamais
    // GetDecimal() — pour que la re-sérialisation reproduise l'échelle source octet par octet (ADR-0007).
    private static decimal Dec(JsonElement element, string name) => ParseDecimal(element.GetProperty(name));

    private static decimal? DecimalOrNull(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) ? ParseDecimal(value) : null;

    private static decimal ParseDecimal(JsonElement element) =>
        decimal.Parse(element.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static TEnum EnumByName<TEnum>(string name)
        where TEnum : struct =>
        Enum.Parse<TEnum>(name);

    // Nature d'opération optionnelle (ADR-0031 amendé) : absente du JSON → null (la plateforme la remplit à
    // l'ingestion ; un pivot émis par l'agent ne la porte pas). Miroir exact du writer (omis si null).
    private static OperationCategory? ReadOperationCategory(JsonElement element) =>
        TryString(element, "OperationCategory", out string value) ? EnumByName<OperationCategory>(value) : null;

    private static List<string> StrList(JsonElement element, string name)
    {
        JsonElement array = element.GetProperty(name);
        var list = new List<string>(array.GetArrayLength());
        foreach (JsonElement item in array.EnumerateArray())
        {
            list.Add(item.GetString()!);
        }

        return list;
    }

    private static List<T> ReadList<T>(JsonElement element, string name, Func<JsonElement, T> read)
    {
        JsonElement array = element.GetProperty(name);
        var list = new List<T>(array.GetArrayLength());
        foreach (JsonElement item in array.EnumerateArray())
        {
            list.Add(read(item));
        }

        return list;
    }

    // Collection OPTIONNELLE (frais vendeur B2C-08) : absente du JSON → null (le writer omet la clé quand le
    // champ n'est pas porté, champ additif hash-neutre). Miroir exact du writer — le round-trip d'un document
    // sans frais vendeur reste octet par octet identique (la collection n'est jamais coalescée en vide).
    private static List<T>? ReadListOrNull<T>(JsonElement element, string name, Func<JsonElement, T> read) =>
        element.TryGetProperty(name, out _) ? ReadList(element, name, read) : null;

    private static bool TryObject(JsonElement element, string name, out JsonElement value) =>
        element.TryGetProperty(name, out value);
}
