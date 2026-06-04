namespace Liakont.Modules.Payments.Contracts.DTOs;

using System;

/// <summary>
/// Entrée de la piste d'audit d'un agrégat de paiement exposée en lecture (F06 §3 / F09, item TRK04). Reflète
/// l'immuabilité du journal côté base : les consommateurs n'en lisent que l'historique, jamais une mutation.
/// </summary>
public sealed record PaymentAggregateEventDto
{
    public required Guid Id { get; init; }

    public required Guid AggregateId { get; init; }

    public required DateTimeOffset TimestampUtc { get; init; }

    public required string EventType { get; init; }

    public required string State { get; init; }

    public string? Detail { get; init; }

    public string? PayloadSnapshot { get; init; }

    public string? PaResponseSnapshot { get; init; }
}
