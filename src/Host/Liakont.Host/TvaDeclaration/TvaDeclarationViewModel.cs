namespace Liakont.Host.TvaDeclaration;

using System.Collections.Generic;

/// <summary>
/// Modèle de présentation de la page « TVA / Déclaration » (aide à la déclaration de TVA sous le régime de la
/// marge, L2) : les agrégats du mois par devise × taux, et le total à reporter. Pur conteneur présentationnel
/// (aucune logique métier/fiscale, CLAUDE.md n°2) : les totaux sont la SOMME des lignes déjà calculées par le
/// module Pipeline (agrégation de présentation, jamais une dérivation fiscale). Un record (testable en bUnit).
/// </summary>
internal sealed record TvaDeclarationViewModel
{
    /// <summary>Lignes mois × devise × taux (base HT + TVA sur marge), triées (devise, taux).</summary>
    public required IReadOnlyList<TvaDeclarationRow> Lines { get; init; }

    /// <summary>Total des bases HT de marge du mois (somme des lignes) — repère pour la déclaration.</summary>
    public required decimal TotalBaseHt { get; init; }

    /// <summary>Total de la TVA sur marge du mois (somme des lignes) — TVA due à reporter.</summary>
    public required decimal TotalVat { get; init; }

    /// <summary>Modèle vide (aucune marge sur la période).</summary>
    public static TvaDeclarationViewModel Empty { get; } = new()
    {
        Lines = [],
        TotalBaseHt = 0m,
        TotalVat = 0m,
    };
}
