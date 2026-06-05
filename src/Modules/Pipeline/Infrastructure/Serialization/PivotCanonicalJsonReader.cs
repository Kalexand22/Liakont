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
        supplier: ReadParty(element.GetProperty("Supplier")),
        totals: ReadTotals(element.GetProperty("Totals")),
        operationCategory: EnumByName<OperationCategory>(Str(element, "OperationCategory")),
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
        sourceData: StrOrNull(element, "SourceData"));

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
        sourceData: StrOrNull(element, "SourceData"));

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

    private static DateTime Date(JsonElement element, string name) =>
        DateTime.ParseExact(Str(element, name), "yyyy-MM-dd", CultureInfo.InvariantCulture);

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

    private static bool TryObject(JsonElement element, string name, out JsonElement value) =>
        element.TryGetProperty(name, out value);
}
