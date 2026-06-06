namespace Liakont.Modules.Pipeline.Infrastructure.Check;

using System;
using System.Collections.Generic;
using System.Globalization;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TvaMapping.Contracts.Services;

/// <summary>
/// Logique PURE (sans I/O) du mapping TVA d'un document au CHECK (PIP01b) : construit les requêtes de
/// mapping depuis le pivot, agrège le résultat en décision (bloqué / prêt) et — si prêt — produit un pivot
/// ENRICHI immuable (catégorie + VATEX par ligne). Aucune règle fiscale n'est inventée ici (CLAUDE.md n°2).
///
/// <para><b>Part = Autre (décision tracée).</b> La table de mapping est clé par le couple
/// (code régime, part) en match EXACT (F03 §4.1, item TVA02). La DÉRIVATION de la part adjudication/frais
/// depuis une ligne pivot est une décision fiscale OUVERTE et non sourcée (ADR-0004 / F03 §2.3) : le
/// pivot générique ne porte AUCUN découpage adjudication/frais (cf. <see cref="PivotLineDto"/>). On fournit
/// donc <see cref="TvaMappingPart.Autre"/> = « hors du découpage adjudication/frais » — le choix FIDÈLE à
/// l'absence de cette distinction dans la source, JAMAIS deviné. Deviner adjudication/frais SERAIT inventer
/// une règle. Conséquence sûre par défaut (F03 §4.1, defaultBehavior=block) : un régime/part absent de la
/// table validée BLOQUE le document (jamais d'envoi à l'aveugle). Le découpage adjudication/frais des
/// enchères relève d'une extension future du contrat pivot (ADR), hors périmètre PIP01b.</para>
///
/// <para><b>Forme 1 régime ↔ 1 ventilation (limite V1 tracée).</b> Le moteur peut scinder une ligne en
/// plusieurs lignes pivot quand EN 16931 l'exige (BG-30, ADR-0004 D3-1), mais l'ASSOCIATION d'un code
/// régime à une ventilation TVA particulière n'est pas sourcée pour le cas multi-codes/multi-taxes. CHECK V1
/// n'enrichit donc que les lignes de forme NON AMBIGUË (exactement 1 code régime ET 1 ventilation) ; toute
/// autre ligne est BLOQUÉE avec un motif explicite (aucune association devinée — CLAUDE.md n°2/3). Les
/// documents réels du contrat (golden files contrat-v1) sont tous de cette forme.</para>
/// </summary>
internal static class CheckTvaMapping
{
    /// <summary>Part fournie au mapping pour le pipeline générique (voir la doc de classe : Autre = pas de découpage).</summary>
    public const TvaMappingPart LinePart = TvaMappingPart.Autre;

    /// <summary>
    /// Construit le plan de mapping : une requête par ligne de forme non ambiguë, et un motif de blocage
    /// pour chaque ligne hors forme V1. L'ordre des requêtes est l'ordre des lignes conformes.
    /// </summary>
    public static CheckMappingPlan BuildPlan(PivotDocumentDto pivot)
    {
        ArgumentNullException.ThrowIfNull(pivot);

        var requests = new List<TvaLineMappingRequest>();
        var requestLineIndexes = new List<int>();
        var shapeBlockReasons = new List<string>();

        for (var i = 0; i < pivot.Lines.Count; i++)
        {
            var line = pivot.Lines[i];

            if (line.SourceRegimeCodes.Count != 1 || line.Taxes.Count != 1)
            {
                shapeBlockReasons.Add(
                    $"{LinePrefix(i, line)} porte {line.SourceRegimeCodes.Count} code(s) régime et " +
                    $"{line.Taxes.Count} ventilation(s) TVA : l'association régime → ventilation n'est pas " +
                    "déterminée par une règle sourcée (scission EN 16931 BG-30 / ADR-0004 D3-1 non couverte par " +
                    "le contrôle V1) — document bloqué (aucune association devinée). Action opérateur : vérifiez " +
                    "la donnée source de cette ligne ; ce cas sera couvert par une évolution ultérieure du pipeline.");
                continue;
            }

            // SourceFlags = null : le pivot générique ne porte aucun flag source STRUCTURÉ (F03 §3 :
            // RegimeMarge / assujetti_tva). Une règle de table qui exige un flag restera donc non satisfaite
            // → blocage (sûr par défaut), jamais un mapping deviné. L'extraction de flags est hors périmètre V1.
            requests.Add(new TvaLineMappingRequest
            {
                SourceRegimeCode = line.SourceRegimeCodes[0],
                Part = LinePart,
                SourceFlags = null,
                LineRef = i.ToString(CultureInfo.InvariantCulture),
            });
            requestLineIndexes.Add(i);
        }

        return new CheckMappingPlan
        {
            Requests = requests,
            RequestLineIndexes = requestLineIndexes,
            ShapeBlockReasons = shapeBlockReasons,
        };
    }

