namespace Liakont.Agent.Contracts.Serialization;

using System;
using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Sérialisation JSON canonique du modèle pivot (PIV02). Produit, via <see cref="CanonicalJsonWriter"/>,
/// une représentation déterministe d'un <see cref="PivotDocumentDto"/> :
/// <list type="bullet">
/// <item>les membres sont émis dans l'ORDRE DE DÉCLARATION du DTO (ordre figé du contrat ; en V1 un
/// champ s'AJOUTE en fin, ne se renomme/supprime jamais — <c>ADR-0007</c>, AgentContractVersion) ;</item>
/// <item>les noms de membres sont les noms de propriété C# (PascalCase) ;</item>
/// <item>un champ optionnel <c>null</c> est OMIS (jamais émis à <c>null</c>) ; une collection est
/// toujours émise, même vide (<c>[]</c>) ;</item>
/// <item>les énumérations sont émises par leur NOM (ex. catégorie UNCL5305 <c>"E"</c>,
/// <c>OperationCategory</c> <c>"Mixte"</c>), pas par leur valeur numérique.</item>
/// </list>
/// C'est le JSON hashé pour l'anti-doublon et la détection d'altération (<see cref="PayloadHasher"/>,
/// consommé par PIV04/TRK03). Aucune logique métier : ni calcul, ni validation, ni règle TVA.
/// </summary>
public static class CanonicalJson
{
    /// <summary>Sérialise un document pivot en JSON canonique.</summary>
    /// <param name="document">Le document à sérialiser (non nul).</param>
    /// <returns>Le JSON canonique (ASCII, compact, déterministe).</returns>
    public static string Serialize(PivotDocumentDto document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var writer = new CanonicalJsonWriter();
        WriteDocument(writer, document);
        return writer.ToString();
    }

    private static void WriteDocument(CanonicalJsonWriter writer, PivotDocumentDto document)
    {
        writer.BeginObject();

        writer.WritePropertyName("SourceDocumentKind");
        writer.WriteString(document.SourceDocumentKind);
        writer.WritePropertyName("Number");
        writer.WriteString(document.Number);
        writer.WritePropertyName("IssueDate");
        writer.WriteDate(document.IssueDate);
        writer.WritePropertyName("SourceReference");
        writer.WriteString(document.SourceReference);
        writer.WritePropertyName("Supplier");
        WriteParty(writer, document.Supplier);
        writer.WritePropertyName("Totals");
        WriteTotals(writer, document.Totals);
        writer.WritePropertyName("OperationCategory");
        writer.WriteString(document.OperationCategory.ToString());
        writer.WritePropertyName("CurrencyCode");
        writer.WriteString(document.CurrencyCode);

        if (document.Customer != null)
        {
            writer.WritePropertyName("Customer");
            WriteParty(writer, document.Customer);
        }

        writer.WritePropertyName("Lines");
        WriteArray(writer, document.Lines, WriteLine);
        writer.WritePropertyName("CreditNoteRefs");
        WriteArray(writer, document.CreditNoteRefs, WriteDocumentRef);
        writer.WritePropertyName("Payments");
        WriteArray(writer, document.Payments, WritePayment);
        writer.WritePropertyName("DocumentCharges");
        WriteArray(writer, document.DocumentCharges, WriteCharge);

        if (document.Invoicer != null)
        {
            writer.WritePropertyName("Invoicer");
            WriteParty(writer, document.Invoicer);
        }

        if (document.Payee != null)
        {
            writer.WritePropertyName("Payee");
            WriteParty(writer, document.Payee);
        }

        writer.WritePropertyName("IsSelfBilled");
        writer.WriteBoolean(document.IsSelfBilled);

        if (document.PrepaidAmount.HasValue)
        {
            writer.WritePropertyName("PrepaidAmount");
            writer.WriteDecimal(document.PrepaidAmount.Value);
        }

        if (document.SourceData != null)
        {
            writer.WritePropertyName("SourceData");
            writer.WriteString(document.SourceData);
        }

        writer.EndObject();
    }

    private static void WriteParty(CanonicalJsonWriter writer, PivotPartyDto party)
    {
        writer.BeginObject();

        writer.WritePropertyName("Name");
        writer.WriteString(party.Name);
        WriteOptionalString(writer, "Siren", party.Siren);
        WriteOptionalString(writer, "Siret", party.Siret);
        WriteOptionalString(writer, "VatNumber", party.VatNumber);

        if (party.Address != null)
        {
            writer.WritePropertyName("Address");
            WriteAddress(writer, party.Address);
        }

        WriteOptionalString(writer, "Email", party.Email);
        writer.WritePropertyName("IsCompanyHint");
        writer.WriteBoolean(party.IsCompanyHint);

        writer.EndObject();
    }

    private static void WriteAddress(CanonicalJsonWriter writer, PivotAddressDto address)
    {
        writer.BeginObject();
        WriteOptionalString(writer, "Line1", address.Line1);
        WriteOptionalString(writer, "Line2", address.Line2);
        WriteOptionalString(writer, "PostalCode", address.PostalCode);
        WriteOptionalString(writer, "City", address.City);
        WriteOptionalString(writer, "CountryCode", address.CountryCode);
        writer.EndObject();
    }

