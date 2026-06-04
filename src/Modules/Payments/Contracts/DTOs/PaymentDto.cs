namespace Liakont.Modules.Payments.Contracts.DTOs;

using System;

/// <summary>
/// Encaissement brut exposé en lecture (F09, item TRK04). Reflète le paiement tel que persisté dans la base
/// DU TENANT — montant en <see cref="decimal"/> (CLAUDE.md n°1).
/// </summary>
public sealed record PaymentDto
{
    public required Guid Id { get; init; }

    public required DateOnly PaymentDate { get; init; }

    public required decimal Amount { get; init; }

    public string? Method { get; init; }

    public string? RelatedDocumentNumber { get; init; }

    public string? SourceReference { get; init; }

    public required DateTimeOffset ReceivedUtc { get; init; }
}
