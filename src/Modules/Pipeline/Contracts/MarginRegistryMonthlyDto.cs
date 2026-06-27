namespace Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Agrégat mois × devise × taux du registre de la marge (<c>pipeline.margin_registry</c>, Livrable 2) — une
/// ligne de l'aide à la déclaration de TVA : la base HT et la TVA sur marge à reporter (CA3) pour un mois et un
/// taux donnés. Lecture seule : les montants viennent du registre (peuplé au CHECK depuis les cœurs purs
/// sourcés), repris tels quels, jamais redérivés ici (CLAUDE.md n°2). Montants en <see cref="decimal"/> (n°1).
/// </summary>
public sealed record MarginRegistryMonthlyDto
{
    /// <summary>Période année-mois (<c>"yyyy-MM"</c>) du jour d'émission des documents agrégés.</summary>
    public required string Period { get; init; }

    /// <summary>Devise ISO 4217 de l'agrégat (EN 16931 BT-5).</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>Taux de TVA en pourcentage (ex. <c>20</c> pour 20 %).</summary>
    public required decimal RatePercent { get; init; }

    /// <summary>Somme des bases HT de marge du mois pour ce taux (à reporter en base imposable).</summary>
    public required decimal MarginBaseHt { get; init; }

    /// <summary>Somme des TVA sur marge du mois pour ce taux (TVA due à déclarer).</summary>
    public required decimal MarginVat { get; init; }

    /// <summary>Nombre de documents (pièces) contribuant à cet agrégat mois × taux.</summary>
    public required int DocumentCount { get; init; }
}
