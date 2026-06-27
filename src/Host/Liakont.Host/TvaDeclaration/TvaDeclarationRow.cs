namespace Liakont.Host.TvaDeclaration;

using Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Projection de présentation d'un agrégat mois × devise × taux du registre de la marge
/// (<see cref="MarginRegistryMonthlyDto"/>, L2) pour la page « TVA / Déclaration » : la base HT et la TVA sur
/// marge à reporter en déclaration de TVA (CA3). PRÉSENTATION pure : les montants <see cref="decimal"/>
/// (CLAUDE.md n°1) viennent du module Pipeline, repris VERBATIM — rien n'est recalculé ni requalifié ici
/// (aucune logique métier/fiscale dans la page, CLAUDE.md n°2). Toutes les propriétés sont NON-NULLABLES (le
/// tri réflexif de <c>DeclaredListPage</c> remplacerait un null par <c>string.Empty</c>, hétérogène à l'OrderBy).
/// </summary>
internal sealed record TvaDeclarationRow
{
    /// <summary>Période année-mois (<c>"yyyy-MM"</c>) — identité de regroupement, non affichée (filtre = un mois).</summary>
    public required string Period { get; init; }

    /// <summary>Devise ISO 4217 de l'agrégat (colonne « Devise »).</summary>
    public required string Currency { get; init; }

    /// <summary>Taux de TVA de la marge (colonne « Taux », valeur ex. <c>20</c> pour 20 %).</summary>
    public required decimal RatePercent { get; init; }

    /// <summary>Base HT de la marge du mois pour ce taux (colonne « Base HT »).</summary>
    public required decimal MarginBaseHt { get; init; }

    /// <summary>TVA sur la marge du mois pour ce taux (colonne « TVA sur marge »), montant à déclarer.</summary>
    public required decimal MarginVat { get; init; }

    /// <summary>Nombre de documents (pièces) contribuant à l'agrégat (colonne « Pièces »).</summary>
    public required int DocumentCount { get; init; }

    /// <summary>Projette un DTO du module Pipeline en ligne de présentation (formatage uniquement).</summary>
    public static TvaDeclarationRow FromDto(MarginRegistryMonthlyDto dto) => new()
    {
        Period = dto.Period,
        Currency = dto.CurrencyCode,
        RatePercent = dto.RatePercent,
        MarginBaseHt = dto.MarginBaseHt,
        MarginVat = dto.MarginVat,
        DocumentCount = dto.DocumentCount,
    };
}
