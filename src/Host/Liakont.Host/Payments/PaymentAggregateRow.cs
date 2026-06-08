namespace Liakont.Host.Payments;

using System;
using Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Projection de présentation d'un agrégat jour×taux de l'e-reporting de paiement
/// (<see cref="PaymentDailyAggregateDto"/>, projection PIP03a) pour la page Encaissements (WEB06, F10 §2.4).
/// PRÉSENTATION pure : les montants <see cref="decimal"/> (CLAUDE.md n°1) et la qualification fiscale
/// (<see cref="Status"/>) sont repris VERBATIM du module Pipeline — rien n'est recalculé ni requalifié ici
/// (aucune logique métier/fiscale dans la page, CLAUDE.md n°2). Toutes les propriétés sont NON-NULLABLES :
/// le tri réflexif de <c>DeclaredListPage</c> remplace un null par <c>string.Empty</c>, si bien qu'une
/// colonne nullable mélangeant null et valeurs typées ferait lever <c>OrderBy</c> sur un type de clé
/// hétérogène — d'où <see cref="Reason"/> rendu « — » plutôt que null (même patron que <c>PipelineRunRow</c>).
/// </summary>
internal sealed record PaymentAggregateRow
{
    /// <summary>Identifiant de la ligne de projection (identité stable de la ligne pour la sélection/dédup).</summary>
    public required Guid Id { get; init; }

    /// <summary>Jour d'encaissement agrégé (colonne « Jour », tri par défaut décroissant).</summary>
    public required DateOnly AggregateDate { get; init; }

    /// <summary>Taux de TVA de la ventilation (colonne « Taux », valeur de taux ex. <c>20</c> pour 20 %).</summary>
    public required decimal VatRate { get; init; }

    /// <summary>Base taxable HT encaissée du jour pour ce taux (colonne « Base HT », peut être négative).</summary>
    public required decimal TaxableBase { get; init; }

    /// <summary>TVA encaissée du jour pour ce taux (colonne « TVA », peut être négative).</summary>
    public required decimal VatAmount { get; init; }

    /// <summary>Qualification fiscale brute (colonne « État ») telle que produite par PIP03a, traduite à l'affichage.</summary>
    public required string Status { get; init; }

    /// <summary>Message opérateur quand l'agrégat n'est pas transmissible (colonne « Motif »), « — » si absent.</summary>
    public required string Reason { get; init; }

    /// <summary>Projette un DTO du module Pipeline en ligne de présentation (formatage uniquement).</summary>
    public static PaymentAggregateRow FromDto(PaymentDailyAggregateDto dto) => new()
    {
        Id = dto.Id,
        AggregateDate = dto.AggregateDate,
        VatRate = dto.VatRate,
        TaxableBase = dto.TaxableBase,
        VatAmount = dto.VatAmount,
        Status = dto.Status,
        Reason = string.IsNullOrWhiteSpace(dto.Reason) ? "—" : dto.Reason!,
    };
}
