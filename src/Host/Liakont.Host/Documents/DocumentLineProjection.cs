namespace Liakont.Host.Documents;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Host.Components;
using Liakont.Modules.Pipeline.Infrastructure.Serialization;
using Liakont.Platform.Pivot;

/// <summary>
/// Projette le pivot TRANSMIS d'un document (snapshot canonique ADR-0007 porté par le dernier événement
/// <c>DocumentIssued</c>/<c>DocumentRejected</c>) en contenu affichable (<see cref="DocumentContentView"/>)
/// pour l'onglet « Contenu » du détail (FIX205, F10 §2.3) : lignes, charges/remises de niveau document, et
/// contrôle de cohérence des totaux. Le snapshot étant le pivot RÉELLEMENT envoyé à la Plateforme Agréée, la
/// catégorie/le VATEX y sont déjà résolus par le mapping plateforme — c'est la seule source au niveau Contracts
/// qui porte « régime source → catégorie/VATEX résultante » (un document non transmis n'a pas encore de pivot
/// mappé exposé : la vue affiche alors une note, pas de ligne inventée). PURE et SANS état : aucune règle métier
/// ni calcul fiscal inventé — les montants viennent du pivot (la source), la désérialisation réutilise le lecteur
/// canonique du Pipeline (échelle décimale préservée, ADR-0007) et le contrôle de cohérence est le MIROIR de la
/// règle de validation <c>LineTotalsRule</c> (F04 §3.3, BR-CO-13), via l'arrondi commun <see cref="PivotRounding"/>.
/// Un snapshot illisible NE DOIT JAMAIS casser le détail en lecture (le pivot fait foi dans l'export WORM) : la
/// projection retombe sur un contenu vide.
/// </summary>
public static class DocumentLineProjection
{
    /// <summary>Sépare les valeurs jointes (régimes source, catégories multiples) d'une cellule.</summary>
    private const string JoinSeparator = ", ";

    private const string Placeholder = "—";

    /// <summary>
    /// Construit le contenu affichable depuis le JSON canonique du pivot transmis. <c>null</c>/vide (document
    /// non encore transmis) ou snapshot illisible → contenu vide (la vue bascule alors sur sa note).
    /// </summary>
    /// <param name="transmittedPivotJson">JSON canonique du pivot transmis (PayloadSnapshot), ou <c>null</c>.</param>
    public static DocumentContentView FromTransmittedSnapshot(string? transmittedPivotJson)
    {
        if (string.IsNullOrWhiteSpace(transmittedPivotJson))
        {
            return DocumentContentView.Empty;
        }

        PivotDocumentDto pivot;
        try
        {
            pivot = PivotCanonicalJsonReader.Read(transmittedPivotJson);
        }
        catch (Exception ex) when (
            ex is JsonException
            or FormatException
            or OverflowException
            or ArgumentException
            or KeyNotFoundException
            or InvalidOperationException)
        {
            // Snapshot transmis illisible (jamais attendu : il a été produit par le writer canonique au moment
            // de l'envoi) : on NE casse PAS la lecture du détail — le pivot intègre reste dans le coffre WORM,
            // récupérable via l'export pour contrôle fiscal (onglet Archive). La vue affichera sa note.
            return DocumentContentView.Empty;
        }

        return new DocumentContentView
        {
            Lines = pivot.Lines.Select(ToView).ToList(),
            Charges = pivot.DocumentCharges.Select(ToChargeView).ToList(),
            Totals = BuildTotalsCheck(pivot),
        };
    }

    private static DocumentLineView ToView(PivotLineDto line)
    {
        var taxes = line.Taxes;

        return new DocumentLineView
        {
            Label = line.Description,
            Quantity = line.Quantity,
            NetAmount = line.NetAmount,
            SourceRegime = line.SourceRegimeCodes.Count == 0
                ? Placeholder
                : string.Join(JoinSeparator, line.SourceRegimeCodes),
            Category = CategoryDisplay(taxes),
            Vatex = VatexDisplay(taxes),
            TaxAmount = taxes.Count == 0 ? null : taxes.Sum(t => t.TaxAmount),
            Rate = UniformRate(taxes),
        };
    }

    private static DocumentChargeView ToChargeView(PivotDocumentChargeDto charge) => new()
    {
        Label = string.IsNullOrWhiteSpace(charge.Reason)
            ? (charge.IsCharge ? "Charge de niveau document" : "Remise de niveau document")
            : charge.Reason!,
        IsCharge = charge.IsCharge,
        Amount = charge.Amount,
    };

    /// <summary>
    /// Contrôle de cohérence MIROIR de <c>LineTotalsRule</c> (BR-CO-13, F04 §3.3) : net réconcilié AVEC les
    /// charges/remises de niveau document ; TVA réconciliée UNIQUEMENT en l'absence de charge/remise globale
    /// (sa TVA n'est pas résolue à ce stade — sinon faux écart). Arrondi half-up 2 déc., tolérance 0.
    /// </summary>
    private static DocumentTotalsCheck BuildTotalsCheck(PivotDocumentDto pivot)
    {
        // Net réconcilié via la formule BR-CO-13 PARTAGÉE avec LineTotalsRule (PivotReconciliation) : aucune
        // dérive possible entre la validation bloquante et le verdict affiché à l'opérateur.
        var expectedNet = PivotReconciliation.ExpectedNet(pivot);
        var documentNet = PivotRounding.RoundAmount(pivot.Totals.TotalNet);

        // La TVA des charges/remises de niveau document n'est pas résolue à ce stade : ne réconcilier la TVA
        // que lorsque le document n'en porte aucune (même limite connue que LineTotalsRule, F04 §3.3).
        var taxChecked = pivot.DocumentCharges.Count == 0;
        var linesTax = PivotRounding.RoundAmount(pivot.Lines.Sum(line => line.Taxes.Sum(tax => tax.TaxAmount)));
        var documentTax = PivotRounding.RoundAmount(pivot.Totals.TotalTax);

        return new DocumentTotalsCheck
        {
            ExpectedNet = expectedNet,
            DocumentNet = documentNet,
            NetConsistent = expectedNet == documentNet,
            TaxChecked = taxChecked,
            LinesTax = linesTax,
            DocumentTax = documentTax,
            TaxConsistent = !taxChecked || linesTax == documentTax,
        };
    }

    /// <summary>Catégorie(s) en français. Cas normal (pivot mappé, EN 16931 BG-30 = 1 catégorie/ligne) : une seule.</summary>
    private static string CategoryDisplay(IReadOnlyList<PivotLineTaxDto> taxes)
    {
        if (taxes.Count == 0)
        {
            return Placeholder;
        }

        return string.Join(JoinSeparator, taxes.Select(t => VatCategoryDisplay.For(t.CategoryCode)));
    }

    private static string VatexDisplay(IReadOnlyList<PivotLineTaxDto> taxes)
    {
        var codes = taxes
            .Select(t => t.VatexCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToList();

        return codes.Count == 0 ? Placeholder : string.Join(JoinSeparator, codes);
    }

    /// <summary>Taux affiché seulement s'il est connu et UNIFORME sur la ligne (sinon <c>null</c> : on n'agrège pas un taux).</summary>
    private static decimal? UniformRate(IReadOnlyList<PivotLineTaxDto> taxes)
    {
        var rates = taxes.Select(t => t.Rate).Distinct().ToList();
        return rates.Count == 1 ? rates[0] : null;
    }
}
