namespace Liakont.Host.B2cReporting;

using System;
using Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Projection de présentation d'un AGRÉGAT d'émission e-reporting B2C de la marge
/// (<see cref="B2cMarginEmissionAggregateDto"/>, journal B4) pour la page console des émissions de marge.
/// PRÉSENTATION pure : aucune logique métier/fiscale (CLAUDE.md n°2) ; le journal ne porte AUCUN montant
/// (la vue trace le cycle d'émission, pas une forme fiscale). Toutes les propriétés sont NON-NULLABLES :
/// le tri réflexif de <c>DeclaredListPage</c> remplace un null par <c>string.Empty</c>, si bien qu'une
/// colonne nullable mélangeant null et valeurs ferait lever <c>OrderBy</c> — d'où <see cref="PaEmissionId"/>
/// et <see cref="Detail"/> rendus « — » plutôt que null (même patron que <c>PaymentAggregateRow</c>).
/// </summary>
internal sealed record B2cMarginEmissionRow
{
    /// <summary>Identité de la transmission (lot d'émission) : non affichée, garantit l'unicité de la ligne (deux transmissions d'un même contenu restent distinctes).</summary>
    public required Guid EmissionBatchId { get; init; }

    /// <summary>Jour de l'agrégat (colonne « Jour », tri par défaut décroissant).</summary>
    public required DateOnly AggregateDate { get; init; }

    /// <summary>Devise ISO 4217 de l'agrégat (colonne « Devise »).</summary>
    public required string Currency { get; init; }

    /// <summary>Code catégorie de transaction (colonne « Catégorie », ex. <c>TMA1</c>).</summary>
    public required string Category { get; init; }

    /// <summary>Code rôle du déclarant (colonne « Rôle », ex. <c>SE</c>).</summary>
    public required string Role { get; init; }

    /// <summary>Nombre de pièces ayant contribué à l'agrégat (colonne « Pièces »).</summary>
    public required int DocumentCount { get; init; }

    /// <summary>NOM du statut courant (colonne « État »), traduit/coloré à l'affichage.</summary>
    public required string Status { get; init; }

    /// <summary>Identifiant serveur côté PA (colonne « Id plateforme »), « — » si l'agrégat n'est pas encore émis.</summary>
    public required string PaEmissionId { get; init; }

    /// <summary>Message opérateur de la dernière issue non terminale (colonne « Détail »), « — » si absent.</summary>
    public required string Detail { get; init; }

    /// <summary>Horodatage de la dernière entrée de l'agrégat (colonne « Dernière activité »).</summary>
    public required DateTimeOffset LastActivityUtc { get; init; }

    /// <summary>Projette un DTO du module Pipeline en ligne de présentation (formatage uniquement).</summary>
    public static B2cMarginEmissionRow FromDto(B2cMarginEmissionAggregateDto dto) => new()
    {
        EmissionBatchId = dto.EmissionBatchId,
        AggregateDate = dto.AggregateDate,
        Currency = dto.CurrencyCode,
        Category = dto.Category,
        Role = dto.Role,
        DocumentCount = dto.DocumentCount,
        Status = dto.Status,
        PaEmissionId = string.IsNullOrWhiteSpace(dto.PaEmissionId) ? "—" : dto.PaEmissionId!,
        Detail = string.IsNullOrWhiteSpace(dto.Detail) ? "—" : dto.Detail!,
        LastActivityUtc = dto.LastActivityUtc,
    };
}
