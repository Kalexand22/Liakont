namespace Liakont.Agent.Adapters.DemoErpA;

using System;
using System.Collections.Generic;
using Liakont.Agent.Adapters.DemoErpA.Source;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core.Extraction;
using Newtonsoft.Json;

/// <summary>
/// Transforme une facture BRUTE DemoErpA (<see cref="DemoErpAInvoice"/> + ses lignes) en document pivot
/// EN 16931. Respecte le contrat d'extraction (F01-F02 §4.2) : ne mappe PAS la TVA (R3 — régimes bruts,
/// <c>CategoryCode</c>/<c>VatexCode</c> nuls), ne classe PAS facture/avoir (<c>SourceDocumentKind</c>
/// brut « FAC »/« AVO », ADR-0004 D3-3), ne calcule aucun montant (les montants <c>decimal</c> de la
/// source sont seulement arrondis au centime via <see cref="PivotRounding"/>). L'identité de l'émetteur
/// et la nature d'opération viennent de la config (paramétrage tenant — absents de la base, F01-F02 §4.3).
/// </summary>
internal static class DemoErpARowMapper
{
    /// <summary>Type de pièce source « facture ».</summary>
    internal const string PieceFacture = "FAC";

    /// <summary>Type de pièce source « avoir ».</summary>
    internal const string PieceAvoir = "AVO";

    /// <summary>Préfixe NAMESPACÉ de la référence source (anti-collision cross-agent — ADR-0023 D8).</summary>
    internal const string SourceReferencePrefix = "demoerpa:";

    private const string CurrencyDefault = "EUR";

    /// <summary>Mappe une facture (ou un avoir) DemoErpA en document pivot.</summary>
    /// <param name="invoice">La facture source (avec ses lignes).</param>
    /// <param name="config">La configuration de l'adaptateur (émetteur, nature d'opération).</param>
    /// <returns>Le document pivot correspondant.</returns>
    public static PivotDocumentDto MapDocument(DemoErpAInvoice invoice, SourceEmitterConfig config)
    {
        if (invoice is null)
        {
            throw new ArgumentNullException(nameof(invoice));
        }

        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        string factureId = RequireField(invoice.FactureId, "facture_id", invoice.FactureId);
        string number = RequireField(invoice.Numero, "numero", factureId);
        string kind = RequireField(invoice.TypePiece, "type_piece", factureId);

        if (invoice.DateEmission == default(DateTime))
        {
            throw new SourceSchemaException(
                $"Champ source obligatoire « date_emission » absent ou invalide (facture « {factureId} ») : "
                + "document bloqué, la date n'est jamais devinée (ADR-0004 D3-3). Vérifiez l'extraction des données source.");
        }

        var lines = new List<PivotLineDto>();
        foreach (DemoErpALine line in invoice.Lignes)
        {
            lines.Add(MapLine(line, factureId));
        }

        PivotDocumentRefDto[] creditNoteRefs = MapCreditNoteRefs(invoice, kind);

        return new PivotDocumentDto(
            sourceDocumentKind: kind,
            number: number,
            issueDate: invoice.DateEmission,
            sourceReference: SourceReferencePrefix + number,
            supplier: MapEmitter(config),
            totals: new PivotTotalsDto(
                totalNet: PivotRounding.RoundAmount(invoice.TotalHt),
                totalTax: PivotRounding.RoundAmount(invoice.TotalTva),
                totalGross: PivotRounding.RoundAmount(invoice.TotalTtc),
                sourceTotalGross: PivotRounding.RoundAmount(invoice.TotalTtc)),
            operationCategory: config.OperationCategory,
            currencyCode: string.IsNullOrWhiteSpace(invoice.Devise) ? CurrencyDefault : invoice.Devise!.Trim(),
            customer: MapCustomer(invoice),
            lines: lines,
            creditNoteRefs: creditNoteRefs,
            payments: null,
            documentCharges: null,
            invoicer: null,
            payee: null,
            isSelfBilled: false,
            prepaidAmount: null,
            sourceData: BuildSourceData(invoice),
            paymentDueDate: null);
    }

