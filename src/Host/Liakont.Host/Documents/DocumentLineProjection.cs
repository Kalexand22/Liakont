namespace Liakont.Host.Documents;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Host.Components;
using Liakont.Modules.Pipeline.Infrastructure.Serialization;

/// <summary>
/// Projette le pivot TRANSMIS d'un document (snapshot canonique ADR-0007 porté par le dernier événement
/// <c>DocumentIssued</c>/<c>DocumentRejected</c>) en lignes prêtes à afficher (<see cref="DocumentLineView"/>)
/// pour l'onglet « Contenu » du détail (FIX205, F10 §2.3). Le snapshot étant le pivot RÉELLEMENT envoyé à la
/// Plateforme Agréée, la catégorie/le VATEX y sont déjà résolus par le mapping plateforme — c'est la seule
/// source au niveau Contracts qui porte « régime source → catégorie/VATEX résultante » (un document non
/// transmis n'a pas encore de pivot mappé exposé : la vue affiche alors une note, pas de ligne inventée).
/// PURE et SANS état : aucune règle métier ni calcul fiscal — les montants viennent du pivot (la source),
/// la désérialisation réutilise le lecteur canonique du Pipeline (échelle décimale préservée, ADR-0007).
/// Un snapshot illisible NE DOIT JAMAIS casser le détail en lecture (le pivot fait foi dans l'export WORM) :
/// la projection retombe sur une liste vide.
/// </summary>
public static class DocumentLineProjection
{
    /// <summary>Sépare les valeurs jointes (régimes source, catégories multiples) d'une cellule.</summary>
    private const string JoinSeparator = ", ";

    private const string Placeholder = "—";

    /// <summary>
    /// Construit les lignes affichables depuis le JSON canonique du pivot transmis. <c>null</c>/vide (document
    /// non encore transmis) ou snapshot illisible → liste vide (la vue bascule alors sur sa note).
    /// </summary>
    /// <param name="transmittedPivotJson">JSON canonique du pivot transmis (PayloadSnapshot), ou <c>null</c>.</param>
    public static IReadOnlyList<DocumentLineView> FromTransmittedSnapshot(string? transmittedPivotJson)
    {
        if (string.IsNullOrWhiteSpace(transmittedPivotJson))
        {
            return Array.Empty<DocumentLineView>();
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
            return Array.Empty<DocumentLineView>();
        }

        return pivot.Lines.Select(ToView).ToList();
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
