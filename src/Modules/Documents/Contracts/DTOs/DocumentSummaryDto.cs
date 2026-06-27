namespace Liakont.Modules.Documents.Contracts.DTOs;

using System;

/// <summary>
/// Ligne de liste d'un document (vues paginées par état pour la console — item TRK01). Sous-ensemble
/// de <see cref="DocumentDto"/> utile à l'affichage d'une file (état, numéro, totaux, dates).
/// </summary>
public sealed record DocumentSummaryDto
{
    public required Guid Id { get; init; }

    /// <summary>
    /// Référence du document dans le système source (ex. <c>encheresv6:ba:9000004</c>) — la console en dérive
    /// la FAMILLE de pièce affichée (BUG-20 : bordereau acheteur/vendeur, facture client, note d'honoraires),
    /// distinction que le type facture/avoir ne porte pas. Brut, jamais interprété fiscalement.
    /// </summary>
    public string? SourceReference { get; init; }

    public required string DocumentNumber { get; init; }

    public required string DocumentType { get; init; }

    public required DateOnly IssueDate { get; init; }

    public string? CustomerName { get; init; }

    public required decimal TotalGross { get; init; }

    public required string State { get; init; }

    public required DateTimeOffset LastUpdateUtc { get; init; }
}