    private static PivotLineDto MapLine(DemoErpALine line, string factureId)
    {
        string description = RequireField(line.Designation, "designation", factureId);

        IReadOnlyList<string> regimeCodes = string.IsNullOrWhiteSpace(line.CodeRegime)
            ? Array.Empty<string>()
            : new[] { line.CodeRegime!.Trim() };

        var taxes = new[]
        {
            new PivotLineTaxDto(
                taxAmount: PivotRounding.RoundAmount(line.MontantTva),
                rate: line.TauxTva,
                categoryCode: null,
                vatexCode: null),
        };

        return new PivotLineDto(
            description: description,
            netAmount: PivotRounding.RoundAmount(line.MontantHt),
            quantity: line.Quantite,
            unitPriceNet: line.PrixUnitaire.HasValue ? PivotRounding.RoundAmount(line.PrixUnitaire.Value) : (decimal?)null,
            sourceRegimeCodes: regimeCodes,
            taxes: taxes,
            sourceLineRef: line.NoLigne,
            sourceData: null);
    }

    private static PivotPartyDto MapEmitter(SourceEmitterConfig config) =>
        new PivotPartyDto(name: config.EmitterName, siren: config.EmitterSiren, isCompanyHint: true);

    private static PivotPartyDto? MapCustomer(DemoErpAInvoice invoice)
    {
        // B2C anonyme : une facture peut n'avoir aucun acheteur nommé (le e-reporting B2C ne transmet pas
        // de données nominatives) — dans ce cas, pas de tiers destinataire (F01-F02 §6).
        if (string.IsNullOrWhiteSpace(invoice.ClientNom))
        {
            return null;
        }

        PivotAddressDto? address = HasAddress(invoice.ClientCodePostal, invoice.ClientVille, invoice.ClientPays)
            ? new PivotAddressDto(postalCode: invoice.ClientCodePostal, city: invoice.ClientVille, countryCode: invoice.ClientPays)
            : null;

        // IsCompanyHint = transcription BRUTE du champ source « est_societe » — aucune heuristique côté
        // adaptateur ; toute décision B2B/B2C vit dans la Validation plateforme (VAL05).
        return new PivotPartyDto(
            name: invoice.ClientNom!,
            siren: invoice.ClientSiren,
            address: address,
            isCompanyHint: invoice.ClientEstSociete);
    }

    private static PivotDocumentRefDto[] MapCreditNoteRefs(DemoErpAInvoice invoice, string kind)
    {
        if (!string.Equals(kind, PieceAvoir, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<PivotDocumentRefDto>();
        }

        // Un avoir DOIT référencer sa facture d'origine, avec sa date (EN 16931 BT-25, non-nullable) : une
        // origine non résoluble bloque l'avoir, jamais devinée (ADR-0004 D3-3, CLAUDE.md n°3).
        if (string.IsNullOrWhiteSpace(invoice.FactureOrigineNumero) || invoice.OrigineDate is null)
        {
            throw new SourceSchemaException(
                $"Avoir « {invoice.Numero} » sans facture d'origine résoluble (origine « {invoice.FactureOrigineNumero} ») "
                + "ou sans date d'origine : document bloqué, l'origine n'est jamais devinée (ADR-0004 D3-3).");
        }

        return new[]
        {
            new PivotDocumentRefDto(
                number: invoice.FactureOrigineNumero!,
                issueDate: invoice.OrigineDate.Value,
                sourceReference: SourceReferencePrefix + invoice.FactureOrigineNumero),
        };
    }

    private static bool HasAddress(string? postalCode, string? city, string? countryCode) =>
        !string.IsNullOrWhiteSpace(postalCode) || !string.IsNullOrWhiteSpace(city) || !string.IsNullOrWhiteSpace(countryCode);

    private static string RequireField(string? value, string fieldName, string? factureId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SourceSchemaException(
                $"Champ source obligatoire « {fieldName} » absent (facture « {factureId} ») : schéma incompatible. Vérifiez l'extraction.");
        }

        return value!;
    }

    private static string BuildSourceData(DemoErpAInvoice invoice) =>
        JsonConvert.SerializeObject(new SourceDataDocument
        {
            FactureId = invoice.FactureId,
            TotalHtBrut = invoice.TotalHt,
            TotalTvaBrut = invoice.TotalTva,
            TotalTtcBrut = invoice.TotalTtc,
        });

    // Vue déterministe (ordre figé) des montants source ORIGINAUX, sérialisée en JSON pour la
    // traçabilité (F01-F02 §3.7 règle 1) — empreinte stable.
    private sealed class SourceDataDocument
    {
        [JsonProperty("facture_id", NullValueHandling = NullValueHandling.Include)]
        public string? FactureId { get; set; }

        [JsonProperty("total_ht_brut")]
        public decimal TotalHtBrut { get; set; }

        [JsonProperty("total_tva_brut")]
        public decimal TotalTvaBrut { get; set; }

        [JsonProperty("total_ttc_brut")]
        public decimal TotalTtcBrut { get; set; }
    }
}
