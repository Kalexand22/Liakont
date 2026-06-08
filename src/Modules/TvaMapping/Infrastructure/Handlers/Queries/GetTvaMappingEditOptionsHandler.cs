namespace Liakont.Modules.TvaMapping.Infrastructure.Handlers.Queries;

using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Liakont.Modules.TvaMapping.Contracts.Queries;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Domain.Services;
using MediatR;

/// <summary>
/// Handler des listes FERMÉES d'édition de la table de mapping TVA (item TVA05 / WEB07b). Vocabulaire
/// STATIQUE, sans tenant ni accès base : les CODES sont dérivés des mêmes sources que le moteur
/// d'édition (énumérations du domaine + <see cref="VatCategoryParser.AllowedCodes"/> + <see cref="VatexCatalog"/>),
/// donc IMPOSSIBLES à faire diverger ; les libellés d'affichage sont TRANSCRITS de F03 §2.1/§2.2 (un
/// code sans libellé retombe sur son code — aucune valeur fiscale inventée, CLAUDE.md n°2). Le handler
/// ne porte aucune logique fiscale : il ne fait qu'exposer le vocabulaire sourcé.
/// </summary>
public sealed class GetTvaMappingEditOptionsHandler
    : IRequestHandler<GetTvaMappingEditOptionsQuery, TvaMappingEditOptionsDto>
{
    // Libellés des catégories UNCL5305, transcrits de F03 §2.1 (et des commentaires de l'enum
    // VatCategory). Aucune catégorie n'est ajoutée : les CODES viennent de VatCategoryParser.AllowedCodes.
    private static readonly IReadOnlyDictionary<string, string> CategoryLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["S"] = "Taux normal",
            ["AA"] = "Taux réduit",
            ["AAA"] = "Taux particulier (super-réduit)",
            ["Z"] = "Taux zéro (assujetti)",
            ["E"] = "Exonéré (motif VATEX requis)",
            ["AE"] = "Autoliquidation",
            ["G"] = "Export hors UE détaxé",
            ["K"] = "Livraison/prestation intracommunautaire",
            ["O"] = "Hors champ d'application de la TVA",
        };

    // Libellés des parts, transcrits de F03 §4.1 / §2.3 (l'enum MappingPart reste la source des CODES).
    private static readonly IReadOnlyDictionary<string, string> PartLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(MappingPart.Adjudication)] = "Adjudication (le bien vendu)",
            [nameof(MappingPart.Frais)] = "Frais (frais acheteur, toujours taxables)",
            [nameof(MappingPart.Autre)] = "Autre",
        };

    // Libellés des modes de taux, transcrits des commentaires de l'enum RateMode / F03 §4.1.
    private static readonly IReadOnlyDictionary<string, string> RateModeLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(RateMode.Fixed)] = "Taux fixe",
            [nameof(RateMode.ComputedFromSource)] = "Calculé depuis la source",
        };

    public Task<TvaMappingEditOptionsDto> Handle(
        GetTvaMappingEditOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var dto = new TvaMappingEditOptionsDto
        {
            Categories = BuildOptions(VatCategoryParser.AllowedCodes, CategoryLabels),
            Parts = BuildOptions(Enum.GetNames<MappingPart>(), PartLabels),
            RateModes = BuildOptions(Enum.GetNames<RateMode>(), RateModeLabels),
            VatexCodes = VatexCatalog.All
                .Select(entry => new TvaMappingOptionDto(entry.Code, $"{entry.Code} — {entry.Description}"))
                .ToArray(),
        };

        return Task.FromResult(dto);
    }

    /// <summary>
    /// Compose les options à partir du jeu de CODES autoritatif (énum / liste sourcée) et d'une table
    /// de libellés. Un code sans libellé retombe sur lui-même : on n'invente jamais de libellé manquant.
    /// </summary>
    private static List<TvaMappingOptionDto> BuildOptions(
        IReadOnlyList<string> codes,
        IReadOnlyDictionary<string, string> labels)
    {
        var options = new List<TvaMappingOptionDto>(codes.Count);
        foreach (var code in codes)
        {
            var label = labels.TryGetValue(code, out var found) ? found : code;
            options.Add(new TvaMappingOptionDto(code, label));
        }

        return options;
    }
}