    private static void WriteTotals(CanonicalJsonWriter writer, PivotTotalsDto totals)
    {
        writer.BeginObject();
        writer.WritePropertyName("TotalNet");
        writer.WriteDecimal(totals.TotalNet);
        writer.WritePropertyName("TotalTax");
        writer.WriteDecimal(totals.TotalTax);
        writer.WritePropertyName("TotalGross");
        writer.WriteDecimal(totals.TotalGross);
        WriteOptionalDecimal(writer, "SourceTotalGross", totals.SourceTotalGross);
        writer.EndObject();
    }

    private static void WriteLine(CanonicalJsonWriter writer, PivotLineDto line)
    {
        writer.BeginObject();
        writer.WritePropertyName("Description");
        writer.WriteString(line.Description);
        writer.WritePropertyName("NetAmount");
        writer.WriteDecimal(line.NetAmount);
        writer.WritePropertyName("Quantity");
        writer.WriteDecimal(line.Quantity);
        WriteOptionalDecimal(writer, "UnitPriceNet", line.UnitPriceNet);
        writer.WritePropertyName("SourceRegimeCodes");
        WriteStringArray(writer, line.SourceRegimeCodes);
        writer.WritePropertyName("Taxes");
        WriteArray(writer, line.Taxes, WriteLineTax);
        WriteOptionalString(writer, "SourceLineRef", line.SourceLineRef);
        WriteOptionalString(writer, "SourceData", line.SourceData);
        writer.EndObject();
    }

    private static void WriteLineTax(CanonicalJsonWriter writer, PivotLineTaxDto tax)
    {
        writer.BeginObject();
        writer.WritePropertyName("TaxAmount");
        writer.WriteDecimal(tax.TaxAmount);
        WriteOptionalDecimal(writer, "Rate", tax.Rate);

        if (tax.CategoryCode.HasValue)
        {
            writer.WritePropertyName("CategoryCode");
            writer.WriteString(tax.CategoryCode.Value.ToString());
        }

        WriteOptionalString(writer, "VatexCode", tax.VatexCode);
        writer.EndObject();
    }

    private static void WriteDocumentRef(CanonicalJsonWriter writer, PivotDocumentRefDto reference)
    {
        writer.BeginObject();
        writer.WritePropertyName("Number");
        writer.WriteString(reference.Number);
        writer.WritePropertyName("IssueDate");
        writer.WriteDate(reference.IssueDate);
        WriteOptionalString(writer, "SourceReference", reference.SourceReference);
        writer.EndObject();
    }

    private static void WritePayment(CanonicalJsonWriter writer, PivotPaymentDto payment)
    {
        writer.BeginObject();
        writer.WritePropertyName("PaymentDate");
        writer.WriteDate(payment.PaymentDate);
        writer.WritePropertyName("Amount");
        writer.WriteDecimal(payment.Amount);
        WriteOptionalString(writer, "Method", payment.Method);
        WriteOptionalString(writer, "RelatedDocumentNumber", payment.RelatedDocumentNumber);
        WriteOptionalString(writer, "SourceReference", payment.SourceReference);
        writer.EndObject();
    }

    private static void WriteCharge(CanonicalJsonWriter writer, PivotDocumentChargeDto charge)
    {
        writer.BeginObject();
        writer.WritePropertyName("IsCharge");
        writer.WriteBoolean(charge.IsCharge);
        writer.WritePropertyName("Amount");
        writer.WriteDecimal(charge.Amount);
        WriteOptionalString(writer, "Reason", charge.Reason);
        WriteOptionalString(writer, "ReasonCode", charge.ReasonCode);
        writer.WritePropertyName("SourceRegimeCodes");
        WriteStringArray(writer, charge.SourceRegimeCodes);
        writer.EndObject();
    }

    private static void WriteArray<T>(
        CanonicalJsonWriter writer,
        IReadOnlyList<T> items,
        Action<CanonicalJsonWriter, T> writeItem)
    {
        writer.BeginArray();
        foreach (T item in items)
        {
            writer.BeginArrayElement();
            writeItem(writer, item);
        }

        writer.EndArray();
    }

    private static void WriteStringArray(CanonicalJsonWriter writer, IReadOnlyList<string> items)
    {
        writer.BeginArray();
        foreach (string item in items)
        {
            writer.BeginArrayElement();
            writer.WriteString(item);
        }

        writer.EndArray();
    }

    private static void WriteOptionalString(CanonicalJsonWriter writer, string name, string? value)
    {
        if (value != null)
        {
            writer.WritePropertyName(name);
            writer.WriteString(value);
        }
    }

    private static void WriteOptionalDecimal(CanonicalJsonWriter writer, string name, decimal? value)
    {
        if (value.HasValue)
        {
            writer.WritePropertyName(name);
            writer.WriteDecimal(value.Value);
        }
    }
}