    /// <summary>
    /// Agrège le résultat du mapping (dont les lignes correspondent, dans l'ordre, à
    /// <see cref="CheckMappingPlan.Requests"/>) en décision. Si aucune ligne n'est bloquée, produit le pivot
    /// enrichi (catégorie + VATEX posés sur l'unique ventilation de chaque ligne conforme) et la version de table.
    /// </summary>
    public static CheckEvaluation Evaluate(PivotDocumentDto pivot, CheckMappingPlan plan, DocumentTvaMappingResult mapping)
    {
        ArgumentNullException.ThrowIfNull(pivot);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(mapping);

        if (mapping.Lines.Count != plan.Requests.Count)
        {
            throw new InvalidOperationException(
                "Incohérence interne du CHECK : le mapping a retourné un nombre de lignes différent du nombre de requêtes.");
        }

        var reasons = new List<string>(plan.ShapeBlockReasons);

        for (var i = 0; i < mapping.Lines.Count; i++)
        {
            var result = mapping.Lines[i];
            if (!result.IsMapped)
            {
                var lineIndex = plan.RequestLineIndexes[i];
                reasons.Add($"{LinePrefix(lineIndex, pivot.Lines[lineIndex])} : {result.BlockReason}");
            }
        }

        if (reasons.Count > 0)
        {
            return CheckEvaluation.Blocked(string.Join(Environment.NewLine, reasons));
        }

        var enrichedLines = new List<PivotLineDto>(pivot.Lines);
        for (var i = 0; i < mapping.Lines.Count; i++)
        {
            var lineIndex = plan.RequestLineIndexes[i];
            enrichedLines[lineIndex] = EnrichLine(pivot.Lines[lineIndex], mapping.Lines[i]);
        }

        var enriched = Rebuild(pivot, enrichedLines);
        return CheckEvaluation.Ready(enriched, mapping.MappingVersion);
    }

    /// <summary>
    /// Pose la catégorie UNCL5305 et le VATEX issus du mapping sur l'UNIQUE ventilation de la ligne. Les
    /// montants et le taux source ne sont JAMAIS recalculés (le pivot ne calcule rien — F01-F02 §3.7 règle 2 ;
    /// la résolution d'un taux ComputedFromSource est un geste d'envoi aval, PIP01c). Seule la classification
    /// (catégorie/VATEX), produit du mapping plateforme, est ajoutée.
    /// </summary>
    private static PivotLineDto EnrichLine(PivotLineDto line, TvaLineMappingResult result)
    {
        var originalTax = line.Taxes[0];
        var category = Enum.Parse<VatCategory>(result.Category!, ignoreCase: false);

        var enrichedTax = new PivotLineTaxDto(
            taxAmount: originalTax.TaxAmount,
            rate: originalTax.Rate,
            categoryCode: category,
            vatexCode: result.Vatex);

        return new PivotLineDto(
            description: line.Description,
            netAmount: line.NetAmount,
            quantity: line.Quantity,
            unitPriceNet: line.UnitPriceNet,
            sourceRegimeCodes: line.SourceRegimeCodes,
            taxes: new[] { enrichedTax },
            sourceLineRef: line.SourceLineRef,
            sourceData: line.SourceData);
    }

    private static PivotDocumentDto Rebuild(PivotDocumentDto pivot, IReadOnlyList<PivotLineDto> lines)
    {
        return new PivotDocumentDto(
            sourceDocumentKind: pivot.SourceDocumentKind,
            number: pivot.Number,
            issueDate: pivot.IssueDate,
            sourceReference: pivot.SourceReference,
            supplier: pivot.Supplier,
            totals: pivot.Totals,
            operationCategory: pivot.OperationCategory,
            currencyCode: pivot.CurrencyCode,
            customer: pivot.Customer,
            lines: lines,
            creditNoteRefs: pivot.CreditNoteRefs,
            payments: pivot.Payments,
            documentCharges: pivot.DocumentCharges,
            invoicer: pivot.Invoicer,
            payee: pivot.Payee,
            isSelfBilled: pivot.IsSelfBilled,
            prepaidAmount: pivot.PrepaidAmount,
            sourceData: pivot.SourceData);
    }

    private static string LinePrefix(int index, PivotLineDto line) =>
        $"Ligne {(index + 1).ToString(CultureInfo.InvariantCulture)} (« {line.Description} »)";
}
